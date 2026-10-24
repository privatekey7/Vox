namespace Vox.Core.Text;

/// <summary>
/// Learns from the user's own fixes: compares a text field right after a dictation was pasted with
/// the same field later. If inside the dictation exactly one word was replaced by one similar-looking
/// word ("кубернетис" → "Kubernetes"), that pair is a correction worth remembering.
/// </summary>
/// <remarks>
/// Deliberately strict — learning a wrong pair is worse than missing one: several changed words,
/// deletions, insertions, case-only changes, meaning changes ("кот" → "пёс") and grammar fixes of
/// the user's own wording ("тёмные" → "тёмной", "блоге" → "логе") are all ignored.
/// </remarks>
public static class CorrectionDetector
{
    private const string WordPunct = ".,!?;:—–-\"'«»()[]{}…";
    private const double MinSimilarity = 0.5;
    private const double MinSoundSimilarity = 0.75; // the sound skeleton is coarser than letters: "кошка" ~ "cat" is 0.5 on it
    private const int MinSoundSkeleton = 3;
    private const int MaxSplitWords = 3;

    private static readonly Dictionary<char, string> Translit = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "e",
        ['ж'] = "zh", ['з'] = "z", ['и'] = "i", ['й'] = "i", ['к'] = "k", ['л'] = "l", ['м'] = "m",
        ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u",
        ['ф'] = "f", ['х'] = "h", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sch", ['ъ'] = "",
        ['ы'] = "y", ['ь'] = "", ['э'] = "e", ['ю'] = "yu", ['я'] = "ya",
    };

    /// <summary>The (wrong, correct) pair, or null when the edit is not a single-word correction.</summary>
    /// <param name="before">Field text right after the dictation was pasted.</param>
    /// <param name="after">The same field later.</param>
    /// <param name="inserted">The pasted dictation (without the leading separator).</param>
    /// <param name="isTarget">Is this word a dictionary target Vox wrote itself ("Fluxar" from "флюксар → Fluxar")?
    /// A fix of such a word is a typo in the target, so the grammar-edit guard does not apply.</param>
    /// <param name="confirmed">The user pressed Ctrl+S right after the fix: the words need not look alike
    /// ("кошка" → "cat"); every other guard stays.</param>
    public static (string Wrong, string Correct)? Detect(
        string before, string after, string inserted, Func<string, bool>? isTarget = null, bool confirmed = false) =>
        Analyze(before, after, inserted, isTarget, confirmed).Pair;

    private static ((string Wrong, string Correct)? Pair, string Reason) Analyze(
        string before, string after, string inserted, Func<string, bool>? isTarget, bool confirmed)
    {
        if (Changes(before, after, inserted) is not var (old, now, changes, first, last))
        {
            return (null, "the dictation is no longer in the field");
        }

        if (changes.Count == 0)
        {
            return (null, "no change inside the dictation");
        }

        // One word, or a term the model split in two or three ("Zena Koder." → "Zencoder.").
        if (changes is not [{ OldStart: var os, OldEnd: var oe, NewStart: var ns, NewEnd: var ne }])
        {
            return (null, "several places changed");
        }

        if (oe - os is < 1 or > MaxSplitWords)
        {
            return (null, "not a word replacement");
        }

        // The user went on typing right after (or before) the fixed word: "PPFI." → "Pipify. Дальше".
        if (ne - ns > 1 && oe - 1 == last)
        {
            ne = ns + 1;
        }
        else if (ne - ns > 1 && os == first)
        {
            ns = ne - 1;
        }

        if (ne - ns != 1)
        {
            return (null, "not a word replacement");
        }

        var wrong = string.Join(' ', old.Skip(os).Take(oe - os).Select(w => w.Text)).Trim(WordPunct.ToCharArray());
        var correct = now[ns].Text.Trim(WordPunct.ToCharArray());
        (wrong, correct) = ChangedPart(wrong, correct);
        if (wrong.Length == 0 || correct.Length == 0 || string.Equals(wrong, correct, StringComparison.OrdinalIgnoreCase))
        {
            return (null, "only punctuation or case changed");
        }

        if (!confirmed && Similarity(wrong, correct) is var similarity && similarity < MinSimilarity)
        {
            return (null, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"the words are not alike ({similarity:0.00})"));
        }

        if (IsGrammarEdit(wrong, correct) && isTarget?.Invoke(wrong) != true)
        {
            return (null, "a grammar or typo edit of an ordinary word");
        }

        // As the user fixed it, case kept ("Лебовски" → "Lebowski"): the dictionary stores it lower-cased
        // and UserCorrections works out the base form for other cases.
        return ((wrong, correct), "");
    }

    /// <summary>
    /// Short log line for an edit that was not learned: why, and which word changes touch the dictation
    /// ("several places changed; 2 changes: \"PPFI.\" → \"Pipify.\", \"\" → \"тест\"").
    /// </summary>
    public static string Describe(
        string before, string after, string inserted, Func<string, bool>? isTarget = null, bool confirmed = false)
    {
        var reason = Analyze(before, after, inserted, isTarget, confirmed).Reason;
        if (Changes(before, after, inserted) is not var (old, now, changes, _, _) || changes.Count == 0)
        {
            return reason;
        }

        string Span(List<Word> words, int from, int to) =>
            Clip(string.Join(' ', words.Skip(from).Take(to - from).Select(w => w.Text)));
        var shown = changes.Take(3).Select(c => $"\"{Span(old, c.OldStart, c.OldEnd)}\" → \"{Span(now, c.NewStart, c.NewEnd)}\"");
        return $"{reason}; {changes.Count} change{(changes.Count == 1 ? "" : "s")}: {string.Join(", ", shown)}{(changes.Count > 3 ? ", …" : "")}";
    }

    /// <summary>
    /// <see cref="Describe"/> without the words, for a log without texts: why, and how many places changed
    /// ("several places changed; 2 changes"). What the user typed stays out of the log.
    /// </summary>
    public static string DescribeWithoutText(
        string before, string after, string inserted, Func<string, bool>? isTarget = null, bool confirmed = false)
    {
        var reason = Analyze(before, after, inserted, isTarget, confirmed).Reason;
        var count = Changes(before, after, inserted)?.Changes.Count ?? 0;
        return count == 0 ? reason : $"{reason}; {count} change{(count == 1 ? "" : "s")}";
    }

    /// <summary>
    /// The places inside the dictation where <paramref name="after"/> differs from <paramref name="before"/>: the words
    /// replaced (indices of <paramref name="before"/>'s words) and the text now there. For <see cref="TypingTrace"/>.
    /// </summary>
    internal static List<(int OldStart, int OldEnd, string Text)> DictationEdits(string before, string after, string inserted) =>
        Changes(before, after, inserted) is var (_, now, changes, _, _)
            ? [.. changes.Select(c => (c.OldStart, c.OldEnd, string.Join(' ', now.Skip(c.NewStart).Take(c.NewEnd - c.NewStart).Select(w => w.Text))))]
            : [];

    private static string Clip(string s) => s.Length > 40 ? s[..40] + "…" : s;

    private static (List<Word> Old, List<Word> Now, List<Change> Changes, int First, int Last)? Changes(
        string before, string after, string inserted)
    {
        inserted = inserted.Trim();
        var start = before.LastIndexOf(inserted, StringComparison.Ordinal);
        if (inserted.Length == 0 || start < 0)
        {
            return null;
        }

        // Long documents: only the neighbourhood of the dictation matters, and the word diff is
        // quadratic. Cut words at the window edges show up as changes outside the dictation and
        // are ignored below.
        const int margin = 1500;
        if (before.Length > inserted.Length + 2 * margin)
        {
            var from = Math.Max(0, start - margin);
            var to = Math.Min(before.Length, start + inserted.Length + margin);
            var afterTo = Math.Min(after.Length, to + margin);
            after = after[Math.Min(from, after.Length)..afterTo];
            before = before[from..to];
            start -= from;
        }

        var old = Words(before);
        var now = Words(after);
        var first = old.FindIndex(w => w.End > start);
        var last = old.FindLastIndex(w => w.Start < start + inserted.Length);
        if (first < 0 || last < first)
        {
            return null;
        }

        // Single-word replacement inside [first, last]: same words before and after it.
        var changes = Diff(old.Select(w => w.Text).ToList(), now.Select(w => w.Text).ToList())
            .Where(c => c.OldEnd > first && c.OldStart <= last)
            .ToList();
        return (old, now, changes, first, last);
    }

    // A hyphenated word with one part fixed: the pair is that part ("ЛМ-полировки" → "LLM-полировки" is "ЛМ" → "LLM").
    private static (string Wrong, string Correct) ChangedPart(string wrong, string correct)
    {
        var a = wrong.Split('-');
        var b = correct.Split('-');
        if (a.Length < 2 || a.Length != b.Length)
        {
            return (wrong, correct);
        }

        var changed = Enumerable.Range(0, a.Length).Where(i => a[i] != b[i]).ToList();
        return changed is [var only] ? (a[only], b[only]) : (wrong, correct);
    }

    /// <summary>Do two words look like spellings of the same sound (across Cyrillic and Latin)?</summary>
    public static bool LookAlike(string a, string b) => Similarity(a, b) >= MinSimilarity;

    private static double Similarity(string a, string b)
    {
        var x = Latin(a).Replace(" ", "", StringComparison.Ordinal); // "zena koder" vs "zencoder"
        var y = Latin(b).Replace(" ", "", StringComparison.Ordinal);
        var longest = Math.Max(x.Length, y.Length);
        var letters = longest == 0 ? 0 : 1.0 - (double)Levenshtein(x, y) / longest;
        return Math.Max(letters, SoundSimilarity(a, b));
    }

    // Across scripts letters miss what sound keeps: "апгрейд" is "apgreid", 0.43 from "upgrade" by letters,
    // and both have the skeleton "апграт". Counted as a pass only from MinSoundSimilarity.
    private static double SoundSimilarity(string a, string b)
    {
        if (IsCyrillic(a.ToLowerInvariant()) == IsCyrillic(b.ToLowerInvariant()))
        {
            return 0;
        }

        var x = FuzzyTerms.Skeleton(a.Replace(" ", "", StringComparison.Ordinal));
        var y = FuzzyTerms.Skeleton(b.Replace(" ", "", StringComparison.Ordinal));
        if (Math.Min(x.Length, y.Length) < MinSoundSkeleton)
        {
            return 0;
        }

        var sound = FuzzyTerms.Similarity(x, y);
        return sound >= MinSoundSimilarity ? sound : 0;
    }

    /// <summary>
    /// Same-script edits that only touch the word's ending or add/remove a letter or two at its
    /// edge: inflection or a typo fix, not a misheard word (those are learned across scripts or
    /// change the stem).
    /// </summary>
    private static bool IsGrammarEdit(string a, string b)
    {
        a = a.ToLowerInvariant();
        b = b.ToLowerInvariant();
        if (IsCyrillic(a) != IsCyrillic(b))
        {
            return false; // "гитхаб" → "GitHub"
        }

        var shorter = Math.Min(a.Length, b.Length);
        var prefix = 0;
        while (prefix < shorter && a[prefix] == b[prefix])
        {
            prefix++;
        }

        var (lo, hi) = a.Length <= b.Length ? (a, b) : (b, a);
        var edgeOnly = hi.Length - lo.Length <= 2 && (hi.StartsWith(lo, StringComparison.Ordinal) || hi.EndsWith(lo, StringComparison.Ordinal));
        var endingOnly = prefix >= 3 && prefix >= shorter - 3; // the stem is the same, at most 3 letters differ at the end
        return edgeOnly || endingOnly;
    }

    private static bool IsCyrillic(string word) => word.Any(c => c is >= 'а' and <= 'я' or 'ё');

    private static string Latin(string word) =>
        string.Concat(word.ToLowerInvariant().Select(c => Translit.TryGetValue(c, out var t) ? t : c.ToString()));

    private static int Levenshtein(string a, string b)
    {
        var prev = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var cur = new int[b.Length + 1];
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }

            prev = cur;
        }

        return prev[b.Length];
    }

    private sealed record Word(string Text, int Start, int End);

    private sealed record Change(int OldStart, int OldEnd, int NewStart, int NewEnd);

    private static List<Word> Words(string text)
    {
        var words = new List<Word>();
        var i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            var s = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            if (i > s)
            {
                words.Add(new Word(text[s..i], s, i));
            }
        }

        return words;
    }

    /// <summary>Non-equal regions of a word-level LCS diff.</summary>
    private static List<Change> Diff(List<string> a, List<string> b)
    {
        var lcs = new int[a.Count + 1, b.Count + 1];
        for (var i = a.Count - 1; i >= 0; i--)
        {
            for (var j = b.Count - 1; j >= 0; j--)
            {
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var changes = new List<Change>();
        int x = 0, y = 0, cx = -1, cy = -1;
        while (x < a.Count || y < b.Count)
        {
            if (x < a.Count && y < b.Count && a[x] == b[y])
            {
                Flush();
                x++;
                y++;
            }
            else
            {
                if (cx < 0)
                {
                    (cx, cy) = (x, y);
                }

                if (y < b.Count && (x == a.Count || lcs[x, y + 1] >= lcs[x + 1, y]))
                {
                    y++;
                }
                else
                {
                    x++;
                }
            }
        }

        Flush();
        return changes;

        void Flush()
        {
            if (cx >= 0)
            {
                changes.Add(new Change(cx, x, cy, y));
                cx = cy = -1;
            }
        }
    }
}
