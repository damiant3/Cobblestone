using Codex.Core;

namespace Codex.Types;

public sealed class TypeEnvironment
{
    readonly Map<string, CodexType> m_bindings;

    public TypeEnvironment() : this(Map<string, CodexType>.s_empty)
    {
    }

    TypeEnvironment(Map<string, CodexType> bindings)
    {
        m_bindings = bindings;
    }

    public TypeEnvironment Bind(string name, CodexType type) => new(m_bindings.Set(name, type));

    public TypeEnvironment Bind(Name name, CodexType type) => Bind(name.Value, type);

    public CodexType? Lookup(string name) => m_bindings[name];

    public CodexType? Lookup(Name name) => Lookup(name.Value);

    public bool Contains(string name) => m_bindings.ContainsKey(name);

    public static TypeEnvironment WithBuiltins()
    {
        TypeEnvironment env = new();
        env = env.Bind("show", new ForAllType(0,
            new FunctionType(new TypeVariable(0), TextType.s_instance)));
        env = env.Bind("negate", new FunctionType(IntegerType.s_instance, IntegerType.s_instance));

        // File I/O builtins (pure signatures — effect-annotated ops are loaded from prelude)
        env = env.Bind("file-exists", new FunctionType(TextType.s_instance, BooleanType.s_instance));
        env = env.Bind("list-files", new FunctionType(TextType.s_instance,
            new FunctionType(TextType.s_instance,
                new ListType(TextType.s_instance))));

        // String operations
        env = env.Bind("text-split", new FunctionType(TextType.s_instance,
            new FunctionType(TextType.s_instance,
                new ListType(TextType.s_instance))));
        env = env.Bind("text-contains", new FunctionType(TextType.s_instance,
            new FunctionType(TextType.s_instance, BooleanType.s_instance)));
        env = env.Bind("text-starts-with", new FunctionType(TextType.s_instance,
            new FunctionType(TextType.s_instance, BooleanType.s_instance)));

        // System builtins
        env = env.Bind("get-args", new ListType(TextType.s_instance));
        env = env.Bind("get-env", new FunctionType(TextType.s_instance, TextType.s_instance));
        env = env.Bind("current-dir", TextType.s_instance);
        env = env.Bind("run-process", new FunctionType(TextType.s_instance,
            new FunctionType(TextType.s_instance, TextType.s_instance)));

        env = env.Bind("char-at", new FunctionType(TextType.s_instance,
            new FunctionType(IntegerType.s_instance, TextType.s_instance)));
        env = env.Bind("text-length", new FunctionType(TextType.s_instance, IntegerType.s_instance));
        env = env.Bind("substring", new FunctionType(TextType.s_instance,
            new FunctionType(IntegerType.s_instance,
                new FunctionType(IntegerType.s_instance, TextType.s_instance))));
        env = env.Bind("is-letter", new FunctionType(TextType.s_instance, BooleanType.s_instance));
        env = env.Bind("is-digit", new FunctionType(TextType.s_instance, BooleanType.s_instance));
        env = env.Bind("is-whitespace", new FunctionType(TextType.s_instance, BooleanType.s_instance));
        env = env.Bind("text-to-integer", new FunctionType(TextType.s_instance, IntegerType.s_instance));
        env = env.Bind("integer-to-text", new FunctionType(IntegerType.s_instance, TextType.s_instance));
        env = env.Bind("text-replace", new FunctionType(TextType.s_instance,
            new FunctionType(TextType.s_instance,
                new FunctionType(TextType.s_instance, TextType.s_instance))));
        env = env.Bind("char-code", new FunctionType(TextType.s_instance, IntegerType.s_instance));
        env = env.Bind("char-code-at", new FunctionType(TextType.s_instance,
            new FunctionType(IntegerType.s_instance, IntegerType.s_instance)));
        env = env.Bind("code-to-char", new FunctionType(IntegerType.s_instance, TextType.s_instance));

        env = env.Bind("list-length", new ForAllType(0,
            new FunctionType(new ListType(new TypeVariable(0)), IntegerType.s_instance)));
        env = env.Bind("list-at", new ForAllType(0,
            new FunctionType(new ListType(new TypeVariable(0)),
                new FunctionType(IntegerType.s_instance, new TypeVariable(0)))));
        
        // map : (a → [e] b) → List a → [e] List b
        // ForAll e. ForAll a. ForAll b. ...
        // e (id=100) is an EffectRowVariable, a (id=101) and b (id=102) are TypeVariables
        EffectRowVariable mapRowVar = new(100);
        TypeVariable mapA = new(101);
        TypeVariable mapB = new(102);
        EffectfulType mapFnReturn = new([], mapB, mapRowVar);
        FunctionType mapFn = new(mapA, mapFnReturn);
        EffectfulType mapReturn = new([], new ListType(mapB), mapRowVar);
        CodexType mapType = new ForAllType(100,
            new ForAllType(101,
                new ForAllType(102,
                    new FunctionType(mapFn,
                        new FunctionType(new ListType(mapA), mapReturn)))));
        env = env.Bind("map", mapType);

        // run-state : s -> [State s, e] a -> [e] a  (effect handler, not an operation)
        TypeVariable stateS = new(200);
        TypeVariable stateA = new(201);
        EffectRowVariable stateE = new(202);
        EffectfulType runCompType = new(
            [new EffectType(new Name("State"))], stateA, stateE);
        EffectfulType runStateReturn = new([], stateA, stateE);
        env = env.Bind("run-state", new ForAllType(200,
            new ForAllType(201,
                new ForAllType(202,
                    new FunctionType(stateS,
                        new FunctionType(runCompType, runStateReturn))))));

        // Structured concurrency (Camp III-C)
        TypeVariable forkA = new(300);
        ConstructedType taskOfA = new(new Name("Task"), [forkA]);
        env = env.Bind("fork", new ForAllType(300,
            new FunctionType(new FunctionType(NothingType.s_instance, forkA), taskOfA)));
        env = env.Bind("await", new ForAllType(300,
            new FunctionType(taskOfA, forkA)));

        TypeVariable parA = new(310);
        TypeVariable parB = new(311);
        env = env.Bind("par", new ForAllType(310,
            new ForAllType(311,
                new FunctionType(new FunctionType(parA, parB),
                    new FunctionType(new ListType(parA), new ListType(parB))))));

        TypeVariable raceA = new(320);
        env = env.Bind("race", new ForAllType(320,
            new FunctionType(new ListType(new FunctionType(NothingType.s_instance, raceA)), raceA)));

        return env;
    }
}
