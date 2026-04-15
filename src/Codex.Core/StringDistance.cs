namespace Codex.Core;

public static class StringDistance
{
    public static int Levenshtein(string a, string b)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        int[] prev = new int[b.Length + 1];
        int[] curr = new int[b.Length + 1];

        for (int j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }

        return prev[b.Length];
    }

    public static string? FindClosest(string target, IEnumerable<string> candidates, int maxDistance = 2)
    {
        string? best = null;
        int bestDist = maxDistance + 1;

        foreach (string candidate in candidates)
        {
            // Skip if length difference alone exceeds max distance
            if (Math.Abs(candidate.Length - target.Length) > maxDistance)
            {
                continue;
            }

            int dist = Levenshtein(target, candidate);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = candidate;
            }
        }

        return best;
    }
}
