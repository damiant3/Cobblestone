// What the server said from a scope's start (docs/Designs/Active/Apps/AssistStructure.md, primitives), in place of a
// JournalSeq mark kept by hand: using (Heard heard = new(k.State)) { ...act...; heard.Has("You must wait") }.
#nullable enable
using System;
using System.Collections.Generic;

namespace Uoaix.Assist;

public sealed class Heard : IDisposable
{
    readonly AssistState m_state;
    readonly long m_from;
    long m_to = long.MaxValue;

    public Heard(AssistState state)
    {
        m_state = state;
        lock (state.Gate)
            m_from = state.JournalSeq;
    }

    // The journal lines said since the scope began, up to its end once disposed.
    public List<string> Lines() => m_state.JournalBetween(m_from, m_to);

    // The server's own lines among them, never a label or anyone's speech.
    public List<string> Server() => m_state.ServerBetween(m_from, m_to);

    // What can answer the scope's action among them: no label, none of the player's own words
    // (AssistState.AnswersBetween; AssistTesting.md T2, every answer reader on it).
    public List<string> Answers() => m_state.AnswersBetween(m_from, m_to);

    // Whether the player spoke among them: a spell's words mean the server took the cast.
    public bool PlayerSpoke() => m_state.PlayerSpokeBetween(m_from, m_to);

    // The last answer, or null.
    public string? Last()
    {
        List<string> lines = Answers();
        return lines.Count == 0 ? null : lines[lines.Count - 1];
    }

    // The first answer holding text, matched anywhere in the line (the journal prefixes a speaker), or null.
    public string? Find(string text)
    {
        foreach (string line in Answers())
        {
            if (line.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                return line;
        }
        return null;
    }

    public bool Has(string text)
    {
        return Find(text) is not null;
    }

    public void Dispose()
    {
        if (m_to != long.MaxValue)
            return;
        lock (m_state.Gate)
            m_to = m_state.JournalSeq;
    }
}
