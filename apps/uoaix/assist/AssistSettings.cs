#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Uoaix.Assist;

// key=value lines in %LOCALAPPDATA%\uoaix-assist\settings.txt: delays the server corrected, round counts, the arm set,
// the reagent list, advice corrections, display choices.
public sealed class AssistSettings
{
    readonly string m_path; Map<string, string> m_kv = Map<string, string>.Empty; readonly Lock m_kvGate = new();
    public AssistSettings(string path)
    {
        m_path = path;
        lock (s_fileGate)
            if (File.Exists(path))
                foreach (string line in File.ReadAllLines(path))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0)
                        m_kv = m_kv.Set(line.Substring(0, eq), line.Substring(eq + 1));
                }
    }
    public string Get(string key, string def)
    {
        return m_kv.Get(key, def);
    }
    public int GetInt(string key, int def)
    {
        int v;
        return int.TryParse(Get(key, ""), out v) ? v : def;
    }
    // An unchanged value writes nothing; the file is written outside the lock Get takes, newest snapshot wins. One gate
    // for every instance: two on one file (the suites' cases, each with its own) wrote it at once, an IOException in a
    // lumber case (once in 11 suite runs, 2026-10-23).
    static readonly Lock s_fileGate = new(); readonly Lock m_writeGate = new(); long m_version, m_written;
    public void Set(string key, string value)
    {
        List<string> lines = new();
        long v;
        lock (m_kvGate)
        {
            string? old;
            if (m_kv.TryGet(key, out old) && old == value)
                return;
            m_kv = m_kv.Set(key, value);
            v = ++m_version;
            foreach (KeyValuePair<string, string> e in m_kv)
                lines.Add(e.Key + "=" + e.Value);
        }
        lock (m_writeGate)
            lock (s_fileGate)
            {
                if (v < m_written)
                    return;
                m_written = v;
                Directory.CreateDirectory(Path.GetDirectoryName(m_path)!);
                File.WriteAllLines(m_path, lines);
            }
    }
}
