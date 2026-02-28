namespace DeskSwitch;

static class FuzzyMatcher
{
    /// <summary>
    /// Returns a score >= 0 if the query matches the text, or -1 if no match.
    /// Higher scores = better match. Exact substring match scores highest.
    /// </summary>
    public static int Score(string text, string query)
    {
        if (string.IsNullOrEmpty(query)) return 0;
        if (string.IsNullOrEmpty(text)) return -1;

        // Exact substring match (best)
        int idx = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
            return 1000 - idx; // prefer matches at the start

        // Subsequence match: all query chars appear in order
        int qi = 0;
        int consecutive = 0;
        int maxConsecutive = 0;
        int lastMatchIdx = -2;

        for (int ti = 0; ti < text.Length && qi < query.Length; ti++)
        {
            if (char.ToLowerInvariant(text[ti]) == char.ToLowerInvariant(query[qi]))
            {
                consecutive = (ti == lastMatchIdx + 1) ? consecutive + 1 : 1;
                if (consecutive > maxConsecutive) maxConsecutive = consecutive;
                lastMatchIdx = ti;
                qi++;
            }
        }

        if (qi < query.Length) return -1; // not all chars matched

        return maxConsecutive * 10;
    }
}
