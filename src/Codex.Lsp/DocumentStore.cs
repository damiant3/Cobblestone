using Codex.Core;

namespace Codex.Lsp;

internal sealed class DocumentStore
{
    volatile Map<string, DocumentEntry> m_entries = Map<string, DocumentEntry>.s_empty;

    public void Update(string uri, string text)
    {
        DocumentEntry? existing = m_entries[uri];
        DocumentEntry entry = existing is not null
            ? existing with { Text = text }
            : new DocumentEntry(text, null);
        m_entries = m_entries.Set(uri, entry);
    }

    public void UpdateResult(string uri, AnalysisResult result)
    {
        DocumentEntry? existing = m_entries[uri];
        if (existing is not null)
        {
            m_entries = m_entries.Set(uri, existing with { Result = result });
        }
    }

    public void Remove(string uri)
    {
        m_entries = m_entries.Remove(uri);
    }

    public string? GetText(string uri)
    {
        return m_entries[uri]?.Text;
    }

    public AnalysisResult? GetResult(string uri)
    {
        return m_entries[uri]?.Result;
    }
}

internal sealed record DocumentEntry(string Text, AnalysisResult? Result);
