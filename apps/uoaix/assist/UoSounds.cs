#nullable enable
using System;
using System.IO;
using System.Text;

namespace Uoaix.Assist;

// The 1.25 sound names: SOUNDIDX.MUL holds 12 bytes a sound (offset, length, extra), and each SOUND.MUL entry opens
// with its file name, NUL-padded, in the first 20 bytes. Null for an id with no sound.
public static class UoSounds
{
    static string[]? s_names;
    public static string? Name(int id)
    {
        if (UoArt.Folder == null)
            return null;
        if (s_names == null)
        {
            string[] l = new string[0];
            try
            {
                byte[] idx = File.ReadAllBytes(Path.Combine(UoArt.Folder, "SOUNDIDX.MUL"));
                l = new string[idx.Length / 12];
                using (FileStream fs = File.OpenRead(Path.Combine(UoArt.Folder, "SOUND.MUL")))
                {
                    byte[] b = new byte[20];
                    for (int i = 0; i < l.Length; i++)
                    {
                        int off = BitConverter.ToInt32(idx, i * 12), len = BitConverter.ToInt32(idx, i * 12 + 4);
                        if (off < 0 || len <= 20)
                            continue;
                        fs.Position = off;
                        if (fs.Read(b, 0, 20) < 20)
                            continue;
                        int n = Array.IndexOf(b, (byte)0);
                        string s = Encoding.ASCII.GetString(b, 0, n < 0 ? 20 : n);
                        l[i] = s.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? s.Substring(0, s.Length - 4)
                            : s;
                    }
                }
            }
            catch { }
            s_names = l;
        }
        return id >= 0 && id < s_names.Length ? s_names[id] : null;
    }
}
