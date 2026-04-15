using System.Text;
using Codex.Core;
using Codex.IR;
using Codex.Types;

namespace Codex.Emit.Wasm;

sealed partial class WasmModuleBuilder
{
    void EmitFdWriteFromLengthPrefixed(MemoryStream body, int ptrLocal)
    {
        // WASI fd_write needs: iov at a scratch address
        // iov = { buf_ptr: i32, buf_len: i32 }
        // We use memory address 0 as scratch for iov struct (before data section starts)

        // iov[0].buf = ptr + 4 (skip length prefix)
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 0); // scratch addr for iov
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, ptrLocal);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 4);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpI32Store); body.WriteByte(0x02); WriteUnsignedLeb128(body, 0);

        // iov[0].len = *ptr (the length prefix)
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 4); // scratch addr + 4
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, ptrLocal);
        body.WriteByte(OpI32Load); body.WriteByte(0x02); WriteUnsignedLeb128(body, 0);
        body.WriteByte(OpI32Store); body.WriteByte(0x02); WriteUnsignedLeb128(body, 0);

        // fd_write(1, iov=0, iovs_len=1, nwritten=8)
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 1); // stdout
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 0); // iov ptr
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 1); // 1 iov
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 8); // nwritten scratch
        body.WriteByte(OpCall); WriteUnsignedLeb128(body, m_fdWriteIndex);
        body.WriteByte(OpDrop); // drop return value
    }

    void EmitWriteNewline(MemoryStream body, List<byte> localTypes)
    {
        // Write "\n"
        int nlOffset = AddDataSegment(EncodeLengthPrefixedString("\n"));

        // iov[0].buf = nlOffset + 4
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 0);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, nlOffset + 4);
        body.WriteByte(OpI32Store); body.WriteByte(0x02); WriteUnsignedLeb128(body, 0);

        // iov[0].len = 1
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 4);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 1);
        body.WriteByte(OpI32Store); body.WriteByte(0x02); WriteUnsignedLeb128(body, 0);

        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 1);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 0);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 1);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 8);
        body.WriteByte(OpCall); WriteUnsignedLeb128(body, m_fdWriteIndex);
        body.WriteByte(OpDrop);
    }

    // ── Runtime helpers ──────────────────────────────────────────

    void EmitRuntimeHelpers()
    {
        EmitPrintI64Helper();
        EmitPrintBoolHelper();
        EmitStrEqHelper();
    }

    void EmitPrintI64Helper()
    {
        int typeIndex = AddFuncType(new byte[] { WasmI64 }, Array.Empty<byte>());
        int funcSlot = m_printI64Index - m_importCount;
        m_functionTypeIndices[funcSlot] = typeIndex;

        MemoryStream body = new();
        List<byte> localTypes = new();

        // Locals: value is param 0
        int bufStart = 0; localTypes.Add(WasmI32); // scratch buffer start
        int bufPos = 1; localTypes.Add(WasmI32);   // current write position
        int isNeg = 2; localTypes.Add(WasmI32);    // is negative flag
        int digit = 3; localTypes.Add(WasmI32);    // temp digit
        int absVal = 4; localTypes.Add(WasmI64);   // absolute value

        // Allocate 24 bytes on heap for digit buffer
        body.WriteByte(OpGlobalGet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, bufStart + 1);

        body.WriteByte(OpGlobalGet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 24);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpGlobalSet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);

        // bufPos = bufStart + 23 (write right-to-left)
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, bufStart + 1);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 23);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, bufPos + 1);

        // Check if negative
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, 0); // param value
        body.WriteByte(OpI64Const); WriteSignedLeb128(body, 0);
        body.WriteByte(OpI64LtS);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, isNeg + 1);

        // absVal = if neg then -value else value
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, isNeg + 1);
        body.WriteByte(OpIf); body.WriteByte(BlockTypeVoid);
        body.WriteByte(OpI64Const); WriteSignedLeb128(body, 0);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, 0);
        body.WriteByte(OpI64Sub);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, absVal + 1);
        body.WriteByte(OpElse);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, 0);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, absVal + 1);
        body.WriteByte(OpEnd);

        // Handle zero case
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, absVal + 1);
        body.WriteByte(OpI64Eqz);
        body.WriteByte(OpIf); body.WriteByte(BlockTypeVoid);
        // Store '0' at bufPos
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, bufPos + 1);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 48); // '0'
        body.WriteByte(OpI32Store8); body.WriteByte(0x00); WriteUnsignedLeb128(body, 0); // i32.store8
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, bufPos + 1);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 1);
        body.WriteByte(OpI32Sub);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, bufPos + 1);
        body.WriteByte(OpEnd);
        body.WriteByte(OpElse);

        // Loop: while absVal > 0
        body.WriteByte(OpBlock); body.WriteByte(BlockTypeVoid);
        body.WriteByte(OpLoop); body.WriteByte(BlockTypeVoid);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, absVal + 1);
        body.WriteByte(OpI64Eqz);
        body.WriteByte(OpBrIf); WriteUnsignedLeb128(body, 1);

        // digit = (i32)(absVal % 10)
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, absVal + 1);
        body.WriteByte(OpI64Const); WriteSignedLeb128(body, 10);
        body.WriteByte((byte)0x81); // i64.rem_s
        body.WriteByte(OpI32WrapI64);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, digit + 1);

        // store '0' + digit at bufPos
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, bufPos + 1);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, digit + 1);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 48);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpI32Store8); body.WriteByte(0x00); WriteUnsignedLeb128(body, 0); // i32.store8

        // bufPos--
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, bufPos + 1);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 1);
        body.WriteByte(OpI32Sub);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, bufPos + 1);

        // absVal /= 10
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, absVal + 1);
        body.WriteByte(OpI64Const); WriteSignedLeb128(body, 10);
        body.WriteByte(OpI64DivS);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, absVal + 1);

        body.WriteByte(OpBr); WriteUnsignedLeb128(body, 0);
        body.WriteByte(OpEnd); // end loop
        body.WriteByte(OpEnd); // end block

        body.WriteByte(OpEnd); // end if/else (zero check)

        // If negative, store '-'
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, isNeg + 1);
        body.WriteByte(OpIf); body.WriteByte(BlockTypeVoid);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, bufPos + 1);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 45); // '-'
        body.WriteByte(OpI32Store8); body.WriteByte(0x00); WriteUnsignedLeb128(body, 0);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, bufPos + 1);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 1);
        body.WriteByte(OpI32Sub);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, bufPos + 1);
        body.WriteByte(OpEnd);

        // Now write from bufPos+1 to bufStart+23 via fd_write
        // iov.buf = bufPos + 1
        // iov.len = bufStart + 23 - bufPos
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 0); // iov scratch
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, bufPos + 1);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 1);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpI32Store); body.WriteByte(0x02); WriteUnsignedLeb128(body, 0);

        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 4);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, bufStart + 1);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 24);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, bufPos + 1);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 1);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpI32Sub);
        body.WriteByte(OpI32Store); body.WriteByte(0x02); WriteUnsignedLeb128(body, 0);

        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 1); // stdout
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 0); // iov
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 1); // 1 iov
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 8); // nwritten
        body.WriteByte(OpCall); WriteUnsignedLeb128(body, m_fdWriteIndex);
        body.WriteByte(OpDrop);

        // Write newline
        EmitWriteNewline(body, localTypes);

        body.WriteByte(OpEnd);
        m_functionBodies.Add(EncodeFunctionBody(body.ToArray(), localTypes));
    }

    void EmitPrintBoolHelper()
    {
        // __print_bool(value: i32) -> void
        int typeIndex = AddFuncType(new byte[] { WasmI32 }, Array.Empty<byte>());
        int funcSlot = m_printBoolIndex - m_importCount;
        m_functionTypeIndices[funcSlot] = typeIndex;

        int trueOffset = AddDataSegment(EncodeLengthPrefixedString("True"));
        int falseOffset = AddDataSegment(EncodeLengthPrefixedString("False"));

        MemoryStream body = new();
        List<byte> localTypes = new();

        int ptrLocal = 0; localTypes.Add(WasmI32);

        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, 0); // param
        body.WriteByte(OpIf); body.WriteByte(BlockTypeVoid);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, trueOffset);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, ptrLocal + 1);
        body.WriteByte(OpElse);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, falseOffset);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, ptrLocal + 1);
        body.WriteByte(OpEnd);

        EmitFdWriteFromLengthPrefixed(body, ptrLocal + 1);
        EmitWriteNewline(body, localTypes);

        body.WriteByte(OpEnd);
        m_functionBodies.Add(EncodeFunctionBody(body.ToArray(), localTypes));
    }

    void EmitStrEqHelper()
    {
        // __str_eq(ptrA: i32, ptrB: i32) -> i32
        // Returns 1 if length-prefixed strings are equal, 0 otherwise
        int typeIndex = AddFuncType(new byte[] { WasmI32, WasmI32 }, new byte[] { WasmI32 });
        int funcSlot = m_strEqIndex - m_importCount;
        m_functionTypeIndices[funcSlot] = typeIndex;

        MemoryStream body = new();
        List<byte> localTypes = new();

        // param 0 = ptrA, param 1 = ptrB
        // local 2 = lenA, local 3 = idx
        int lenA = 2; localTypes.Add(WasmI32);
        int idx = 3; localTypes.Add(WasmI32);

        // Fast path: same pointer → equal
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, 0);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, 1);
        body.WriteByte(OpI32Eq);
        body.WriteByte(OpIf); body.WriteByte(BlockTypeI32);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 1);
        body.WriteByte(OpReturn);
        body.WriteByte(OpElse);

        // Load lenA
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, 0);
        body.WriteByte(OpI32Load); body.WriteByte(0x02); WriteUnsignedLeb128(body, 0);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, lenA);

        // Compare lengths: if lenA != lenB → return 0
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, lenA);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, 1);
        body.WriteByte(OpI32Load); body.WriteByte(0x02); WriteUnsignedLeb128(body, 0);
        body.WriteByte(OpI32Ne);
        body.WriteByte(OpIf); body.WriteByte(BlockTypeI32);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 0);
        body.WriteByte(OpReturn);
        body.WriteByte(OpElse);

        // idx = 0
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 0);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, idx);

        // block { loop {
        body.WriteByte(OpBlock); body.WriteByte(BlockTypeVoid);
        body.WriteByte(OpLoop); body.WriteByte(BlockTypeVoid);

        // if idx >= lenA: break (strings are equal)
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, idx);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, lenA);
        body.WriteByte(OpI32GeS);
        body.WriteByte(OpBrIf); WriteUnsignedLeb128(body, 1);

        // Load ptrA[4+idx]
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, 0);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 4);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, idx);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpI32Load8U);
        body.WriteByte(0x00); WriteUnsignedLeb128(body, 0);

        // Load ptrB[4+idx]
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, 1);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 4);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, idx);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpI32Load8U);
        body.WriteByte(0x00); WriteUnsignedLeb128(body, 0);

        // If bytes differ → return 0
        body.WriteByte(OpI32Ne);
        body.WriteByte(OpIf); body.WriteByte(BlockTypeVoid);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 0);
        body.WriteByte(OpReturn);
        body.WriteByte(OpEnd);

        // idx++
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, idx);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 1);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, idx);

        // continue loop
        body.WriteByte(OpBr); WriteUnsignedLeb128(body, 0);

        body.WriteByte(OpEnd); // end loop
        body.WriteByte(OpEnd); // end block

        // Fell through loop → all bytes match → return 1
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 1);

        body.WriteByte(OpEnd); // end length-compare else
        body.WriteByte(OpEnd); // end pointer-compare else

        body.WriteByte(OpEnd); // end function
        m_functionBodies.Add(EncodeFunctionBody(body.ToArray(), localTypes));
    }

    void EmitI64ToString(MemoryStream body, ref int nextLocal, List<byte> localTypes)
    {
        // Simple approach: call __print_i64-like logic but write to heap and return pointer
        // For Phase 1, just use show on integers — we can optimize later
        // Write digits to heap, return length-prefixed pointer

        int valLocal = nextLocal++;
        localTypes.Add(WasmI64);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, valLocal);

        int bufStart = nextLocal++;
        localTypes.Add(WasmI32);
        int bufPos = nextLocal++;
        localTypes.Add(WasmI32);
        int absVal = nextLocal++;
        localTypes.Add(WasmI64);
        int isNeg = nextLocal++;
        localTypes.Add(WasmI32);
        int digitLocal = nextLocal++;
        localTypes.Add(WasmI32);
        int resultPtr = nextLocal++;
        localTypes.Add(WasmI32);
        int numLen = nextLocal++;
        localTypes.Add(WasmI32);

        // Allocate 24-byte scratch on heap
        body.WriteByte(OpGlobalGet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, bufStart);
        body.WriteByte(OpGlobalGet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 24);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpGlobalSet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);

        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, bufStart);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 23);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, bufPos);

        // isNeg
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, valLocal);
        body.WriteByte(OpI64Const); WriteSignedLeb128(body, 0);
        body.WriteByte(OpI64LtS);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, isNeg);

        // absVal
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, isNeg);
        body.WriteByte(OpIf); body.WriteByte(BlockTypeVoid);
        body.WriteByte(OpI64Const); WriteSignedLeb128(body, 0);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, valLocal);
        body.WriteByte(OpI64Sub);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, absVal);
        body.WriteByte(OpElse);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, valLocal);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, absVal);
        body.WriteByte(OpEnd);

        // Zero case
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, absVal);
        body.WriteByte(OpI64Eqz);
        body.WriteByte(OpIf); body.WriteByte(BlockTypeVoid);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, bufPos);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 48); // '0'
        body.WriteByte(OpI32Store8); body.WriteByte(0x00); WriteUnsignedLeb128(body, 0); // i32.store8
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, bufPos);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 1);
        body.WriteByte(OpI32Sub);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, bufPos);
        body.WriteByte(OpEnd);
        body.WriteByte(OpElse);

        // Digit loop
        body.WriteByte(OpBlock); body.WriteByte(BlockTypeVoid);
        body.WriteByte(OpLoop); body.WriteByte(BlockTypeVoid);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, absVal);
        body.WriteByte(OpI64Eqz);
        body.WriteByte(OpBrIf); WriteUnsignedLeb128(body, 1);

        // digit = (i32)(absVal % 10)
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, absVal);
        body.WriteByte(OpI64Const); WriteSignedLeb128(body, 10);
        body.WriteByte((byte)0x81); // i64.rem_s
        body.WriteByte(OpI32WrapI64);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, digitLocal);

        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, bufPos);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, digitLocal);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 48);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpI32Store8); body.WriteByte(0x00); WriteUnsignedLeb128(body, 0); // i32.store8

        // bufPos--
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, bufPos);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 1);
        body.WriteByte(OpI32Sub);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, bufPos);

        // absVal /= 10
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, absVal);
        body.WriteByte(OpI64Const); WriteSignedLeb128(body, 10);
        body.WriteByte(OpI64DivS);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, absVal);

        body.WriteByte(OpBr); WriteUnsignedLeb128(body, 0);
        body.WriteByte(OpEnd); // end loop
        body.WriteByte(OpEnd); // end block

        body.WriteByte(OpEnd); // end if/else (zero check)

        // If negative, store '-'
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, isNeg);
        body.WriteByte(OpIf); body.WriteByte(BlockTypeVoid);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, bufPos);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 45); // '-'
        body.WriteByte(OpI32Store8); body.WriteByte(0x00); WriteUnsignedLeb128(body, 0);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, bufPos);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 1);
        body.WriteByte(OpI32Sub);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, bufPos);
        body.WriteByte(OpEnd);

        // numLen = bufStart + 23 - bufPos
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, bufStart);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 23);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, bufPos);
        body.WriteByte(OpI32Sub);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, numLen);

        // Allocate length-prefixed result: 4 + numLen
        body.WriteByte(OpGlobalGet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, resultPtr);
        body.WriteByte(OpGlobalGet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, numLen);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 4);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpGlobalSet); WriteUnsignedLeb128(body, m_heapPtrGlobalIndex);

        // Store length
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, resultPtr);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, numLen);
        body.WriteByte(OpI32Store); body.WriteByte(0x02); WriteUnsignedLeb128(body, 0);

        // Copy digits from buf to result+4 (skip length prefix)
        int destLocal = nextLocal++;
        localTypes.Add(WasmI32);
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, resultPtr);
        body.WriteByte(OpI32Const); WriteSignedLeb128(body, 4);
        body.WriteByte(OpI32Add);
        body.WriteByte(OpLocalSet); WriteUnsignedLeb128(body, destLocal);

        EmitMemCopyDirect(body, destLocal, bufPos, 1, numLen, ref nextLocal, localTypes);

        // Return resultPtr
        body.WriteByte(OpLocalGet); WriteUnsignedLeb128(body, resultPtr);
    }

    // ── Encoding helpers ─────────────────────────────────────────

    static byte[] EncodeLengthPrefixedString(string value)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        byte[] result = new byte[4 + utf8.Length];
        BitConverter.TryWriteBytes(result.AsSpan(0, 4), utf8.Length);
        utf8.CopyTo(result, 4);
        return result;
    }

    int AddDataSegment(byte[] data)
    {
        int offset = m_dataOffset;
        m_dataSegments.Add(data);
        m_dataOffset += data.Length;
        // Update the global heap pointer initial value
        if (m_globals.Count > 0)
        {
            m_globals[0] = new WasmGlobal(WasmI32, GlobalMut, m_dataOffset);
        }
        return offset;
    }

    int AddFuncType(byte[] paramTypes, byte[] resultTypes)
    {
        // Check for existing identical type
        for (int i = 0; i < m_types.Count; i++)
        {
            if (m_types[i].Params.SequenceEqual(paramTypes) &&
                m_types[i].Results.SequenceEqual(resultTypes))
            {
                return i;
            }
        }
        m_types.Add(new WasmFuncType(paramTypes, resultTypes));
        return m_types.Count - 1;
    }

    static byte WasmTypeFor(CodexType type)
    {
        return type switch
        {
            IntegerType => WasmI64,
            NumberType => WasmF64,
            BooleanType => WasmI32,
            TextType => WasmI32,
            VoidType or NothingType => WasmI32,
            _ => WasmI32
        };
    }

    static byte WasmBlockTypeFor(CodexType type)
    {
        return type switch
        {
            IntegerType => BlockTypeI64,
            NumberType => BlockTypeF64,
            BooleanType => BlockTypeI32,
            TextType => BlockTypeI32,
            VoidType or NothingType => BlockTypeVoid,
            _ => BlockTypeI32
        };
    }

    static CodexType ComputeReturnType(CodexType type, int paramCount)
    {
        CodexType current = type;
        for (int i = 0; i < paramCount; i++)
        {
            if (current is FunctionType ft)
            {
                current = ft.Return;
            }
            else
            {
                break;
            }
        }
        return current;
    }

    static byte[] EncodeFunctionBody(byte[] code, List<byte> localTypes)
    {
        // Group consecutive identical local types
        List<(int Count, byte Type)> localGroups = new();
        for (int i = 0; i < localTypes.Count; i++)
        {
            if (localGroups.Count > 0 && localGroups[^1].Type == localTypes[i])
            {
                (int c, byte t) = localGroups[^1];
                localGroups[^1] = (c + 1, t);
            }
            else
            {
                localGroups.Add((1, localTypes[i]));
            }
        }

        MemoryStream bodyStream = new();
        WriteUnsignedLeb128(bodyStream, localGroups.Count);
        foreach ((int count, byte valType) in localGroups)
        {
            WriteUnsignedLeb128(bodyStream, count);
            bodyStream.WriteByte(valType);
        }
        bodyStream.Write(code, 0, code.Length);

        byte[] bodyBytes = bodyStream.ToArray();
        MemoryStream result = new();
        WriteUnsignedLeb128(result, bodyBytes.Length);
        result.Write(bodyBytes, 0, bodyBytes.Length);
        return result.ToArray();
    }

    // ── Section writers ──────────────────────────────────────────

    void WriteTypeSection(BinaryWriter w)
    {
        MemoryStream section = new();
        WriteUnsignedLeb128(section, m_types.Count);
        foreach (WasmFuncType ft in m_types)
        {
            section.WriteByte(0x60); // func type
            WriteUnsignedLeb128(section, ft.Params.Length);
            section.Write(ft.Params, 0, ft.Params.Length);
            WriteUnsignedLeb128(section, ft.Results.Length);
            section.Write(ft.Results, 0, ft.Results.Length);
        }
        WriteSection(w, SectionType, section.ToArray());
    }

    void WriteImportSection(BinaryWriter w)
    {
        if (m_imports.Count == 0)
        {
            return;
        }

        MemoryStream section = new();
        WriteUnsignedLeb128(section, m_imports.Count);
        foreach (WasmImport imp in m_imports)
        {
            WriteString(section, imp.Module);
            WriteString(section, imp.Name);
            section.WriteByte(imp.Kind);
            WriteUnsignedLeb128(section, imp.TypeIndex);
        }
        WriteSection(w, SectionImport, section.ToArray());
    }

    void WriteFunctionSection(BinaryWriter w)
    {
        MemoryStream section = new();
        WriteUnsignedLeb128(section, m_functionTypeIndices.Count);
        foreach (int typeIdx in m_functionTypeIndices)
        {
            WriteUnsignedLeb128(section, typeIdx);
        }
        WriteSection(w, SectionFunction, section.ToArray());
    }

    void WriteMemorySection(BinaryWriter w)
    {
        MemoryStream section = new();
        WriteUnsignedLeb128(section, 1); // 1 memory
        section.WriteByte(0x00); // no max
        WriteUnsignedLeb128(section, 1); // initial 1 page (64KB)
        WriteSection(w, SectionMemory, section.ToArray());
    }

    void WriteGlobalSection(BinaryWriter w)
    {
        if (m_globals.Count == 0)
        {
            return;
        }

        MemoryStream section = new();
        WriteUnsignedLeb128(section, m_globals.Count);
        foreach (WasmGlobal g in m_globals)
        {
            section.WriteByte(g.ValType);
            section.WriteByte(g.Mutability);
            // Init expression: i32.const <value> end
            section.WriteByte(OpI32Const);
            WriteSignedLeb128(section, g.InitValue);
            section.WriteByte(OpEnd);
        }
        WriteSection(w, SectionGlobal, section.ToArray());
    }

    void WriteExportSection(BinaryWriter w)
    {
        MemoryStream section = new();
        WriteUnsignedLeb128(section, m_exports.Count);
        foreach (WasmExport exp in m_exports)
        {
            WriteString(section, exp.Name);
            section.WriteByte(exp.Kind);
            WriteUnsignedLeb128(section, exp.Index);
        }
        WriteSection(w, SectionExport, section.ToArray());
    }

    void WriteCodeSection(BinaryWriter w)
    {
        MemoryStream section = new();
        WriteUnsignedLeb128(section, m_functionBodies.Count);
        foreach (byte[] body in m_functionBodies)
        {
            section.Write(body, 0, body.Length);
        }
        WriteSection(w, SectionCode, section.ToArray());
    }

    void WriteDataSection(BinaryWriter w)
    {
        if (m_dataSegments.Count == 0)
        {
            return;
        }

        MemoryStream section = new();
        WriteUnsignedLeb128(section, m_dataSegments.Count);
        int currentOffset = 1024;
        foreach (byte[] data in m_dataSegments)
        {
            section.WriteByte(0x00); // active segment, memory 0
            // Offset expression: i32.const <offset> end
            section.WriteByte(OpI32Const);
            WriteSignedLeb128(section, currentOffset);
            section.WriteByte(OpEnd);
            WriteUnsignedLeb128(section, data.Length);
            section.Write(data, 0, data.Length);
            currentOffset += data.Length;
        }
        WriteSection(w, SectionData, section.ToArray());
    }

    static void WriteSection(BinaryWriter w, byte sectionId, byte[] content)
    {
        w.Write(sectionId);
        MemoryStream lenStream = new();
        WriteUnsignedLeb128(lenStream, content.Length);
        w.Write(lenStream.ToArray());
        w.Write(content);
    }

    // ── LEB128 encoding ──────────────────────────────────────────

    static void WriteUnsignedLeb128(MemoryStream stream, int value)
    {
        uint v = (uint)value;
        do
        {
            byte b = (byte)(v & 0x7F);
            v >>= 7;
            if (v != 0)
            {
                b |= 0x80;
            }

            stream.WriteByte(b);
        } while (v != 0);
    }

    static void WriteSignedLeb128(MemoryStream stream, long value)
    {
        bool more = true;
        while (more)
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            if ((value == 0 && (b & 0x40) == 0) || (value == -1 && (b & 0x40) != 0))
            {
                more = false;
            }
            else
            {
                b |= 0x80;
            }

            stream.WriteByte(b);
        }
    }

    static void WriteString(MemoryStream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteUnsignedLeb128(stream, bytes.Length);
        stream.Write(bytes, 0, bytes.Length);
    }
}

sealed record WasmExport(string Name, byte Kind, int Index);

sealed record WasmGlobal(byte ValType, byte Mutability, int InitValue);
