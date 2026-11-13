using System;

namespace TitanGameHub
{
    /// <summary>
    /// Basit bulanık arama: yazım hatası toleransı ve kısmi eşleşme.
    /// </summary>
    public static class FuzzySearchHelper
    {
        public static int Score(string query, string target)
        {
            if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(target)) return 0;
            string q = Normalize(query);
            string t = Normalize(target);
            if (t == q) return 1000;
            if (t.Contains(q)) return 700 + Math.Max(0, 100 - (t.Length - q.Length));
            if (t.StartsWith(q)) return 800;

            string[] qTokens = q.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string[] tTokens = t.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int matched = 0;
            foreach (string qt in qTokens)
            {
                foreach (string tt in tTokens)
                {
                    if (tt.Contains(qt) || qt.Contains(tt) || LevenshteinDistance(qt, tt) <= 1)
                    { matched++; break; }
                }
            }
            if (qTokens.Length > 0 && matched == qTokens.Length) return 500;
            if (matched > 0) return 200 + matched * 50;

            if (q.Length <= 12)
            {
                int dist = LevenshteinDistance(q, t.Length > q.Length + 6 ? t.Substring(0, q.Length + 6) : t);
                if (dist <= 2) return 150 - dist * 30;
            }
            return 0;
        }

        public static bool Matches(string query, string target, int threshold = 150)
        {
            return Score(query, target) >= threshold;
        }

        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.ToLowerInvariant().Trim();
            s = s.Replace('ı', 'i').Replace('İ', 'i').Replace('ş', 's').Replace('Ş', 's')
                 .Replace('ğ', 'g').Replace('Ğ', 'g').Replace('ü', 'u').Replace('Ü', 'u')
                 .Replace('ö', 'o').Replace('Ö', 'o').Replace('ç', 'c').Replace('Ç', 'c');
            return s;
        }

        public static int LevenshteinDistance(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b == null ? 0 : b.Length;
            if (string.IsNullOrEmpty(b)) return a.Length;
            int[,] d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }
            return d[a.Length, b.Length];
        }
    }
}
