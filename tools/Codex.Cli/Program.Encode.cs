using Codex.Core;

namespace Codex.Cli;

public static partial class Program
{
    static int RunEncode(string[] args)
    {
        string from = "utf8";
        string to = "cce";
        string? inputFile = null;
        string? outputFile = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--from" or "-f" when i + 1 < args.Length:
                    from = args[++i].ToLowerInvariant();
                    break;
                case "--to" or "-t" when i + 1 < args.Length:
                    to = args[++i].ToLowerInvariant();
                    break;
                case "--output" or "-o" when i + 1 < args.Length:
                    outputFile = args[++i];
                    break;
                case "--help" or "-h":
                    PrintEncodeUsage();
                    return 0;
                default:
                    if (!args[i].StartsWith('-'))
                    {
                        inputFile = args[i];
                    }
                    else
                    {
                        Console.Error.WriteLine($"Unknown option: {args[i]}");
                        PrintEncodeUsage();
                        return 1;
                    }
                    break;
            }
        }

        // Normalize encoding names
        from = NormalizeEncoding(from);
        to = NormalizeEncoding(to);

        if (from == to)
        {
            Console.Error.WriteLine("Source and target encodings are the same.");
            return 1;
        }

        if ((from != "utf8" && from != "cce") || (to != "utf8" && to != "cce"))
        {
            Console.Error.WriteLine("Supported encodings: utf8 (unicode), cce (codex)");
            return 1;
        }

        try
        {
            string input = inputFile != null
                ? File.ReadAllText(inputFile)
                : Console.In.ReadToEnd();

            string output = (from, to) switch
            {
                ("utf8", "cce") => CceTable.Encode(input),
                ("cce", "utf8") => CceTable.Decode(input),
                _ => throw new InvalidOperationException()
            };

            if (outputFile != null)
            {
                File.WriteAllText(outputFile, output);
            }
            else
            {
                Console.Write(output);
            }

            return 0;
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"File not found: {ex.FileName}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    static string NormalizeEncoding(string name) => name switch
    {
        "utf8" or "utf-8" or "unicode" => "utf8",
        "cce" or "codex" or "cce-v1" => "cce",
        _ => name
    };

    static void PrintEncodeUsage()
    {
        Console.WriteLine("Usage: codex encode [options] [file]");
        Console.WriteLine();
        Console.WriteLine("Convert text between Unicode (UTF-8) and Codex Character Encoding (CCE).");
        Console.WriteLine("Reads from stdin if no file is given. Writes to stdout if no -o is given.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -f, --from <enc>    Source encoding (utf8, cce)     [default: utf8]");
        Console.WriteLine("  -t, --to <enc>      Target encoding (utf8, cce)     [default: cce]");
        Console.WriteLine("  -o, --output <file> Write output to file instead of stdout");
        Console.WriteLine("  -h, --help          Show this help");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  codex encode < source.codex > source.cce");
        Console.WriteLine("  codex encode -f cce -t utf8 type-diag.txt");
        Console.WriteLine("  codex encode -f cce -t utf8 -o readable.txt unify-errors.txt");
    }
}
