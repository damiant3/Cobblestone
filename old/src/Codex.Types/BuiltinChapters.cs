using System.Collections.Immutable;
using Codex.Ast;
using Codex.Core;

namespace Codex.Types;

public sealed record BuiltinChapter(
    string Name,
    ImmutableArray<(string Name, CodexType Type)> TypedBindings,
    string? EffectSource,
    ImmutableArray<TypeDef> TypeDefs = default);

public static class BuiltinChapters
{
    public static ImmutableArray<BuiltinChapter> All => s_all;

    public static BuiltinChapter? LookupByName(string name) =>
        s_byName.TryGetValue(name, out BuiltinChapter? bc) ? bc : null;

    public static Set<string> AllNames()
    {
        Set<string> set = Set<string>.s_empty;
        foreach (BuiltinChapter c in s_all)
        {
            foreach ((string name, _) in c.TypedBindings)
                set = set.Add(name);
        }
        return set;
    }

    public static void BindTypedInto(TypeEnvironment env, out TypeEnvironment result)
    {
        TypeEnvironment e = env;
        foreach (BuiltinChapter c in s_all)
        {
            foreach ((string name, CodexType type) in c.TypedBindings)
                e = e.Bind(name, type);
        }
        result = e;
    }

    public static ImmutableArray<string> EffectSources()
    {
        ImmutableArray<string>.Builder b = ImmutableArray.CreateBuilder<string>();
        foreach (BuiltinChapter c in s_all)
        {
            if (c.EffectSource is not null)
                b.Add(c.EffectSource);
        }
        return b.ToImmutable();
    }

    // Returns (chapter-name, type-defs) pairs for every builtin chapter that
    // declares record/variant types. BuiltinTypes wraps each list in a
    // synthetic Chapter so `cites Codex chapter X` can inject X's type defs
    // into the citing chapter's scope via the normal ResolvedChapter path.
    public static ImmutableArray<(string ChapterName, ImmutableArray<TypeDef> TypeDefs)> TypeDefLists()
    {
        ImmutableArray<(string, ImmutableArray<TypeDef>)>.Builder b =
            ImmutableArray.CreateBuilder<(string, ImmutableArray<TypeDef>)>();
        foreach (BuiltinChapter c in s_all)
        {
            if (!c.TypeDefs.IsDefaultOrEmpty)
                b.Add((c.Name, c.TypeDefs));
        }
        return b.ToImmutable();
    }

    static CodexType Fn(CodexType a, CodexType b) => new FunctionType(a, b);
    static CodexType Fn(CodexType a, CodexType b, CodexType c) => new FunctionType(a, new FunctionType(b, c));
    static CodexType Fn(CodexType a, CodexType b, CodexType c, CodexType d) =>
        new FunctionType(a, new FunctionType(b, new FunctionType(c, d)));
    static readonly CodexType s_int = IntegerType.s_instance;
    static readonly CodexType s_text = TextType.s_instance;
    static readonly CodexType s_bool = BooleanType.s_instance;
    static readonly CodexType s_char = CharType.s_instance;
    static readonly CodexType s_nothing = NothingType.s_instance;

    static readonly CodexType s_intIntInt = Fn(s_int, s_int, s_int);

    // ProcessResult — nominal record returned by run-process-full. Built as
    // a pair: s_processResult is the RecordType for run-process-full's
    // signature; s_processResultTypeDef is the AST TypeDef wrapped in the
    // synthetic Process chapter so `cites Codex chapter Process` brings the
    // name ProcessResult into the citing chapter's scope. TypeChecker's
    // structural equality on RecordType unifies the two. BuildRecordPair
    // makes this a single edit point — if future builtin chapters grow
    // nominal types, they use the same helper.
    static readonly SourceSpan s_builtinSpan = SourceSpan.Single(0, 1, 1, "<builtin>");

    static readonly (CodexType Type, TypeDef Def) s_processResultPair = BuildRecordPair(
        "ProcessResult",
        [
            ("stdout", s_text, "Text"),
            ("stderr", s_text, "Text"),
            ("exit-code", s_int, "Integer"),
        ]);

    static readonly CodexType s_processResult = s_processResultPair.Type;
    static readonly TypeDef s_processResultTypeDef = s_processResultPair.Def;

    // Public accessor so downstream sites (Lowering.cs's builtin-type map,
    // emitter lookups) can reuse the one canonical RecordType instead of
    // reconstructing a structurally-equal copy. Keeps the schema single-
    // sourced — add/remove a field here and every consumer picks it up.
    public static CodexType ProcessResultType => s_processResult;

    // Produces a structurally-matched (RecordType, RecordTypeDef) pair from
    // a single field list. Each field carries its runtime CodexType (for the
    // RecordType) and the surface type-name (for the TypeDef's NamedTypeExpr,
    // which the resolver resolves back to the same CodexType). Keep them in
    // sync by construction, not by hand.
    static (CodexType Type, TypeDef Def) BuildRecordPair(
        string typeName,
        ImmutableArray<(string FieldName, CodexType FieldType, string FieldTypeName)> fields)
    {
        Name tn = new(typeName);
        RecordType ty = new(tn, [],
            [.. fields.Select(f => new RecordFieldType(new Name(f.FieldName), f.FieldType))]);
        RecordTypeDef def = new(tn, [],
            [.. fields.Select(f => new RecordFieldDef(
                new Name(f.FieldName),
                new NamedTypeExpr(new Name(f.FieldTypeName), s_builtinSpan),
                s_builtinSpan))],
            s_builtinSpan);
        return (ty, def);
    }

    static readonly ImmutableArray<BuiltinChapter> s_all = BuildAll();
    static readonly IReadOnlyDictionary<string, BuiltinChapter> s_byName = BuildIndex(s_all);

    static IReadOnlyDictionary<string, BuiltinChapter> BuildIndex(ImmutableArray<BuiltinChapter> all)
    {
        Dictionary<string, BuiltinChapter> d = new(all.Length);
        foreach (BuiltinChapter c in all) d[c.Name] = c;
        return d;
    }

    static ImmutableArray<BuiltinChapter> BuildAll()
    {
        ImmutableArray<BuiltinChapter>.Builder b = ImmutableArray.CreateBuilder<BuiltinChapter>();

        // General — odd-ones-out bucket (show today; Time/Random will move here once effect-typing is decoupled).
        b.Add(new BuiltinChapter("General",
            [
                ("show", new ForAllType(0, Fn(new TypeVariable(0), s_text))),
            ],
            EffectSource: null));

        // Console
        b.Add(new BuiltinChapter("Console",
            TypedBindings: [],
            EffectSource: """
                effect Console where
                  print-line : Text -> [Console] Nothing
                  read-line  : [Console] Text
                """));

        // Files — pure file-exists/list-files + effect FileSystem
        b.Add(new BuiltinChapter("Files",
            [
                ("file-exists", Fn(s_text, s_bool)),
                ("list-files", Fn(s_text, s_text, new ListType(s_text))),
            ],
            EffectSource: """
                effect FileSystem where
                  open-file  : Text -> [FileSystem] linear FileHandle
                  read-all   : linear FileHandle -> [FileSystem] Pair Text (linear FileHandle)
                  close-file : linear FileHandle -> [FileSystem] Nothing
                  read-file  : Text -> [FileSystem] Text
                  write-file : Text -> Text -> [FileSystem] Nothing
                  write-binary : List Integer -> [FileSystem] Nothing
                """));

        // Process — all 5 ops are observably effectful: the program's argv
        // and environment can change between calls (via parent setenv, via
        // chdir), and run-process / run-process-full literally shell out.
        // Typing them pure (as they were pre-2026-04-23) was a footgun — a
        // single run-process call inside a `let` produced silently-wrong
        // purity analysis, and effect-propagation up call chains was lost.
        // Now every op is in the Process effect row; callers must either
        // bind with `x <- ...` inside an act block or declare [Process] on
        // their signature. ProcessResult is declared inline via TypeDefs so
        // a single `cites Codex chapter Process` exposes both the ops and
        // the record type run-process-full returns.
        b.Add(new BuiltinChapter("Process",
            TypedBindings: [],
            EffectSource: """
                effect Process where
                  get-args         : [Process] List Text
                  get-env          : Text -> [Process] Text
                  current-dir      : [Process] Text
                  run-process      : Text -> Text -> [Process] Text
                  run-process-full : Text -> Text -> [Process] ProcessResult
                  process-exit     : Integer -> [Process] Nothing
                """,
            TypeDefs: [s_processResultTypeDef]));

        // Text
        b.Add(new BuiltinChapter("Text",
            [
                ("text-length", Fn(s_text, s_int)),
                ("substring", Fn(s_text, s_int, s_int, s_text)),
                ("text-replace", Fn(s_text, s_text, s_text, s_text)),
                ("text-split", Fn(s_text, s_text, new ListType(s_text))),
                ("text-contains", Fn(s_text, s_text, s_bool)),
                ("text-starts-with", Fn(s_text, s_text, s_bool)),
                ("text-compare", Fn(s_text, s_text, s_int)),
                ("text-concat-list", Fn(new ListType(s_text), s_text)),
                ("text-to-integer", Fn(s_text, s_int)),
                ("text-to-double-bits", Fn(s_text, s_int)),
                ("integer-to-text", Fn(s_int, s_text)),
            ],
            EffectSource: null));

        // Characters
        b.Add(new BuiltinChapter("Characters",
            [
                ("char-at", Fn(s_text, s_int, s_char)),
                ("char-to-text", Fn(s_char, s_text)),
                ("char-code", Fn(s_char, s_int)),
                ("char-code-at", Fn(s_text, s_int, s_int)),
                ("code-to-char", Fn(s_int, s_char)),
                ("is-letter", Fn(s_char, s_bool)),
                ("is-digit", Fn(s_char, s_bool)),
                ("is-whitespace", Fn(s_char, s_bool)),
            ],
            EffectSource: null));

        // Numbers
        b.Add(new BuiltinChapter("Numbers",
            [
                ("negate", Fn(s_int, s_int)),
                ("abs", Fn(s_int, s_int)),
                ("min", s_intIntInt),
                ("max", s_intIntInt),
                ("int-mod", s_intIntInt),
            ],
            EffectSource: null));

        // Bitwise
        b.Add(new BuiltinChapter("Bitwise",
            [
                ("bit-and", s_intIntInt),
                ("bit-or", s_intIntInt),
                ("bit-xor", s_intIntInt),
                ("bit-shl", s_intIntInt),
                ("bit-shr", s_intIntInt),
                ("bit-not", Fn(s_int, s_int)),
            ],
            EffectSource: null));

        // Lists
        TypeVariable la = new(0);
        TypeVariable lb = new(1);
        b.Add(new BuiltinChapter("Lists",
            [
                ("list-length", new ForAllType(0, Fn(new ListType(la), s_int))),
                ("list-at", new ForAllType(0, Fn(new ListType(la), s_int, la))),
                ("list-insert-at", new ForAllType(0,
                    Fn(new ListType(la), s_int, la, new ListType(la)))),
                ("list-set-at", new ForAllType(0,
                    Fn(new ListType(la), s_int, la, new ListType(la)))),
                ("list-snoc", new ForAllType(0,
                    Fn(new ListType(la), la, new ListType(la)))),
                ("map", BuildMapType()),
            ],
            EffectSource: null));

        // Concurrency — fork/await/par/race are pure-typed (ForAll + FunctionType); run-state too.
        b.Add(new BuiltinChapter("Concurrency",
            BuildConcurrencyBindings(),
            EffectSource: null));

        // Time
        b.Add(new BuiltinChapter("Time",
            TypedBindings: [],
            EffectSource: """
                effect Time where
                  now : [Time] Integer
                """));

        // Random
        b.Add(new BuiltinChapter("Random",
            TypedBindings: [],
            EffectSource: """
                effect Random where
                  random-integer : Integer -> Integer -> [Random] Integer
                """));

        // Graphics (existing effect is named 'Display' — kept for now; chapter renamed to Graphics)
        b.Add(new BuiltinChapter("Graphics",
            TypedBindings: [],
            EffectSource: """
                effect Display where
                  draw-text  : Text -> Integer -> Integer -> [Display] Nothing
                  draw-rect  : Integer -> Integer -> Integer -> Integer -> [Display] Nothing
                  clear      : [Display] Nothing
                  set-pixel  : Integer -> Integer -> Integer -> [Display] Nothing
                """));

        // Camera
        b.Add(new BuiltinChapter("Camera",
            TypedBindings: [],
            EffectSource: """
                effect Camera where
                  capture     : [Camera] Text
                  capture-raw : Integer -> Integer -> [Camera] Text
                """));

        // Microphone
        b.Add(new BuiltinChapter("Microphone",
            TypedBindings: [],
            EffectSource: """
                effect Microphone where
                  listen   : Integer -> [Microphone] Text
                  is-quiet : [Microphone] Boolean
                """));

        // Location
        b.Add(new BuiltinChapter("Location",
            TypedBindings: [],
            EffectSource: """
                effect Location where
                  locate    : [Location] Pair Integer Integer
                  altitude  : [Location] Integer
                """));

        // Sensors
        b.Add(new BuiltinChapter("Sensors",
            TypedBindings: [],
            EffectSource: """
                effect Sensors where
                  accelerometer : [Sensors] Pair Integer (Pair Integer Integer)
                  gyroscope     : [Sensors] Pair Integer (Pair Integer Integer)
                  barometer     : [Sensors] Integer
                  light-level   : [Sensors] Integer
                """));

        // Identity
        b.Add(new BuiltinChapter("Identity",
            TypedBindings: [],
            EffectSource: """
                effect Identity where
                  authenticate : [Identity] Text
                  current-user : [Identity] Text
                """));

        // Network
        b.Add(new BuiltinChapter("Network",
            TypedBindings: [],
            EffectSource: """
                effect Network where
                  fetch       : Text -> [Network] Text
                  post        : Text -> Text -> [Network] Text
                  resolve-dns : Text -> [Network] Text
                """));

        // State — dead (user said drop) but preserved at Step 1 for behavior parity; revisit in Step 2.
        b.Add(new BuiltinChapter("State",
            TypedBindings: [],
            EffectSource: """
                effect State where
                  get-state : [State] s
                  set-state : s -> [State] Nothing
                """));

        // Runtime (hidden — will be __-prefixed in Step 3; kept as-is now to preserve parity).
        b.Add(new BuiltinChapter("Runtime",
            BuildRuntimeBindings(),
            EffectSource: null));

        return b.ToImmutable();
    }

    static CodexType BuildMapType()
    {
        // map : ForAll e. ForAll a. ForAll b. (a -> [e] b) -> List a -> [e] List b
        EffectRowVariable rowE = new(100);
        TypeVariable a = new(101);
        TypeVariable bv = new(102);
        EffectfulType fnRet = new([], bv, rowE);
        FunctionType fn = new(a, fnRet);
        EffectfulType mapRet = new([], new ListType(bv), rowE);
        return new ForAllType(100,
            new ForAllType(101,
                new ForAllType(102,
                    new FunctionType(fn,
                        new FunctionType(new ListType(a), mapRet)))));
    }

    static ImmutableArray<(string, CodexType)> BuildConcurrencyBindings()
    {
        ImmutableArray<(string, CodexType)>.Builder b =
            ImmutableArray.CreateBuilder<(string, CodexType)>();

        // run-state : s -> [State s, e] a -> [e] a
        TypeVariable stateS = new(200);
        TypeVariable stateA = new(201);
        EffectRowVariable stateE = new(202);
        EffectfulType runCompType = new(
            [new EffectType(new Name("State"))], stateA, stateE);
        EffectfulType runStateReturn = new([], stateA, stateE);
        b.Add(("run-state", new ForAllType(200,
            new ForAllType(201,
                new ForAllType(202,
                    new FunctionType(stateS,
                        new FunctionType(runCompType, runStateReturn)))))));

        // fork, await, par, race — all carry [Concurrent]
        EffectType concurrentEffect = new(new Name("Concurrent"));
        ImmutableArray<EffectType> concurrentEffects = [concurrentEffect];

        TypeVariable forkA = new(300);
        ConstructedType taskOfA = new(new Name("Task"), [forkA]);
        EffectfulType forkReturn = new(concurrentEffects, taskOfA);
        b.Add(("fork", new ForAllType(300,
            new FunctionType(new FunctionType(s_nothing, forkA), forkReturn))));

        EffectfulType awaitReturn = new(concurrentEffects, forkA);
        b.Add(("await", new ForAllType(300,
            new FunctionType(taskOfA, awaitReturn))));

        TypeVariable parA = new(310);
        TypeVariable parB = new(311);
        EffectfulType parReturn = new(concurrentEffects, new ListType(parB));
        b.Add(("par", new ForAllType(310,
            new ForAllType(311,
                new FunctionType(new FunctionType(parA, parB),
                    new FunctionType(new ListType(parA), parReturn))))));

        TypeVariable raceA = new(320);
        EffectfulType raceReturn = new(concurrentEffects, raceA);
        b.Add(("race", new ForAllType(320,
            new FunctionType(new ListType(new FunctionType(s_nothing, raceA)), raceReturn))));

        return b.ToImmutable();
    }

    static ImmutableArray<(string, CodexType)> BuildRuntimeBindings()
    {
        TypeVariable rsRec = new(0);
        TypeVariable rsFld = new(1);
        CodexType recordSet = new ForAllType(0,
            new ForAllType(1,
                Fn(rsRec, s_text, rsFld, rsRec)));

        TypeVariable lwcA = new(0);
        CodexType listWithCap = new ForAllType(0,
            Fn(s_int, new ListType(lwcA)));

        CodexType linkedListEmpty = Fn(s_int, new LinkedListType(new ListType(s_int)));
        CodexType linkedListPush = Fn(
            new LinkedListType(new ListType(s_int)),
            new ListType(s_int),
            new LinkedListType(new ListType(s_int)));
        CodexType linkedListToList = Fn(
            new LinkedListType(new ListType(s_int)),
            new ListType(new ListType(s_int)));

        return
        [
            (Builtins.RecordSet, recordSet),
            (Builtins.HeapSave, s_int),
            (Builtins.HeapRestore, Fn(s_int, s_nothing)),
            (Builtins.HeapAdvance, Fn(s_int, s_nothing)),
            (Builtins.ListWithCapacity, listWithCap),
            (Builtins.BufWriteByte, Fn(s_int, s_int, s_int, s_int)),
            (Builtins.BufWriteBytes, Fn(s_int, s_int, new ListType(s_int), s_int)),
            (Builtins.BufReadBytes, Fn(s_int, s_int, s_int, new ListType(s_int))),
            (Builtins.LinkedListEmpty, linkedListEmpty),
            (Builtins.LinkedListPush, linkedListPush),
            (Builtins.LinkedListToList, linkedListToList),
        ];
    }
}
