using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vox.Core.Text;

/// <summary>A name seen on screen, for <see cref="FuzzyTerms"/>: in a window right now, or only remembered.</summary>
public sealed record ScreenName(string Name, bool OnScreen);

/// <summary>
/// Latin names in window titles ("Remotion Studio - Google Chrome" → "Remotion"):
/// words of 3+ letters, CamelCase, with digits ("LMArena", "HAPP", "Qwen3"), and pairs of capitalised words.
/// </summary>
/// <remarks>
/// Only single names leave this class, never the text. Secrets are dropped whole: a run without spaces longer
/// than <see cref="MaxRun"/> (API keys "sk-proj-…", wallet addresses "0x…", "bc1q…") loses all its parts, and a
/// word that does not look like a name ("xK9mQ2pL": digits inside, case jumping) is dropped. Seed phrases are
/// ordinary English words, which <paramref name="isKnown"/> drops.
/// </remarks>
public static partial class ScreenWords
{
    public const int MaxRun = 24;

    // A run without spaces: a key or an address is one ("sk-proj-AbC_…", "0x3f…", "ghp_…").
    [GeneratedRegex(@"[A-Za-z0-9_\-+/=.:@#$%&~]+")] private static partial Regex Run();

    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9]*")] private static partial Regex Token();

    // Word, CamelCase, an acronym, before or after a word ("LMArena", "macOS", "DeepL"), at most two digits and a letter at the end.
    [GeneratedRegex(@"^(?:[A-Z]{2,6}|[A-Z]?[a-z]+)(?:[A-Z][a-z]+){0,3}(?:[A-Z]{1,4})?(?:[0-9]{1,2}[a-z]?)?$")] private static partial Regex NameShape();

    /// <param name="isKnown">An ordinary English word: not a name (a term on screen still is one).</param>
    public static List<string> Extract(string text, Func<string, bool> isKnown)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? previous = null; // the last name-like word, for a pair
        var previousEnd = -1;
        foreach (Match run in Run().Matches(text))
        {
            if (run.Length > MaxRun)
            {
                previous = null;
                continue; // a key, an address, a hash: none of its parts
            }

            foreach (Match token in Token().Matches(run.Value))
            {
                var word = token.Value;
                var start = run.Index + token.Index;
                if (!IsName(word))
                {
                    previous = null;
                    continue;
                }

                var known = isKnown(word);
                if (!known && seen.Add(word))
                {
                    names.Add(word);
                }

                // "Remotion Studio": two capitalised words with one space, not both known.
                if (previous is not null && char.IsUpper(word[0]) && char.IsUpper(previous[0])
                    && start == previousEnd + 1 && text[previousEnd] == ' '
                    && !(known && isKnown(previous)))
                {
                    var pair = previous + " " + word;
                    if (!isKnown(pair) && seen.Add(pair))
                    {
                        names.Add(pair);
                    }
                }

                previous = word;
                previousEnd = start + word.Length;
            }
        }

        return Distinct(names);
    }

    public const int MaxWindowNames = 50;

    /// <summary>
    /// Names in the text of the window the user dictates into (a page, a chat): at most <paramref name="limit"/>,
    /// the ones met most often first, then the latest (in a chat, the messages nearest the input field).
    /// </summary>
    /// <remarks>A long article holds hundreds of capitalised words; each extra name is one more chance for
    /// <see cref="FuzzyTerms"/> to replace a word it should not.</remarks>
    public static List<string> FromWindowText(string text, Func<string, bool> isKnown, int limit = MaxWindowNames)
    {
        var names = Extract(text, isKnown);
        return names.Count <= limit
            ? names
            : [.. names
                .Select(n => (Name: n, Count: Occurrences(text, n), Last: text.LastIndexOf(n, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(n => n.Count).ThenByDescending(n => n.Last)
                .Take(limit).Select(n => n.Name)];
    }

    private static int Occurrences(string text, string name)
    {
        var count = 0;
        for (var i = text.IndexOf(name, StringComparison.OrdinalIgnoreCase); i >= 0; i = text.IndexOf(name, i + name.Length, StringComparison.OrdinalIgnoreCase))
        {
            count++;
        }

        return count;
    }

    /// <summary>Names worth remembering: a term is in the dictionary already.</summary>
    public static IEnumerable<string> Unknown(IEnumerable<string> names) => names.Where(n => !FuzzyTerms.IsKnownName(n));

    /// <summary>One spelling per name, a capitalised one first ("Cruzade" over "cruzade" of a domain).</summary>
    public static List<string> Distinct(IEnumerable<string> names) =>
        [.. names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Select(g => g.FirstOrDefault(n => n.Any(char.IsUpper)) ?? g.First())];

    private static bool IsName(string word) =>
        word.Length <= MaxRun && word.Count(char.IsAsciiLetter) >= 3 && NameShape().IsMatch(word)
        && (word.Any(char.IsUpper) || word.Any(c => "aeiouy".Contains(c, StringComparison.Ordinal))); // "xkcd"-like noise
}

/// <summary>
/// Latin words the model itself wrote (raw dictations): Vox's own text in a window, a chat titled after a dictation
/// ("Jamper X Change"), must not come back as a name, or a misheard word would teach itself.
/// </summary>
public sealed partial class OwnWords
{
    public const int HistoryLimit = 1000;

    private readonly Lock _lock = new();
    private readonly HashSet<string> _words = new(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9]*")] private static partial Regex Latin();

    public void Add(string raw)
    {
        lock (_lock)
        {
            foreach (Match m in Latin().Matches(raw))
            {
                _words.Add(m.Value);
            }
        }
    }

    /// <summary>Did the model write this name, or a word of it ("Jamper" of "Jamper Change")?</summary>
    public bool Contains(string name)
    {
        lock (_lock)
        {
            return name.Split(' ').Any(_words.Contains);
        }
    }
}

/// <summary>
/// Names Vox has seen in window titles, in screen_names.json next to the history: name, how often, when last.
/// At most <see cref="Limit"/>; the ones not seen for longest go first.
/// </summary>
/// <remarks>Only names from <see cref="ScreenWords.Extract"/>, without digits, never written by the model (<see cref="OwnWords"/>).</remarks>
public sealed class ScreenMemory
{
    public const int Limit = 5000;

    private readonly string? _path;
    private readonly int _limit;
    private readonly Lock _lock = new();
    private readonly Lock _saveLock = new();
    private readonly Dictionary<string, Entry> _names = new(StringComparer.OrdinalIgnoreCase);

    public sealed record Entry(string Name, int Count, DateTime Last);

    public ScreenMemory(string? path = null, int limit = Limit)
    {
        _path = path;
        _limit = limit;
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _names.Count;
            }
        }
    }

    public IReadOnlyList<Entry> Entries
    {
        get
        {
            lock (_lock)
            {
                return [.. _names.Values];
            }
        }
    }

    /// <summary>Counts each name once per call; returns whether anything changed.</summary>
    public bool See(IEnumerable<string> names, DateTime now)
    {
        lock (_lock)
        {
            var changed = false;
            foreach (var name in names.Where(IsRememberable).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                // A capitalised spelling stays: "cruzade" of a domain does not replace "Cruzade".
                _names[name] = _names.TryGetValue(name, out var e)
                    ? e with { Name = name.Any(char.IsUpper) || !e.Name.Any(char.IsUpper) ? name : e.Name, Count = e.Count + 1, Last = now }
                    : new Entry(name, 1, now);
                changed = true;
            }

            DropOverLimit();
            return changed;
        }
    }

    /// <summary>
    /// A name with digits ("Qwen3", but also "Mypass12") is used while on screen and never saved; nor is a pair
    /// ("Flux Krea" would pull in "Fluxar").
    /// </summary>
    public static bool IsRememberable(string name) => !name.Any(c => char.IsDigit(c) || c == ' ');

    /// <summary>
    /// Takes names remembered on another PC: the larger count and the later time stay (so importing the same file
    /// twice changes nothing); returns how many are new.
    /// </summary>
    /// <remarks>
    /// The file may come from someone else (security audit L2): only names <see cref="ScreenWords"/> could have
    /// remembered (Latin letters, at most <see cref="ScreenWords.MaxRun"/>), no time later than
    /// <paramref name="now"/>, and the oldest go over <see cref="Limit"/> as in <see cref="See"/>.
    /// </remarks>
    public int Merge(IEnumerable<Entry> entries, DateTime? now = null)
    {
        var latest = now ?? DateTime.UtcNow;
        lock (_lock)
        {
            var added = 0;
            foreach (var e in entries.Where(IsMergeable).Select(e => e with { Count = Math.Clamp(e.Count, 1, MaxCount), Last = e.Last > latest ? latest : e.Last }))
            {
                if (_names.TryGetValue(e.Name, out var mine))
                {
                    _names[e.Name] = mine with { Count = Math.Max(mine.Count, e.Count), Last = mine.Last > e.Last ? mine.Last : e.Last };
                }
                else
                {
                    _names[e.Name] = e;
                    added++;
                }
            }

            DropOverLimit();
            return added;
        }
    }

    private const int MaxCount = 1_000_000;

    private static bool IsMergeable(Entry? e) =>
        e?.Name is { Length: > 0 and <= ScreenWords.MaxRun } name && name.All(char.IsAsciiLetter);

    // The ones not seen for longest go first; the caller holds _lock.
    private void DropOverLimit()
    {
        if (_names.Count > _limit)
        {
            foreach (var old in _names.Values.OrderBy(e => e.Last).ThenBy(e => e.Count).Take(_names.Count - _limit).ToList())
            {
                _names.Remove(old.Name);
            }
        }
    }

    /// <summary>Drops the remembered names <paramref name="drop"/> picks; returns how many.</summary>
    public int Forget(Func<string, bool> drop)
    {
        lock (_lock)
        {
            var gone = _names.Keys.Where(drop).ToList();
            gone.ForEach(n => _names.Remove(n));
            return gone.Count;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _names.Clear();
        }

        lock (_saveLock)
        {
            if (_path is not null && File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
    }

    public static ScreenMemory Load(string path, int limit = Limit)
    {
        var memory = new ScreenMemory(path, limit);
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<List<Stored>>(File.ReadAllText(path)) is { } stored)
            {
                foreach (var s in stored.Where(s => !string.IsNullOrWhiteSpace(s.N) && IsRememberable(s.N)))
                {
                    memory._names[s.N] = new Entry(s.N, s.C, s.T);
                }
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // corrupt or locked: start empty, the next save replaces it
        }

        return memory;
    }

    /// <summary>Atomic write, as <see cref="Settings.Save"/>.</summary>
    public void Save()
    {
        if (_path is null)
        {
            return;
        }

        string json;
        lock (_lock)
        {
            json = JsonSerializer.Serialize(_names.Values.Select(e => new Stored(e.Name, e.Count, e.Last)));
        }

        lock (_saveLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
    }

    private sealed record Stored(string N, int C, DateTime T);
}
