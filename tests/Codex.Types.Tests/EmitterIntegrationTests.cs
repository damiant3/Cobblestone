using Xunit;

namespace Codex.Types.Tests;

public class EmitterIntegrationTests // this FILE IS LOCKED.  Use another.
{
    static string ReadSample(string name)
    {
        string path = Path.Combine(FindSamplesDir(), name);
        return File.ReadAllText(path);
    }

    static string FindSamplesDir()
    {
        string dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "samples");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir)!;
        }
        throw new DirectoryNotFoundException("Cannot find samples/ directory");
    }

    // ── hello.codex ────────────────────────────────────────────

    [Fact]
    public void Hello_emits_csharp()
    {
        string source = ReadSample("hello.codex");
        string? cs = Helpers.CompileToCS(source, "hello");
        Assert.NotNull(cs);
        Assert.Contains("square", cs);
    }

#if LEGACY_EMITTERS
    [Fact]
    public void Hello_emits_javascript()
    {
        string source = ReadSample("hello.codex");
        string? js = Helpers.CompileToJS(source, "hello");
        Assert.NotNull(js);
        Assert.Contains("function square", js);
        Assert.Contains("5n", js);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void Hello_emits_rust()
    {
        string source = ReadSample("hello.codex");
        string? rs = Helpers.CompileToRust(source, "hello");
        Assert.NotNull(rs);
        Assert.Contains("fn square", rs);
        Assert.Contains("i64", rs);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void Hello_emits_python()
    {
        string source = ReadSample("hello.codex");
        string? py = Helpers.CompileToPython(source, "hello");
        Assert.NotNull(py);
        Assert.Contains("def square", py);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void Hello_emits_cpp()
    {
        string source = ReadSample("hello.codex");
        string? cpp = Helpers.CompileToCpp(source, "hello");
        Assert.NotNull(cpp);
        Assert.Contains("int64_t square", cpp);
        Assert.Contains("std::cout", cpp);
    }

    [Fact]
    public void Hello_emits_go()
    {
        string source = ReadSample("hello.codex");
        string? go = Helpers.CompileToGo(source, "hello");
        Assert.NotNull(go);
        Assert.Contains("func square", go);
        Assert.Contains("package main", go);
    }

    [Fact]
    public void Hello_emits_java()
    {
        string source = ReadSample("hello.codex");
        string? java = Helpers.CompileToJava(source, "hello");
        Assert.NotNull(java);
        Assert.Contains("long square", java);
        Assert.Contains("public class", java);
    }
#endif

    // ── factorial.codex ────────────────────────────────────────

    [Fact]
    public void Factorial_emits_csharp()
    {
        string source = ReadSample("factorial.codex");
        string? cs = Helpers.CompileToCS(source, "factorial");
        Assert.NotNull(cs);
        Assert.Contains("factorial", cs);
    }

#if LEGACY_EMITTERS
    [Fact]
    public void Factorial_emits_javascript()
    {
        string source = ReadSample("factorial.codex");
        string? js = Helpers.CompileToJS(source, "factorial");
        Assert.NotNull(js);
        Assert.Contains("function factorial", js);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void Factorial_emits_rust()
    {
        string source = ReadSample("factorial.codex");
        string? rs = Helpers.CompileToRust(source, "factorial");
        Assert.NotNull(rs);
        Assert.Contains("fn factorial", rs);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void Factorial_emits_python()
    {
        string source = ReadSample("factorial.codex");
        string? py = Helpers.CompileToPython(source, "factorial");
        Assert.NotNull(py);
        Assert.Contains("def factorial", py);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void Factorial_emits_cpp()
    {
        string source = ReadSample("factorial.codex");
        string? cpp = Helpers.CompileToCpp(source, "factorial");
        Assert.NotNull(cpp);
        Assert.Contains("int64_t factorial", cpp);
    }

    [Fact]
    public void Factorial_emits_go()
    {
        string source = ReadSample("factorial.codex");
        string? go = Helpers.CompileToGo(source, "factorial");
        Assert.NotNull(go);
        Assert.Contains("func factorial", go);
    }

    [Fact]
    public void Factorial_emits_java()
    {
        string source = ReadSample("factorial.codex");
        string? java = Helpers.CompileToJava(source, "factorial");
        Assert.NotNull(java);
        Assert.Contains("long factorial", java);
    }
#endif

    // ── fibonacci.codex ────────────────────────────────────────

    [Fact]
    public void Fibonacci_emits_csharp()
    {
        string source = ReadSample("fibonacci.codex");
        string? cs = Helpers.CompileToCS(source, "fibonacci");
        Assert.NotNull(cs);
        Assert.Contains("fib", cs);
    }

#if LEGACY_EMITTERS
    [Fact]
    public void Fibonacci_emits_javascript()
    {
        string source = ReadSample("fibonacci.codex");
        string? js = Helpers.CompileToJS(source, "fibonacci");
        Assert.NotNull(js);
        Assert.Contains("function fib", js);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void Fibonacci_emits_rust()
    {
        string source = ReadSample("fibonacci.codex");
        string? rs = Helpers.CompileToRust(source, "fibonacci");
        Assert.NotNull(rs);
        Assert.Contains("fn fib", rs);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void Fibonacci_emits_python()
    {
        string source = ReadSample("fibonacci.codex");
        string? py = Helpers.CompileToPython(source, "fibonacci");
        Assert.NotNull(py);
        Assert.Contains("def fib", py);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void Fibonacci_emits_cpp()
    {
        string source = ReadSample("fibonacci.codex");
        string? cpp = Helpers.CompileToCpp(source, "fibonacci");
        Assert.NotNull(cpp);
        Assert.Contains("int64_t fib", cpp);
    }

    [Fact]
    public void Fibonacci_emits_go()
    {
        string source = ReadSample("fibonacci.codex");
        string? go = Helpers.CompileToGo(source, "fibonacci");
        Assert.NotNull(go);
        Assert.Contains("func fib", go);
    }

    [Fact]
    public void Fibonacci_emits_java()
    {
        string source = ReadSample("fibonacci.codex");
        string? java = Helpers.CompileToJava(source, "fibonacci");
        Assert.NotNull(java);
        Assert.Contains("long fib", java);
    }
#endif

    // ── greeting.codex ─────────────────────────────────────────

    [Fact]
    public void Greeting_emits_csharp()
    {
        string source = ReadSample("greeting.codex");
        string? cs = Helpers.CompileToCS(source, "greeting");
        Assert.NotNull(cs);
        Assert.Contains("greeting", cs);
    }

#if LEGACY_EMITTERS
    [Fact]
    public void Greeting_emits_javascript()
    {
        string source = ReadSample("greeting.codex");
        string? js = Helpers.CompileToJS(source, "greeting");
        Assert.NotNull(js);
        Assert.Contains("function greeting", js);
        Assert.Contains("\"Hello, \"", js);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void Greeting_emits_rust()
    {
        string source = ReadSample("greeting.codex");
        string? rs = Helpers.CompileToRust(source, "greeting");
        Assert.NotNull(rs);
        Assert.Contains("fn greeting", rs);
        Assert.Contains("format!", rs);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void Greeting_emits_python()
    {
        string source = ReadSample("greeting.codex");
        string? py = Helpers.CompileToPython(source, "greeting");
        Assert.NotNull(py);
        Assert.Contains("def greeting", py);
        Assert.Contains("\"Hello, \"", py);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void Greeting_emits_cpp()
    {
        string source = ReadSample("greeting.codex");
        string? cpp = Helpers.CompileToCpp(source, "greeting");
        Assert.NotNull(cpp);
        Assert.Contains("std::string greeting", cpp);
    }

    [Fact]
    public void Greeting_emits_go()
    {
        string source = ReadSample("greeting.codex");
        string? go = Helpers.CompileToGo(source, "greeting");
        Assert.NotNull(go);
        Assert.Contains("func greeting", go);
    }

    [Fact]
    public void Greeting_emits_java()
    {
        string source = ReadSample("greeting.codex");
        string? java = Helpers.CompileToJava(source, "greeting");
        Assert.NotNull(java);
        Assert.Contains("String greeting", java);
    }
#endif

    // ── shapes.codex ───────────────────────────────────────────

    [Fact]
    public void Shapes_emits_csharp()
    {
        string source = ReadSample("shapes.codex");
        string? cs = Helpers.CompileToCS(source, "shapes");
        Assert.NotNull(cs);
        Assert.Contains("Circle", cs);
        Assert.Contains("Rectangle", cs);
    }

#if LEGACY_EMITTERS
    [Fact]
    public void Shapes_emits_javascript()
    {
        string source = ReadSample("shapes.codex");
        string? js = Helpers.CompileToJS(source, "shapes");
        Assert.NotNull(js);
        Assert.Contains("function Circle", js);
        Assert.Contains("function Rectangle", js);
        Assert.Contains("tag", js);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void Shapes_emits_rust()
    {
        string source = ReadSample("shapes.codex");
        string? rs = Helpers.CompileToRust(source, "shapes");
        Assert.NotNull(rs);
        Assert.Contains("enum Shape", rs);
        Assert.Contains("Circle", rs);
        Assert.Contains("Rectangle", rs);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void Shapes_emits_python()
    {
        string source = ReadSample("shapes.codex");
        string? py = Helpers.CompileToPython(source, "shapes");
        Assert.NotNull(py);
        Assert.Contains("class Circle", py);
        Assert.Contains("class Rectangle", py);
        Assert.Contains("isinstance", py);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void Shapes_emits_cpp()
    {
        string source = ReadSample("shapes.codex");
        string? cpp = Helpers.CompileToCpp(source, "shapes");
        Assert.NotNull(cpp);
        Assert.Contains("struct Circle", cpp);
        Assert.Contains("struct Rectangle", cpp);
        Assert.Contains("std::variant", cpp);
        Assert.Contains("std::visit", cpp);
    }

    [Fact]
    public void Shapes_emits_go()
    {
        string source = ReadSample("shapes.codex");
        string? go = Helpers.CompileToGo(source, "shapes");
        Assert.NotNull(go);
        Assert.Contains("type Shape interface", go);
        Assert.Contains("type Circle struct", go);
    }

    [Fact]
    public void Shapes_emits_java()
    {
        string source = ReadSample("shapes.codex");
        string? java = Helpers.CompileToJava(source, "shapes");
        Assert.NotNull(java);
        Assert.Contains("sealed interface Shape", java);
        Assert.Contains("record Circle", java);
    }
#endif

    // ── person.codex ───────────────────────────────────────────

    [Fact]
    public void Person_emits_csharp()
    {
        string source = ReadSample("person.codex");
        string? cs = Helpers.CompileToCS(source, "person");
        Assert.NotNull(cs);
        Assert.Contains("Person", cs);
        Assert.Contains("name", cs);
    }

#if LEGACY_EMITTERS
    [Fact]
    public void Person_emits_javascript()
    {
        string source = ReadSample("person.codex");
        string? js = Helpers.CompileToJS(source, "person");
        Assert.NotNull(js);
        Assert.Contains("function Person", js);
        Assert.Contains("name", js);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void Person_emits_rust()
    {
        string source = ReadSample("person.codex");
        string? rs = Helpers.CompileToRust(source, "person");
        Assert.NotNull(rs);
        Assert.Contains("struct Person", rs);
        Assert.Contains("name: String", rs);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void Person_emits_python()
    {
        string source = ReadSample("person.codex");
        string? py = Helpers.CompileToPython(source, "person");
        Assert.NotNull(py);
        Assert.Contains("class Person", py);
        Assert.Contains("name", py);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void Person_emits_cpp()
    {
        string source = ReadSample("person.codex");
        string? cpp = Helpers.CompileToCpp(source, "person");
        Assert.NotNull(cpp);
        Assert.Contains("struct Person", cpp);
    }

    [Fact]
    public void Person_emits_go()
    {
        string source = ReadSample("person.codex");
        string? go = Helpers.CompileToGo(source, "person");
        Assert.NotNull(go);
        Assert.Contains("type Person struct", go);
    }

    [Fact]
    public void Person_emits_java()
    {
        string source = ReadSample("person.codex");
        string? java = Helpers.CompileToJava(source, "person");
        Assert.NotNull(java);
        Assert.Contains("record Person", java);
    }
#endif

    // ── safe-divide.codex ──────────────────────────────────────

    [Fact]
    public void SafeDivide_emits_csharp()
    {
        string source = ReadSample("safe-divide.codex");
        string? cs = Helpers.CompileToCS(source, "safe_divide");
        Assert.NotNull(cs);
        Assert.Contains("safe_divide", cs);
        Assert.Contains("Success", cs);
        Assert.Contains("Failure", cs);
    }

#if LEGACY_EMITTERS
    [Fact]
    public void SafeDivide_emits_javascript()
    {
        string source = ReadSample("safe-divide.codex");
        string? js = Helpers.CompileToJS(source, "safe_divide");
        Assert.NotNull(js);
        Assert.Contains("Success", js);
        Assert.Contains("Failure", js);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void SafeDivide_emits_rust()
    {
        string source = ReadSample("safe-divide.codex");
        string? rs = Helpers.CompileToRust(source, "safe_divide");
        Assert.NotNull(rs);
        Assert.Contains("enum Result", rs);
        Assert.Contains("Success", rs);
        Assert.Contains("Failure", rs);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void SafeDivide_emits_python()
    {
        string source = ReadSample("safe-divide.codex");
        string? py = Helpers.CompileToPython(source, "safe_divide");
        Assert.NotNull(py);
        Assert.Contains("class Success", py);
        Assert.Contains("class Failure", py);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void SafeDivide_emits_cpp()
    {
        string source = ReadSample("safe-divide.codex");
        string? cpp = Helpers.CompileToCpp(source, "safe_divide");
        Assert.NotNull(cpp);
        Assert.Contains("struct Success", cpp);
        Assert.Contains("struct Failure", cpp);
        Assert.Contains("std::variant", cpp);
    }

    [Fact]
    public void SafeDivide_emits_go()
    {
        string source = ReadSample("safe-divide.codex");
        string? go = Helpers.CompileToGo(source, "safe_divide");
        Assert.NotNull(go);
        Assert.Contains("type Success struct", go);
        Assert.Contains("type Failure struct", go);
    }

    [Fact]
    public void SafeDivide_emits_java()
    {
        string source = ReadSample("safe-divide.codex");
        string? java = Helpers.CompileToJava(source, "safe_divide");
        Assert.NotNull(java);
        Assert.Contains("record Success", java);
        Assert.Contains("record Failure", java);
    }
#endif

    // ── string-ops.codex ───────────────────────────────────────

    [Fact]
    public void StringOps_emits_csharp()
    {
        string source = ReadSample("string-ops.codex");
        string? cs = Helpers.CompileToCS(source, "string_ops");
        Assert.NotNull(cs);
        Assert.Contains("count_letters", cs);
    }

#if LEGACY_EMITTERS
    [Fact]
    public void StringOps_emits_javascript()
    {
        string source = ReadSample("string-ops.codex");
        string? js = Helpers.CompileToJS(source, "string_ops");
        Assert.NotNull(js);
        Assert.Contains("function count_letters", js);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void StringOps_emits_rust()
    {
        string source = ReadSample("string-ops.codex");
        string? rs = Helpers.CompileToRust(source, "string_ops");
        Assert.NotNull(rs);
        Assert.Contains("fn count_letters", rs);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void StringOps_emits_python()
    {
        string source = ReadSample("string-ops.codex");
        string? py = Helpers.CompileToPython(source, "string_ops");
        Assert.NotNull(py);
        Assert.Contains("def count_letters", py);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void StringOps_emits_cpp()
    {
        string source = ReadSample("string-ops.codex");
        string? cpp = Helpers.CompileToCpp(source, "string_ops");
        Assert.NotNull(cpp);
        Assert.Contains("int64_t count_letters", cpp);
    }

    [Fact]
    public void StringOps_emits_go()
    {
        string source = ReadSample("string-ops.codex");
        string? go = Helpers.CompileToGo(source, "string_ops");
        Assert.NotNull(go);
        Assert.Contains("func count_letters", go);
    }

    [Fact]
    public void StringOps_emits_java()
    {
        string source = ReadSample("string-ops.codex");
        string? java = Helpers.CompileToJava(source, "string_ops");
        Assert.NotNull(java);
        Assert.Contains("long count_letters", java);
    }
#endif

    // ── prose-greeting.codex ───────────────────────────────────

    [Fact]
    public void ProseGreeting_emits_csharp()
    {
        string source = ReadSample("prose-greeting.codex");
        string? cs = Helpers.CompileToCS(source, "prose_greeting");
        Assert.NotNull(cs);
        Assert.Contains("greet", cs);
    }

#if LEGACY_EMITTERS
    [Fact]
    public void ProseGreeting_emits_javascript()
    {
        string source = ReadSample("prose-greeting.codex");
        string? js = Helpers.CompileToJS(source, "prose_greeting");
        Assert.NotNull(js);
        Assert.Contains("function greet", js);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void ProseGreeting_emits_rust()
    {
        string source = ReadSample("prose-greeting.codex");
        string? rs = Helpers.CompileToRust(source, "prose_greeting");
        Assert.NotNull(rs);
        Assert.Contains("fn greet", rs);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void ProseGreeting_emits_python()
    {
        string source = ReadSample("prose-greeting.codex");
        string? py = Helpers.CompileToPython(source, "prose_greeting");
        Assert.NotNull(py);
        Assert.Contains("def greet", py);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void ProseGreeting_emits_cpp()
    {
        string source = ReadSample("prose-greeting.codex");
        string? cpp = Helpers.CompileToCpp(source, "prose_greeting");
        Assert.NotNull(cpp);
        Assert.Contains("std::string greet", cpp);
    }

    [Fact]
    public void ProseGreeting_emits_go()
    {
        string source = ReadSample("prose-greeting.codex");
        string? go = Helpers.CompileToGo(source, "prose_greeting");
        Assert.NotNull(go);
        Assert.Contains("func greet", go);
    }

    [Fact]
    public void ProseGreeting_emits_java()
    {
        string source = ReadSample("prose-greeting.codex");
        string? java = Helpers.CompileToJava(source, "prose_greeting");
        Assert.NotNull(java);
        Assert.Contains("String greet", java);
    }
#endif

    // ── effectful-hello.codex ──────────────────────────────────

    [Fact]
    public void EffectfulHello_emits_csharp()
    {
        string source = ReadSample("effectful-hello.codex");
        string? cs = Helpers.CompileToCS(source, "effectful_hello");
        Assert.NotNull(cs);
        Assert.Contains("Console.WriteLine", cs);
        Assert.Contains("Console.ReadLine", cs);
    }

#if LEGACY_EMITTERS
    [Fact]
    public void EffectfulHello_emits_javascript()
    {
        string source = ReadSample("effectful-hello.codex");
        string? js = Helpers.CompileToJS(source, "effectful_hello");
        Assert.NotNull(js);
        Assert.Contains("console.log", js);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void EffectfulHello_emits_rust()
    {
        string source = ReadSample("effectful-hello.codex");
        string? rs = Helpers.CompileToRust(source, "effectful_hello");
        Assert.NotNull(rs);
        Assert.Contains("println!", rs);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void EffectfulHello_emits_python()
    {
        string source = ReadSample("effectful-hello.codex");
        string? py = Helpers.CompileToPython(source, "effectful_hello");
        Assert.NotNull(py);
        Assert.Contains("print(", py);
        Assert.Contains("input()", py);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void EffectfulHello_emits_cpp()
    {
        string source = ReadSample("effectful-hello.codex");
        string? cpp = Helpers.CompileToCpp(source, "effectful_hello");
        Assert.NotNull(cpp);
        Assert.Contains("std::cout", cpp);
        Assert.Contains("std::getline", cpp);
    }

    [Fact]
    public void EffectfulHello_emits_go()
    {
        string source = ReadSample("effectful-hello.codex");
        string? go = Helpers.CompileToGo(source, "effectful_hello");
        Assert.NotNull(go);
        Assert.Contains("fmt.Println", go);
    }

    [Fact]
    public void EffectfulHello_emits_java()
    {
        string source = ReadSample("effectful-hello.codex");
        string? java = Helpers.CompileToJava(source, "effectful_hello");
        Assert.NotNull(java);
        Assert.Contains("System.out.println", java);
    }
#endif

    // ── arithmetic.codex ───────────────────────────────────────

    [Fact]
    public void Arithmetic_emits_csharp()
    {
        string source = ReadSample("arithmetic.codex");
        string? cs = Helpers.CompileToCS(source, "arithmetic");
        Assert.NotNull(cs);
        Assert.Contains("max", cs);
        Assert.Contains("clamp", cs);
    }

#if LEGACY_EMITTERS
    [Fact]
    public void Arithmetic_emits_javascript()
    {
        string source = ReadSample("arithmetic.codex");
        string? js = Helpers.CompileToJS(source, "arithmetic");
        Assert.NotNull(js);
        Assert.Contains("function max", js);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void Arithmetic_emits_rust()
    {
        string source = ReadSample("arithmetic.codex");
        string? rs = Helpers.CompileToRust(source, "arithmetic");
        Assert.NotNull(rs);
        Assert.Contains("fn max", rs);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void Arithmetic_emits_python()
    {
        string source = ReadSample("arithmetic.codex");
        string? py = Helpers.CompileToPython(source, "arithmetic");
        Assert.NotNull(py);
        Assert.Contains("def max", py);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void Arithmetic_emits_cpp()
    {
        string source = ReadSample("arithmetic.codex");
        string? cpp = Helpers.CompileToCpp(source, "arithmetic");
        Assert.NotNull(cpp);
        Assert.Contains("int64_t _max", cpp);
    }

    [Fact]
    public void Arithmetic_emits_go()
    {
        string source = ReadSample("arithmetic.codex");
        string? go = Helpers.CompileToGo(source, "arithmetic");
        Assert.NotNull(go);
        Assert.Contains("func max", go);
    }

    [Fact]
    public void Arithmetic_emits_java()
    {
        string source = ReadSample("arithmetic.codex");
        string? java = Helpers.CompileToJava(source, "arithmetic");
        Assert.NotNull(java);
        Assert.Contains("long max", java);
    }
#endif

    // ── effects-demo.codex ─────────────────────────────────────

    [Fact]
    public void EffectsDemo_emits_csharp()
    {
        string source = ReadSample("effects-demo.codex");
        string? cs = Helpers.CompileToCS(source, "effects_demo");
        Assert.NotNull(cs);
        Assert.Contains("greet", cs);
        Assert.Contains("Console.WriteLine", cs);
    }

#if LEGACY_EMITTERS
    [Fact]
    public void EffectsDemo_emits_javascript()
    {
        string source = ReadSample("effects-demo.codex");
        string? js = Helpers.CompileToJS(source, "effects_demo");
        Assert.NotNull(js);
        Assert.Contains("console.log", js);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void EffectsDemo_emits_rust()
    {
        string source = ReadSample("effects-demo.codex");
        string? rs = Helpers.CompileToRust(source, "effects_demo");
        Assert.NotNull(rs);
        Assert.Contains("println!", rs);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void EffectsDemo_emits_python()
    {
        string source = ReadSample("effects-demo.codex");
        string? py = Helpers.CompileToPython(source, "effects_demo");
        Assert.NotNull(py);
        Assert.Contains("print(", py);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void EffectsDemo_emits_cpp()
    {
        string source = ReadSample("effects-demo.codex");
        string? cpp = Helpers.CompileToCpp(source, "effects_demo");
        Assert.NotNull(cpp);
        Assert.Contains("std::cout", cpp);
    }

    [Fact]
    public void EffectsDemo_emits_go()
    {
        string source = ReadSample("effects-demo.codex");
        string? go = Helpers.CompileToGo(source, "effects_demo");
        Assert.NotNull(go);
        Assert.Contains("fmt.Println", go);
    }

    [Fact]
    public void EffectsDemo_emits_java()
    {
        string source = ReadSample("effects-demo.codex");
        string? java = Helpers.CompileToJava(source, "effects_demo");
        Assert.NotNull(java);
        Assert.Contains("System.out.println", java);
    }
#endif

    // ── tco-stress.codex ──────────────────────────────────────

    [Fact]
    public void TcoStress_emits_csharp_with_loop()
    {
        string source = ReadSample("tco-stress.codex");
        string? cs = Helpers.CompileToCS(source, "tco_stress");
        Assert.NotNull(cs);
        Assert.Contains("while (true)", cs);
        Assert.Contains("continue;", cs);
    }

#if LEGACY_EMITTERS
    [Fact]
    public void TcoStress_emits_javascript_with_loop()
    {
        string source = ReadSample("tco-stress.codex");
        string? js = Helpers.CompileToJS(source, "tco_stress");
        Assert.NotNull(js);
        Assert.Contains("while (true)", js);
        Assert.Contains("continue;", js);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void TcoStress_emits_rust_with_loop()
    {
        string source = ReadSample("tco-stress.codex");
        string? rs = Helpers.CompileToRust(source, "tco_stress");
        Assert.NotNull(rs);
        Assert.Contains("loop {", rs);
        Assert.Contains("mut", rs);
        Assert.Contains("continue;", rs);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void TcoStress_emits_python_with_loop()
    {
        string source = ReadSample("tco-stress.codex");
        string? py = Helpers.CompileToPython(source, "tco_stress");
        Assert.NotNull(py);
        Assert.Contains("while True:", py);
        Assert.Contains("continue", py);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void TcoStress_emits_cpp_with_loop()
    {
        string source = ReadSample("tco-stress.codex");
        string? cpp = Helpers.CompileToCpp(source, "tco_stress");
        Assert.NotNull(cpp);
        Assert.Contains("while (true)", cpp);
        Assert.Contains("continue;", cpp);
    }

    [Fact]
    public void TcoStress_emits_go_with_loop()
    {
        string source = ReadSample("tco-stress.codex");
        string? go = Helpers.CompileToGo(source, "tco_stress");
        Assert.NotNull(go);
        Assert.Contains("for {", go);
        Assert.Contains("continue", go);
    }

    [Fact]
    public void TcoStress_emits_java_with_loop()
    {
        string source = ReadSample("tco-stress.codex");
        string? java = Helpers.CompileToJava(source, "tco_stress");
        Assert.NotNull(java);
        Assert.Contains("while (true)", java);
        Assert.Contains("continue;", java);
    }
#endif

    // ── type-checker-test.codex ───────────────────────────────

    [Fact]
    public void TypeCheckerTest_emits_csharp()
    {
        string source = ReadSample("type-checker-test.codex");
        string? cs = Helpers.CompileToCS(source, "type_checker_test");
        Assert.NotNull(cs);
        Assert.Contains("apply_twice", cs);
        Assert.Contains("add_one", cs);
    }

#if LEGACY_EMITTERS
    [Fact]
    public void TypeCheckerTest_emits_javascript()
    {
        string source = ReadSample("type-checker-test.codex");
        string? js = Helpers.CompileToJS(source, "type_checker_test");
        Assert.NotNull(js);
        Assert.Contains("function apply_twice", js);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void TypeCheckerTest_emits_rust()
    {
        string source = ReadSample("type-checker-test.codex");
        string? rs = Helpers.CompileToRust(source, "type_checker_test");
        Assert.NotNull(rs);
        Assert.Contains("fn apply_twice", rs);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void TypeCheckerTest_emits_python()
    {
        string source = ReadSample("type-checker-test.codex");
        string? py = Helpers.CompileToPython(source, "type_checker_test");
        Assert.NotNull(py);
        Assert.Contains("def apply_twice", py);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void TypeCheckerTest_emits_cpp()
    {
        string source = ReadSample("type-checker-test.codex");
        string? cpp = Helpers.CompileToCpp(source, "type_checker_test");
        Assert.NotNull(cpp);
        Assert.Contains("apply_twice", cpp);
    }

    [Fact]
    public void TypeCheckerTest_emits_go()
    {
        string source = ReadSample("type-checker-test.codex");
        string? go = Helpers.CompileToGo(source, "type_checker_test");
        Assert.NotNull(go);
        Assert.Contains("func apply_twice", go);
    }

    [Fact]
    public void TypeCheckerTest_emits_java()
    {
        string source = ReadSample("type-checker-test.codex");
        string? java = Helpers.CompileToJava(source, "type_checker_test");
        Assert.NotNull(java);
        Assert.Contains("apply_twice", java);
    }
#endif

    // ── proofs.codex ──────────────────────────────────────────

    [Fact]
    public void Proofs_emits_csharp()
    {
        string source = ReadSample("proofs.codex");
        string? cs = Helpers.CompileToCS(source, "proofs");
        Assert.NotNull(cs);
    }

#if LEGACY_EMITTERS
    [Fact]
    public void Proofs_emits_javascript()
    {
        string source = ReadSample("proofs.codex");
        string? js = Helpers.CompileToJS(source, "proofs");
        Assert.NotNull(js);
        Assert.Contains("All proofs verified", js);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void Proofs_emits_rust()
    {
        string source = ReadSample("proofs.codex");
        string? rs = Helpers.CompileToRust(source, "proofs");
        Assert.NotNull(rs);
        Assert.Contains("fn main()", rs);
        Assert.Contains("All proofs verified", rs);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void Proofs_emits_python()
    {
        string source = ReadSample("proofs.codex");
        string? py = Helpers.CompileToPython(source, "proofs");
        Assert.NotNull(py);
        Assert.Contains("All proofs verified", py);
    }
#endif

#if LEGACY_EMITTERS
    [Fact]
    public void Proofs_emits_cpp()
    {
        string source = ReadSample("proofs.codex");
        string? cpp = Helpers.CompileToCpp(source, "proofs");
        Assert.NotNull(cpp);
        Assert.Contains("int main()", cpp);
        Assert.Contains("All proofs verified", cpp);
    }

    [Fact]
    public void Proofs_emits_go()
    {
        string source = ReadSample("proofs.codex");
        string? go = Helpers.CompileToGo(source, "proofs");
        Assert.NotNull(go);
        Assert.Contains("func main()", go);
        Assert.Contains("All proofs verified", go);
    }

    [Fact]
    public void Proofs_emits_java()
    {
        string source = ReadSample("proofs.codex");
        string? java = Helpers.CompileToJava(source, "proofs");
        Assert.NotNull(java);
        Assert.Contains("public static void main", java);
        Assert.Contains("All proofs verified", java);
    }

    [Fact]
    public void Proofs_emits_ada()
    {
        string source = ReadSample("proofs.codex");
        string? ada = Helpers.CompileToAda(source, "proofs");
        Assert.NotNull(ada);
        Assert.Contains("Put_Line", ada);
        Assert.Contains("All proofs verified", ada);
    }

    [Fact]
    public void Proofs_emits_babbage()
    {
        string source = ReadSample("proofs.codex");
        string? ae = Helpers.CompileToBabbage(source, "proofs");
        Assert.NotNull(ae);
        Assert.Contains("All proofs verified", ae);
        Assert.Contains("H  . Halt", ae);
    }

    // ── Ada tests ──────────────────────────────────────────────

    [Fact]
    public void Hello_emits_ada()
    {
        string source = ReadSample("hello.codex");
        string? ada = Helpers.CompileToAda(source, "hello");
        Assert.NotNull(ada);
        Assert.Contains("function Square", ada);
        Assert.Contains("Long_Long_Integer", ada);
    }

    [Fact]
    public void Factorial_emits_ada()
    {
        string source = ReadSample("factorial.codex");
        string? ada = Helpers.CompileToAda(source, "factorial");
        Assert.NotNull(ada);
        Assert.Contains("function Factorial", ada);
    }

    [Fact]
    public void Fibonacci_emits_ada()
    {
        string source = ReadSample("fibonacci.codex");
        string? ada = Helpers.CompileToAda(source, "fibonacci");
        Assert.NotNull(ada);
        Assert.Contains("function Fib", ada);
    }

    [Fact]
    public void Greeting_emits_ada()
    {
        string source = ReadSample("greeting.codex");
        string? ada = Helpers.CompileToAda(source, "greeting");
        Assert.NotNull(ada);
        Assert.Contains("function Greeting", ada);
        Assert.Contains("Unbounded_String", ada);
    }

    [Fact]
    public void Shapes_emits_ada()
    {
        string source = ReadSample("shapes.codex");
        string? ada = Helpers.CompileToAda(source, "shapes");
        Assert.NotNull(ada);
        Assert.Contains("Tag_Circle", ada);
        Assert.Contains("Tag_Rectangle", ada);
    }

    [Fact]
    public void Person_emits_ada()
    {
        string source = ReadSample("person.codex");
        string? ada = Helpers.CompileToAda(source, "person");
        Assert.NotNull(ada);
        Assert.Contains("type Person is record", ada);
    }

    [Fact]
    public void SafeDivide_emits_ada()
    {
        string source = ReadSample("safe-divide.codex");
        string? ada = Helpers.CompileToAda(source, "safe_divide");
        Assert.NotNull(ada);
        Assert.Contains("Tag_Success", ada);
        Assert.Contains("Tag_Failure", ada);
    }

    [Fact]
    public void StringOps_emits_ada()
    {
        string source = ReadSample("string-ops.codex");
        string? ada = Helpers.CompileToAda(source, "string_ops");
        Assert.NotNull(ada);
        Assert.Contains("function Count_letters", ada);
    }

    [Fact]
    public void ProseGreeting_emits_ada()
    {
        string source = ReadSample("prose-greeting.codex");
        string? ada = Helpers.CompileToAda(source, "prose_greeting");
        Assert.NotNull(ada);
        Assert.Contains("function Greet", ada);
    }

    [Fact]
    public void EffectfulHello_emits_ada()
    {
        string source = ReadSample("effectful-hello.codex");
        string? ada = Helpers.CompileToAda(source, "effectful_hello");
        Assert.NotNull(ada);
        Assert.Contains("Put_Line", ada);
    }

    [Fact]
    public void Arithmetic_emits_ada()
    {
        string source = ReadSample("arithmetic.codex");
        string? ada = Helpers.CompileToAda(source, "arithmetic");
        Assert.NotNull(ada);
        Assert.Contains("function Max", ada);
    }

    [Fact]
    public void EffectsDemo_emits_ada()
    {
        string source = ReadSample("effects-demo.codex");
        string? ada = Helpers.CompileToAda(source, "effects_demo");
        Assert.NotNull(ada);
        Assert.Contains("Put_Line", ada);
    }

    [Fact]
    public void TcoStress_emits_ada_with_loop()
    {
        string source = ReadSample("tco-stress.codex");
        string? ada = Helpers.CompileToAda(source, "tco_stress");
        Assert.NotNull(ada);
        Assert.Contains("loop", ada);
    }

    [Fact]
    public void TypeCheckerTest_emits_ada()
    {
        string source = ReadSample("type-checker-test.codex");
        string? ada = Helpers.CompileToAda(source, "type_checker_test");
        Assert.NotNull(ada);
        Assert.Contains("Apply_twice", ada);
    }

    // ── Babbage Analytical Engine tests ────────────────────────

    [Fact]
    public void Hello_emits_babbage()
    {
        string source = ReadSample("hello.codex");
        string? ae = Helpers.CompileToBabbage(source, "hello");
        Assert.NotNull(ae);
        Assert.Contains("Analytical Engine", ae);
        Assert.Contains("Mill", ae);
        Assert.Contains("H  . Halt", ae);
    }

    [Fact]
    public void Factorial_emits_babbage()
    {
        string source = ReadSample("factorial.codex");
        string? ae = Helpers.CompileToBabbage(source, "factorial");
        Assert.NotNull(ae);
        Assert.Contains("FUNCTION: factorial", ae);
        Assert.Contains("*  . Multiply", ae);
    }

    [Fact]
    public void Fibonacci_emits_babbage()
    {
        string source = ReadSample("fibonacci.codex");
        string? ae = Helpers.CompileToBabbage(source, "fibonacci");
        Assert.NotNull(ae);
        Assert.Contains("FUNCTION: fib", ae);
    }

    [Fact]
    public void Greeting_emits_babbage()
    {
        string source = ReadSample("greeting.codex");
        string? ae = Helpers.CompileToBabbage(source, "greeting");
        Assert.NotNull(ae);
        Assert.Contains("FUNCTION: greeting", ae);
    }

    [Fact]
    public void Shapes_emits_babbage()
    {
        string source = ReadSample("shapes.codex");
        string? ae = Helpers.CompileToBabbage(source, "shapes");
        Assert.NotNull(ae);
        Assert.Contains("Store", ae);
    }

    [Fact]
    public void Person_emits_babbage()
    {
        string source = ReadSample("person.codex");
        string? ae = Helpers.CompileToBabbage(source, "person");
        Assert.NotNull(ae);
        Assert.Contains("FUNCTION: greet", ae);
    }

    [Fact]
    public void SafeDivide_emits_babbage()
    {
        string source = ReadSample("safe-divide.codex");
        string? ae = Helpers.CompileToBabbage(source, "safe_divide");
        Assert.NotNull(ae);
        Assert.Contains("FUNCTION: safe-divide", ae);
    }

    [Fact]
    public void StringOps_emits_babbage()
    {
        string source = ReadSample("string-ops.codex");
        string? ae = Helpers.CompileToBabbage(source, "string_ops");
        Assert.NotNull(ae);
        Assert.Contains("FUNCTION: count-letters", ae);
    }

    [Fact]
    public void ProseGreeting_emits_babbage()
    {
        string source = ReadSample("prose-greeting.codex");
        string? ae = Helpers.CompileToBabbage(source, "prose_greeting");
        Assert.NotNull(ae);
        Assert.Contains("FUNCTION: greet", ae);
    }

    [Fact]
    public void EffectfulHello_emits_babbage()
    {
        string source = ReadSample("effectful-hello.codex");
        string? ae = Helpers.CompileToBabbage(source, "effectful_hello");
        Assert.NotNull(ae);
        Assert.Contains("MAIN PROGRAM", ae);
    }

    [Fact]
    public void Arithmetic_emits_babbage()
    {
        string source = ReadSample("arithmetic.codex");
        string? ae = Helpers.CompileToBabbage(source, "arithmetic");
        Assert.NotNull(ae);
        Assert.Contains("FUNCTION: max", ae);
    }

    [Fact]
    public void EffectsDemo_emits_babbage()
    {
        string source = ReadSample("effects-demo.codex");
        string? ae = Helpers.CompileToBabbage(source, "effects_demo");
        Assert.NotNull(ae);
        Assert.Contains("MAIN PROGRAM", ae);
    }

    [Fact]
    public void TcoStress_emits_babbage_with_loop()
    {
        string source = ReadSample("tco-stress.codex");
        string? ae = Helpers.CompileToBabbage(source, "tco_stress");
        Assert.NotNull(ae);
        Assert.Contains("Continue loop (tail call)", ae);
        Assert.Contains("CB LABEL_", ae);
    }

    [Fact]
    public void TypeCheckerTest_emits_babbage()
    {
        string source = ReadSample("type-checker-test.codex");
        string? ae = Helpers.CompileToBabbage(source, "type_checker_test");
        Assert.NotNull(ae);
        Assert.Contains("Analytical Engine", ae);
        Assert.Contains("H  . Halt", ae);
    }

    // ── Fortran tests ─────────────────────────────────────────

    [Fact]
    public void Hello_emits_fortran()
    {
        string source = ReadSample("hello.codex");
        string? f90 = Helpers.CompileToFortran(source, "hello");
        Assert.NotNull(f90);
        Assert.Contains("function square", f90);
        Assert.Contains("integer(8)", f90);
    }

    [Fact]
    public void Factorial_emits_fortran()
    {
        string source = ReadSample("factorial.codex");
        string? f90 = Helpers.CompileToFortran(source, "factorial");
        Assert.NotNull(f90);
        Assert.Contains("function factorial", f90);
    }

    [Fact]
    public void Fibonacci_emits_fortran()
    {
        string source = ReadSample("fibonacci.codex");
        string? f90 = Helpers.CompileToFortran(source, "fibonacci");
        Assert.NotNull(f90);
        Assert.Contains("function fib", f90);
    }

    [Fact]
    public void Greeting_emits_fortran()
    {
        string source = ReadSample("greeting.codex");
        string? f90 = Helpers.CompileToFortran(source, "greeting");
        Assert.NotNull(f90);
        Assert.Contains("function greeting", f90);
    }

    [Fact]
    public void Shapes_emits_fortran()
    {
        string source = ReadSample("shapes.codex");
        string? f90 = Helpers.CompileToFortran(source, "shapes");
        Assert.NotNull(f90);
        Assert.Contains("TAG_Circle", f90);
    }

    [Fact]
    public void Person_emits_fortran()
    {
        string source = ReadSample("person.codex");
        string? f90 = Helpers.CompileToFortran(source, "person");
        Assert.NotNull(f90);
        Assert.Contains("type :: Person", f90);
    }

    [Fact]
    public void SafeDivide_emits_fortran()
    {
        string source = ReadSample("safe-divide.codex");
        string? f90 = Helpers.CompileToFortran(source, "safe_divide");
        Assert.NotNull(f90);
        Assert.Contains("TAG_Success", f90);
    }

    [Fact]
    public void StringOps_emits_fortran()
    {
        string source = ReadSample("string-ops.codex");
        string? f90 = Helpers.CompileToFortran(source, "string_ops");
        Assert.NotNull(f90);
        Assert.Contains("function count_letters", f90);
    }

    [Fact]
    public void ProseGreeting_emits_fortran()
    {
        string source = ReadSample("prose-greeting.codex");
        string? f90 = Helpers.CompileToFortran(source, "prose_greeting");
        Assert.NotNull(f90);
        Assert.Contains("function greet", f90);
    }

    [Fact]
    public void EffectfulHello_emits_fortran()
    {
        string source = ReadSample("effectful-hello.codex");
        string? f90 = Helpers.CompileToFortran(source, "effectful_hello");
        Assert.NotNull(f90);
        Assert.Contains("print *,", f90);
    }

    [Fact]
    public void Arithmetic_emits_fortran()
    {
        string source = ReadSample("arithmetic.codex");
        string? f90 = Helpers.CompileToFortran(source, "arithmetic");
        Assert.NotNull(f90);
        Assert.Contains("function max", f90);
    }

    [Fact]
    public void EffectsDemo_emits_fortran()
    {
        string source = ReadSample("effects-demo.codex");
        string? f90 = Helpers.CompileToFortran(source, "effects_demo");
        Assert.NotNull(f90);
        Assert.Contains("print *,", f90);
    }

    [Fact]
    public void TcoStress_emits_fortran_with_loop()
    {
        string source = ReadSample("tco-stress.codex");
        string? f90 = Helpers.CompileToFortran(source, "tco_stress");
        Assert.NotNull(f90);
        Assert.Contains("do while (.true.)", f90);
        Assert.Contains("cycle", f90);
    }

    [Fact]
    public void TypeCheckerTest_emits_fortran()
    {
        string source = ReadSample("type-checker-test.codex");
        string? f90 = Helpers.CompileToFortran(source, "type_checker_test");
        Assert.NotNull(f90);
        Assert.Contains("apply_twice", f90);
    }

    [Fact]
    public void Proofs_emits_fortran()
    {
        string source = ReadSample("proofs.codex");
        string? f90 = Helpers.CompileToFortran(source, "proofs");
        Assert.NotNull(f90);
        Assert.Contains("All proofs verified", f90);
    }

    // ── COBOL tests ───────────────────────────────────────────

    [Fact]
    public void Hello_emits_cobol()
    {
        string source = ReadSample("hello.codex");
        string? cob = Helpers.CompileToCobol(source, "hello");
        Assert.NotNull(cob);
        Assert.Contains("IDENTIFICATION DIVISION", cob);
        Assert.Contains("PROCEDURE DIVISION", cob);
    }

    [Fact]
    public void Factorial_emits_cobol()
    {
        string source = ReadSample("factorial.codex");
        string? cob = Helpers.CompileToCobol(source, "factorial");
        Assert.NotNull(cob);
        Assert.Contains("FACTORIAL", cob);
        Assert.Contains("PERFORM", cob);
    }

    [Fact]
    public void Fibonacci_emits_cobol()
    {
        string source = ReadSample("fibonacci.codex");
        string? cob = Helpers.CompileToCobol(source, "fibonacci");
        Assert.NotNull(cob);
        Assert.Contains("FIB", cob);
    }

    [Fact]
    public void Greeting_emits_cobol()
    {
        string source = ReadSample("greeting.codex");
        string? cob = Helpers.CompileToCobol(source, "greeting");
        Assert.NotNull(cob);
        Assert.Contains("GREETING", cob);
    }

    [Fact]
    public void Shapes_emits_cobol()
    {
        string source = ReadSample("shapes.codex");
        string? cob = Helpers.CompileToCobol(source, "shapes");
        Assert.NotNull(cob);
        Assert.Contains("WORKING-STORAGE", cob);
    }

    [Fact]
    public void Person_emits_cobol()
    {
        string source = ReadSample("person.codex");
        string? cob = Helpers.CompileToCobol(source, "person");
        Assert.NotNull(cob);
        Assert.Contains("PERSON", cob);
    }

    [Fact]
    public void SafeDivide_emits_cobol()
    {
        string source = ReadSample("safe-divide.codex");
        string? cob = Helpers.CompileToCobol(source, "safe_divide");
        Assert.NotNull(cob);
        Assert.Contains("SAFE_DIVIDE", cob);
    }

    [Fact]
    public void StringOps_emits_cobol()
    {
        string source = ReadSample("string-ops.codex");
        string? cob = Helpers.CompileToCobol(source, "string_ops");
        Assert.NotNull(cob);
        Assert.Contains("COUNT_LETTERS", cob);
    }

    [Fact]
    public void ProseGreeting_emits_cobol()
    {
        string source = ReadSample("prose-greeting.codex");
        string? cob = Helpers.CompileToCobol(source, "prose_greeting");
        Assert.NotNull(cob);
        Assert.Contains("GREET", cob);
    }

    [Fact]
    public void EffectfulHello_emits_cobol()
    {
        string source = ReadSample("effectful-hello.codex");
        string? cob = Helpers.CompileToCobol(source, "effectful_hello");
        Assert.NotNull(cob);
        Assert.Contains("DISPLAY", cob);
    }

    [Fact]
    public void Arithmetic_emits_cobol()
    {
        string source = ReadSample("arithmetic.codex");
        string? cob = Helpers.CompileToCobol(source, "arithmetic");
        Assert.NotNull(cob);
        Assert.Contains("MAX", cob);
    }

    [Fact]
    public void EffectsDemo_emits_cobol()
    {
        string source = ReadSample("effects-demo.codex");
        string? cob = Helpers.CompileToCobol(source, "effects_demo");
        Assert.NotNull(cob);
        Assert.Contains("DISPLAY", cob);
    }

    [Fact]
    public void TcoStress_emits_cobol_with_goto()
    {
        string source = ReadSample("tco-stress.codex");
        string? cob = Helpers.CompileToCobol(source, "tco_stress");
        Assert.NotNull(cob);
        Assert.Contains("GO TO", cob);
        Assert.Contains("SUM_TO-LOOP", cob);
    }

    [Fact]
    public void TypeCheckerTest_emits_cobol()
    {
        string source = ReadSample("type-checker-test.codex");
        string? cob = Helpers.CompileToCobol(source, "type_checker_test");
        Assert.NotNull(cob);
        Assert.Contains("APPLY_TWICE", cob);
    }

    [Fact]
    public void Proofs_emits_cobol()
    {
        string source = ReadSample("proofs.codex");
        string? cob = Helpers.CompileToCobol(source, "proofs");
        Assert.NotNull(cob);
        Assert.Contains("All proofs verified", cob);
        Assert.Contains("STOP RUN", cob);
    }
#endif
}
