using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Codex.Core;
using Codex.IR;
using Codex.Types;

namespace Codex.Emit.IL;

sealed partial class ILAssemblyBuilder
{
    readonly string m_assemblyName;
    readonly MetadataBuilder m_metadata = new();
    readonly BlobBuilder m_ilStream = new();
    readonly MethodBodyStreamEncoder m_methodBodies;

    AssemblyReferenceHandle m_corlibRef;
    TypeReferenceHandle m_consoleRef;
    TypeReferenceHandle m_stringRef;
    TypeReferenceHandle m_objectRef;
    TypeReferenceHandle m_charRef;
    TypeReferenceHandle m_int64Ref;
    TypeReferenceHandle m_doubleRef;
    TypeReferenceHandle m_booleanRef;
    MemberReferenceHandle m_writeLineStringRef;
    MemberReferenceHandle m_writeLineInt64Ref;
    MemberReferenceHandle m_writeLineBoolRef;
    MemberReferenceHandle m_writeLineDoubleRef;
    MemberReferenceHandle m_stringConcatRef;
    MemberReferenceHandle m_int64ToStringRef;
    MemberReferenceHandle m_boolToStringRef;
    MemberReferenceHandle m_objectCtorRef;
    MemberReferenceHandle m_objectToStringRef;
    MemberReferenceHandle m_consoleReadLineRef;
    MemberReferenceHandle m_stringGetLengthRef;
    MemberReferenceHandle m_stringGetCharsRef;
    MemberReferenceHandle m_stringSubstringRef;
    MemberReferenceHandle m_stringReplaceRef;
    MemberReferenceHandle m_int64ParseRef;
    MemberReferenceHandle m_int64TryParseRef;
    MemberReferenceHandle m_doubleParseRef;
    MemberReferenceHandle m_charToStringRef;
    MemberReferenceHandle m_doubleToStringRef;
    MemberReferenceHandle m_cceEncodeRef;       // CceTable.Encode(string) → string
    MemberReferenceHandle m_cceDecodeRef;       // CceTable.Decode(string) → string
    MemberReferenceHandle m_cceUniToCceRef;     // CceTable.UnicharToCce(long) → long
    MemberReferenceHandle m_cceCceToUniRef;     // CceTable.CceToUnichar(long) → long
    MemberReferenceHandle m_cceEncodeListRef;   // CceTable.EncodeList(IEnumerable<string>) → List<string>
    MemberReferenceHandle m_stringEqualsRef;

    // ── List<T> support ──────────────────────────────────────────
    TypeReferenceHandle m_listOpenRef;              // System.Collections.Generic.List`1
    TypeReferenceHandle m_ienumerableOpenRef;        // System.Collections.Generic.IEnumerable`1
    // Per-element-type instantiations (TypeSpec + MemberRefs) are cached
    // in m_listCache — see ILAssemblyBuilder.Generics.cs

    // ── String.Split ─────────────────────────────────────────────
    MemberReferenceHandle m_stringSplitRef;           // String.Split(string, StringSplitOptions) : string[]

    // ── Environment ──────────────────────────────────────────────
    TypeReferenceHandle m_environmentRef;
    MemberReferenceHandle m_getCommandLineArgsRef;    // Environment.GetCommandLineArgs() : string[]

    // ── File I/O ─────────────────────────────────────────────────
    TypeReferenceHandle m_fileRef;
    MemberReferenceHandle m_fileReadAllTextRef;       // File.ReadAllText(string) : string
    MemberReferenceHandle m_fileExistsRef;            // File.Exists(string) : bool
    MemberReferenceHandle m_fileWriteAllTextRef;      // File.WriteAllText(string, string) : void

    // ── Process ───────────────────────────────────────────────────
    MemberReferenceHandle m_processStartRef;          // Process.Start(ProcessStartInfo) : Process
    MemberReferenceHandle m_processWaitForExitRef;    // Process.WaitForExit() : void
    MemberReferenceHandle m_processGetStdOutRef;      // Process.get_StandardOutput() : StreamReader
    MemberReferenceHandle m_streamReaderReadToEndRef; // StreamReader.ReadToEnd() : string
    MemberReferenceHandle m_psiCtorRef;               // ProcessStartInfo(string, string)
    MemberReferenceHandle m_psiSetRedirectRef;        // set_RedirectStandardOutput(bool)
    MemberReferenceHandle m_psiSetShellRef;           // set_UseShellExecute(bool)

    // ── Directory ───────────────────────────────────────────────
    TypeReferenceHandle m_directoryRef;
    MemberReferenceHandle m_directoryGetFilesRef;     // Directory.GetFiles(string, string) : string[]
    MemberReferenceHandle m_directoryGetCurrentRef;   // Directory.GetCurrentDirectory() : string

    // ── Additional String instance methods ──────────────────────
    MemberReferenceHandle m_stringContainsRef;        // String.Contains(string) : bool
    MemberReferenceHandle m_stringStartsWithRef;      // String.StartsWith(string) : bool

    // ── Additional Environment statics ──────────────────────────
    MemberReferenceHandle m_getEnvVarRef;             // Environment.GetEnvironmentVariable(string) : string

    TypeDefinitionHandle m_moduleClassDef;
    ValueMap<string, MethodDefinitionHandle> m_definedMethods = ValueMap<string, MethodDefinitionHandle>.s_empty;
    ValueMap<string, TypeDefinitionHandle> m_emittedTypes = ValueMap<string, TypeDefinitionHandle>.s_empty;
    ValueMap<string, MethodDefinitionHandle> m_ctorDefs = ValueMap<string, MethodDefinitionHandle>.s_empty;
    Map<string, List<(string Name, CodexType Type)>> m_typeFields = Map<string, List<(string Name, CodexType Type)>>.s_empty;
    ValueMap<string, FieldDefinitionHandle> m_fieldDefs = ValueMap<string, FieldDefinitionHandle>.s_empty;
    Map<string, string> m_ctorToBaseType = Map<string, string>.s_empty;

    ValueMap<string, int> m_definitionGenericArity = ValueMap<string, int>.s_empty;
    ValueMap<string, ImmutableArray<int>> m_definitionTypeVarIds = ValueMap<string, ImmutableArray<int>>.s_empty;
    ImmutableArray<int> m_currentTypeVarIds = ImmutableArray<int>.Empty;

    // ── Effect handler inlining context ───────────────────────────
    Map<string, IRDefinition> m_definitions = Map<string, IRDefinition>.s_empty;
    Map<string, IRHandleClause> m_activeHandlerClauses = Map<string, IRHandleClause>.s_empty;
    string? m_activeResumeName;

    public ILAssemblyBuilder(string assemblyName)
    {
        m_assemblyName = assemblyName;
        m_methodBodies = new MethodBodyStreamEncoder(m_ilStream);
        BuildCorlibReferences();
    }

    void BuildCorlibReferences()
    {
        m_corlibRef = m_metadata.AddAssemblyReference(
            m_metadata.GetOrAddString("System.Runtime"),
            new Version(8, 0, 0, 0),
            default,
            m_metadata.GetOrAddBlob(
                ImmutableArray.Create<byte>(
                    0xB0, 0x3F, 0x5F, 0x7F, 0x11, 0xD5, 0x0A, 0x3A)),
            default,
            default);

        AssemblyReferenceHandle consoleAsmRef = m_metadata.AddAssemblyReference(
            m_metadata.GetOrAddString("System.Console"),
            new Version(8, 0, 0, 0),
            default,
            m_metadata.GetOrAddBlob(
                ImmutableArray.Create<byte>(
                    0xB0, 0x3F, 0x5F, 0x7F, 0x11, 0xD5, 0x0A, 0x3A)),
            default,
            default);

        m_consoleRef = m_metadata.AddTypeReference(
            consoleAsmRef,
            m_metadata.GetOrAddString("System"),
            m_metadata.GetOrAddString("Console"));

        m_stringRef = m_metadata.AddTypeReference(
            m_corlibRef,
            m_metadata.GetOrAddString("System"),
            m_metadata.GetOrAddString("String"));

        m_objectRef = m_metadata.AddTypeReference(
            m_corlibRef,
            m_metadata.GetOrAddString("System"),
            m_metadata.GetOrAddString("Object"));

        // Type references for value types — must be before member references that use them
        m_charRef = m_metadata.AddTypeReference(
            m_corlibRef,
            m_metadata.GetOrAddString("System"),
            m_metadata.GetOrAddString("Char"));

        m_int64Ref = m_metadata.AddTypeReference(
            m_corlibRef,
            m_metadata.GetOrAddString("System"),
            m_metadata.GetOrAddString("Int64"));

        m_doubleRef = m_metadata.AddTypeReference(
            m_corlibRef,
            m_metadata.GetOrAddString("System"),
            m_metadata.GetOrAddString("Double"));

        m_booleanRef = m_metadata.AddTypeReference(
            m_corlibRef,
            m_metadata.GetOrAddString("System"),
            m_metadata.GetOrAddString("Boolean"));

        m_writeLineStringRef = m_metadata.AddMemberReference(
            m_consoleRef,
            m_metadata.GetOrAddString("WriteLine"),
            EncodeMethodSignature(SignatureCallingConvention.Default, true,
                returnType: b => b.Void(),
                parameters: new Action<ParameterTypeEncoder>[] { p => p.Type().String() }));

        m_writeLineInt64Ref = m_metadata.AddMemberReference(
            m_consoleRef,
            m_metadata.GetOrAddString("WriteLine"),
            EncodeMethodSignature(SignatureCallingConvention.Default, true,
                returnType: b => b.Void(),
                parameters: new Action<ParameterTypeEncoder>[] { p => p.Type().Int64() }));

        m_writeLineBoolRef = m_metadata.AddMemberReference(
            m_consoleRef,
            m_metadata.GetOrAddString("WriteLine"),
            EncodeMethodSignature(SignatureCallingConvention.Default, true,
                returnType: b => b.Void(),
                parameters: new Action<ParameterTypeEncoder>[] { p => p.Type().Boolean() }));

        m_stringConcatRef = m_metadata.AddMemberReference(
            m_stringRef,
            m_metadata.GetOrAddString("Concat"),
            EncodeMethodSignature(SignatureCallingConvention.Default, true,
                returnType: b => b.Type().String(),
                parameters: new Action<ParameterTypeEncoder>[]
                {
                    p => p.Type().String(),
                    p => p.Type().String()
                }));

        // String.op_Equality(string, string) : bool  (static)
        m_stringEqualsRef = m_metadata.AddMemberReference(
            m_stringRef,
            m_metadata.GetOrAddString("op_Equality"),
            EncodeMethodSignature(SignatureCallingConvention.Default, true,
                returnType: b => b.Type().Boolean(),
                parameters: new Action<ParameterTypeEncoder>[]
                {
                    p => p.Type().String(),
                    p => p.Type().String()
                }));

        m_int64ToStringRef = m_metadata.AddMemberReference(
            m_int64Ref,
            m_metadata.GetOrAddString("ToString"),
            EncodeMethodSignature(SignatureCallingConvention.Default, false,
                returnType: b => b.Type().String(),
                parameters: Array.Empty<Action<ParameterTypeEncoder>>()));

        m_boolToStringRef = m_metadata.AddMemberReference(
            m_booleanRef,
            m_metadata.GetOrAddString("ToString"),
            EncodeMethodSignature(SignatureCallingConvention.Default, false,
                returnType: b => b.Type().String(),
                parameters: Array.Empty<Action<ParameterTypeEncoder>>()));

        m_objectCtorRef = m_metadata.AddMemberReference(
            m_objectRef,
            m_metadata.GetOrAddString(".ctor"),
            EncodeCtorSignature(Array.Empty<Action<ParameterTypeEncoder>>()));

        m_objectToStringRef = m_metadata.AddMemberReference(
            m_objectRef,
            m_metadata.GetOrAddString("ToString"),
            EncodeMethodSignature(SignatureCallingConvention.Default, false,
                returnType: b => b.Type().String(),
                parameters: Array.Empty<Action<ParameterTypeEncoder>>()));

        // Console.ReadLine() : string
        m_consoleReadLineRef = m_metadata.AddMemberReference(
            m_consoleRef,
            m_metadata.GetOrAddString("ReadLine"),
            EncodeMethodSignature(SignatureCallingConvention.Default, true,
                returnType: b => b.Type().String(),
                parameters: Array.Empty<Action<ParameterTypeEncoder>>()));

        // Console.WriteLine(double)
        m_writeLineDoubleRef = m_metadata.AddMemberReference(
            m_consoleRef,
            m_metadata.GetOrAddString("WriteLine"),
            EncodeMethodSignature(SignatureCallingConvention.Default, true,
                returnType: b => b.Void(),
                parameters: new Action<ParameterTypeEncoder>[] { p => p.Type().Double() }));

        // String.get_Length() : int  (instance)
        m_stringGetLengthRef = m_metadata.AddMemberReference(
            m_stringRef,
            m_metadata.GetOrAddString("get_Length"),
            EncodeMethodSignature(SignatureCallingConvention.Default, false,
                returnType: b => b.Type().Int32(),
                parameters: Array.Empty<Action<ParameterTypeEncoder>>()));

        // String.get_Chars(int) : char  (instance)
        m_stringGetCharsRef = m_metadata.AddMemberReference(
            m_stringRef,
            m_metadata.GetOrAddString("get_Chars"),
            EncodeMethodSignature(SignatureCallingConvention.Default, false,
                returnType: b => b.Type().Char(),
                parameters: new Action<ParameterTypeEncoder>[] { p => p.Type().Int32() }));

        // String.Substring(int, int) : string  (instance)
        m_stringSubstringRef = m_metadata.AddMemberReference(
            m_stringRef,
            m_metadata.GetOrAddString("Substring"),
            EncodeMethodSignature(SignatureCallingConvention.Default, false,
                returnType: b => b.Type().String(),
                parameters: new Action<ParameterTypeEncoder>[]
                {
                    p => p.Type().Int32(),
                    p => p.Type().Int32()
                }));

        // String.Replace(string, string) : string  (instance)
        m_stringReplaceRef = m_metadata.AddMemberReference(
            m_stringRef,
            m_metadata.GetOrAddString("Replace"),
            EncodeMethodSignature(SignatureCallingConvention.Default, false,
                returnType: b => b.Type().String(),
                parameters: new Action<ParameterTypeEncoder>[]
                {
                    p => p.Type().String(),
                    p => p.Type().String()
                }));

        // Int64.Parse(string) : long  (static)
        m_int64ParseRef = m_metadata.AddMemberReference(
            m_int64Ref,
            m_metadata.GetOrAddString("Parse"),
            EncodeMethodSignature(SignatureCallingConvention.Default, true,
                returnType: b => b.Type().Int64(),
                parameters: new Action<ParameterTypeEncoder>[] { p => p.Type().String() }));

        // Int64.TryParse(string, out long) : bool  (static)
        {
            BlobBuilder sig = new();
            BlobEncoder encoder = new(sig);
            MethodSignatureEncoder methodSig = encoder.MethodSignature(SignatureCallingConvention.Default, 0, false);
            methodSig.Parameters(2,
                ret => ret.Type().Boolean(),
                p =>
                {
                    p.AddParameter().Type().String();
                    // out long = byref int64
                    SignatureTypeEncoder byRefEncoder = p.AddParameter().Type(isByRef: true);
                    byRefEncoder.Int64();
                });
            m_int64TryParseRef = m_metadata.AddMemberReference(
                m_int64Ref,
                m_metadata.GetOrAddString("TryParse"),
                m_metadata.GetOrAddBlob(sig));
        }

        // Double.Parse(string) : double  (static)
        m_doubleParseRef = m_metadata.AddMemberReference(
            m_doubleRef,
            m_metadata.GetOrAddString("Parse"),
            EncodeMethodSignature(SignatureCallingConvention.Default, true,
                returnType: b => b.Type().Double(),
                parameters: new Action<ParameterTypeEncoder>[] { p => p.Type().String() }));

        // Char.ToString() : string  (instance on char)
        m_charToStringRef = m_metadata.AddMemberReference(
            m_charRef,
            m_metadata.GetOrAddString("ToString"),
            EncodeMethodSignature(SignatureCallingConvention.Default, false,
                returnType: b => b.Type().String(),
                parameters: Array.Empty<Action<ParameterTypeEncoder>>()));

        // Double.ToString() : string  (instance on double)
        m_doubleToStringRef = m_metadata.AddMemberReference(
            m_doubleRef,
            m_metadata.GetOrAddString("ToString"),
            EncodeMethodSignature(SignatureCallingConvention.Default, false,
                returnType: b => b.Type().String(),
                parameters: Array.Empty<Action<ParameterTypeEncoder>>()));

        // ── List<T> support ──────────────────────────────────────
        AssemblyReferenceHandle collectionsAsmRef = m_metadata.AddAssemblyReference(
            m_metadata.GetOrAddString("System.Collections"),
            new Version(8, 0, 0, 0),
            default,
            m_metadata.GetOrAddBlob(
                ImmutableArray.Create<byte>(
                    0xB0, 0x3F, 0x5F, 0x7F, 0x11, 0xD5, 0x0A, 0x3A)),
            default,
            default);

        m_listOpenRef = m_metadata.AddTypeReference(
            collectionsAsmRef,
            m_metadata.GetOrAddString("System.Collections.Generic"),
            m_metadata.GetOrAddString("List`1"));

        m_ienumerableOpenRef = m_metadata.AddTypeReference(
            m_corlibRef,
            m_metadata.GetOrAddString("System.Collections.Generic"),
            m_metadata.GetOrAddString("IEnumerable`1"));

        // Build reusable List<T> signature blobs — per-element-type
        // instantiations are created on demand via GetOrCreateListInstantiation()
        InitializeListSignatureBlobs();

        // ── String.Split(string, StringSplitOptions) : string[] ──
        {
            // StringSplitOptions is an enum in System namespace — must be referenced as the type
            TypeReferenceHandle stringSplitOptionsRef = m_metadata.AddTypeReference(
                m_corlibRef,
                m_metadata.GetOrAddString("System"),
                m_metadata.GetOrAddString("StringSplitOptions"));

            BlobBuilder sig = new();
            new BlobEncoder(sig).MethodSignature(
                SignatureCallingConvention.Default, 0, isInstanceMethod: true)
                .Parameters(2,
                    returnType =>
                    {
                        returnType.Type().SZArray().String();
                    },
                    parameters =>
                    {
                        parameters.AddParameter().Type().String();
                        parameters.AddParameter().Type().Type(stringSplitOptionsRef, isValueType: true);
                    });
            m_stringSplitRef = m_metadata.AddMemberReference(
                m_stringRef,
                m_metadata.GetOrAddString("Split"),
                m_metadata.GetOrAddBlob(sig));
        }

        // ── Environment ──────────────────────────────────────────
        m_environmentRef = m_metadata.AddTypeReference(
            m_corlibRef,
            m_metadata.GetOrAddString("System"),
            m_metadata.GetOrAddString("Environment"));

        // Environment.GetCommandLineArgs() : string[]  (static)
        {
            BlobBuilder sig = new();
            new BlobEncoder(sig).MethodSignature(
                SignatureCallingConvention.Default, 0, isInstanceMethod: false)
                .Parameters(0,
                    returnType =>
                    {
                        returnType.Type().SZArray().String();
                    },
                    _ => { });
            m_getCommandLineArgsRef = m_metadata.AddMemberReference(
                m_environmentRef,
                m_metadata.GetOrAddString("GetCommandLineArgs"),
                m_metadata.GetOrAddBlob(sig));
        }

        // ── File I/O ─────────────────────────────────────────────
        AssemblyReferenceHandle fileSystemAsmRef = m_metadata.AddAssemblyReference(
            m_metadata.GetOrAddString("System.IO.FileSystem"),
            new Version(8, 0, 0, 0),
            default,
            m_metadata.GetOrAddBlob(
                ImmutableArray.Create<byte>(
                    0xB0, 0x3F, 0x5F, 0x7F, 0x11, 0xD5, 0x0A, 0x3A)),
            default,
            default);

        m_fileRef = m_metadata.AddTypeReference(
            fileSystemAsmRef,
            m_metadata.GetOrAddString("System.IO"),
            m_metadata.GetOrAddString("File"));

        // File.ReadAllText(string) : string  (static)
        m_fileReadAllTextRef = m_metadata.AddMemberReference(
            m_fileRef,
            m_metadata.GetOrAddString("ReadAllText"),
            EncodeMethodSignature(SignatureCallingConvention.Default, true,
                returnType: b => b.Type().String(),
                parameters: new Action<ParameterTypeEncoder>[] { p => p.Type().String() }));

        // File.Exists(string) : bool  (static)
        m_fileExistsRef = m_metadata.AddMemberReference(
            m_fileRef,
            m_metadata.GetOrAddString("Exists"),
            EncodeMethodSignature(SignatureCallingConvention.Default, true,
                returnType: b => b.Type().Boolean(),
                parameters: new Action<ParameterTypeEncoder>[] { p => p.Type().String() }));

        // File.WriteAllText(string, string) : void  (static)
        m_fileWriteAllTextRef = m_metadata.AddMemberReference(
            m_fileRef,
            m_metadata.GetOrAddString("WriteAllText"),
            EncodeMethodSignature(SignatureCallingConvention.Default, true,
                returnType: b => b.Void(),
                parameters: new Action<ParameterTypeEncoder>[]
                {
                    p => p.Type().String(),
                    p => p.Type().String()
                }));

        // ── Process support ───────────────────────────────────────
        AssemblyReferenceHandle processAsmRef = m_metadata.AddAssemblyReference(
            m_metadata.GetOrAddString("System.Diagnostics.Process"),
            new Version(8, 0, 0, 0),
            default,
            m_metadata.GetOrAddBlob(
                ImmutableArray.Create<byte>(
                    0xB0, 0x3F, 0x5F, 0x7F, 0x11, 0xD5, 0x0A, 0x3A)),
            default,
            default);

        TypeReferenceHandle processRef = m_metadata.AddTypeReference(
            processAsmRef,
            m_metadata.GetOrAddString("System.Diagnostics"),
            m_metadata.GetOrAddString("Process"));

        TypeReferenceHandle psiRef = m_metadata.AddTypeReference(
            processAsmRef,
            m_metadata.GetOrAddString("System.Diagnostics"),
            m_metadata.GetOrAddString("ProcessStartInfo"));

        TypeReferenceHandle streamReaderRef = m_metadata.AddTypeReference(
            m_corlibRef,
            m_metadata.GetOrAddString("System.IO"),
            m_metadata.GetOrAddString("StreamReader"));

        // ProcessStartInfo(string, string) ctor (instance)
        m_psiCtorRef = m_metadata.AddMemberReference(
            psiRef,
            m_metadata.GetOrAddString(".ctor"),
            EncodeMethodSignature(SignatureCallingConvention.Default, false,
                returnType: b => b.Void(),
                parameters: new Action<ParameterTypeEncoder>[]
                {
                    p => p.Type().String(),
                    p => p.Type().String()
                }));

        // ProcessStartInfo.set_RedirectStandardOutput(bool) (instance)
        m_psiSetRedirectRef = m_metadata.AddMemberReference(
            psiRef,
            m_metadata.GetOrAddString("set_RedirectStandardOutput"),
            EncodeMethodSignature(SignatureCallingConvention.Default, false,
                returnType: b => b.Void(),
                parameters: new Action<ParameterTypeEncoder>[] { p => p.Type().Boolean() }));

        // ProcessStartInfo.set_UseShellExecute(bool) (instance)
        m_psiSetShellRef = m_metadata.AddMemberReference(
            psiRef,
            m_metadata.GetOrAddString("set_UseShellExecute"),
            EncodeMethodSignature(SignatureCallingConvention.Default, false,
                returnType: b => b.Void(),
                parameters: new Action<ParameterTypeEncoder>[] { p => p.Type().Boolean() }));

        // Process.Start(ProcessStartInfo) : Process (static)
        {
            BlobBuilder sig = new();
            new BlobEncoder(sig).MethodSignature(
                SignatureCallingConvention.Default, 0, isInstanceMethod: false)
                .Parameters(1,
                    returnType => returnType.Type().Type(processRef, isValueType: false),
                    parameters => parameters.AddParameter().Type().Type(psiRef, isValueType: false));
            m_processStartRef = m_metadata.AddMemberReference(
                processRef,
                m_metadata.GetOrAddString("Start"),
                m_metadata.GetOrAddBlob(sig));
        }

        // Process.get_StandardOutput() : StreamReader (instance)
        {
            BlobBuilder sig = new();
            new BlobEncoder(sig).MethodSignature(
                SignatureCallingConvention.Default, 0, isInstanceMethod: true)
                .Parameters(0,
                    returnType => returnType.Type().Type(streamReaderRef, isValueType: false),
                    parameters => { });
            m_processGetStdOutRef = m_metadata.AddMemberReference(
                processRef,
                m_metadata.GetOrAddString("get_StandardOutput"),
                m_metadata.GetOrAddBlob(sig));
        }

        // Process.WaitForExit() : void (instance)
        m_processWaitForExitRef = m_metadata.AddMemberReference(
            processRef,
            m_metadata.GetOrAddString("WaitForExit"),
            EncodeMethodSignature(SignatureCallingConvention.Default, false,
                returnType: b => b.Void(),
                parameters: Array.Empty<Action<ParameterTypeEncoder>>()));

        // StreamReader.ReadToEnd() : string (instance)
        m_streamReaderReadToEndRef = m_metadata.AddMemberReference(
            streamReaderRef,
            m_metadata.GetOrAddString("ReadToEnd"),
            EncodeMethodSignature(SignatureCallingConvention.Default, false,
                returnType: b => b.Type().String(),
                parameters: Array.Empty<Action<ParameterTypeEncoder>>()));

        // ── Additional String instance methods ──────────────────
        // String.Contains(string, StringComparison) : bool (instance)
        // Must use the ordinal-aware overload — strings are CCE-encoded byte
        // sequences, and the default Contains/StartsWith are culture-sensitive,
        // which collapses control-char prefixes (CCE's letter range is 13-64).
        TypeReferenceHandle stringComparisonRef = m_metadata.AddTypeReference(
            m_corlibRef,
            m_metadata.GetOrAddString("System"),
            m_metadata.GetOrAddString("StringComparison"));
        m_stringContainsRef = m_metadata.AddMemberReference(
            m_stringRef,
            m_metadata.GetOrAddString("Contains"),
            EncodeMethodSignature(SignatureCallingConvention.Default, false,
                returnType: b => b.Type().Boolean(),
                parameters: new Action<ParameterTypeEncoder>[]
                {
                    p => p.Type().String(),
                    p => p.Type().Type(stringComparisonRef, isValueType: true),
                }));

        // String.StartsWith(string, StringComparison) : bool (instance)
        m_stringStartsWithRef = m_metadata.AddMemberReference(
            m_stringRef,
            m_metadata.GetOrAddString("StartsWith"),
            EncodeMethodSignature(SignatureCallingConvention.Default, false,
                returnType: b => b.Type().Boolean(),
                parameters: new Action<ParameterTypeEncoder>[]
                {
                    p => p.Type().String(),
                    p => p.Type().Type(stringComparisonRef, isValueType: true),
                }));

        // ── Environment.GetEnvironmentVariable ──────────────────
        m_getEnvVarRef = m_metadata.AddMemberReference(
            m_environmentRef,
            m_metadata.GetOrAddString("GetEnvironmentVariable"),
            EncodeMethodSignature(SignatureCallingConvention.Default, true,
                returnType: b => b.Type().String(),
                parameters: new Action<ParameterTypeEncoder>[] { p => p.Type().String() }));

        // ── Directory (System.IO) ───────────────────────────────
        m_directoryRef = m_metadata.AddTypeReference(
            fileSystemAsmRef,
            m_metadata.GetOrAddString("System.IO"),
            m_metadata.GetOrAddString("Directory"));

        // Directory.GetCurrentDirectory() : string (static)
        m_directoryGetCurrentRef = m_metadata.AddMemberReference(
            m_directoryRef,
            m_metadata.GetOrAddString("GetCurrentDirectory"),
            EncodeMethodSignature(SignatureCallingConvention.Default, true,
                returnType: b => b.Type().String(),
                parameters: Array.Empty<Action<ParameterTypeEncoder>>()));

        // Directory.GetFiles(string, string) : string[] (static)
        {
            BlobBuilder sig = new();
            new BlobEncoder(sig).MethodSignature(
                SignatureCallingConvention.Default, 0, isInstanceMethod: false)
                .Parameters(2,
                    returnType => returnType.Type().SZArray().String(),
                    parameters =>
                    {
                        parameters.AddParameter().Type().String();
                        parameters.AddParameter().Type().String();
                    });
            m_directoryGetFilesRef = m_metadata.AddMemberReference(
                m_directoryRef,
                m_metadata.GetOrAddString("GetFiles"),
                m_metadata.GetOrAddBlob(sig));
        }

        // ── Codex.Core.CceTable (for CCE ↔ Unicode conversion at I/O boundaries) ──
        AssemblyReferenceHandle codexCoreAsmRef = m_metadata.AddAssemblyReference(
            m_metadata.GetOrAddString("Codex.Core"),
            new Version(1, 0, 0, 0),
            default, default, default, default);

        TypeReferenceHandle cceTableRef = m_metadata.AddTypeReference(
            codexCoreAsmRef,
            m_metadata.GetOrAddString("Codex.Core"),
            m_metadata.GetOrAddString("CceTable"));

        m_cceEncodeRef = m_metadata.AddMemberReference(
            cceTableRef,
            m_metadata.GetOrAddString("Encode"),
            EncodeMethodSignature(SignatureCallingConvention.Default, true,
                returnType: b => b.Type().String(),
                parameters: new Action<ParameterTypeEncoder>[] { p => p.Type().String() }));

        m_cceDecodeRef = m_metadata.AddMemberReference(
            cceTableRef,
            m_metadata.GetOrAddString("Decode"),
            EncodeMethodSignature(SignatureCallingConvention.Default, true,
                returnType: b => b.Type().String(),
                parameters: new Action<ParameterTypeEncoder>[] { p => p.Type().String() }));

        m_cceUniToCceRef = m_metadata.AddMemberReference(
            cceTableRef,
            m_metadata.GetOrAddString("UnicharToCce"),
            EncodeMethodSignature(SignatureCallingConvention.Default, true,
                returnType: b => b.Type().Int64(),
                parameters: new Action<ParameterTypeEncoder>[] { p => p.Type().Int64() }));

        m_cceCceToUniRef = m_metadata.AddMemberReference(
            cceTableRef,
            m_metadata.GetOrAddString("CceToUnichar"),
            EncodeMethodSignature(SignatureCallingConvention.Default, true,
                returnType: b => b.Type().Int64(),
                parameters: new Action<ParameterTypeEncoder>[] { p => p.Type().Int64() }));

        // CceTable.EncodeList(string[]) → List<string>
        {
            BlobBuilder sig = new();
            new BlobEncoder(sig)
                .MethodSignature(SignatureCallingConvention.Default, 0, isInstanceMethod: false)
                .Parameters(1,
                    returnType =>
                    {
                        GenericTypeArgumentsEncoder genArgs = returnType.Type()
                            .GenericInstantiation(m_listOpenRef, 1, isValueType: false);
                        genArgs.AddArgument().String();
                    },
                    parameters =>
                    {
                        parameters.AddParameter().Type().SZArray().String();
                    });
            m_cceEncodeListRef = m_metadata.AddMemberReference(
                cceTableRef,
                m_metadata.GetOrAddString("EncodeList"),
                m_metadata.GetOrAddBlob(sig));
        }
    }

    public void EmitModule(IRChapter module)
    {
        m_metadata.AddModule(
            0,
            m_metadata.GetOrAddString(m_assemblyName + ".dll"),
            m_metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);

        m_metadata.AddAssembly(
            m_metadata.GetOrAddString(m_assemblyName),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            AssemblyHashAlgorithm.None);

        // <Module> must be the first type (row 1). It has no methods or fields of its own.
        m_metadata.AddTypeDefinition(
            default,
            default,
            m_metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        EmitTypeDefinitions(module);

        // The static "Program" class holds all user-defined methods and the entry point.
        // It must be defined after type definitions so its MethodList points past the ctors.
        int firstMethodRow = m_metadata.GetRowCount(TableIndex.MethodDef) + 1;
        int firstFieldRow = m_metadata.GetRowCount(TableIndex.Field) + 1;
        m_moduleClassDef = m_metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            m_metadata.GetOrAddString(""),
            m_metadata.GetOrAddString("Program"),
            m_objectRef,
            MetadataTokens.FieldDefinitionHandle(firstFieldRow),
            MetadataTokens.MethodDefinitionHandle(firstMethodRow));

        // Pre-collect generic arities for all definitions
        foreach (IRDefinition def in module.Definitions)
        {
            ImmutableArray<int> typeVarIds = CollectTypeVarIds(def.Type);
            m_definitionGenericArity = m_definitionGenericArity.Set(def.Name, typeVarIds.Length);
            m_definitionTypeVarIds = m_definitionTypeVarIds.Set(def.Name, typeVarIds);
        }

        // Pre-register method handles so recursive/forward calls resolve correctly.
        int methodRow = firstMethodRow;
        foreach (IRDefinition def in module.Definitions)
        {
            m_definedMethods = m_definedMethods.Set(def.Name, MetadataTokens.MethodDefinitionHandle(methodRow));
            methodRow++;
        }
        // Reserve a row for the synthetic Main entry point.
        m_definedMethods = m_definedMethods.Set("__entryMain", MetadataTokens.MethodDefinitionHandle(methodRow));

        // Store definitions for handler inlining (must be before EmitDefinition calls).
        foreach (IRDefinition def in module.Definitions)
        {
            m_definitions = m_definitions.Set(def.Name, def);
        }

        foreach (IRDefinition def in module.Definitions)
        {
            EmitDefinition(def);
        }

        EmitEntryPoint(module);
    }

    void EmitDefinition(IRDefinition def)
    {
        ImmutableArray<int> typeVarIds = ImmutableArray<int>.Empty;
        m_definitionTypeVarIds.TryGet(def.Name, out typeVarIds);
        if (typeVarIds.IsDefault)
        {
            typeVarIds = ImmutableArray<int>.Empty;
        }

        m_currentTypeVarIds = typeVarIds;
        int genericArity = typeVarIds.Length;

        BlobBuilder sig = new();
        BlobEncoder sigEncoder = new(sig);

        int paramCount = def.Parameters.Length;
        CodexType returnType = ComputeReturnType(def.Type, paramCount);
        MethodSignatureEncoder methodSig = sigEncoder.MethodSignature(
            SignatureCallingConvention.Default, genericArity, false);
        methodSig.Parameters(paramCount,
            rt => EncodeType(rt.Type(), returnType),
            parameters =>
            {
                foreach (IRParameter param in def.Parameters)
                {
                    EncodeType(parameters.AddParameter().Type(), param.Type);
                }
            });

        bool isTco = HasSelfTailCall(def);

        ControlFlowBuilder controlFlow = new();
        InstructionEncoder il = new(new BlobBuilder(), controlFlow);
        LocalsBuilder locals = new(m_metadata, EncodeType);

        if (isTco)
        {
            EmitTailCallBody(il, def, locals);
        }
        else
        {
            EmitExpr(il, def.Body, locals, def.Parameters);
            il.OpCode(ILOpCode.Ret);
        }

        // Scale maxStack with function complexity.  The evaluation stack depth
        // depends on both local count (let bindings) and expression nesting
        // (e.g. a 30-segment string concat chain requires ~30 stack slots).
        int exprDepth = EstimateStackDepth(def.Body);
        int maxStack = Math.Max(16, Math.Max(locals.Count, exprDepth) + 16);

        int bodyOffset;
        if (locals.Count > 0)
        {
            StandaloneSignatureHandle localSig = locals.BuildSignature();
            bodyOffset = m_methodBodies.AddMethodBody(il, maxStack: maxStack, localVariablesSignature: localSig);
        }
        else
        {
            bodyOffset = m_methodBodies.AddMethodBody(il, maxStack: maxStack);
        }

        MethodDefinitionHandle methodDef = m_metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL | MethodImplAttributes.Managed,
            m_metadata.GetOrAddString(SanitizeName(def.Name)),
            m_metadata.GetOrAddBlob(sig),
            bodyOffset,
            default);

        for (int i = 0; i < def.Parameters.Length; i++)
        {
            m_metadata.AddParameter(
                ParameterAttributes.None,
                m_metadata.GetOrAddString(SanitizeName(def.Parameters[i].Name)),
                i + 1);
        }

        for (int i = 0; i < genericArity; i++)
        {
            m_metadata.AddGenericParameter(
                methodDef,
                GenericParameterAttributes.None,
                m_metadata.GetOrAddString($"T{typeVarIds[i]}"),
                i);
        }

        m_currentTypeVarIds = ImmutableArray<int>.Empty;
    }

    void EmitEntryPoint(IRChapter module)
    {
        IRDefinition? mainDef = null;
        foreach (IRDefinition d in module.Definitions)
        {
            if (d.Name == "main" && d.Parameters.Length == 0)
            {
                mainDef = d;
                break;
            }
        }

        if (mainDef is null)
        {
            return;
        }

        ControlFlowBuilder entryControlFlow = new();
        InstructionEncoder il = new(new BlobBuilder(), entryControlFlow);

        // Unwrap effect annotations: main's runtime value has the inner type.
        // VoidType/NothingType both lower to an IL void return (see EncodeType),
        // so the call leaves the stack empty — just Ret. Otherwise we print.
        CodexType innerType = mainDef.Type is EffectfulType eff ? eff.Return : mainDef.Type;

        il.Call(m_definedMethods["main"]!.Value);

        switch (innerType)
        {
            case VoidType:
            case NothingType:
                break;
            case TextType:
                il.Call(m_cceDecodeRef);
                il.Call(m_writeLineStringRef);
                break;
            default:
                MemberReferenceHandle writeLine = innerType switch
                {
                    IntegerType or CharType => m_writeLineInt64Ref,
                    NumberType => m_writeLineDoubleRef,
                    BooleanType => m_writeLineBoolRef,
                    _ => m_writeLineStringRef,
                };
                il.Call(writeLine);
                break;
        }

        il.OpCode(ILOpCode.Ret);

        int bodyOffset = m_methodBodies.AddMethodBody(il);

        BlobBuilder sig = new();
        BlobEncoder sigEncoder = new(sig);
        sigEncoder.MethodSignature().Parameters(0,
            returnType => returnType.Void(),
            _ => { });

        m_metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL | MethodImplAttributes.Managed,
            m_metadata.GetOrAddString("Main"),
            m_metadata.GetOrAddBlob(sig),
            bodyOffset,
            default);
    }

    void EmitExpr(InstructionEncoder il, IRExpr expr, LocalsBuilder locals, ImmutableArray<IRParameter> parameters)
    {
        switch (expr)
        {
            case IRTextLit t:
                il.LoadString(m_metadata.GetOrAddUserString(CceTable.Encode(t.Value)));
                break;

            case IRCharLit c:
                il.LoadConstantI8(CceTable.UnicharToCce(c.Value));
                break;

            case IRIntegerLit i:
                il.LoadConstantI8(i.Value);
                break;

            case IRNumberLit n:
                il.LoadConstantR8((double)n.Value);
                break;

            case IRBoolLit b:
                il.LoadConstantI4(b.Value ? 1 : 0);
                break;

            case IRNegate neg:
                EmitExpr(il, neg.Operand, locals, parameters);
                il.OpCode(ILOpCode.Neg);
                break;

            case IRName name:
                int paramIndex = FindParameter(name.Name, parameters);
                if (paramIndex >= 0)
                {
                    il.LoadArgument(paramIndex);
                }
                else if (locals.TryGetLocal(name.Name, out int localIndex))
                {
                    il.LoadLocal(localIndex);
                }
                else if (m_activeHandlerClauses.TryGet(name.Name, out IRHandleClause? zeroArgClause)
                    && zeroArgClause.Parameters.Length == 0)
                {
                    // Zero-arg effect operation (e.g. `ask`): inline the handler clause body.
                    EmitHandlerClauseInline(il, zeroArgClause, ImmutableArray<IRExpr>.Empty, locals, parameters);
                }
                else if (TryEmitBuiltin(il, name.Name, new List<IRExpr>(), locals, parameters))
                {
                    // Zero-arg builtin handled (read-line, get-args, etc.)
                }
                else if (m_ctorDefs.TryGet(name.Name, out MethodDefinitionHandle ctorDef)
                    && name.Type is not FunctionType)
                {
                    il.OpCode(ILOpCode.Newobj);
                    il.Token(ctorDef);
                }
                else if (m_definedMethods.TryGet(name.Name, out MethodDefinitionHandle methodRef))
                {
                    EmitCallToMethod(il, name.Name, methodRef, ImmutableArray<IRExpr>.Empty);
                }
                break;

            case IRBinary bin:
                EmitBinary(il, bin, locals, parameters);
                break;

            case IRIf ifExpr:
                EmitIf(il, ifExpr, locals, parameters);
                break;

            case IRLet letExpr:
                EmitLet(il, letExpr, locals, parameters);
                break;

            case IRApply apply:
                EmitApply(il, apply, locals, parameters);
                break;

            case IRDo doExpr:
                EmitDo(il, doExpr, locals, parameters);
                break;

            case IRRecord rec:
                EmitRecordConstruction(il, rec, locals, parameters);
                break;

            case IRFieldAccess fa:
                EmitFieldAccess(il, fa, locals, parameters);
                break;

            case IRRegion region:
                EmitExpr(il, region.Body, locals, parameters);
                break;

            case IRMatch match:
                EmitMatch(il, match, locals, parameters);
                break;

            case IRList list:
                EmitListLiteral(il, list, locals, parameters);
                break;

            case IRGetState:
                if (locals.TryGetLocal("__state", out int getStateIdx))
                {
                    il.LoadLocal(getStateIdx);
                }

                break;

            case IRSetState setState:
                EmitExpr(il, setState.NewValue, locals, parameters);
                if (locals.TryGetLocal("__state", out int setStateIdx))
                {
                    il.StoreLocal(setStateIdx);
                }

                break;

            case IRRunState runState:
                EmitRunState(il, runState, locals, parameters);
                break;

            case IRHandle handle:
                EmitHandle(il, handle, locals, parameters);
                break;
        }
    }

    void EmitBinary(InstructionEncoder il, IRBinary bin, LocalsBuilder locals,
        ImmutableArray<IRParameter> parameters)
    {
        if (bin.Op == IRBinaryOp.AppendText)
        {
            EmitExpr(il, bin.Left, locals, parameters);
            EmitExpr(il, bin.Right, locals, parameters);
            il.Call(m_stringConcatRef);
            return;
        }

        EmitExpr(il, bin.Left, locals, parameters);
        EmitExpr(il, bin.Right, locals, parameters);

        switch (bin.Op)
        {
            case IRBinaryOp.AddInt or IRBinaryOp.AddNum:
                il.OpCode(ILOpCode.Add);
                break;
            case IRBinaryOp.SubInt or IRBinaryOp.SubNum:
                il.OpCode(ILOpCode.Sub);
                break;
            case IRBinaryOp.MulInt or IRBinaryOp.MulNum:
                il.OpCode(ILOpCode.Mul);
                break;
            case IRBinaryOp.DivInt or IRBinaryOp.DivNum:
                il.OpCode(ILOpCode.Div);
                break;
            case IRBinaryOp.Eq:
                if (IsTextLike(bin.Left.Type))
                {
                    il.Call(m_stringEqualsRef);
                }
                else
                {
                    il.OpCode(ILOpCode.Ceq);
                }

                break;
            case IRBinaryOp.NotEq:
                if (IsTextLike(bin.Left.Type))
                {
                    il.Call(m_stringEqualsRef);
                    il.LoadConstantI4(0);
                    il.OpCode(ILOpCode.Ceq);
                }
                else
                {
                    il.OpCode(ILOpCode.Ceq);
                    il.LoadConstantI4(0);
                    il.OpCode(ILOpCode.Ceq);
                }
                break;
            case IRBinaryOp.Lt:
                il.OpCode(ILOpCode.Clt);
                break;
            case IRBinaryOp.Gt:
                il.OpCode(ILOpCode.Cgt);
                break;
            case IRBinaryOp.LtEq:
                il.OpCode(ILOpCode.Cgt);
                il.LoadConstantI4(0);
                il.OpCode(ILOpCode.Ceq);
                break;
            case IRBinaryOp.GtEq:
                il.OpCode(ILOpCode.Clt);
                il.LoadConstantI4(0);
                il.OpCode(ILOpCode.Ceq);
                break;
            case IRBinaryOp.And:
                il.OpCode(ILOpCode.And);
                break;
            case IRBinaryOp.Or:
                il.OpCode(ILOpCode.Or);
                break;
        }
    }

    void EmitIf(InstructionEncoder il, IRIf ifExpr, LocalsBuilder locals,
        ImmutableArray<IRParameter> parameters)
    {
        LabelHandle elseLabel = il.DefineLabel();
        LabelHandle endLabel = il.DefineLabel();

        EmitExpr(il, ifExpr.Condition, locals, parameters);
        il.Branch(ILOpCode.Brfalse, elseLabel);

        EmitExpr(il, ifExpr.Then, locals, parameters);
        il.Branch(ILOpCode.Br, endLabel);

        il.MarkLabel(elseLabel);
        EmitExpr(il, ifExpr.Else, locals, parameters);

        il.MarkLabel(endLabel);
    }

    void EmitLet(InstructionEncoder il, IRLet letExpr, LocalsBuilder locals,
        ImmutableArray<IRParameter> parameters)
    {
        EmitExpr(il, letExpr.Value, locals, parameters);
        int localIndex = locals.AddLocal(letExpr.Name, letExpr.Value.Type);
        il.StoreLocal(localIndex);
        EmitExpr(il, letExpr.Body, locals, parameters);
    }

    void EmitApply(InstructionEncoder il, IRApply apply, LocalsBuilder locals,
        ImmutableArray<IRParameter> parameters)
    {
        List<IRExpr> args = new();
        IRExpr func = apply;
        while (func is IRApply inner)
        {
            args.Add(inner.Argument);
            func = inner.Function;
        }
        args.Reverse();

        if (func is IRName funcName)
        {
            // Resume interception: `resume x` in a handler clause just produces x.
            if (m_activeResumeName is not null && funcName.Name == m_activeResumeName
                && args.Count >= 1)
            {
                EmitExpr(il, args[args.Count - 1], locals, parameters);
                return;
            }

            // Effect operation interception: inline the handler clause body.
            if (m_activeHandlerClauses.TryGet(funcName.Name, out IRHandleClause? opClause)
                && args.Count >= opClause.Parameters.Length)
            {
                ImmutableArray<IRExpr> clauseArgs = args.Take(opClause.Parameters.Length)
                    .ToImmutableArray();
                EmitHandlerClauseInline(il, opClause, clauseArgs, locals, parameters);
                return;
            }

            if (TryEmitBuiltin(il, funcName.Name, args, locals, parameters))
            {
                return;
            }

            if (m_ctorDefs.TryGet(funcName.Name, out MethodDefinitionHandle ctorDef))
            {
                string ctorSanitized = SanitizeName(funcName.Name);
                List<(string Name, CodexType Type)>? fieldTypes = m_typeFields[ctorSanitized];
                for (int ai = 0; ai < args.Count; ai++)
                {
                    EmitExpr(il, args[ai], locals, parameters);
                    if (fieldTypes is not null && ai < fieldTypes.Count)
                    {
                        EmitBoxIfNeeded(il, args[ai].Type, fieldTypes[ai].Type);
                    }
                }
                il.OpCode(ILOpCode.Newobj);
                il.Token(ctorDef);
                return;
            }

            if (m_definedMethods.TryGet(funcName.Name, out MethodDefinitionHandle methodDef))
            {
                foreach (IRExpr arg in args)
                {
                    EmitExpr(il, arg, locals, parameters);
                }
                ImmutableArray<IRExpr> argArray = args.ToImmutableArray();
                EmitCallToMethod(il, funcName.Name, methodDef, argArray);
            }
        }
    }

    bool TryEmitBuiltin(InstructionEncoder il, string name, List<IRExpr> args,
        LocalsBuilder locals, ImmutableArray<IRParameter> parameters)
    {
        return TryEmitBuiltinCore(il, name, args, locals,
            expr => EmitExpr(il, expr, locals, parameters));
    }

    bool TryEmitBuiltinCore(InstructionEncoder il, string name, List<IRExpr> args,
        LocalsBuilder locals, Action<IRExpr> emitSub)
    {
        switch (name)
        {
            case "print-line" when args.Count == 1:
                emitSub(args[0]);
                // Choose the right overload based on the argument type.
                // Text is stored as CCE internally — decode to Unicode before writing.
                if (args[0].Type is TextType)
                {
                    il.Call(m_cceDecodeRef);
                    il.Call(m_writeLineStringRef);
                }
                else
                {
                    MemberReferenceHandle writeLine = args[0].Type switch
                    {
                        IntegerType or CharType => m_writeLineInt64Ref,
                        NumberType => m_writeLineDoubleRef,
                        BooleanType => m_writeLineBoolRef,
                        _ => m_writeLineStringRef,
                    };
                    il.Call(writeLine);
                }
                return true;

            case "show" when args.Count == 1:
                emitSub(args[0]); EmitValueToString(il, locals, args[0].Type);
                return true;

            case "negate" when args.Count == 1:
                emitSub(args[0]);
                il.OpCode(ILOpCode.Neg);
                return true;

            case "text-length" when args.Count == 1:
                emitSub(args[0]);
                il.Call(m_stringGetLengthRef);
                il.OpCode(ILOpCode.Conv_i8);
                return true;

            case "char-at" when args.Count == 2:
                // (long)s[(int)i] — returns Char as i64, zero allocation
                emitSub(args[0]);
                emitSub(args[1]);
                il.OpCode(ILOpCode.Conv_i4);
                il.Call(m_stringGetCharsRef);
                il.OpCode(ILOpCode.Conv_i8);
                return true;

            case "char-to-text" when args.Count == 1:
                // ((char)val).ToString()
                emitSub(args[0]);
                il.OpCode(ILOpCode.Conv_u2);
                EmitCharToString(il, locals);
                return true;

            case "char-code-at" when args.Count == 2:
                // (long)s[(int)i]
                emitSub(args[0]);
                emitSub(args[1]);
                il.OpCode(ILOpCode.Conv_i4);
                il.Call(m_stringGetCharsRef);
                il.OpCode(ILOpCode.Conv_i8);
                return true;

            case "char-code" when args.Count == 1:
                // Char -> Integer: identity (both are i64)
                emitSub(args[0]);
                return true;

            case "code-to-char" when args.Count == 1:
                // Integer -> Char: identity (both are i64)
                emitSub(args[0]);
                return true;

            case "substring" when args.Count == 3:
                // s.Substring((int)start, (int)len)
                emitSub(args[0]);
                emitSub(args[1]);
                il.OpCode(ILOpCode.Conv_i4);
                emitSub(args[2]);
                il.OpCode(ILOpCode.Conv_i4);
                il.Call(m_stringSubstringRef);
                return true;

            case "text-replace" when args.Count == 3:
                // s.Replace(old, new)
                emitSub(args[0]);
                emitSub(args[1]);
                emitSub(args[2]);
                il.Call(m_stringReplaceRef);
                return true;

            case "text-to-integer" when args.Count == 1:
            {
                // Safe parse: Int64.TryParse(_Cce.Decode(s), out result) ? result : 0L
                emitSub(args[0]);
                il.Call(m_cceDecodeRef); // CCE → Unicode for Int64.TryParse
                int tmpResult = locals.AddLocal("__tti_result", IntegerType.s_instance);
                il.OpCode(ILOpCode.Ldloca_s);
                il.CodeBuilder.WriteByte((byte)tmpResult);
                il.Call(m_int64TryParseRef);
                LabelHandle successLabel = il.DefineLabel();
                LabelHandle endLabel = il.DefineLabel();
                il.Branch(ILOpCode.Brtrue_s, successLabel);
                il.LoadConstantI8(0);
                il.Branch(ILOpCode.Br_s, endLabel);
                il.MarkLabel(successLabel);
                il.LoadLocal(tmpResult);
                il.MarkLabel(endLabel);
                return true;
            }

            case "integer-to-text" when args.Count == 1:
                emitSub(args[0]);
                EmitValueToString(il, locals, args[0].Type);
                return true;

            case "is-letter" when args.Count == 1:
                // CCE: letters are 13-64 inclusive. (val - 13) u< 52 covers [13, 64].
                emitSub(args[0]);
                il.LoadConstantI8(13);
                il.OpCode(ILOpCode.Sub);
                il.LoadConstantI8(52);
                il.OpCode(ILOpCode.Clt_un);
                return true;

            case "is-digit" when args.Count == 1:
                // CCE: digits are 3-12 inclusive. (val - 3) u< 10 covers [3, 12].
                emitSub(args[0]);
                il.LoadConstantI8(3);
                il.OpCode(ILOpCode.Sub);
                il.LoadConstantI8(10);
                il.OpCode(ILOpCode.Clt_un);
                return true;

            case "is-whitespace" when args.Count == 1:
                // CCE: whitespace is 0-2 inclusive. val u< 3 covers [0, 2].
                emitSub(args[0]);
                il.LoadConstantI8(3);
                il.OpCode(ILOpCode.Clt_un);
                return true;

            case "read-line" when args.Count == 0:
                // Console.ReadLine() returns Unicode; encode to CCE.
                il.Call(m_consoleReadLineRef);
                il.Call(m_cceEncodeRef);
                return true;

            // ── List<T> builtins ─────────────────────────────────
            case "get-args" when args.Count == 0:
            {
                // CceTable.EncodeList(Environment.GetCommandLineArgs()) — CCE-encoded List<string>.
                il.Call(m_getCommandLineArgsRef);
                il.Call(m_cceEncodeListRef);
                return true;
            }

            case "text-split" when args.Count == 2:
            {
                // new List<string>(text.Split(delim, StringSplitOptions.None))
                ListInstantiation inst = GetOrCreateListInstantiation(TextType.s_instance);
                emitSub(args[0]);               // push text
                emitSub(args[1]);               // push delimiter
                il.LoadConstantI4(0);           // StringSplitOptions.None
                il.Call(m_stringSplitRef);       // → string[]
                il.OpCode(ILOpCode.Newobj);
                il.Token(inst.CtorIEnumerable); // → List<string>
                return true;
            }

            case "list-length" when args.Count == 1:
            {
                // (long)list.Count — works for any List<T>
                CodexType elemType = ExtractListElementType(args, 0);
                ListInstantiation inst = GetOrCreateListInstantiation(elemType);
                emitSub(args[0]);
                il.OpCode(ILOpCode.Callvirt);
                il.Token(inst.GetCount);
                il.OpCode(ILOpCode.Conv_i8);
                return true;
            }

            case "list-at" when args.Count == 2:
            {
                // list[(int)index] — returns element of the actual type
                CodexType elemType = ExtractListElementType(args, 0);
                ListInstantiation inst = GetOrCreateListInstantiation(elemType);
                emitSub(args[0]);
                emitSub(args[1]);
                il.OpCode(ILOpCode.Conv_i4);
                il.OpCode(ILOpCode.Callvirt);
                il.Token(inst.GetItem);
                return true;
            }

            // ── File I/O builtins ────────────────────────────────
            // Paths and content are CCE-encoded internally — decode to Unicode
            // before calling .NET I/O; encode results back to CCE.
            case "read-file" when args.Count == 1:
                emitSub(args[0]);
                il.Call(m_cceDecodeRef);     // CCE path → Unicode
                il.Call(m_fileReadAllTextRef);
                il.Call(m_cceEncodeRef);     // Unicode content → CCE
                return true;

            case "file-exists" when args.Count == 1:
                emitSub(args[0]);
                il.Call(m_cceDecodeRef);     // CCE path → Unicode
                il.Call(m_fileExistsRef);
                return true;

            case "write-file" when args.Count == 2:
                emitSub(args[0]);
                il.Call(m_cceDecodeRef);     // CCE path → Unicode
                emitSub(args[1]);
                il.Call(m_cceDecodeRef);     // CCE content → Unicode
                il.Call(m_fileWriteAllTextRef);
                // File.WriteAllText is void — leave nothing on the stack. Do-bind
                // sites bind write-file's Nothing result via IRDoBind, which skips
                // StoreLocal for void-like NameType (see EmitDo).
                return true;

            // ── Process builtins ────────────────────────────────
            case "run-process" when args.Count == 2:
                // var psi = new ProcessStartInfo(program, arguments); — args are CCE
                emitSub(args[0]);
                il.Call(m_cceDecodeRef);     // CCE → Unicode for OS call
                emitSub(args[1]);
                il.Call(m_cceDecodeRef);     // CCE → Unicode for OS call
                il.OpCode(ILOpCode.Newobj);
                il.Token(m_psiCtorRef);

                // psi.RedirectStandardOutput = true;
                il.OpCode(ILOpCode.Dup);
                il.LoadConstantI4(1);
                il.OpCode(ILOpCode.Callvirt);
                il.Token(m_psiSetRedirectRef);

                // psi.UseShellExecute = false;
                il.OpCode(ILOpCode.Dup);
                il.LoadConstantI4(0);
                il.OpCode(ILOpCode.Callvirt);
                il.Token(m_psiSetShellRef);

                // var proc = Process.Start(psi);
                il.Call(m_processStartRef);

                // var output = proc.StandardOutput.ReadToEnd();
                il.OpCode(ILOpCode.Dup);
                il.OpCode(ILOpCode.Callvirt);
                il.Token(m_processGetStdOutRef);
                il.OpCode(ILOpCode.Callvirt);
                il.Token(m_streamReaderReadToEndRef);

                // stash output, call WaitForExit, load output, encode Unicode → CCE
                {
                    int tmpOut = locals.AddLocal("__proc_out", TextType.s_instance);
                    il.StoreLocal(tmpOut);
                    il.OpCode(ILOpCode.Callvirt);
                    il.Token(m_processWaitForExitRef);
                    il.LoadLocal(tmpOut);
                    il.Call(m_cceEncodeRef);
                }
                return true;

            // ── Additional text builtins ─────────────────────────
            case "text-contains" when args.Count == 2:
                // text.Contains(substring, StringComparison.Ordinal) : bool
                emitSub(args[0]);
                emitSub(args[1]);
                il.LoadConstantI4(4); // StringComparison.Ordinal
                il.OpCode(ILOpCode.Callvirt);
                il.Token(m_stringContainsRef);
                return true;

            case "text-starts-with" when args.Count == 2:
                // text.StartsWith(prefix, StringComparison.Ordinal) : bool
                emitSub(args[0]);
                emitSub(args[1]);
                il.LoadConstantI4(4); // StringComparison.Ordinal
                il.OpCode(ILOpCode.Callvirt);
                il.Token(m_stringStartsWithRef);
                return true;

            // ── Environment / Directory builtins ─────────────────
            case "get-env" when args.Count == 1:
                // Environment.GetEnvironmentVariable(name) : string? — decode name, encode result.
                // Null result from a missing env var would NRE downstream; substitute "" like the C# emitter does.
                emitSub(args[0]);
                il.Call(m_cceDecodeRef);
                il.Call(m_getEnvVarRef);
                {
                    int tmpEnv = locals.AddLocal("__env_val", TextType.s_instance);
                    il.StoreLocal(tmpEnv);
                    il.LoadLocal(tmpEnv);
                    LabelHandle nonNull = il.DefineLabel();
                    il.Branch(ILOpCode.Brtrue_s, nonNull);
                    il.LoadString(m_metadata.GetOrAddUserString(""));
                    LabelHandle done = il.DefineLabel();
                    il.Branch(ILOpCode.Br_s, done);
                    il.MarkLabel(nonNull);
                    il.LoadLocal(tmpEnv);
                    il.MarkLabel(done);
                }
                il.Call(m_cceEncodeRef);
                return true;

            case "current-dir" when args.Count == 0:
                // Directory.GetCurrentDirectory() returns Unicode; encode to CCE.
                il.Call(m_directoryGetCurrentRef);
                il.Call(m_cceEncodeRef);
                return true;

            case "list-files" when args.Count == 2:
            {
                // CceTable.EncodeList(Directory.GetFiles(Decode(path), Decode(pattern)))
                emitSub(args[0]);
                il.Call(m_cceDecodeRef);
                emitSub(args[1]);
                il.Call(m_cceDecodeRef);
                il.Call(m_directoryGetFilesRef);
                il.Call(m_cceEncodeListRef);
                return true;
            }

            default:
                return false;
        }
    }

    void EmitValueToString(InstructionEncoder il, LocalsBuilder locals, CodexType type)
    {
        switch (type)
        {
            case IntegerType:
            {
                // Store to local, ldloca, call Int64::ToString()
                int tmp = locals.AddLocal("__show_i64", type);
                il.StoreLocal(tmp);
                il.OpCode(ILOpCode.Ldloca_s);
                il.CodeBuilder.WriteByte((byte)tmp);
                il.Call(m_int64ToStringRef);
                break;
            }
            case NumberType:
            {
                int tmp = locals.AddLocal("__show_f64", type);
                il.StoreLocal(tmp);
                il.OpCode(ILOpCode.Ldloca_s);
                il.CodeBuilder.WriteByte((byte)tmp);
                il.Call(m_doubleToStringRef);
                break;
            }
            case BooleanType:
            {
                int tmp = locals.AddLocal("__show_bool", type);
                il.StoreLocal(tmp);
                il.OpCode(ILOpCode.Ldloca_s);
                il.CodeBuilder.WriteByte((byte)tmp);
                il.Call(m_boolToStringRef);
                break;
            }
            case CharType:
                // Convert i64 -> char -> string
                il.OpCode(ILOpCode.Conv_u2);
                EmitCharToString(il, locals);
                break;
            case TextType:
                // Already a CCE-encoded string, nothing to do
                return;
            default:
                // Reference type — just callvirt Object.ToString()
                il.OpCode(ILOpCode.Callvirt);
                il.Token(m_objectToStringRef);
                break;
        }
        // .NET ToString produces Unicode; encode to CCE to match the C# emitter.
        il.Call(m_cceEncodeRef);
    }

    void EmitCharToString(InstructionEncoder il, LocalsBuilder locals)
    {
        // Box the char value and call Object.ToString()
        il.OpCode(ILOpCode.Box);
        il.Token(m_charRef);
        il.Call(m_objectToStringRef);
    }

    void EmitCallToMethod(InstructionEncoder il, string name,
        MethodDefinitionHandle methodDef, ImmutableArray<IRExpr> args)
    {
        if (!m_definitionGenericArity.TryGet(name, out int genericArity) || genericArity == 0)
        {
            il.Call(methodDef);
            return;
        }

        // Build a MethodSpec with instantiated type arguments.
        // For erased generics, all type args become object.
        BlobBuilder specSig = new();
        BlobEncoder specEncoder = new(specSig);
        GenericTypeArgumentsEncoder genSig = specEncoder.MethodSpecificationSignature(genericArity);
        for (int i = 0; i < genericArity; i++)
        {
            genSig.AddArgument().Object();
        }

        MethodSpecificationHandle methodSpec = m_metadata.AddMethodSpecification(
            methodDef,
            m_metadata.GetOrAddBlob(specSig));
        il.Call(methodSpec);
    }

    void EmitDo(InstructionEncoder il, IRDo doExpr, LocalsBuilder locals,
        ImmutableArray<IRParameter> parameters)
    {
        for (int i = 0; i < doExpr.Statements.Length; i++)
        {
            IRDoStatement stmt = doExpr.Statements[i];
            switch (stmt)
            {
                case IRDoBind bind:
                    EmitExpr(il, bind.Value, locals, parameters);
                    // Void-like binds (e.g. `_ <- write-file ...`) produce no
                    // IL value — skip StoreLocal rather than storing from empty stack.
                    if (!IsVoidLike(bind.NameType))
                    {
                        int localIndex = locals.AddLocal(bind.Name, bind.NameType);
                        il.StoreLocal(localIndex);
                    }
                    break;
                case IRDoExec exec:
                    EmitExpr(il, exec.Expression, locals, parameters);
                    bool isLast = i == doExpr.Statements.Length - 1;
                    if (!isLast && !IsVoidLike(exec.Expression.Type))
                    {
                        il.OpCode(ILOpCode.Pop);
                    }
                    break;
            }
        }
    }

    void EmitRunState(InstructionEncoder il, IRRunState runState, LocalsBuilder locals,
        ImmutableArray<IRParameter> parameters)
    {
        // Evaluate the initial state and store in a __state local.
        EmitExpr(il, runState.InitialState, locals, parameters);
        int stateLocal = locals.AddLocal("__state", runState.StateType);
        il.StoreLocal(stateLocal);

        // Emit the computation.  If it is a do-block we inline the statements
        // directly so that IRGetState / IRSetState resolve the __state local.
        if (runState.Computation is IRDo doExpr)
        {
            for (int i = 0; i < doExpr.Statements.Length; i++)
            {
                IRDoStatement stmt = doExpr.Statements[i];
                switch (stmt)
                {
                    case IRDoBind bind:
                        EmitExpr(il, bind.Value, locals, parameters);
                        // The lowering may assign ErrorType to binds of get-state
                        // because the do-block is lowered without state-type context.
                        // Use the run-state's StateType when the bind type is unusable.
                        CodexType bindType = bind.NameType is ErrorType
                            ? runState.StateType
                            : bind.NameType;
                        int bindLocal = locals.AddLocal(bind.Name, bindType);
                        il.StoreLocal(bindLocal);
                        break;
                    case IRDoExec exec:
                        EmitExpr(il, exec.Expression, locals, parameters);
                        bool isLast = i == doExpr.Statements.Length - 1;
                        if (!isLast && !IsVoidLike(exec.Expression.Type))
                        {
                            il.OpCode(ILOpCode.Pop);
                        }
                        break;
                }
            }
        }
        else
        {
            // Single-expression computation (e.g. `run-state 42 get-state`).
            EmitExpr(il, runState.Computation, locals, parameters);
        }
    }

    void EmitHandle(InstructionEncoder il, IRHandle handle, LocalsBuilder locals,
        ImmutableArray<IRParameter> parameters)
    {
        // Build a map from operation name to handler clause.
        Map<string, IRHandleClause> clauseMap = Map<string, IRHandleClause>.s_empty;
        foreach (IRHandleClause clause in handle.Clauses)
        {
            clauseMap = clauseMap.Set(clause.OperationName, clause);
        }

        // Save and install the handler context so that EmitExpr intercepts
        // effect operation calls and resume invocations within the computation.
        Map<string, IRHandleClause> savedClauses = m_activeHandlerClauses;
        string? savedResume = m_activeResumeName;
        m_activeHandlerClauses = clauseMap;

        // Resolve the computation body.  If it is a reference to a zero-param
        // definition, inline that definition's body so operations are intercepted
        // at emit time (a plain method call would not be rewritten).
        IRExpr computation = handle.Computation;
        if (computation is IRName nameRef
            && m_definitions.TryGet(nameRef.Name, out IRDefinition? def)
            && def.Parameters.Length == 0)
        {
            computation = def.Body;
        }

        EmitExpr(il, computation, locals, parameters);

        // Restore previous handler context (supports nesting).
        m_activeHandlerClauses = savedClauses;
        m_activeResumeName = savedResume;
    }

    void EmitHandlerClauseInline(InstructionEncoder il, IRHandleClause clause,
        ImmutableArray<IRExpr> args, LocalsBuilder locals, ImmutableArray<IRParameter> parameters)
    {
        // Bind operation parameters as locals.
        for (int i = 0; i < clause.Parameters.Length && i < args.Length; i++)
        {
            EmitExpr(il, args[i], locals, parameters);
            int paramLocal = locals.AddLocal(clause.Parameters[i], clause.ParameterTypes[i]);
            il.StoreLocal(paramLocal);
        }

        // Install resume name so `resume x` in the clause body is intercepted.
        string? savedResume = m_activeResumeName;
        m_activeResumeName = clause.ResumeName;

        EmitExpr(il, clause.Body, locals, parameters);

        m_activeResumeName = savedResume;
    }

    public byte[] Build()
    {
        BlobBuilder peBlob = new();

        MethodDefinitionHandle entryPoint = m_definedMethods.TryGet("__entryMain", out MethodDefinitionHandle ep)
            ? ep
            : MetadataTokens.MethodDefinitionHandle(m_metadata.GetRowCount(TableIndex.MethodDef));

        ManagedPEBuilder peBuilder = new(
            header: new PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage),
            metadataRootBuilder: new MetadataRootBuilder(m_metadata),
            ilStream: m_ilStream,
            entryPoint: entryPoint);

        peBuilder.Serialize(peBlob);
        return peBlob.ToArray();
    }

    void EncodeType(SignatureTypeEncoder encoder, CodexType type)
    {
        switch (type)
        {
            case IntegerType:
                encoder.Int64();
                break;
            case NumberType:
                encoder.Double();
                break;
            case TextType:
                encoder.String();
                break;
            case BooleanType:
                encoder.Boolean();
                break;
            case CharType:
                encoder.Int64();
                break;
            case VoidType or NothingType:
                encoder.Builder.WriteByte((byte)SignatureTypeCode.Void);
                break;
            case RecordType rec:
                EncodeUserType(encoder, SanitizeName(rec.TypeName.Value));
                break;
            case SumType sum:
                EncodeUserType(encoder, SanitizeName(sum.TypeName.Value));
                break;
            case ConstructedType ct:
                EncodeUserType(encoder, SanitizeName(ct.Constructor.Value));
                break;
            case TypeVariable tv:
                int mvarIndex = m_currentTypeVarIds.IndexOf(tv.Id);
                if (mvarIndex >= 0)
                {
                    encoder.Builder.WriteByte((byte)SignatureTypeCode.GenericMethodParameter);
                    encoder.Builder.WriteCompressedInteger(mvarIndex);
                }
                else
                {
                    encoder.Object();
                }
                break;
            case ForAllType fa:
                EncodeType(encoder, fa.Body);
                break;
            case ListType lt:
                // Encode as generic instantiation: List<elementType>
                EncodeType(
                    encoder.GenericInstantiation(m_listOpenRef, 1, isValueType: false)
                        .AddArgument(),
                    lt.Element);
                break;
            default:
                encoder.Object();
                break;
        }
    }

    void EncodeUserType(SignatureTypeEncoder encoder, string typeName)
    {
        if (m_emittedTypes.TryGet(typeName, out TypeDefinitionHandle handle))
        {
            encoder.Type(handle, false);
        }
        else
        {
            encoder.Object();
        }
    }

    BlobHandle EncodeMethodSignature(
        SignatureCallingConvention convention,
        bool isStatic,
        Action<ReturnTypeEncoder> returnType,
        Action<ParameterTypeEncoder>[] parameters)
    {
        BlobBuilder sig = new();
        BlobEncoder encoder = new(sig);
        MethodSignatureEncoder methodSig = encoder.MethodSignature(convention, 0, !isStatic);
        methodSig.Parameters(parameters.Length,
            returnType,
            p =>
            {
                foreach (Action<ParameterTypeEncoder> param in parameters)
                {
                    ParameterTypeEncoder paramEncoder = p.AddParameter();
                    param(paramEncoder);
                }
            });
        return m_metadata.GetOrAddBlob(sig);
    }

    static int FindParameter(string name, ImmutableArray<IRParameter> parameters)
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].Name == name)
            {
                return i;
            }
        }
        return -1;
    }

    static string SanitizeName(string name) => name.Replace('-', '_').Replace('.', '_');

    static bool IsVoidLike(CodexType type) => type is VoidType or NothingType
        or EffectfulType { Return: VoidType or NothingType };

    static bool IsTextLike(CodexType type) => type is TextType
        or EffectfulType { Return: TextType };

    void EmitBoxIfNeeded(InstructionEncoder il, CodexType actualType, CodexType expectedType)
    {
        if (expectedType is not (TypeVariable or ForAllType))
        {
            return;
        }

        TypeReferenceHandle? boxTarget = actualType switch
        {
            IntegerType or CharType => m_int64Ref,
            NumberType => m_doubleRef,
            BooleanType => m_booleanRef,
            _ => null
        };
        if (boxTarget is not null)
        {
            il.OpCode(ILOpCode.Box);
            il.Token(boxTarget.Value);
        }
    }

    void EmitUnboxIfNeeded(InstructionEncoder il, CodexType storedType, CodexType targetType)
    {
        if (storedType is not (TypeVariable or ForAllType))
        {
            return;
        }

        TypeReferenceHandle? unboxTarget = targetType switch
        {
            IntegerType or CharType => m_int64Ref,
            NumberType => m_doubleRef,
            BooleanType => m_booleanRef,
            _ => null
        };
        if (unboxTarget is not null)
        {
            il.OpCode(ILOpCode.Unbox_any);
            il.Token(unboxTarget.Value);
        }
        else if (targetType is not TypeVariable and not ForAllType
            and not TextType)
        {
            // Reference type stored as object — cast down
            string? typeName = targetType switch
            {
                RecordType rec => SanitizeName(rec.TypeName.Value),
                SumType sum => SanitizeName(sum.TypeName.Value),
                ConstructedType ct => SanitizeName(ct.Constructor.Value),
                _ => null
            };
            if (typeName is not null && m_emittedTypes.TryGet(typeName, out TypeDefinitionHandle typeDef))
            {
                il.OpCode(ILOpCode.Castclass);
                il.Token(typeDef);
            }
        }
    }

    static CodexType ComputeReturnType(CodexType fullType, int parameterCount)
    {
        CodexType current = fullType;
        // Unwrap ForAllType wrappers
        while (current is ForAllType fa)
        {
            current = fa.Body;
        }

        for (int i = 0; i < parameterCount; i++)
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
        if (current is EffectfulType eft)
        {
            current = eft.Return;
        }

        return current;
    }

    // ── Generics helpers ──────────────────────────────────────────

    static ImmutableArray<int> CollectTypeVarIds(CodexType type)
    {
        HashSet<int> ids = [];
        CollectTypeVarIdsInto(type, ids);
        return ids.Order().ToImmutableArray();
    }

    static void CollectTypeVarIdsInto(CodexType type, HashSet<int> ids)
    {
        switch (type)
        {
            case TypeVariable tv:
                ids.Add(tv.Id);
                break;
            case FunctionType ft:
                CollectTypeVarIdsInto(ft.Parameter, ids);
                CollectTypeVarIdsInto(ft.Return, ids);
                break;
            case ListType lt:
                CollectTypeVarIdsInto(lt.Element, ids);
                break;
            case ForAllType fa:
                CollectTypeVarIdsInto(fa.Body, ids);
                break;
            case ConstructedType ct:
                foreach (CodexType arg in ct.Arguments)
                {
                    CollectTypeVarIdsInto(arg, ids);
                }

                break;
        }
    }

    // ── Tail-call optimization ────────────────────────────────────

    static bool HasSelfTailCall(IRDefinition def)
    {
        if (def.Parameters.Length == 0)
        {
            return false;
        }

        return ExprHasTailCall(def.Body, def.Name);
    }

    static bool ExprHasTailCall(IRExpr expr, string funcName)
    {
        return expr switch
        {
            IRIf iff => ExprHasTailCall(iff.Then, funcName)
                     || ExprHasTailCall(iff.Else, funcName),
            IRLet let => ExprHasTailCall(let.Body, funcName),
            IRMatch match => match.Branches.Any(b => ExprHasTailCall(b.Body, funcName)),
            IRApply app => IsSelfCall(app, funcName),
            IRRegion region => ExprHasTailCall(region.Body, funcName),
            _ => false
        };
    }

    static bool IsSelfCall(IRApply app, string funcName)
    {
        IRExpr root = app.Function;
        while (root is IRApply inner)
        {
            root = inner.Function;
        }

        return root is IRName name && name.Name == funcName;
    }

    void EmitTailCallBody(InstructionEncoder il, IRDefinition def, LocalsBuilder locals)
    {
        // Allocate locals that mirror each parameter so we can reassign them in the loop
        int[] paramLocals = new int[def.Parameters.Length];
        for (int i = 0; i < def.Parameters.Length; i++)
        {
            paramLocals[i] = locals.AddLocal($"__tco_p{i}", def.Parameters[i].Type);
            il.LoadArgument(i);
            il.StoreLocal(paramLocals[i]);
        }

        // Build a modified parameter list that reads from locals instead of args
        ImmutableArray<IRParameter> tcoParams = def.Parameters;

        LabelHandle loopStart = il.DefineLabel();
        il.MarkLabel(loopStart);

        EmitTailCallExpr(il, def.Body, def.Name, tcoParams, paramLocals, locals, loopStart);
    }

    void EmitTailCallExpr(InstructionEncoder il, IRExpr expr, string funcName,
        ImmutableArray<IRParameter> parameters, int[] paramLocals,
        LocalsBuilder locals, LabelHandle loopStart)
    {
        switch (expr)
        {
            case IRIf iff:
            {
                LabelHandle elseLabel = il.DefineLabel();

                EmitTcoExpr(il, iff.Condition, locals, parameters, paramLocals);
                il.Branch(ILOpCode.Brfalse, elseLabel);

                EmitTailCallExpr(il, iff.Then, funcName, parameters, paramLocals, locals, loopStart);

                il.MarkLabel(elseLabel);
                EmitTailCallExpr(il, iff.Else, funcName, parameters, paramLocals, locals, loopStart);
                break;
            }

            case IRLet let:
            {
                EmitTcoExpr(il, let.Value, locals, parameters, paramLocals);
                int localIndex = locals.AddLocal(let.Name, let.Value.Type);
                il.StoreLocal(localIndex);
                EmitTailCallExpr(il, let.Body, funcName, parameters, paramLocals, locals, loopStart);
                break;
            }

            case IRRegion region:
                EmitTailCallExpr(il, region.Body, funcName, parameters, paramLocals, locals, loopStart);
                break;

            case IRMatch match:
            {
                EmitTcoMatch(il, match, funcName, parameters, paramLocals, locals, loopStart);
                break;
            }

            case IRApply app when IsSelfCall(app, funcName):
            {
                List<IRExpr> args = new();
                IRExpr func = app;
                while (func is IRApply inner)
                {
                    args.Add(inner.Argument);
                    func = inner.Function;
                }
                args.Reverse();

                // Evaluate all arguments into temp locals
                int[] tempLocals = new int[args.Count];
                for (int i = 0; i < args.Count && i < parameters.Length; i++)
                {
                    EmitTcoExpr(il, args[i], locals, parameters, paramLocals);
                    tempLocals[i] = locals.AddLocal($"__tco_t{i}", parameters[i].Type);
                    il.StoreLocal(tempLocals[i]);
                }
                // Assign temps to parameter locals
                for (int i = 0; i < args.Count && i < parameters.Length; i++)
                {
                    il.LoadLocal(tempLocals[i]);
                    il.StoreLocal(paramLocals[i]);
                }
                il.Branch(ILOpCode.Br, loopStart);
                break;
            }

            default:
            {
                EmitTcoExpr(il, expr, locals, parameters, paramLocals);
                il.OpCode(ILOpCode.Ret);
                break;
            }
        }
    }

    void EmitTcoMatch(InstructionEncoder il, IRMatch match, string funcName,
        ImmutableArray<IRParameter> parameters, int[] paramLocals,
        LocalsBuilder locals, LabelHandle loopStart)
    {
        EmitTcoExpr(il, match.Scrutinee, locals, parameters, paramLocals);
        int scrutineeLocal = locals.AddLocal("__tco_scrutinee", match.Scrutinee.Type);
        il.StoreLocal(scrutineeLocal);

        int resultLocal = locals.AddLocal("__tco_match_result", match.Type);

        LabelHandle endLabel = il.DefineLabel();
        bool anyBranchIsTailCall = match.Branches.Any(b => ExprHasTailCall(b.Body, funcName));

        for (int i = 0; i < match.Branches.Length; i++)
        {
            IRMatchBranch branch = match.Branches[i];
            bool isLast = i == match.Branches.Length - 1;

            switch (branch.Pattern)
            {
                case IRWildcardPattern:
                    if (ExprHasTailCall(branch.Body, funcName))
                    {
                        EmitTailCallExpr(il, branch.Body, funcName, parameters, paramLocals, locals, loopStart);
                    }
                    else
                    {
                        EmitTcoExpr(il, branch.Body, locals, parameters, paramLocals);
                        il.StoreLocal(resultLocal);
                        if (!isLast)
                        {
                            il.Branch(ILOpCode.Br, endLabel);
                        }
                    }
                    break;

                case IRVarPattern varPat:
                    il.LoadLocal(scrutineeLocal);
                    int varLocal = locals.AddLocal(varPat.Name, varPat.Type);
                    il.StoreLocal(varLocal);
                    if (ExprHasTailCall(branch.Body, funcName))
                    {
                        EmitTailCallExpr(il, branch.Body, funcName, parameters, paramLocals, locals, loopStart);
                    }
                    else
                    {
                        EmitTcoExpr(il, branch.Body, locals, parameters, paramLocals);
                        il.StoreLocal(resultLocal);
                        if (!isLast)
                        {
                            il.Branch(ILOpCode.Br, endLabel);
                        }
                    }
                    break;

                case IRLiteralPattern litPat:
                {
                    LabelHandle nextLabel = il.DefineLabel();
                    il.LoadLocal(scrutineeLocal);
                    switch (litPat.Value)
                    {
                        case long l:
                            il.LoadConstantI8(l);
                            break;
                        case bool b:
                            il.LoadConstantI4(b ? 1 : 0);
                            break;
                        case string s:
                            il.LoadString(m_metadata.GetOrAddUserString(s));
                            break;
                        default:
                            il.LoadConstantI8(0);
                            break;
                    }
                    il.OpCode(ILOpCode.Ceq);
                    il.Branch(ILOpCode.Brfalse, nextLabel);

                    if (ExprHasTailCall(branch.Body, funcName))
                    {
                        EmitTailCallExpr(il, branch.Body, funcName, parameters, paramLocals, locals, loopStart);
                    }
                    else
                    {
                        EmitTcoExpr(il, branch.Body, locals, parameters, paramLocals);
                        il.StoreLocal(resultLocal);
                        il.Branch(ILOpCode.Br, endLabel);
                    }

                    il.MarkLabel(nextLabel);
                    break;
                }

                case IRCtorPattern ctorPat:
                {
                    string ctorName = SanitizeName(ctorPat.Name);
                    LabelHandle nextLabel = il.DefineLabel();

                    if (!m_emittedTypes.TryGet(ctorName, out TypeDefinitionHandle ctorTypeDef))
                    {
                        il.MarkLabel(nextLabel);
                        break;
                    }

                    il.LoadLocal(scrutineeLocal);
                    il.OpCode(ILOpCode.Isinst);
                    il.Token(ctorTypeDef);
                    il.OpCode(ILOpCode.Dup);
                    il.Branch(ILOpCode.Brfalse, nextLabel);

                    int castLocal = locals.AddLocal($"__tco_cast_{ctorName}", ctorPat.Type);
                    il.StoreLocal(castLocal);

                    BindCtorSubPatterns(il, ctorPat, ctorName, castLocal, locals, parameters);

                    if (ExprHasTailCall(branch.Body, funcName))
                    {
                        EmitTailCallExpr(il, branch.Body, funcName, parameters, paramLocals, locals, loopStart);
                    }
                    else
                    {
                        EmitTcoExpr(il, branch.Body, locals, parameters, paramLocals);
                        il.StoreLocal(resultLocal);
                        il.Branch(ILOpCode.Br, endLabel);
                    }

                    il.MarkLabel(nextLabel);
                    il.OpCode(ILOpCode.Pop);
                    break;
                }
            }
        }

        il.MarkLabel(endLabel);
        il.LoadLocal(resultLocal);
        il.OpCode(ILOpCode.Ret);
    }

    void EmitTcoExpr(InstructionEncoder il, IRExpr expr, LocalsBuilder locals,
        ImmutableArray<IRParameter> parameters, int[] paramLocals)
    {
        switch (expr)
        {
            case IRName name:
                int paramIndex = FindParameter(name.Name, parameters);
                if (paramIndex >= 0)
                {
                    il.LoadLocal(paramLocals[paramIndex]);
                    return;
                }
                break;
        }
        EmitExprWithParamLocals(il, expr, locals, parameters, paramLocals);
    }

    void EmitExprWithParamLocals(InstructionEncoder il, IRExpr expr, LocalsBuilder locals,
        ImmutableArray<IRParameter> parameters, int[] paramLocals)
    {
        switch (expr)
        {
            case IRTextLit t:
                il.LoadString(m_metadata.GetOrAddUserString(CceTable.Encode(t.Value)));
                break;

            case IRCharLit c:
                il.LoadConstantI8(CceTable.UnicharToCce(c.Value));
                break;

            case IRIntegerLit i:
                il.LoadConstantI8(i.Value);
                break;

            case IRNumberLit n:
                il.LoadConstantR8((double)n.Value);
                break;

            case IRBoolLit b:
                il.LoadConstantI4(b.Value ? 1 : 0);
                break;

            case IRNegate neg:
                EmitTcoExpr(il, neg.Operand, locals, parameters, paramLocals);
                il.OpCode(ILOpCode.Neg);
                break;

            case IRName name:
            {
                int paramIndex = FindParameter(name.Name, parameters);
                if (paramIndex >= 0)
                {
                    il.LoadLocal(paramLocals[paramIndex]);
                }
                else if (locals.TryGetLocal(name.Name, out int localIndex))
                {
                    il.LoadLocal(localIndex);
                }
                else if (TryEmitBuiltinCore(il, name.Name, new List<IRExpr>(), locals,
                    expr => EmitTcoExpr(il, expr, locals, parameters, paramLocals)))
                {
                    // Zero-arg builtin handled
                }
                else if (m_ctorDefs.TryGet(name.Name, out MethodDefinitionHandle ctorDef)
                    && name.Type is not FunctionType)
                {
                    il.OpCode(ILOpCode.Newobj);
                    il.Token(ctorDef);
                }
                else if (m_definedMethods.TryGet(name.Name, out MethodDefinitionHandle methodRef))
                {
                    EmitCallToMethod(il, name.Name, methodRef, ImmutableArray<IRExpr>.Empty);
                }
                break;
            }

            case IRBinary bin:
                EmitTcoBinary(il, bin, locals, parameters, paramLocals);
                break;

            case IRIf ifExpr:
            {
                LabelHandle elseLabel = il.DefineLabel();
                LabelHandle endLabel = il.DefineLabel();
                EmitTcoExpr(il, ifExpr.Condition, locals, parameters, paramLocals);
                il.Branch(ILOpCode.Brfalse, elseLabel);
                EmitTcoExpr(il, ifExpr.Then, locals, parameters, paramLocals);
                il.Branch(ILOpCode.Br, endLabel);
                il.MarkLabel(elseLabel);
                EmitTcoExpr(il, ifExpr.Else, locals, parameters, paramLocals);
                il.MarkLabel(endLabel);
                break;
            }

            case IRLet letExpr:
                EmitTcoExpr(il, letExpr.Value, locals, parameters, paramLocals);
                int letLocal = locals.AddLocal(letExpr.Name, letExpr.Value.Type);
                il.StoreLocal(letLocal);
                EmitTcoExpr(il, letExpr.Body, locals, parameters, paramLocals);
                break;

            case IRApply apply:
                EmitTcoApply(il, apply, locals, parameters, paramLocals);
                break;

            case IRDo doExpr:
                for (int i = 0; i < doExpr.Statements.Length; i++)
                {
                    IRDoStatement stmt = doExpr.Statements[i];
                    switch (stmt)
                    {
                        case IRDoBind bind:
                            EmitTcoExpr(il, bind.Value, locals, parameters, paramLocals);
                            int doLocal = locals.AddLocal(bind.Name, bind.NameType);
                            il.StoreLocal(doLocal);
                            break;
                        case IRDoExec exec:
                            EmitTcoExpr(il, exec.Expression, locals, parameters, paramLocals);
                            bool isLast = i == doExpr.Statements.Length - 1;
                            if (!isLast && !IsVoidLike(exec.Expression.Type))
                            {
                                il.OpCode(ILOpCode.Pop);
                            }
                            break;
                    }
                }
                break;

            case IRRecord rec:
            {
                string typeName = SanitizeName(rec.TypeName);
                foreach ((string _, IRExpr value) in rec.Fields)
                {
                    EmitTcoExpr(il, value, locals, parameters, paramLocals);
                }
                if (m_ctorDefs.TryGet(typeName, out MethodDefinitionHandle ctorDef2))
                {
                    il.OpCode(ILOpCode.Newobj);
                    il.Token(ctorDef2);
                }
                break;
            }

            case IRFieldAccess fa:
            {
                EmitTcoExpr(il, fa.Record, locals, parameters, paramLocals);
                string ownerTypeName = ResolveOwnerTypeName(fa.Record.Type);
                string fieldName = SanitizeName(fa.FieldName);
                string fieldKey = $"{ownerTypeName}.{fieldName}";
                if (m_fieldDefs.TryGet(fieldKey, out FieldDefinitionHandle fieldHandle))
                {
                    il.OpCode(ILOpCode.Ldfld);
                    il.Token(fieldHandle);
                }
                break;
            }

            case IRRegion region:
                EmitExprWithParamLocals(il, region.Body, locals, parameters, paramLocals);
                break;

            case IRMatch match:
                EmitTcoMatchExpr(il, match, locals, parameters, paramLocals);
                break;

            default:
                EmitExpr(il, expr, locals, parameters);
                break;
        }
    }

    void EmitTcoBinary(InstructionEncoder il, IRBinary bin, LocalsBuilder locals,
        ImmutableArray<IRParameter> parameters, int[] paramLocals)
    {
        if (bin.Op == IRBinaryOp.AppendText)
        {
            EmitTcoExpr(il, bin.Left, locals, parameters, paramLocals);
            EmitTcoExpr(il, bin.Right, locals, parameters, paramLocals);
            il.Call(m_stringConcatRef);
            return;
        }

        EmitTcoExpr(il, bin.Left, locals, parameters, paramLocals);
        EmitTcoExpr(il, bin.Right, locals, parameters, paramLocals);

        switch (bin.Op)
        {
            case IRBinaryOp.AddInt or IRBinaryOp.AddNum:
                il.OpCode(ILOpCode.Add);
                break;
            case IRBinaryOp.SubInt or IRBinaryOp.SubNum:
                il.OpCode(ILOpCode.Sub);
                break;
            case IRBinaryOp.MulInt or IRBinaryOp.MulNum:
                il.OpCode(ILOpCode.Mul);
                break;
            case IRBinaryOp.DivInt or IRBinaryOp.DivNum:
                il.OpCode(ILOpCode.Div);
                break;
            case IRBinaryOp.Eq:
                if (IsTextLike(bin.Left.Type))
                {
                    il.Call(m_stringEqualsRef);
                }
                else
                {
                    il.OpCode(ILOpCode.Ceq);
                }

                break;
            case IRBinaryOp.NotEq:
                if (IsTextLike(bin.Left.Type))
                {
                    il.Call(m_stringEqualsRef);
                    il.LoadConstantI4(0);
                    il.OpCode(ILOpCode.Ceq);
                }
                else
                {
                    il.OpCode(ILOpCode.Ceq);
                    il.LoadConstantI4(0);
                    il.OpCode(ILOpCode.Ceq);
                }
                break;
            case IRBinaryOp.Lt:
                il.OpCode(ILOpCode.Clt);
                break;
            case IRBinaryOp.Gt:
                il.OpCode(ILOpCode.Cgt);
                break;
            case IRBinaryOp.LtEq:
                il.OpCode(ILOpCode.Cgt);
                il.LoadConstantI4(0);
                il.OpCode(ILOpCode.Ceq);
                break;
            case IRBinaryOp.GtEq:
                il.OpCode(ILOpCode.Clt);
                il.LoadConstantI4(0);
                il.OpCode(ILOpCode.Ceq);
                break;
            case IRBinaryOp.And:
                il.OpCode(ILOpCode.And);
                break;
            case IRBinaryOp.Or:
                il.OpCode(ILOpCode.Or);
                break;
        }
    }

    void EmitTcoApply(InstructionEncoder il, IRApply apply, LocalsBuilder locals,
        ImmutableArray<IRParameter> parameters, int[] paramLocals)
    {
        List<IRExpr> args = new();
        IRExpr func = apply;
        while (func is IRApply inner)
        {
            args.Add(inner.Argument);
            func = inner.Function;
        }
        args.Reverse();

        if (func is IRName funcName)
        {
            if (TryEmitBuiltinCore(il, funcName.Name, args, locals,
                    expr => EmitTcoExpr(il, expr, locals, parameters, paramLocals)))
            {
                return;
            }

            if (m_ctorDefs.TryGet(funcName.Name, out MethodDefinitionHandle ctorDef))
            {
                string ctorSanitized = SanitizeName(funcName.Name);
                List<(string Name, CodexType Type)>? fieldTypes = m_typeFields[ctorSanitized];
                for (int ai = 0; ai < args.Count; ai++)
                {
                    EmitTcoExpr(il, args[ai], locals, parameters, paramLocals);
                    if (fieldTypes is not null && ai < fieldTypes.Count)
                    {
                        EmitBoxIfNeeded(il, args[ai].Type, fieldTypes[ai].Type);
                    }
                }
                il.OpCode(ILOpCode.Newobj);
                il.Token(ctorDef);
                return;
            }

            if (m_definedMethods.TryGet(funcName.Name, out MethodDefinitionHandle methodDef))
            {
                foreach (IRExpr arg in args)
                {
                    EmitTcoExpr(il, arg, locals, parameters, paramLocals);
                }
                ImmutableArray<IRExpr> argArray = args.ToImmutableArray();
                EmitCallToMethod(il, funcName.Name, methodDef, argArray);
            }
        }
    }

    void EmitTcoMatchExpr(InstructionEncoder il, IRMatch match, LocalsBuilder locals,
        ImmutableArray<IRParameter> parameters, int[] paramLocals)
    {
        EmitTcoExpr(il, match.Scrutinee, locals, parameters, paramLocals);
        int scrutineeLocal = locals.AddLocal("__tco_scr", match.Scrutinee.Type);
        il.StoreLocal(scrutineeLocal);

        int resultLocal = locals.AddLocal("__tco_mr", match.Type);
        LabelHandle endLabel = il.DefineLabel();

        for (int i = 0; i < match.Branches.Length; i++)
        {
            IRMatchBranch branch = match.Branches[i];
            bool isLast = i == match.Branches.Length - 1;

            switch (branch.Pattern)
            {
                case IRWildcardPattern:
                    EmitTcoExpr(il, branch.Body, locals, parameters, paramLocals);
                    il.StoreLocal(resultLocal);
                    if (!isLast)
                    {
                        il.Branch(ILOpCode.Br, endLabel);
                    }

                    break;

                case IRVarPattern varPat:
                    il.LoadLocal(scrutineeLocal);
                    int varLocal = locals.AddLocal(varPat.Name, varPat.Type);
                    il.StoreLocal(varLocal);
                    EmitTcoExpr(il, branch.Body, locals, parameters, paramLocals);
                    il.StoreLocal(resultLocal);
                    if (!isLast)
                    {
                        il.Branch(ILOpCode.Br, endLabel);
                    }

                    break;

                case IRLiteralPattern litPat:
                {
                    LabelHandle nextLabel = il.DefineLabel();
                    il.LoadLocal(scrutineeLocal);
                    switch (litPat.Value)
                    {
                        case long l:
                            il.LoadConstantI8(l);
                            break;
                        case bool b:
                            il.LoadConstantI4(b ? 1 : 0);
                            break;
                        case string s:
                            il.LoadString(m_metadata.GetOrAddUserString(s));
                            break;
                        default:
                            il.LoadConstantI8(0);
                            break;
                    }
                    il.OpCode(ILOpCode.Ceq);
                    il.Branch(ILOpCode.Brfalse, nextLabel);

                    EmitTcoExpr(il, branch.Body, locals, parameters, paramLocals);
                    il.StoreLocal(resultLocal);
                    il.Branch(ILOpCode.Br, endLabel);

                    il.MarkLabel(nextLabel);
                    break;
                }

                case IRCtorPattern ctorPat:
                {
                    string ctorName = SanitizeName(ctorPat.Name);
                    LabelHandle nextLabel = il.DefineLabel();

                    if (!m_emittedTypes.TryGet(ctorName, out TypeDefinitionHandle ctorTypeDef))
                    {
                        il.MarkLabel(nextLabel);
                        break;
                    }

                    il.LoadLocal(scrutineeLocal);
                    il.OpCode(ILOpCode.Isinst);
                    il.Token(ctorTypeDef);
                    il.OpCode(ILOpCode.Dup);
                    il.Branch(ILOpCode.Brfalse, nextLabel);

                    int castLocal = locals.AddLocal($"__tco_c_{ctorName}", ctorPat.Type);
                    il.StoreLocal(castLocal);

                    BindCtorSubPatterns(il, ctorPat, ctorName, castLocal, locals, parameters);

                    EmitTcoExpr(il, branch.Body, locals, parameters, paramLocals);
                    il.StoreLocal(resultLocal);
                    il.Branch(ILOpCode.Br, endLabel);

                    il.MarkLabel(nextLabel);
                    il.OpCode(ILOpCode.Pop);
                    break;
                }
            }
        }

        il.MarkLabel(endLabel);
        il.LoadLocal(resultLocal);
    }

    static int EstimateStackDepth(IRExpr expr)
    {
        switch (expr)
        {
            case IRBinary bin:
                int leftDepth = EstimateStackDepth(bin.Left);
                int rightDepth = EstimateStackDepth(bin.Right);
                return Math.Max(leftDepth, 1 + rightDepth);

            case IRApply apply:
                int fnDepth = EstimateStackDepth(apply.Function);
                int argDepth = EstimateStackDepth(apply.Argument);
                return Math.Max(fnDepth, 1 + argDepth);

            case IRLet letExpr:
                int valDepth = EstimateStackDepth(letExpr.Value);
                int bodyDepth = EstimateStackDepth(letExpr.Body);
                return Math.Max(valDepth, bodyDepth);

            case IRIf ifExpr:
                int condDepth = EstimateStackDepth(ifExpr.Condition);
                int thenDepth = EstimateStackDepth(ifExpr.Then);
                int elseDepth = EstimateStackDepth(ifExpr.Else);
                return Math.Max(condDepth, Math.Max(thenDepth, elseDepth));

            case IRDo doExpr:
                int maxDo = 0;
                foreach (IRDoStatement stmt in doExpr.Statements)
                {
                    int d = stmt switch
                    {
                        IRDoBind bind => EstimateStackDepth(bind.Value),
                        IRDoExec exec => EstimateStackDepth(exec.Expression),
                        _ => 1
                    };
                    maxDo = Math.Max(maxDo, d);
                }
                return maxDo;

            case IRMatch match:
                int scrutDepth = EstimateStackDepth(match.Scrutinee);
                int branchMax = 0;
                foreach (IRMatchBranch branch in match.Branches)
                {
                    branchMax = Math.Max(branchMax, EstimateStackDepth(branch.Body));
                }

                return Math.Max(scrutDepth, branchMax);

            case IRRegion region:
                return EstimateStackDepth(region.Body);

            case IRLambda lambda:
                return EstimateStackDepth(lambda.Body);

            case IRNegate neg:
                return EstimateStackDepth(neg.Operand);

            default:
                return 1;
        }
    }
}

