using Codex.Core;
using Codex.IR;
using Codex.Types;

namespace Codex.Emit.Wasm;

sealed partial class WasmModuleBuilder
{
    // ── WASM Section IDs ─────────────────────────────────────────
    const byte SectionType = 1;
    const byte SectionImport = 2;
    const byte SectionFunction = 3;
    const byte SectionMemory = 5;
    const byte SectionGlobal = 6;
    const byte SectionExport = 7;
    const byte SectionStart = 8;
    const byte SectionCode = 10;
    const byte SectionData = 11;

    // ── WASM Value Types ─────────────────────────────────────────
    const byte WasmI32 = 0x7F;
    const byte WasmI64 = 0x7E;
    const byte WasmF64 = 0x7C;

    // ── WASM Op Codes ────────────────────────────────────────────
    const byte OpUnreachable = 0x00;
    const byte OpEnd = 0x0B;
    const byte OpReturn = 0x0F;
    const byte OpCall = 0x10;
    const byte OpDrop = 0x1A;
    const byte OpLocalGet = 0x20;
    const byte OpLocalSet = 0x21;
    const byte OpLocalTee = 0x22;
    const byte OpGlobalGet = 0x23;
    const byte OpGlobalSet = 0x24;
    const byte OpI32Load = 0x28;
    const byte OpI64Load = 0x29;
    const byte OpF64Load = 0x2B;
    const byte OpI32Load8U = 0x2D;
    const byte OpI32Store = 0x36;
    const byte OpI64Store = 0x37;
    const byte OpF64Store = 0x39;
    const byte OpI32Store8 = 0x3A;
    const byte OpI32Const = 0x41;
    const byte OpI64Const = 0x42;
    const byte OpF64Const = 0x44;
    const byte OpI32Eqz = 0x45;
    const byte OpI32Eq = 0x46;
    const byte OpI32Ne = 0x47;
    const byte OpI32LtS = 0x48;
    const byte OpI32GtS = 0x4A;
    const byte OpI32LeS = 0x4C;
    const byte OpI32GeS = 0x4E;
    const byte OpI64Eqz = 0x50;
    const byte OpI64Eq = 0x51;
    const byte OpI64Ne = 0x52;
    const byte OpI64LtS = 0x53;
    const byte OpI64GtS = 0x55;
    const byte OpI64LeS = 0x57;
    const byte OpI64GeS = 0x59;
    const byte OpF64Eq = 0x61;
    const byte OpF64Ne = 0x62;
    const byte OpF64Lt = 0x63;
    const byte OpF64Gt = 0x64;
    const byte OpF64Le = 0x65;
    const byte OpF64Ge = 0x66;
    const byte OpI32Add = 0x6A;
    const byte OpI32Sub = 0x6B;
    const byte OpI32Mul = 0x6C;
    const byte OpI64Add = 0x7C;
    const byte OpI64Sub = 0x7D;
    const byte OpI64Mul = 0x7E;
    const byte OpI64DivS = 0x7F;
    const byte OpF64Add = 0xA0;
    const byte OpF64Sub = 0xA1;
    const byte OpF64Mul = 0xA2;
    const byte OpF64Div = 0xA3;
    const byte OpF64Neg = 0x9A;
    const byte OpI64ExtendI32S = 0xAC;
    const byte OpI32WrapI64 = 0xA7;
    const byte OpF64ConvertI64S = 0xB9;

    // ── WASM Block/If opcodes ────────────────────────────────────
    const byte OpBlock = 0x02;
    const byte OpLoop = 0x03;
    const byte OpIf = 0x04;
    const byte OpElse = 0x05;
    const byte OpBr = 0x0C;
    const byte OpBrIf = 0x0D;
    const byte OpBrTable = 0x0E;

    // ── WASM block types ─────────────────────────────────────────
    const byte BlockTypeVoid = 0x40;
    const byte BlockTypeI32 = 0x7F;
    const byte BlockTypeI64 = 0x7E;
    const byte BlockTypeF64 = 0x7C;

    // ── Export kinds ─────────────────────────────────────────────
    const byte ExportFunc = 0x00;
    const byte ExportMemory = 0x02;

    // ── Import kinds ─────────────────────────────────────────────
    const byte ImportFunc = 0x00;

    // ── Global mutability ────────────────────────────────────────
    const byte GlobalConst = 0x00;
    const byte GlobalMut = 0x01;

    // ── Data ─────────────────────────────────────────────────────
    readonly List<byte[]> m_dataSegments = new();
    int m_dataOffset = 1024; // data starts at offset 1024, leaving room for iov structs

    readonly List<WasmFuncType> m_types = new();
    readonly List<WasmImport> m_imports = new();
    readonly List<int> m_functionTypeIndices = new();
    readonly List<byte[]> m_functionBodies = new();
    readonly List<WasmExport> m_exports = new();
    readonly List<WasmGlobal> m_globals = new();

    ValueMap<string, int> m_functionIndex = ValueMap<string, int>.s_empty;
    ValueMap<string, int> m_stringOffsets = ValueMap<string, int>.s_empty;
    ValueMap<string, int> m_stringLengths = ValueMap<string, int>.s_empty;

    int m_nextFuncIndex;
    int m_importCount;

    // ── WASI import function indices ─────────────────────────────
    int m_fdWriteIndex;

    // ── Heap pointer global index ────────────────────────────────
    int m_heapPtrGlobalIndex;

    // ── Region stack ──────────────────────────────────────────────
    const int RegionStackBase = 64;
    int m_regionSpGlobalIndex;

    // ── Runtime helper function indices ──────────────────────────
    int m_printI64Index;
    int m_printBoolIndex;
    int m_strEqIndex;

    public void EmitModule(IRModule module)
    {
        EmitImports();
        EmitRuntimeGlobals();
        PreRegisterFunctions(module);
        EmitRuntimeHelpers();

        foreach (IRDefinition def in module.Definitions)
        {
            EmitDefinition(def);
        }

        EmitStartFunction(module);
    }

    public byte[] Build()
    {
        MemoryStream ms = new();
        BinaryWriter w = new(ms);

        // Magic + version
        w.Write((byte)0x00); w.Write((byte)0x61); w.Write((byte)0x73); w.Write((byte)0x6D);
        w.Write((byte)0x01); w.Write((byte)0x00); w.Write((byte)0x00); w.Write((byte)0x00);

        WriteTypeSection(w);
        WriteImportSection(w);
        WriteFunctionSection(w);
        WriteMemorySection(w);
        WriteGlobalSection(w);
        WriteExportSection(w);
        WriteCodeSection(w);
        WriteDataSection(w);

        return ms.ToArray();
    }

    sealed record WasmFuncType(byte[] Params, byte[] Results);

    sealed record WasmImport(string Module, string Name, byte Kind, int TypeIndex);

    sealed record WasmExport(string Name, byte Kind, int Index);
}
