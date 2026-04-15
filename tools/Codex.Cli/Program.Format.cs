using Codex.Core;
using Codex.Syntax;

namespace Codex.Cli;

public static partial class Program
{
    static int RunFormat(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: codex format <file.codex> [--write]");
            Console.Error.WriteLine("  Without --write, prints formatted output to stdout.");
            Console.Error.WriteLine("  With --write, overwrites the file in place.");
            return 1;
        }

        string filePath = args[0];
        bool writeInPlace = args.Contains("--write");

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"File not found: {filePath}");
            return 1;
        }

        string content = File.ReadAllText(filePath);
        string formatted = FormatCodexSource(content);

        if (writeInPlace)
        {
            File.WriteAllText(filePath, formatted);
            Console.Error.WriteLine($"Formatted: {filePath}");
        }
        else
        {
            Console.Write(formatted);
        }
        return 0;
    }

    static string FormatCodexSource(string content)
    {
        string[] lines = content.Split('\n');
        System.Text.StringBuilder output = new System.Text.StringBuilder(content.Length);
        bool inCode = false;
        bool lastWasBlank = false;
        int chainIndent = -1; // tracks base indent of current in-let chain

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            string trimmed = line.TrimStart();

            // Blank line
            if (trimmed.Length == 0)
            {
                if (!lastWasBlank)
                {
                    output.Append('\n');
                }

                lastWasBlank = true;
                chainIndent = -1;
                continue;
            }

            lastWasBlank = false;
            int currentIndent = line.Length - trimmed.Length;

            // Chapter header: "Chapter: ..."
            if (trimmed.StartsWith("Chapter:"))
            {
                output.Append(trimmed);
                output.Append('\n');
                inCode = false;
                chainIndent = -1;
                continue;
            }

            // Section header: "Section: ..."
            if (trimmed.StartsWith("Section:"))
            {
                output.Append(trimmed);
                output.Append('\n');
                inCode = false;
                chainIndent = -1;
                continue;
            }

            // Page marker: "Page N" or "Page N of M"
            if (trimmed.StartsWith("Page ") && trimmed.Length >= 6 && char.IsDigit(trimmed[5]))
            {
                output.Append(trimmed);
                output.Append('\n');
                continue;
            }

            // Cites declaration: "  cites ..."
            if (trimmed.StartsWith("cites "))
            {
                output.Append("  ");
                output.Append(trimmed);
                output.Append('\n');
                inCode = true;
                chainIndent = -1;
                continue;
            }

            // Detect if this is a code line or prose line.
            // Code lines: type annotations (name : Type), definitions (name (params) =),
            //   type defs (Name = record/variant), continuation lines
            // Prose lines: everything else at column 1-2

            bool isCodeStart = IsCodeStartLine(trimmed);

            if (isCodeStart && !inCode && currentIndent >= 2)
            {
                // Entering code from prose — only when already at code-level indent.
                // Lines at prose indent (0-1 spaces) that pattern-match code
                // (e.g. "R10 = heap bump pointer.") stay as prose.
                inCode = true;
                chainIndent = -1;
                output.Append("  ");
                output.Append(trimmed);
                output.Append('\n');
                continue;
            }

            if (inCode)
            {
                // Inside a code block — normalize indentation
                if (isCodeStart && currentIndent <= 2)
                {
                    // New top-level def/type annotation
                    chainIndent = -1;
                    output.Append("  ");
                    output.Append(trimmed);
                    output.Append('\n');
                }
                else if (currentIndent == 0 && !isCodeStart)
                {
                    // Back to prose
                    inCode = false;
                    chainIndent = -1;
                        output.Append(' ');
                    output.Append(trimmed);
                    output.Append('\n');
                }
                else
                {
                    // Continuation/body line
                    int indent = currentIndent > 0 ? currentIndent : 3;

                    // Flatten in-let chains: "in ..." and "else let/do" lines at
                    // increasing indentation are normalized to the chain base.
                    // Plain "else <expr>" keeps its alignment with the if.
                    bool isChainLine = trimmed.StartsWith("in ")
                        || trimmed.StartsWith("else let ")
                        || trimmed.StartsWith("else do")
                        || trimmed == "else";
                    if (isChainLine)
                    {
                        if (chainIndent < 0)
                        {
                            chainIndent = indent;
                        }
                        else if (indent > chainIndent)
                        {
                            indent = chainIndent;
                        }
                        else
                        {
                            chainIndent = indent;
                        }
                    }

                    output.Append(new string(' ', indent));
                    output.Append(trimmed);
                    output.Append('\n');
                }
                continue;
            }

            // Prose line — 1-space indent
            output.Append(' ');
            output.Append(trimmed);
            output.Append('\n');
        }

        return output.ToString();
    }

    static bool IsCodeStartLine(string trimmed)
    {
        if (trimmed.Length == 0)
        {
            return false;
        }

        // Type definition: starts with uppercase Name followed by = or type params
        // Function annotation: starts with lowercase name followed by :
        // Function definition: starts with lowercase name followed by (
        // Variant constructor: starts with |

        char first = trimmed[0];

        // Variant constructor line
        if (first == '|')
        {
            return true;
        }

        // Must start with letter or underscore
        if (!char.IsLetter(first) && first != '_')
        {
            return false;
        }

        // Scan past the first word (letters, digits, hyphens, underscores)
        int i = 1;
        while (i < trimmed.Length && trimmed[i] != ' ')
        {
            i++;
        }

        // Skip whitespace after the word
        while (i < trimmed.Length && trimmed[i] == ' ')
        {
            i++;
        }

        if (i >= trimmed.Length)
        {
            return false;
        }

        // The character after "name " determines the line type
        char afterName = trimmed[i];
        return afterName == ':' || afterName == '=' || afterName == '(';
    }
}
