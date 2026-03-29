using System.Text;
using Codex.Core;
using Codex.IR;
using Codex.Types;

namespace Codex.Emit.Wasm;

sealed partial class WasmModuleBuilder
{
    bool TryEmitBuiltin(MemoryStream body, string name, List<IRExpr> args,
        ValueMap<string, int> localMap, ref int nextLocal, List<byte> localTypes)
    {
        switch (name)
        {
            case "print-line" when args.Count == 1:
                EmitExpr(body, args[0], localMap, ref nextLocal, localTypes, args[0].Type);
                EmitPrintLineForType(body, args[0].Type, ref nextLocal, localTypes);
                return true;

            case "show" when args.Count == 1:
                EmitExpr(body, args[0], localMap, ref nextLocal, localTypes, args[0].Type);
                EmitShow(body, args[0].Type, ref nextLocal, localTypes);
                return true;

            case "text-length" when args.Count == 1:
                EmitExpr(body, args[0], localMap, ref nextLocal, localTypes, args[0].Type);
                // Load length from pointer (first 4 bytes)
                body.WriteByte(OpI32Load);
                body.WriteByte(0x02); // align 4
                WriteUnsignedLeb128(body, 0); // offset 0
                body.WriteByte(OpI64ExtendI32S);
                return true;

            case "integer-to-text" when args.Count == 1:
                EmitExpr(body, args[0], localMap, ref nextLocal, localTypes, args[0].Type);
                EmitI64ToString(body, ref nextLocal, localTypes);
                return true;

            case "text-to-integer" when args.Count == 1:
                EmitExpr(body, args[0], localMap, ref nextLocal, localTypes, args[0].Type);
                EmitTextToInteger(body, ref nextLocal, localTypes);
                return true;

            case "char-at" when args.Count == 2:
            {
                // char-at text index → Integer (byte value in i64)
                EmitExpr(body, args[0], localMap, ref nextLocal, localTypes, args[0].Type);
                int caPtr = nextLocal++; localTypes.Add(WasmI32);
                body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, caPtr);

                EmitExpr(body, args[1], localMap, ref nextLocal, localTypes, args[1].Type);
                body.WriteByte(OpI32WrapI64); // index i64 → i32
                int caIdx = nextLocal++; localTypes.Add(WasmI32);
                body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, caIdx);

                // Load byte at ptr + 4 + idx, extend to i64
                body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, caPtr);
                body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, caIdx);
                body.WriteByte(OpI32Add);
                body.WriteByte(OpI32Const); WriteSignedLeb128(body, 4);
                body.WriteByte(OpI32Add);
                body.WriteByte(OpI32Load8U); body.WriteByte(0x00); WriteUnsignedLeb128(body, 0);
                body.WriteByte(OpI64ExtendI32U);
                return true;
            }

            case "char-code" when args.Count == 1:
                // char-code: identity — Char is already an integer
                EmitExpr(body, args[0], localMap, ref nextLocal, localTypes, args[0].Type);
                return true;

            case "code-to-char" when args.Count == 1:
                // code-to-char: identity — Char is already an integer
                EmitExpr(body, args[0], localMap, ref nextLocal, localTypes, args[0].Type);
                return true;

            case "char-to-text" when args.Count == 1:
                EmitCharToText(body, args[0], localMap, ref nextLocal, localTypes);
                return true;

            case "is-letter" when args.Count == 1:
            {
                // CCE: letters are 13-64 (lowercase 13-38, uppercase 39-64)
                // Single range check: (val - 13) <= 51 (unsigned)
                EmitExpr(body, args[0], localMap, ref nextLocal, localTypes, args[0].Type);
                body.WriteByte(OpI32WrapI64);
                body.WriteByte(OpI32Const); WriteSignedLeb128(body, 13);
                body.WriteByte(OpI32Sub);
                body.WriteByte(OpI32Const); WriteSignedLeb128(body, 52); // 64 - 13 + 1
                body.WriteByte(OpI32LtU);
                body.WriteByte(OpI64ExtendI32U);
                return true;
            }

            case "is-digit" when args.Count == 1:
            {
                // CCE: digits are 3-12
                // (val - 3) <= 9 (unsigned)
                EmitExpr(body, args[0], localMap, ref nextLocal, localTypes, args[0].Type);
                body.WriteByte(OpI32WrapI64);
                body.WriteByte(OpI32Const); WriteSignedLeb128(body, 3);
                body.WriteByte(OpI32Sub);
                body.WriteByte(OpI32Const); WriteSignedLeb128(body, 10);
                body.WriteByte(OpI32LtU);
                body.WriteByte(OpI64ExtendI32U);
                return true;
            }

            case "is-whitespace" when args.Count == 1:
            {
                // CCE: whitespace is 0-2 (NUL, LF, Space)
                // val <= 2 (unsigned), same as val < 3
                EmitExpr(body, args[0], localMap, ref nextLocal, localTypes, args[0].Type);
                body.WriteByte(OpI32WrapI64);
                body.WriteByte(OpI32Const); WriteSignedLeb128(body, 3);
                body.WriteByte(OpI32LtU);
                body.WriteByte(OpI64ExtendI32U);
                return true;
            }

            case "substring" when args.Count == 3:
                EmitSubstring(body, args[0], args[1], args[2], localMap, ref nextLocal, localTypes);
                return true;

            case "negate" when args.Count == 1:
                EmitExpr(body, args[0], localMap, ref nextLocal, localTypes, args[0].Type);
                if (args[0].Type is NumberType)
                    body.WriteByte(OpF64Neg);
                else
                {
                    // 0 - value: save value, push 0, restore value, subtract
                    int tmpLocal = nextLocal++;
                    localTypes.Add(WasmI64);
                    body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, tmpLocal);
                    body.WriteByte(OpI64Const); WriteSignedLeb128(body, 0);
                    body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, tmpLocal);
                    body.WriteByte(OpI64Sub);
                }
                return true;

            default:
                return false;
        }
    }

    void EmitPrintLineForType(MemoryStream body, CodexType type,
        ref int nextLocal, List<byte> localTypes)
    {
        switch (type)
        {
            case IntegerType:
                body.WriteByte(OpCall);
                WriteUnsignedLeb128(body, m_printI64Index);
                break;

            case BooleanType:
                body.WriteByte(OpCall);
                WriteUnsignedLeb128(body, m_printBoolIndex);
                break;

            case TextType:
                int ptrLocal = nextLocal++;
                localTypes.Add(WasmI32);
                body.WriteByte(OpLocalSet);
                WriteUnsignedLeb128(body, ptrLocal);
                EmitFdWriteFromLengthPrefixed(body, ptrLocal);
                EmitWriteNewline(body, localTypes);
                break;

            default:
                body.WriteByte(OpDrop);
                break;
        }
    }

    void EmitShow(MemoryStream body, CodexType type,
        ref int nextLocal, List<byte> localTypes)
    {
        switch (type)
        {
            case IntegerType:
                // Convert i64 to string in memory, return pointer
                EmitI64ToString(body, ref nextLocal, localTypes);
                break;

            case BooleanType:
                // If true → "True", else → "False"
                int truePtr = AddDataSegment(EncodeLengthPrefixedString("True"));
                int falsePtr = AddDataSegment(EncodeLengthPrefixedString("False"));

                body.WriteByte(OpIf);
                body.WriteByte(BlockTypeI32);
                body.WriteByte(OpI32Const); WriteSignedLeb128(body, truePtr);
                body.WriteByte(OpElse);
                body.WriteByte(OpI32Const); WriteSignedLeb128(body, falsePtr);
                body.WriteByte(OpEnd);
                break;

            case TextType:
                // Already a string pointer, nothing to do
                break;

            default:
                break;
        }
    }

    // ── Text operations ──────────────────────────────────────────

    void EmitTextToInteger(MemoryStream body, ref int nextLocal, List<byte> localTypes)
    {
        // Stack has i32 (pointer to length-prefixed string)
        // Parse decimal digits → i64
        int ptrLocal = nextLocal++; localTypes.Add(WasmI32);
        int lenLocal = nextLocal++; localTypes.Add(WasmI32);
        int idxLocal = nextLocal++; localTypes.Add(WasmI32);
        int resultLocal = nextLocal++; localTypes.Add(WasmI64);
        int negLocal = nextLocal++; localTypes.Add(WasmI32);

        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, ptrLocal);

        // Load length
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, ptrLocal);
        body.WriteByte(OpI32Load); body.WriteByte(0x02); WriteUnsignedLeb128(body, 0);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, lenLocal);

        // result = 0, idx = 0, neg = 0
        body.WriteByte(OpI64Const); WriteSignedLeb128(body, 0);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, resultLocal);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 0);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, idxLocal);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 0);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, negLocal);

        // Check for '-' at position 0
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, lenLocal);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 0);
        body.WriteByte(OpI32GtS);
        body.WriteByte(OpIf); body.WriteByte(BlockTypeVoid);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, ptrLocal);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 4);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpI32Load8U); body.WriteByte(0x00); WriteUnsignedLeb128(body, 0);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 45); // '-'
        body.WriteByte(OpI32Eq);
        body.WriteByte(OpIf); body.WriteByte(BlockTypeVoid);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 1);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, negLocal);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 1);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, idxLocal);
        body.WriteByte(OpEnd);
        body.WriteByte(OpEnd);

        // Parse loop: block { loop {
        body.WriteByte(OpBlock); body.WriteByte(BlockTypeVoid);
        body.WriteByte(OpLoop); body.WriteByte(BlockTypeVoid);

        // if idx >= len: break
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, idxLocal);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, lenLocal);
        body.WriteByte(OpI32GeS);
        body.WriteByte(OpBrIf); WriteUnsignedLeb128(body, 1);

        // result = result * 10 + (byte - 48)
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, resultLocal);
        body.WriteByte(OpI64Const); WriteSignedLeb128(body, 10);
        body.WriteByte(OpI64Mul);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, ptrLocal);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 4);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, idxLocal);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpI32Load8U); body.WriteByte(0x00); WriteUnsignedLeb128(body, 0);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 48);
        body.WriteByte(OpI32Sub);
        body.WriteByte(OpI64ExtendI32S);
        body.WriteByte(OpI64Add);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, resultLocal);

        // idx++
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, idxLocal);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 1);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, idxLocal);
        body.WriteByte(OpBr); WriteUnsignedLeb128(body, 0);

        body.WriteByte(OpEnd); // end loop
        body.WriteByte(OpEnd); // end block

        // If negative, negate result
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, negLocal);
        body.WriteByte(OpIf); body.WriteByte(BlockTypeI64);
        body.WriteByte(OpI64Const); WriteSignedLeb128(body, 0);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, resultLocal);
        body.WriteByte(OpI64Sub);
        body.WriteByte(OpElse);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, resultLocal);
        body.WriteByte(OpEnd);
    }

    void EmitCharToText(MemoryStream body, IRExpr charExpr,
        ValueMap<string, int> localMap, ref int nextLocal, List<byte> localTypes)
    {
        // char-to-text: allocate 1-char length-prefixed string from integer Char value
        EmitExpr(body, charExpr, localMap, ref nextLocal, localTypes, charExpr.Type);
        body.WriteByte(OpI32WrapI64); // char value i64 → i32
        int byteLocal = nextLocal++; localTypes.Add(WasmI32);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, byteLocal);

        // Allocate 5 bytes: 4 (length=1) + 1 (the byte)
        int resultLocal = nextLocal++; localTypes.Add(WasmI32);
        body.WriteByte(OpGlobalGet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, resultLocal);
        body.WriteByte(OpGlobalGet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 5);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpGlobalSet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);

        // Store length = 1
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, resultLocal);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 1);
        body.WriteByte(OpI32Store); body.WriteByte(0x02); WriteUnsignedLeb128(body, 0);

        // Store byte value: result[4] = charValue
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, resultLocal);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 4);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, byteLocal);
        body.WriteByte(OpI32Store8); body.WriteByte(0x00); WriteUnsignedLeb128(body, 0);

        // Return result pointer
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, resultLocal);
    }

    void EmitSubstring(MemoryStream body, IRExpr textExpr, IRExpr startExpr, IRExpr lenExpr,
        ValueMap<string, int> localMap, ref int nextLocal, List<byte> localTypes)
    {
        // substring text start len → Text
        EmitExpr(body, textExpr, localMap, ref nextLocal, localTypes, textExpr.Type);
        int ptrLocal = nextLocal++; localTypes.Add(WasmI32);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, ptrLocal);

        EmitExpr(body, startExpr, localMap, ref nextLocal, localTypes, startExpr.Type);
        body.WriteByte(OpI32WrapI64);
        int startLocal = nextLocal++; localTypes.Add(WasmI32);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, startLocal);

        EmitExpr(body, lenExpr, localMap, ref nextLocal, localTypes, lenExpr.Type);
        body.WriteByte(OpI32WrapI64);
        int subLenLocal = nextLocal++; localTypes.Add(WasmI32);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, subLenLocal);

        // Allocate 4 + subLen bytes
        int resultLocal = nextLocal++; localTypes.Add(WasmI32);
        EmitBumpAlloc(body, subLenLocal, 4, resultLocal);

        // Store length
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, resultLocal);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, subLenLocal);
        body.WriteByte(OpI32Store); body.WriteByte(0x02); WriteUnsignedLeb128(body, 0);

        // Copy bytes: memcpy(result+4, ptr+4+start, subLen)
        int srcStart = nextLocal++; localTypes.Add(WasmI32);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, ptrLocal);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 4);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, startLocal);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, srcStart);

        int destStart = nextLocal++; localTypes.Add(WasmI32);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, resultLocal);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 4);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, destStart);

        EmitMemCopyDirect(body, destStart, srcStart, 0, subLenLocal, ref nextLocal, localTypes);

        // Return result pointer
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, resultLocal);
    }

    void EmitTextLiteral(MemoryStream body, string value)
    {
        string key = value;
        if (!m_stringOffsets.TryGet(key, out int offset))
        {
            byte[] encoded = EncodeLengthPrefixedString(value);
            offset = AddDataSegment(encoded);
            m_stringOffsets = m_stringOffsets.Set(key, offset);
            m_stringLengths = m_stringLengths.Set(key, Encoding.UTF8.GetByteCount(value));
        }

        body.WriteByte(OpI32Const);
        WriteSignedLeb128(body, offset);
    }

    void EmitTextAppend(MemoryStream body, IRExpr left, IRExpr right,
        ValueMap<string, int> localMap, ref int nextLocal, List<byte> localTypes)
    {
        // Evaluate both sides
        EmitExpr(body, left, localMap, ref nextLocal, localTypes, left.Type);
        int leftPtr = nextLocal++;
        localTypes.Add(WasmI32);
        body.WriteByte(OpLocalSet);
        WriteUnsignedLeb128(body, leftPtr);

        EmitExpr(body, right, localMap, ref nextLocal, localTypes, right.Type);
        int rightPtr = nextLocal++;
        localTypes.Add(WasmI32);
        body.WriteByte(OpLocalSet);
        WriteUnsignedLeb128(body, rightPtr);

        // Load lengths
        int leftLen = nextLocal++;
        localTypes.Add(WasmI32);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, leftPtr);
        body.WriteByte(OpI32Load); body.WriteByte(0x02); WriteUnsignedLeb128(body, 0);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, leftLen);

        int rightLen = nextLocal++;
        localTypes.Add(WasmI32);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, rightPtr);
        body.WriteByte(OpI32Load); body.WriteByte(0x02); WriteUnsignedLeb128(body, 0);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, rightLen);

        // Total length
        int totalLen = nextLocal++;
        localTypes.Add(WasmI32);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, leftLen);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, rightLen);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, totalLen);

        // Bump-allocate: 4 bytes for length prefix + totalLen bytes for data
        int resultPtr = nextLocal++;
        localTypes.Add(WasmI32);
        EmitBumpAlloc(body, totalLen, 4, resultPtr);

        // Store total length at resultPtr
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, resultPtr);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, totalLen);
        body.WriteByte(OpI32Store); body.WriteByte(0x02); WriteUnsignedLeb128(body, 0);

        // Copy left bytes: memory.copy is bulk-memory proposal, use byte loop instead
        // For simplicity, use a loop
        EmitMemCopy(body, resultPtr, 4, leftPtr, 4, leftLen, ref nextLocal, localTypes);

        // Copy right bytes after left
        // dest offset = resultPtr + 4 + leftLen
        int rightDest = nextLocal++;
        localTypes.Add(WasmI32);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, resultPtr);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 4);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, leftLen);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, rightDest);

        EmitMemCopyDirect(body, rightDest, rightPtr, 4, rightLen, ref nextLocal, localTypes);

        // Result is the new pointer
        body.WriteByte(OpLocalGet);
        WriteUnsignedLeb128(body, resultPtr);
    }

    void EmitBumpAlloc(MemoryStream body, int sizeLocal, int extraBytes, int resultLocal)
    {
        // resultLocal = heap_ptr
        body.WriteByte(OpGlobalGet);
        WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);
        body.WriteByte(OpLocalSet);
        WriteUnsignedLeb128(body, resultLocal);

        // heap_ptr += extraBytes + sizeLocal
        body.WriteByte(OpGlobalGet);
        WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);
        body.WriteByte(OpI32Const);
        WriteSignedLeb128(body, extraBytes);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpLocalGet);
        WriteUnsignedLeb128(body, sizeLocal);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpGlobalSet);
        WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);
    }

    void EmitMemCopy(MemoryStream body, int destBase, int destOffset,
        int srcBase, int srcOffset, int lenLocal,
        ref int nextLocal, List<byte> localTypes)
    {
        // Byte-by-byte copy loop
        int idx = nextLocal++;
        localTypes.Add(WasmI32);

        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 0);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, idx);

        // block { loop {
        body.WriteByte(OpBlock); body.WriteByte(BlockTypeVoid);
        body.WriteByte(OpLoop); body.WriteByte(BlockTypeVoid);

        // if idx >= len: br 1 (break out of block)
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, idx);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, lenLocal);
        body.WriteByte(OpI32GeS);
        body.WriteByte(OpBrIf); WriteUnsignedLeb128(body, 1);

        // dest[destBase + destOffset + idx] = src[srcBase + srcOffset + idx]
        // Store address
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, destBase);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, destOffset);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, idx);
        body.WriteByte(OpI32Add);

        // Load value
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, srcBase);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, srcOffset);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, idx);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpI32Load8U); // i32.load8_u
        body.WriteByte(0x00); WriteUnsignedLeb128(body, 0);

        body.WriteByte(OpI32Store8); // i32.store8
        body.WriteByte(0x00); WriteUnsignedLeb128(body, 0);

        // idx++
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, idx);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 1);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, idx);

        // br 0 (continue loop)
        body.WriteByte(OpBr); WriteUnsignedLeb128(body, 0);

        body.WriteByte(OpEnd); // end loop
        body.WriteByte(OpEnd); // end block
    }

    void EmitMemCopyDirect(MemoryStream body, int destLocal, int srcBase, int srcOffset,
        int lenLocal, ref int nextLocal, List<byte> localTypes)
    {
        int idx = nextLocal++;
        localTypes.Add(WasmI32);

        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 0);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, idx);

        body.WriteByte(OpBlock); body.WriteByte(BlockTypeVoid);
        body.WriteByte(OpLoop); body.WriteByte(BlockTypeVoid);

        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, idx);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, lenLocal);
        body.WriteByte(OpI32GeS);
        body.WriteByte(OpBrIf); WriteUnsignedLeb128(body, 1);

        // dest[destLocal + idx]
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, destLocal);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, idx);
        body.WriteByte(OpI32Add);

        // src[srcBase + srcOffset + idx]
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, srcBase);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, srcOffset);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, idx);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpI32Load8U); // i32.load8_u
        body.WriteByte(0x00); WriteUnsignedLeb128(body, 0);

        body.WriteByte(OpI32Store8); // i32.store8
        body.WriteByte(0x00); WriteUnsignedLeb128(body, 0);

        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, idx);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 1);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, idx);

        body.WriteByte(OpBr); WriteUnsignedLeb128(body, 0);

        body.WriteByte(OpEnd);
        body.WriteByte(OpEnd);
    }
}
