using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Codex.Core;
using Codex.IR;
using Codex.Types;

namespace Codex.Emit.IL;

// Partial-application support for IL.
//
// When a user function `f : A -> B -> C` is called with fewer args than its
// arity (`f a`), the type-checker gives the call site type `B -> C`. IL
// can't just call f with one arg — that's invalid IL — so the emitter
// synthesizes a per-function closure class and wraps it in a `Func<B, C>`:
//
//   sealed class __f_closure1 {
//       long _a0;
//       public __f_closure1(long a0) { _a0 = a0; }
//       public long Invoke(long b) => f(_a0, b);
//   }
//
//   // at `f a` site:
//   newobj __f_closure1(long)     // captures a
//   ldftn  __f_closure1::Invoke
//   newobj Func<long,long>(object, nativeint)
//
//   // at `(f a) b` site — the leaf isn't a defined method, it's a
//   // Func<B, C>-typed local/param. Just callvirt Invoke.

sealed partial class ILAssemblyBuilder
{
    // Key: (method name, captured-arg-count).
    readonly record struct ClosureKey(string MethodName, int CapturedCount);

    readonly record struct ClosureInfo(
        TypeDefinitionHandle TypeDef,
        MethodDefinitionHandle Ctor,
        MethodDefinitionHandle Invoke,
        MemberReferenceHandle FuncCtor,        // Func<...>.ctor(object, nativeint)
        ImmutableArray<CodexType> CapturedTypes,
        ImmutableArray<CodexType> RemainingTypes,
        CodexType ResultType);

    readonly Dictionary<ClosureKey, ClosureInfo> m_closures = new();

    // Key for the object-erasure adapter classes. Each unique
    // (innerIn, innerOut, outerIn, outerOut) combination gets one class.
    // "outerIn"/"outerOut" are the callee-expected erased types (object when
    // the callee's declared slot is a TypeVariable, else the concrete type).
    readonly record struct AdapterKey(string InnerIn, string InnerOut, string OuterIn, string OuterOut);

    readonly record struct AdapterInfo(
        TypeDefinitionHandle TypeDef,
        MethodDefinitionHandle Ctor,
        MethodDefinitionHandle Invoke,
        MemberReferenceHandle OuterFuncCtor,
        CodexType InnerIn,
        CodexType InnerOut,
        CodexType? OuterIn,       // null = `object` (erased)
        CodexType? OuterOut,      // null = `object` (erased)
        AdapterKey? NextKey);     // non-null when the adapter cascades: inner
                                  // callvirt returns an object that must be
                                  // re-wrapped by the next adapter before
                                  // being returned as outerOut.

    readonly Dictionary<AdapterKey, AdapterInfo> m_adapters = new();

    // Cache of Func<...> TypeSpecs + Invoke MemberRefs, keyed by a string
    // that uniquely identifies the (arg types..., ret type) signature.
    readonly Dictionary<string, (TypeSpecificationHandle TypeSpec, MemberReferenceHandle Invoke)>
        m_funcCache = new();

    // Open-generic Func<T1, ...> type references. We only cache the ones
    // we actually need. Func<T,TRet> has 2 type parameters (1 arg + ret).
    readonly Dictionary<int, TypeReferenceHandle> m_funcOpenRefs = new();

    TypeReferenceHandle GetFuncOpenRef(int typeParamCount)
    {
        if (m_funcOpenRefs.TryGetValue(typeParamCount, out TypeReferenceHandle cached))
            return cached;

        // Func<TResult> has arity 1, Func<T, TResult> has arity 2, etc.
        // All live in `System` namespace with suffix `1, `2, etc.
        string name = typeParamCount == 1 ? "Func`1" : $"Func`{typeParamCount}";
        TypeReferenceHandle handle = m_metadata.AddTypeReference(
            m_corlibRef,
            m_metadata.GetOrAddString("System"),
            m_metadata.GetOrAddString(name));
        m_funcOpenRefs[typeParamCount] = handle;
        return handle;
    }

    // Walks every IRDefinition body and collects the set of methods that need
    // closure chains — either because they're partially applied, or because
    // they're used as a value (bare IRName of FunctionType). For each such
    // method of arity N, generates specs for closure0..closure{N-1} so the
    // Invoke chain cascades: closure{k} takes 1 arg, returns either a Func<>
    // wrap of closure{k+1} (for k < N-1) or the final result (for k == N-1).
    //
    // Also detects where a concrete Func<T, U> needs to be passed to a
    // generic function slot (declared param of TypeVar FunctionType). Each
    // such (T, U) pair requires an object-erased adapter class that wraps
    // Func<T, U> as Func<object, object> with unbox/box around the inner
    // call — otherwise callvirt Func<object,object>.Invoke on a
    // Func<long,long> delegate fails variance.
    // Adapter specs collected during the same scan. Each entry is a unique
    // (innerIn, innerOut, outerIn, outerOut, nextKey) tuple. OuterIn /
    // OuterOut are null when the callee's declared slot is a TypeVariable
    // (erased to `object` in IL). NextKey is set for cascade levels (the
    // closure cascade emits Func<A, object> for arity-N>=2, and the adapter
    // must re-wrap the returned object before returning it to the callee).
    readonly List<(CodexType InnerIn, CodexType InnerOut, CodexType? OuterIn, CodexType? OuterOut, AdapterKey? NextKey)>
        m_adapterSpecs = new();

    List<(ClosureKey Key, int Arity, ImmutableArray<CodexType> ParamTypes, CodexType ResultType)>
        ScanPartialApps(IRChapter module)
    {
        HashSet<string> needsChain = new();
        HashSet<AdapterKey> seenAdapters = new();
        Dictionary<string, int> arityMap = new();
        Dictionary<string, ImmutableArray<IRParameter>> paramsMap = new();
        Dictionary<string, CodexType> resultMap = new();
        foreach (IRDefinition def in module.Definitions)
        {
            arityMap[def.Name] = def.Parameters.Length;
            paramsMap[def.Name] = def.Parameters;
            resultMap[def.Name] = ComputeResultType(def.Type, def.Parameters.Length);
        }

        foreach (IRDefinition def in module.Definitions)
            ScanExpr(def.Body);

        List<(ClosureKey, int, ImmutableArray<CodexType>, CodexType)> result = new();
        foreach (string name in needsChain)
        {
            int arity = arityMap[name];
            ImmutableArray<CodexType> paramTypes = paramsMap[name].Select(p => p.Type).ToImmutableArray();
            CodexType resultT = resultMap[name]!;
            for (int k = 0; k < arity; k++)
                result.Add((new ClosureKey(name, k), arity, paramTypes, resultT));
        }
        // Emit higher-captured closures first so lower-captured ones can
        // reference them when cascading. Within a method, order by capturedCount
        // descending; across methods, order is arbitrary.
        result.Sort((a, b) => b.Item1.CapturedCount - a.Item1.CapturedCount);
        return result;

        void ScanExpr(IRExpr e)
        {
            switch (e)
            {
                case IRApply apply:
                    VisitApply(apply);
                    break;
                case IRBinary bin:
                    ScanExpr(bin.Left); ScanExpr(bin.Right); break;
                case IRIf ifE:
                    ScanExpr(ifE.Condition); ScanExpr(ifE.Then); ScanExpr(ifE.Else); break;
                case IRLet letE:
                    ScanExpr(letE.Value); ScanExpr(letE.Body); break;
                case IRNegate neg:
                    ScanExpr(neg.Operand); break;
                case IRRecord rec:
                    foreach ((string _, IRExpr v) in rec.Fields) ScanExpr(v);
                    break;
                case IRFieldAccess fa:
                    ScanExpr(fa.Record); break;
                case IRList list:
                    foreach (IRExpr el in list.Elements) ScanExpr(el);
                    break;
                case IRMatch match:
                    ScanExpr(match.Scrutinee);
                    foreach (IRMatchBranch br in match.Branches) ScanExpr(br.Body);
                    break;
                case IRAct act:
                    foreach (IRActStatement s in act.Statements)
                    {
                        if (s is IRActBind b) ScanExpr(b.Value);
                        else if (s is IRActExec ex) ScanExpr(ex.Expression);
                    }
                    break;
                case IRRunState rs:
                    ScanExpr(rs.InitialState); ScanExpr(rs.Computation); break;
                case IRSetState ss:
                    ScanExpr(ss.NewValue); break;
                case IRHandle h:
                    ScanExpr(h.Computation);
                    foreach (IRHandleClause c in h.Clauses) ScanExpr(c.Body);
                    break;
                case IRLambda lam:
                    ScanExpr(lam.Body); break;
                case IRName n:
                    // Function-typed bare name used as a value (not as a call
                    // target — those go through VisitApply). Arity ≥ 2 because
                    // arity-1 bare refs use the simpler ldnull+ldftn path.
                    if (IsFunctionTypeExpr(n.Type)
                        && arityMap.TryGetValue(n.Name, out int valArity)
                        && valArity >= 2)
                    {
                        needsChain.Add(n.Name);
                    }
                    break;
                case IRTextLit or IRCharLit or IRIntegerLit or IRNumberLit
                    or IRBoolLit or IRGetState or IRError:
                    break;
                default:
                    throw new InvalidOperationException(
                        $"ScanPartialApps: unhandled IR node {e.GetType().Name} — "
                        + "extend the scan so partial-apps inside it aren't silently missed.");
            }
        }

        void VisitApply(IRApply apply)
        {
            List<IRExpr> args = new();
            IRExpr func = apply;
            while (func is IRApply inner)
            {
                args.Add(inner.Argument);
                ScanExpr(inner.Argument);
                func = inner.Function;
            }
            args.Reverse();

            if (func is IRName name && arityMap.TryGetValue(name.Name, out int arity)
                && paramsMap.TryGetValue(name.Name, out ImmutableArray<IRParameter> parms))
            {
                if (args.Count > 0 && args.Count < arity)
                    needsChain.Add(name.Name);

                // Adapter detection: concrete Func<...> arg → generic slot.
                // Registers one adapter per curry level so a multi-arg
                // function emitted as Func<A, object> via the closure cascade
                // gets chained adapters down to its leaf.
                int cmp = Math.Min(args.Count, parms.Length);
                for (int ai = 0; ai < cmp; ai++)
                    RegisterAdapterChainIfNeeded(args[ai].Type, parms[ai].Type, seenAdapters);
            }
        }
    }

    static bool TryExtractArity1FuncTypes(CodexType t, out CodexType inType, out CodexType outType)
    {
        CodexType u = t is EffectfulType eff ? eff.Return : t;
        if (u is FunctionType ft && ft.Return is not FunctionType
            && (ft.Return is not EffectfulType fe || fe.Return is not FunctionType))
        {
            inType = ft.Parameter;
            outType = u is FunctionType ft2 ? UnwrapEffectful(ft2.Return) : ft.Return;
            return true;
        }
        inType = null!; outType = null!;
        return false;
    }

    // Walks a Func<A, Func<B, ...>> value being passed to a generic slot and
    // registers one adapter per curry level, leaves first so each parent's
    // NextKey references an already-registered child. Returns the outermost
    // AdapterKey, or null if the arg's IL representation already matches the
    // slot's (no adaptation needed) or neither side is a FunctionType.
    AdapterKey? RegisterAdapterChainIfNeeded(CodexType argType, CodexType paramType,
        HashSet<AdapterKey> seen)
    {
        CodexType arg = argType is EffectfulType ea ? ea.Return : argType;
        CodexType param = paramType is EffectfulType ep ? ep.Return : paramType;
        if (arg is not FunctionType af || param is not FunctionType pf) return null;
        if (!ContainsTypeVariable(pf)) return null;
        // Arg must have a more specific IL shape than the slot. If both erase
        // to Func<object, object> at IL level there's nothing to adapt.
        if (ILFuncEncoding(af) == ILFuncEncoding(pf)) return null;

        CodexType argReturn = UnwrapEffectful(af.Return);
        CodexType paramReturn = UnwrapEffectful(pf.Return);

        AdapterKey? nextKey = null;
        CodexType? nextConcreteReturn = null;

        if (argReturn is FunctionType && paramReturn is FunctionType)
        {
            // Cascade: the arg's return is itself a function. Register the
            // child adapter first (Func<B, ...> → Func<outerIn', outerOut'>).
            // At runtime, closure-cascade emits Func<A, object>; after the
            // inner callvirt returns an object of type argReturn, this
            // adapter re-wraps it via the child adapter.
            nextKey = RegisterAdapterChainIfNeeded(argReturn, paramReturn, seen);
            nextConcreteReturn = argReturn;
        }

        CodexType concreteIn = af.Parameter;
        CodexType concreteOut = nextConcreteReturn ?? argReturn;
        CodexType? outerIn = ContainsTypeVariable(pf.Parameter) ? null : pf.Parameter;
        CodexType? outerOut = ContainsTypeVariable(paramReturn) ? null : paramReturn;

        AdapterKey key = new(
            FuncTypeKey(concreteIn),
            FuncTypeKey(concreteOut),
            outerIn is null ? "object" : FuncTypeKey(outerIn),
            outerOut is null ? "object" : FuncTypeKey(outerOut));
        if (seen.Add(key))
            m_adapterSpecs.Add((concreteIn, concreteOut, outerIn, outerOut, nextKey));
        return key;
    }

    static bool IsGenericFunctionSlot(CodexType t)
    {
        CodexType u = t is EffectfulType eff ? eff.Return : t;
        return u is FunctionType ft && ContainsTypeVariable(ft);
    }

    static bool ContainsTypeVariable(CodexType t)
    {
        switch (t)
        {
            case TypeVariable: return true;
            case ForAllType fa: return ContainsTypeVariable(fa.Body);
            case FunctionType ft:
                return ContainsTypeVariable(ft.Parameter) || ContainsTypeVariable(ft.Return);
            case ListType lt: return ContainsTypeVariable(lt.Element);
            case ConstructedType ct:
                foreach (CodexType a in ct.Arguments)
                    if (ContainsTypeVariable(a)) return true;
                return false;
            case EffectfulType eff: return ContainsTypeVariable(eff.Return);
            default: return false;
        }
    }

    // IL-level encoding key — mirrors what EncodeType emits, where
    // TypeVariables/ForAllTypes outside a generic-method context fall
    // through to encoder.Object() and ConstructedType's type args are
    // dropped (EncodeUserType uses just the Constructor name).
    static string ILFuncEncoding(CodexType t) => t switch
    {
        IntegerType => "i64",
        NumberType => "f64",
        BooleanType => "bool",
        TextType => "string",
        CharType => "char",
        VoidType or NothingType => "void",
        RecordType rec => $"r:{rec.TypeName.Value}",
        SumType sum => $"s:{sum.TypeName.Value}",
        ConstructedType ct => $"c:{ct.Constructor.Value}",
        ListType lt => $"l:{ILFuncEncoding(lt.Element)}",
        FunctionType ft => $"f:{ILFuncEncoding(ft.Parameter)}->{ILFuncEncoding(ft.Return)}",
        TypeVariable or ForAllType => "object",
        EffectfulType eff => ILFuncEncoding(eff.Return),
        _ => "object"
    };

    static CodexType UnwrapEffectful(CodexType t) =>
        t is EffectfulType eff ? eff.Return : t;

    static CodexType ComputeResultType(CodexType declared, int arity)
    {
        CodexType t = declared;
        for (int i = 0; i < arity; i++)
        {
            if (t is FunctionType ft) t = ft.Return;
            else break;
        }
        return UnwrapEffectful(t);
    }

    // Emits one closure class per scanned (method, captured-count) pair.
    // Must be called AFTER user method handles are pre-registered
    // (m_definedMethods) and BEFORE Program's TypeDef is added. Specs are
    // pre-sorted by capturedCount DESC so closure{k+1} is populated in
    // m_closures before closure{k}'s Invoke body references it.
    void EmitClosureClasses(
        List<(ClosureKey Key, int Arity, ImmutableArray<CodexType> ParamTypes,
            CodexType ResultType)> specs)
    {
        foreach ((ClosureKey key, int arity, ImmutableArray<CodexType> paramTypes,
                  CodexType resultType) in specs)
        {
            EmitOneClosureClass(key, arity, paramTypes, resultType);
        }
    }

    // Emits object-erasure adapter classes. Each adapter wraps a concrete
    // Func<innerIn, innerOut> as Func<outerIn, outerOut> where outer is the
    // callee-expected erased shape. unbox_any around entry when the inner
    // param is concrete but outer is `object`; box around exit when the inner
    // return is concrete but outer is `object`. For cascade levels (NextKey
    // set), the Invoke body re-wraps the inner callvirt's return in the
    // next adapter before returning.
    void EmitAdapterClasses()
    {
        foreach ((CodexType innerIn, CodexType innerOut, CodexType? outerIn, CodexType? outerOut,
                  AdapterKey? nextKey) in m_adapterSpecs)
        {
            EmitOneAdapterClass(innerIn, innerOut, outerIn, outerOut, nextKey);
        }
    }

    void EmitOneAdapterClass(CodexType innerIn, CodexType innerOut,
        CodexType? outerIn, CodexType? outerOut, AdapterKey? nextKey)
    {
        AdapterKey key = new(
            FuncTypeKey(innerIn),
            FuncTypeKey(innerOut),
            outerIn is null ? "object" : FuncTypeKey(outerIn),
            outerOut is null ? "object" : FuncTypeKey(outerOut));
        string typeName = $"__adapter_{SanitizeForTypeName(key.InnerIn)}_{SanitizeForTypeName(key.InnerOut)}__to__{SanitizeForTypeName(key.OuterIn)}_{SanitizeForTypeName(key.OuterOut)}";

        TypeDefinitionHandle typeDef = m_metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            m_metadata.GetOrAddString(""),
            m_metadata.GetOrAddString(typeName),
            m_objectRef,
            MetadataTokens.FieldDefinitionHandle(m_metadata.GetRowCount(TableIndex.Field) + 1),
            MetadataTokens.MethodDefinitionHandle(m_metadata.GetRowCount(TableIndex.MethodDef) + 1));

        // Field: _inner of type `object` (the Func<innerIn, innerOut> encoded as object).
        BlobBuilder fsig = new();
        new BlobEncoder(fsig).Field().Type().Object();
        FieldDefinitionHandle innerField = m_metadata.AddFieldDefinition(
            FieldAttributes.Public,
            m_metadata.GetOrAddString("_inner"),
            m_metadata.GetOrAddBlob(fsig));

        // Ctor(object f): stores f to _inner.
        MethodDefinitionHandle ctor = EmitAdapterCtor(innerField);

        // Invoke(outerIn x): [unbox]; callvirt Func<innerIn, innerOut>::Invoke; [box or re-wrap via Next].
        // For cascade levels (nextKey set), the closure-cascade emits
        // Func<innerIn, object>, so the inner callvirt uses `object` as its
        // return sig even though the logical type is innerOut.
        MemberReferenceHandle funcInvokeRef = GetFuncInvokeRef(
            ImmutableArray.Create(innerIn), nextKey is null ? innerOut : null);
        MethodDefinitionHandle invoke = EmitAdapterInvoke(
            innerField, innerIn, innerOut, outerIn, outerOut, funcInvokeRef, nextKey);

        // Outer Func<outerIn, outerOut>::.ctor. null → encoded as `object`.
        ImmutableArray<CodexType> outerArg = ImmutableArray.Create(
            outerIn ?? (CodexType)new TypeVariable(int.MaxValue));
        MemberReferenceHandle outerFuncCtor = GetFuncCtorRef(outerArg, outerOut);

        m_adapters[key] = new AdapterInfo(
            typeDef, ctor, invoke, outerFuncCtor, innerIn, innerOut, outerIn, outerOut, nextKey);
    }

    MethodDefinitionHandle EmitAdapterCtor(FieldDefinitionHandle innerField)
    {
        ControlFlowBuilder cf = new();
        InstructionEncoder il = new(new BlobBuilder(), cf);

        il.LoadArgument(0);
        il.Call(m_objectCtorRef);
        il.LoadArgument(0);
        il.LoadArgument(1);
        il.OpCode(ILOpCode.Stfld);
        il.Token(innerField);
        il.OpCode(ILOpCode.Ret);

        int bodyOffset = m_methodBodies.AddMethodBody(il);

        BlobBuilder sig = new();
        new BlobEncoder(sig).MethodSignature(SignatureCallingConvention.Default, 0, isInstanceMethod: true)
            .Parameters(1,
                r => r.Void(),
                ps => ps.AddParameter().Type().Object());

        MethodDefinitionHandle ctor = m_metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.RTSpecialName | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig,
            MethodImplAttributes.IL | MethodImplAttributes.Managed,
            m_metadata.GetOrAddString(".ctor"),
            m_metadata.GetOrAddBlob(sig),
            bodyOffset,
            default);

        m_metadata.AddParameter(ParameterAttributes.None,
            m_metadata.GetOrAddString("f"), 1);

        return ctor;
    }

    MethodDefinitionHandle EmitAdapterInvoke(FieldDefinitionHandle innerField,
        CodexType innerIn, CodexType innerOut, CodexType? outerIn, CodexType? outerOut,
        MemberReferenceHandle funcInvokeRef, AdapterKey? nextKey)
    {
        ControlFlowBuilder cf = new();
        InstructionEncoder il = new(new BlobBuilder(), cf);

        // Load _inner, [unbox] arg, callvirt inner.Invoke, [box OR re-wrap via Next], ret.
        il.LoadArgument(0);
        il.OpCode(ILOpCode.Ldfld);
        il.Token(innerField);
        il.LoadArgument(1);
        // Unbox when the outer param is `object` but the inner expects a
        // concrete value type. EmitUnboxIfNeeded gates on the SOURCE type
        // being generic (TypeVariable) — pass a TypeVariable placeholder
        // when outerIn is null (erased → object on stack).
        if (outerIn is null)
            EmitUnboxIfNeeded(il, new TypeVariable(0), innerIn);
        il.OpCode(ILOpCode.Callvirt);
        il.Token(funcInvokeRef);
        if (nextKey is not null)
        {
            // Cascade: the stack top is an object (actually a concrete Func<>
            // from the next curry step). Wrap it in the next adapter so the
            // callee's subsequent callvirt Func<object,object>.Invoke
            // dispatches correctly.
            if (!m_adapters.TryGetValue(nextKey.Value, out AdapterInfo next))
            {
                throw new InvalidOperationException(
                    $"EmitAdapterInvoke: cascade next adapter {nextKey.Value} not emitted yet. " +
                    "Specs must be ordered so inner (leaf) adapters emit before outer ones.");
            }
            il.OpCode(ILOpCode.Newobj);
            il.Token(next.Ctor);
            il.OpCode(ILOpCode.Ldftn);
            il.Token(next.Invoke);
            il.OpCode(ILOpCode.Newobj);
            il.Token(next.OuterFuncCtor);
        }
        else if (outerOut is null)
        {
            // Box when the outer return is `object` but the inner returns a
            // concrete value type. EmitBoxIfNeeded gates on the TARGET type
            // being generic.
            EmitBoxIfNeeded(il, innerOut, new TypeVariable(0));
        }
        il.OpCode(ILOpCode.Ret);

        int bodyOffset = m_methodBodies.AddMethodBody(il);

        CodexType? oi = outerIn;
        CodexType? oo = outerOut;
        BlobBuilder sig = new();
        new BlobEncoder(sig).MethodSignature(SignatureCallingConvention.Default, 0, isInstanceMethod: true)
            .Parameters(1,
                r =>
                {
                    if (oo is null) r.Type().Object();
                    else if (oo is VoidType or NothingType) r.Void();
                    else EncodeType(r.Type(), oo);
                },
                ps =>
                {
                    SignatureTypeEncoder enc = ps.AddParameter().Type();
                    if (oi is null) enc.Object();
                    else EncodeType(enc, oi);
                });

        MethodDefinitionHandle invoke = m_metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig,
            MethodImplAttributes.IL | MethodImplAttributes.Managed,
            m_metadata.GetOrAddString("Invoke"),
            m_metadata.GetOrAddBlob(sig),
            bodyOffset,
            default);

        m_metadata.AddParameter(ParameterAttributes.None,
            m_metadata.GetOrAddString("x"), 1);

        return invoke;
    }

    static string SanitizeForTypeName(string s)
        => new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    void EmitOneClosureClass(ClosureKey key, int arity,
        ImmutableArray<CodexType> paramTypes, CodexType resultType)
    {
        string typeName = $"__{SanitizeName(key.MethodName)}_closure{key.CapturedCount}";
        int k = key.CapturedCount;
        bool isLeaf = k + 1 == arity;
        ImmutableArray<CodexType> capturedTypes = paramTypes.Take(k).ToImmutableArray();
        CodexType invokeParam = paramTypes[k];
        // What the Invoke returns: at the leaf, the full function's result;
        // otherwise, the next Func<> in the curry chain, represented as
        // `object` at the IL level (EncodeType's fallback for FunctionType).
        CodexType invokeRet = isLeaf ? resultType : null!;

        TypeDefinitionHandle typeDef = m_metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            m_metadata.GetOrAddString(""),
            m_metadata.GetOrAddString(typeName),
            m_objectRef,
            MetadataTokens.FieldDefinitionHandle(m_metadata.GetRowCount(TableIndex.Field) + 1),
            MetadataTokens.MethodDefinitionHandle(m_metadata.GetRowCount(TableIndex.MethodDef) + 1));

        // Fields for captured args
        FieldDefinitionHandle[] fieldHandles = new FieldDefinitionHandle[capturedTypes.Length];
        for (int i = 0; i < capturedTypes.Length; i++)
        {
            BlobBuilder fsig = new();
            FieldTypeEncoder fe = new BlobEncoder(fsig).Field();
            EncodeType(fe.Type(), capturedTypes[i]);
            fieldHandles[i] = m_metadata.AddFieldDefinition(
                FieldAttributes.Public,
                m_metadata.GetOrAddString($"_a{i}"),
                m_metadata.GetOrAddBlob(fsig));
        }

        // Constructor: takes captured args, calls Object..ctor, stores to fields
        MethodDefinitionHandle ctor = EmitClosureCtor(capturedTypes, fieldHandles);

        // Invoke: takes one arg (paramTypes[k]). Leaf calls target method;
        // non-leaf cascades into closure{k+1}.
        MethodDefinitionHandle invoke = EmitClosureInvoke(
            key, arity, fieldHandles, capturedTypes, invokeParam, invokeRet, isLeaf);

        // Func<> ctor ref for wrapping THIS closure at its use site.
        // Signature: Func<paramTypes[k], invokeRet-or-object>.
        MemberReferenceHandle funcCtor = GetFuncCtorRef(
            ImmutableArray.Create(invokeParam),
            isLeaf ? resultType : null);

        m_closures[key] = new ClosureInfo(
            typeDef, ctor, invoke, funcCtor, capturedTypes,
            ImmutableArray<CodexType>.Empty, resultType);
    }

    MethodDefinitionHandle EmitClosureCtor(ImmutableArray<CodexType> capturedTypes,
        FieldDefinitionHandle[] fieldHandles)
    {
        ControlFlowBuilder cf = new();
        InstructionEncoder il = new(new BlobBuilder(), cf);

        il.LoadArgument(0);
        il.Call(m_objectCtorRef);

        for (int i = 0; i < capturedTypes.Length; i++)
        {
            il.LoadArgument(0);
            il.LoadArgument(i + 1);
            il.OpCode(ILOpCode.Stfld);
            il.Token(fieldHandles[i]);
        }

        il.OpCode(ILOpCode.Ret);

        int bodyOffset = m_methodBodies.AddMethodBody(il);

        Action<ParameterTypeEncoder>[] paramEncs = new Action<ParameterTypeEncoder>[capturedTypes.Length];
        for (int i = 0; i < capturedTypes.Length; i++)
        {
            CodexType ct = capturedTypes[i];
            paramEncs[i] = p => EncodeType(p.Type(), ct);
        }
        BlobHandle ctorSig = EncodeCtorSignature(paramEncs);

        MethodDefinitionHandle ctor = m_metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.RTSpecialName | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig,
            MethodImplAttributes.IL | MethodImplAttributes.Managed,
            m_metadata.GetOrAddString(".ctor"),
            ctorSig,
            bodyOffset,
            default);

        for (int i = 0; i < capturedTypes.Length; i++)
            m_metadata.AddParameter(ParameterAttributes.None,
                m_metadata.GetOrAddString($"a{i}"), i + 1);

        return ctor;
    }

    MethodDefinitionHandle EmitClosureInvoke(ClosureKey key, int arity,
        FieldDefinitionHandle[] fieldHandles,
        ImmutableArray<CodexType> capturedTypes,
        CodexType invokeParam,
        CodexType invokeRet,
        bool isLeaf)
    {
        ControlFlowBuilder cf = new();
        InstructionEncoder il = new(new BlobBuilder(), cf);

        if (isLeaf)
        {
            // Leaf: load all captured fields, load the invoke arg, call target.
            for (int i = 0; i < capturedTypes.Length; i++)
            {
                il.LoadArgument(0);
                il.OpCode(ILOpCode.Ldfld);
                il.Token(fieldHandles[i]);
            }
            il.LoadArgument(1);
            if (!m_definedMethods.TryGet(key.MethodName, out MethodDefinitionHandle targetMethod))
            {
                throw new InvalidOperationException(
                    $"EmitClosureInvoke: closure target '{key.MethodName}' not in m_definedMethods.");
            }
            // Route through EmitCallToMethod so generic methods get a
            // MethodSpec (all type args erased to object). Plain il.Call on
            // a generic method triggers "method not fully instantiated" at
            // JIT time.
            EmitCallToMethod(il, key.MethodName, targetMethod, ImmutableArray<IRExpr>.Empty);
        }
        else
        {
            // Non-leaf: build closure{k+1} with all current captured fields + the
            // invoke arg, ldftn its Invoke, wrap in Func<>. Cascaded so each
            // curry step is one Func`2 hop.
            ClosureKey nextKey = new(key.MethodName, key.CapturedCount + 1);
            if (!m_closures.TryGetValue(nextKey, out ClosureInfo next))
            {
                throw new InvalidOperationException(
                    $"EmitClosureInvoke: next closure {nextKey.MethodName}/cap={nextKey.CapturedCount} not emitted yet. Specs must be sorted captured-DESC.");
            }
            for (int i = 0; i < capturedTypes.Length; i++)
            {
                il.LoadArgument(0);
                il.OpCode(ILOpCode.Ldfld);
                il.Token(fieldHandles[i]);
            }
            il.LoadArgument(1);
            il.OpCode(ILOpCode.Newobj);
            il.Token(next.Ctor);
            il.OpCode(ILOpCode.Ldftn);
            il.Token(next.Invoke);
            il.OpCode(ILOpCode.Newobj);
            il.Token(next.FuncCtor);
        }

        il.OpCode(ILOpCode.Ret);

        int bodyOffset = m_methodBodies.AddMethodBody(il);

        // Invoke signature: (invokeParam) -> invokeRet-or-object, instance
        BlobBuilder msig = new();
        new BlobEncoder(msig).MethodSignature(SignatureCallingConvention.Default, 0, isInstanceMethod: true)
            .Parameters(1,
                r =>
                {
                    if (!isLeaf)
                    {
                        r.Type().Object();
                    }
                    else if (invokeRet is VoidType or NothingType)
                    {
                        r.Void();
                    }
                    else
                    {
                        EncodeType(r.Type(), invokeRet);
                    }
                },
                ps =>
                {
                    EncodeType(ps.AddParameter().Type(), invokeParam);
                });

        MethodDefinitionHandle invoke = m_metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig,
            MethodImplAttributes.IL | MethodImplAttributes.Managed,
            m_metadata.GetOrAddString("Invoke"),
            m_metadata.GetOrAddBlob(msig),
            bodyOffset,
            default);

        m_metadata.AddParameter(ParameterAttributes.None,
            m_metadata.GetOrAddString("b0"), 1);

        return invoke;
    }

    // Build Func<R1, R2, ..., TRet>.ctor(object, nativeint) MemberRef
    // (and cache the companion Invoke MemberRef). A null retType means
    // "object" — used for intermediate closures in a cascaded curry chain
    // where the closure's Invoke returns the next Func<> as object.
    MemberReferenceHandle GetFuncCtorRef(ImmutableArray<CodexType> argTypes, CodexType? retType)
    {
        CodexType? effRet = retType is null ? null : UnwrapEffectful(retType);
        string key = FuncCacheKey(argTypes, effRet);
        if (m_funcCache.TryGetValue(key, out (TypeSpecificationHandle TypeSpec, MemberReferenceHandle Invoke) cached))
        {
            // ctor shares the TypeSpec; return ctor from cache via second lookup
            return GetOrAddFuncCtor(key, cached.TypeSpec, argTypes, effRet);
        }

        // Emit TypeSpec for Func<argTypes..., retType>
        int arity = argTypes.Length + 1;  // args + return
        TypeReferenceHandle openRef = GetFuncOpenRef(arity);

        BlobBuilder specBlob = new();
        SignatureTypeEncoder specEnc = new BlobEncoder(specBlob).TypeSpecificationSignature();
        GenericTypeArgumentsEncoder genArgs = specEnc.GenericInstantiation(openRef, arity, isValueType: false);
        foreach (CodexType at in argTypes)
            EncodeType(genArgs.AddArgument(), at);
        if (effRet is null)
            genArgs.AddArgument().Object();
        else
            EncodeType(genArgs.AddArgument(), effRet);
        TypeSpecificationHandle typeSpec = m_metadata.AddTypeSpecification(
            m_metadata.GetOrAddBlob(specBlob));

        // Func<...>.Invoke signature uses generic-type-parameter placeholders
        BlobBuilder invSig = new();
        new BlobEncoder(invSig).MethodSignature(SignatureCallingConvention.Default, 0, isInstanceMethod: true)
            .Parameters(argTypes.Length,
                r =>
                {
                    SignatureTypeEncoder re = r.Type();
                    re.Builder.WriteByte((byte)SignatureTypeCode.GenericTypeParameter);
                    re.Builder.WriteCompressedInteger(argTypes.Length);  // TRet = last genarg
                },
                ps =>
                {
                    for (int i = 0; i < argTypes.Length; i++)
                    {
                        SignatureTypeEncoder pe = ps.AddParameter().Type();
                        pe.Builder.WriteByte((byte)SignatureTypeCode.GenericTypeParameter);
                        pe.Builder.WriteCompressedInteger(i);
                    }
                });
        MemberReferenceHandle invokeRef = m_metadata.AddMemberReference(
            typeSpec, m_metadata.GetOrAddString("Invoke"), m_metadata.GetOrAddBlob(invSig));

        m_funcCache[key] = (typeSpec, invokeRef);
        return GetOrAddFuncCtor(key, typeSpec, argTypes, effRet);
    }

    readonly Dictionary<string, MemberReferenceHandle> m_funcCtorCache = new();

    MemberReferenceHandle GetOrAddFuncCtor(string key, TypeSpecificationHandle typeSpec,
        ImmutableArray<CodexType> argTypes, CodexType? retType)
    {
        if (m_funcCtorCache.TryGetValue(key, out MemberReferenceHandle cached))
            return cached;

        // Delegate ctor signature is (object, nativeint) — same across all Func<>
        BlobBuilder ctorSig = new();
        new BlobEncoder(ctorSig).MethodSignature(SignatureCallingConvention.Default, 0, isInstanceMethod: true)
            .Parameters(2,
                r => r.Void(),
                ps =>
                {
                    ps.AddParameter().Type().Object();
                    ps.AddParameter().Type().IntPtr();
                });

        MemberReferenceHandle ctor = m_metadata.AddMemberReference(
            typeSpec, m_metadata.GetOrAddString(".ctor"), m_metadata.GetOrAddBlob(ctorSig));
        m_funcCtorCache[key] = ctor;
        return ctor;
    }

    MemberReferenceHandle GetFuncInvokeRef(ImmutableArray<CodexType> argTypes, CodexType? retType)
    {
        CodexType? effRet = retType is null ? null : UnwrapEffectful(retType);
        string key = FuncCacheKey(argTypes, effRet);
        if (m_funcCache.TryGetValue(key, out (TypeSpecificationHandle TypeSpec, MemberReferenceHandle Invoke) cached))
            return cached.Invoke;

        // Populate the cache then look up again
        GetFuncCtorRef(argTypes, effRet);
        return m_funcCache[key].Invoke;
    }

    static string FuncCacheKey(ImmutableArray<CodexType> argTypes, CodexType? retType)
    {
        string args = string.Join(",", argTypes.Select(FuncTypeKey));
        return $"{args}->{(retType is null ? "object" : FuncTypeKey(retType))}";
    }

    static string FuncTypeKey(CodexType t) => t switch
    {
        IntegerType => "i64",
        NumberType => "f64",
        BooleanType => "bool",
        TextType => "string",
        CharType => "char",
        VoidType or NothingType => "void",
        RecordType rec => $"r:{rec.TypeName.Value}",
        SumType sum => $"s:{sum.TypeName.Value}",
        ConstructedType ct => $"c:{ct.Constructor.Value}",
        ListType lt => $"l:{FuncTypeKey(lt.Element)}",
        FunctionType ft => $"f:{FuncTypeKey(ft.Parameter)}->{FuncTypeKey(ft.Return)}",
        _ => "object"
    };
}
