using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Codex.Core;
using Codex.IR;
using Codex.Types;

namespace Codex.Emit.IL;

sealed partial class ILAssemblyBuilder
{
    // ── Generic List<T> instantiation cache ─────────────────────
    //
    // The IL emitter must produce distinct TypeSpecificationHandles and
    // MemberReferenceHandles for each concrete List<T> it encounters
    // (List<string>, List<long>, List<object>, etc.).  The member
    // signatures are identical across instantiations — they use !0
    // (GenericTypeParameter 0) which the CLR resolves from the parent
    // TypeSpec — so we store the reusable BlobHandles once and create
    // per-element-type TypeSpec + MemberRef bundles on demand.

    readonly record struct ListInstantiation(
        TypeSpecificationHandle TypeSpec,
        MemberReferenceHandle CtorIEnumerable,
        MemberReferenceHandle CtorNoArg,
        MemberReferenceHandle GetCount,
        MemberReferenceHandle GetItem,
        MemberReferenceHandle Add);

    readonly Dictionary<string, ListInstantiation> m_listCache = new();

    // Reusable signature blobs — built once in InitializeListSignatureBlobs()
    BlobHandle m_listCtorIEnumerableSig;
    BlobHandle m_listCtorNoArgSig;
    BlobHandle m_listGetCountSig;
    BlobHandle m_listGetItemSig;
    BlobHandle m_listAddSig;

    void InitializeListSignatureBlobs()
    {
        // .ctor(IEnumerable<!0>) — instance void
        {
            BlobBuilder sig = new();
            new BlobEncoder(sig).MethodSignature(
                SignatureCallingConvention.Default, 0, isInstanceMethod: true)
                .Parameters(1,
                    returnType => returnType.Void(),
                    parameters =>
                    {
                        SignatureTypeEncoder paramType = parameters.AddParameter().Type();
                        GenericTypeArgumentsEncoder genArgs = paramType
                            .GenericInstantiation(m_ienumerableOpenRef, 1, isValueType: false);
                        SignatureTypeEncoder argEncoder = genArgs.AddArgument();
                        argEncoder.Builder.WriteByte((byte)SignatureTypeCode.GenericTypeParameter);
                        argEncoder.Builder.WriteCompressedInteger(0);
                    });
            m_listCtorIEnumerableSig = m_metadata.GetOrAddBlob(sig);
        }

        // .ctor() — instance void (parameterless)
        {
            BlobBuilder sig = new();
            new BlobEncoder(sig).MethodSignature(
                SignatureCallingConvention.Default, 0, isInstanceMethod: true)
                .Parameters(0,
                    returnType => returnType.Void(),
                    _ => { });
            m_listCtorNoArgSig = m_metadata.GetOrAddBlob(sig);
        }

        // get_Count() : int — instance
        m_listGetCountSig = EncodeMethodSignature(
            SignatureCallingConvention.Default, false,
            returnType: b => b.Type().Int32(),
            parameters: Array.Empty<Action<ParameterTypeEncoder>>());

        // get_Item(int) : !0 — instance
        {
            BlobBuilder sig = new();
            new BlobEncoder(sig).MethodSignature(
                SignatureCallingConvention.Default, 0, isInstanceMethod: true)
                .Parameters(1,
                    returnType =>
                    {
                        SignatureTypeEncoder retEncoder = returnType.Type();
                        retEncoder.Builder.WriteByte((byte)SignatureTypeCode.GenericTypeParameter);
                        retEncoder.Builder.WriteCompressedInteger(0);
                    },
                    parameters =>
                    {
                        parameters.AddParameter().Type().Int32();
                    });
            m_listGetItemSig = m_metadata.GetOrAddBlob(sig);
        }

        // Add(!0) : void — instance
        {
            BlobBuilder sig = new();
            new BlobEncoder(sig).MethodSignature(
                SignatureCallingConvention.Default, 0, isInstanceMethod: true)
                .Parameters(1,
                    returnType => returnType.Void(),
                    parameters =>
                    {
                        SignatureTypeEncoder paramEncoder = parameters.AddParameter().Type();
                        paramEncoder.Builder.WriteByte((byte)SignatureTypeCode.GenericTypeParameter);
                        paramEncoder.Builder.WriteCompressedInteger(0);
                    });
            m_listAddSig = m_metadata.GetOrAddBlob(sig);
        }
    }

    ListInstantiation GetOrCreateListInstantiation(CodexType elementType)
    {
        string key = GetListCacheKey(elementType);
        if (m_listCache.TryGetValue(key, out ListInstantiation cached))
        {
            return cached;
        }

        // Build TypeSpec for List<elementType>
        TypeSpecificationHandle typeSpec;
        {
            BlobBuilder blob = new();
            SignatureTypeEncoder typeEncoder = new BlobEncoder(blob).TypeSpecificationSignature();
            GenericTypeArgumentsEncoder genArgs = typeEncoder
                .GenericInstantiation(m_listOpenRef, 1, isValueType: false);
            EncodeType(genArgs.AddArgument(), elementType);
            typeSpec = m_metadata.AddTypeSpecification(m_metadata.GetOrAddBlob(blob));
        }

        // Create MemberRefs bound to this TypeSpec — same sigs, different parent
        StringHandle ctorName = m_metadata.GetOrAddString(".ctor");
        MemberReferenceHandle ctorIEnum = m_metadata.AddMemberReference(
            typeSpec, ctorName, m_listCtorIEnumerableSig);
        MemberReferenceHandle ctorNoArg = m_metadata.AddMemberReference(
            typeSpec, ctorName, m_listCtorNoArgSig);
        MemberReferenceHandle getCount = m_metadata.AddMemberReference(
            typeSpec, m_metadata.GetOrAddString("get_Count"), m_listGetCountSig);
        MemberReferenceHandle getItem = m_metadata.AddMemberReference(
            typeSpec, m_metadata.GetOrAddString("get_Item"), m_listGetItemSig);
        MemberReferenceHandle add = m_metadata.AddMemberReference(
            typeSpec, m_metadata.GetOrAddString("Add"), m_listAddSig);

        ListInstantiation inst = new(typeSpec, ctorIEnum, ctorNoArg, getCount, getItem, add);
        m_listCache[key] = inst;
        return inst;
    }

    static string GetListCacheKey(CodexType type) => type switch
    {
        IntegerType => "int64",
        NumberType => "double",
        TextType => "string",
        BooleanType => "bool",
        VoidType or NothingType => "void",
        RecordType rec => $"rec:{rec.TypeName.Value}",
        SumType sum => $"sum:{sum.TypeName.Value}",
        ConstructedType ct => $"ct:{ct.Constructor.Value}",
        ListType lt => $"list:{GetListCacheKey(lt.Element)}",
        TypeVariable => "object",
        ForAllType => "object",
        _ => "object"
    };

    // ── List literal emission ───────────────────────────────────

    void EmitListLiteral(InstructionEncoder il, IRList list, LocalsBuilder locals,
        ImmutableArray<IRParameter> parameters)
    {
        ListInstantiation inst = GetOrCreateListInstantiation(list.ElementType);

        // new List<T>()
        il.OpCode(ILOpCode.Newobj);
        il.Token(inst.CtorNoArg);

        // list.Add(element) for each element
        for (int i = 0; i < list.Elements.Length; i++)
        {
            il.OpCode(ILOpCode.Dup); // keep list ref on stack
            EmitExpr(il, list.Elements[i], locals, parameters);
            EmitBoxForListElement(il, list.Elements[i].Type, list.ElementType);
            il.OpCode(ILOpCode.Callvirt);
            il.Token(inst.Add);
        }
        // List<T> remains on stack
    }

    void EmitBoxForListElement(InstructionEncoder il, CodexType actualType, CodexType elementType)
    {
        // If the List element type is object (erased generic), box value types
        if (elementType is TypeVariable or ForAllType)
        {
            EmitBoxIfNeeded(il, actualType, elementType);
        }
    }

    // ── Helper: extract element type from a list argument ────────

    static CodexType ExtractListElementType(List<IRExpr> args, int argIndex)
    {
        if (argIndex < args.Count && args[argIndex].Type is ListType lt)
        {
            return lt.Element;
        }

        return TextType.s_instance; // fallback: List<string> for backward compat
    }
}
