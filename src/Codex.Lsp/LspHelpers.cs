using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Codex.Lsp;

internal static class LspHelpers
{
    internal static TextDocumentSelector s_selector = TextDocumentSelector.ForLanguage("codex");

    internal static string? GetWordAt(string text, int line, int col)
    {
        string[] lines = text.Split('\n');
        if (line < 0 || line >= lines.Length)
        {
            return null;
        }

        string lineText = lines[line];
        if (col < 0 || col >= lineText.Length)
        {
            return null;
        }

        if (!IsIdentChar(lineText[col]))
        {
            return null;
        }

        int start = col;
        while (start > 0 && IsIdentChar(lineText[start - 1]))
        {
            start--;
        }

        int end = col;
        while (end < lineText.Length - 1 && IsIdentChar(lineText[end + 1]))
        {
            end++;
        }

        return lineText[start..(end + 1)];
    }

    static bool IsIdentChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '-';
}
