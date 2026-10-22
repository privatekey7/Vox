using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace Vox.Core.Text;

/// <summary>
/// Phonetic search in terms.json, after the exact <see cref="Terms"/>: a model's own spelling that no
/// entry lists ("Fruentand", "Bek End", "фронтанд") is replaced when one term is clearly the closest.
/// </summary>
/// <remarks>
/// Latin words (one, or two the model split: "Frount End") are compared by a sound skeleton with every term
/// and, one word only, with ordinary English words (english.txt.gz, SCOWL): "water" stays "water" instead of
/// becoming "Wouter", and a garbled English word becomes the word. A Cyrillic word is compared only with the
/// Cyrillic spellings of the "spelling" group ("фронтенд", "бэкенд") and their case forms, letter by letter:
/// by sound, ordinary word forms come too close to brands ("домены" ~ "джемини"). A hyphenated word is
/// compared whole, then part by part ("Фронтент-дизайн"). When two candidates are about as close, the more
/// frequent one wins: a term before an English word, a term that is a common English word ("Python")
/// before another term ("HeyGen"). Never touched: under 5 letters, ordinary Russian words (common-words),
/// English words, words already spelled as a term; a Cyrillic capital mid-sentence (a name) only on an
/// exact match. The user's own dictionary is not searched: it is exact, and a learned typo must not pull
/// in other words.
/// </remarks>
public sealed partial class FuzzyTerms
{
    /// <summary>A replacement, for the log: "[FUZZY] "Fruentand" → "фронтенд" 0.92", "[EN]" for an English word.</summary>
    /// <param name="Screen">A name from the screen: "окно" (a window now) or "память" (seen before), for "[SCREEN]".</param>
    public sealed record Match(string Spoken, string Replacement, double Score, bool English = false, string? Screen = null);

    /// <param name="Key">Sound skeleton (Latin search) or normalised Cyrillic form (Cyrillic search).</param>
    /// <param name="Ending">Case ending of a Cyrillic form ("фронтенде": "е"), null for the base form.</param>
    /// <param name="Rank">Frequency, lower is more frequent: <see cref="TermRank"/>, an English level, or above.</param>
    /// <param name="Screen">A screen name: "окно" or "память" (see <see cref="Match.Screen"/>).</param>
    /// <param name="ShortOnly">A term on screen: searched by the short-word rule only (terms are searched anyway).</param>
    private sealed record Candidate(
        string Key, string Source, string Target, int Rank, bool English = false, string? Ending = null, string? Screen = null, bool ShortOnly = false)
    {
        public ulong Letters { get; } = Mask(Source); // for English words: a quick check before the edit distance

        public string Vowels { get; } = English ? "" : VowelSkeleton(Source); // for short Latin words
    }

    private const int MinLetters = 5;
    private const int ShortKey = 5; // a Latin skeleton this short is checked with vowels
    private const double Threshold = 0.85;
    private const double LatinThreshold = 0.85; // under it, dev words SCOWL lacks: "webhook" ~ "Webpack" 0.83
    private const double Margin = 0.05;
    private const double Tie = 0.02; // closer than this, frequency decides

    // Ranks: a term that is an English word takes the word's SCOWL level (10 most common … 60), other terms
    // TermRank, English words their level plus EnglishRank: Russian speech names terms more than English words,
    // so only the most common ones (level 10) stand level with a term ("Watter": "water" or "Wouter", neither).
    private const int TermRank = 40;
    private const int EnglishRank = 30;
    private const int MaxCandidateLevel = 60; // rarer words (70) only protect themselves

    // Latin in Russian speech is a term more often than an English word: an English word competes with a
    // term at its score minus TermBonus, and replaces only from EnglishThreshold and a common level
    // ("Pigon" stays rather than becoming "pigeon" or a rare word).
    private const double TermBonus = 0.04;
    private const double LooseLetters = 0.75; // letters of an English word next to one, on an exact sound skeleton

    private const double EnglishThreshold = 0.9;
    private const int RareLevel = 50;

    // Screen names (<see cref="ScreenName"/>): one in a window ranks as a term, a remembered one after terms.
    // A name or a spoken word under MinLetters only on an exact skeleton and only while the name is on screen.
    private const int WindowRank = TermRank;
    private const int MemoryRank = TermRank + 5;
    private const double CyrillicScreenThreshold = 0.9;
    private const double MemoryThreshold = 0.9;
    private const string Window = "окно";
    private const string Memory = "память";

    private static readonly Lazy<FuzzyTerms> Default = new(() =>
    {
        var groups = Terms.LoadGroups();
        return new FuzzyTerms(
            groups.Values.SelectMany(g => g).SelectMany(t => t.Value.Select(s => (s, t.Key))),
            groups.TryGetValue("spelling", out var spelling) ? spelling.SelectMany(t => t.Value.Select(s => (s, t.Key))) : [],
            latin: true,
            LoadEnglish());
    });

    private readonly bool _latinOn;

    private readonly Dictionary<string, int> _english = new(StringComparer.Ordinal);
    private readonly Dictionary<(char, int), List<Candidate>> _latin = [];
    private readonly Dictionary<(char, int), List<Candidate>> _latinEnglish = [];
    private readonly Dictionary<(char, int), List<Candidate>> _cyrillic = [];
    private readonly HashSet<string> _termWords = new(StringComparer.OrdinalIgnoreCase); // terms and their words
    private readonly HashSet<string> _targets = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string, bool), Candidate[]> _screenCache = new();

    [GeneratedRegex(@"\p{L}+(?:-\p{L}+)*")] private static partial Regex Word();

    /// <param name="pairs">(spoken source, term) as in terms.json; each term also stands for itself.</param>
    /// <param name="cyrillicPairs">Pairs whose one-word Cyrillic sources a Cyrillic word may match ("spelling").</param>
    /// <param name="latin">Also search Latin words.</param>
    /// <param name="english">Ordinary English words with their SCOWL level (10 most common … 70).</param>
    public FuzzyTerms(
        IEnumerable<(string Source, string Target)> pairs,
        IEnumerable<(string Source, string Target)> cyrillicPairs,
        bool latin,
        IEnumerable<(string Word, int Level)>? english = null)
    {
        _latinOn = latin;
        foreach (var (word, level) in english ?? [])
        {
            _english.TryAdd(word, level);
        }

        var list = pairs.ToList();
        var terms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (source, target) in list.Concat(list.Select(p => p.Target).Distinct().Select(t => (t, t))))
        {
            terms.Add(target.ToLowerInvariant());
            _targets.Add(target);
            _termWords.Add(target);
            _termWords.UnionWith(target.Split(' '));
            // Spelled letters ("бэ гэ пэ") and abbreviations ("BGP" → "бгп") are too short to compare by sound.
            var skeleton = Skeleton(source);
            if (skeleton.Length >= 4 && !source.Split(' ', '-').Any(w => w.Length <= 2))
            {
                Add(_latin, new Candidate(skeleton, source, target, RankOf(target)));
            }
        }

        foreach (var (word, level) in _english)
        {
            var skeleton = Skeleton(word);
            if (level <= MaxCandidateLevel && word.Length >= MinLetters && skeleton.Length >= 4 && !terms.Contains(word))
            {
                // By first letter and length: a garbled English word keeps them ("Beautifull", "promis").
                _latinEnglish.TryAdd((word[0], word.Length), []);
                _latinEnglish[(word[0], word.Length)].Add(new Candidate(skeleton, word, word, EnglishRank + level, English: true));
            }
        }

        foreach (var (source, target) in cyrillicPairs.Where(p => IsCyrillic(p.Source) && !p.Source.Contains(' ', StringComparison.Ordinal)))
        {
            Add(_cyrillic, new Candidate(Normal(source), source, target, TermRank));
            foreach (var (form, ending) in Declension.Forms(source))
            {
                Add(_cyrillic, new Candidate(Normal(form), source, target, TermRank, Ending: ending));
            }
        }
    }

    // "Python" is also a common English word: said more often than "HeyGen".
    private int RankOf(string target) =>
        _english.TryGetValue(target.ToLowerInvariant(), out var level) && level < TermRank ? level : TermRank;

    private static void Add(Dictionary<(char, int), List<Candidate>> index, Candidate c)
    {
        var key = (Class(c.Key[0]), c.Key.Length);
        if (!index.TryGetValue(key, out var bucket))
        {
            index[key] = bucket = [];
        }

        if (!bucket.Any(b => b.Key == c.Key && b.Target == c.Target && b.Vowels == c.Vowels))
        {
            bucket.Add(c);
        }
    }

    /// <summary>english.txt.gz: "#level" lines, each followed by the words of that level.</summary>
    internal static IEnumerable<(string Word, int Level)> LoadEnglish()
    {
        using var stream = typeof(FuzzyTerms).Assembly.GetManifestResourceStream("Vox.Core.english.txt.gz")
            ?? throw new InvalidOperationException("Embedded english.txt.gz is missing");
        using var gzip = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var level = 0;
        while (reader.ReadLine() is { Length: > 0 } line)
        {
            if (line[0] == '#')
            {
                level = int.Parse(line.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                yield return (line, level);
            }
        }
    }

    /// <summary>Loads the dictionaries (about half a second) ahead of the first dictation.</summary>
    public static void Prepare() => _ = Default.Value;

    /// <summary>Replaces unknown spellings of terms in <paramref name="text"/>; each replacement goes to <paramref name="log"/>.</summary>
    /// <param name="screen">Names on screen and remembered ones, searched as terms (<see cref="ScreenWords"/>).</param>
    public static string Apply(string text, ICollection<Match>? log = null, IReadOnlyCollection<ScreenName>? screen = null) =>
        Default.Value.Replace(text, log, screen);

    /// <summary>An English word or a term (any case): not a new name for <see cref="ScreenWords.Extract"/>.</summary>
    public static bool IsKnownName(string word) => Default.Value.IsKnown(word);

    /// <summary>Ordinary English words only ("Visual Studio"): a term on screen is still a name (<see cref="ScreenWords.Extract"/>).</summary>
    public static bool IsEnglishName(string words) => words.Split(' ').All(Default.Value.IsEnglish);

    public bool IsKnown(string word) => _termWords.Contains(word) || word.Split(' ').All(IsEnglish);

    /// <summary>Is this an ordinary English word ("water"; "well-known": every part)?</summary>
    public bool IsEnglish(string word) => word.Split('-').All(p => _english.ContainsKey(p.ToLowerInvariant()));

    public string Replace(string text, ICollection<Match>? log = null, IReadOnlyCollection<ScreenName>? screen = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var names = _latinOn && screen is { Count: > 0 } ? ScreenIndex(screen) : null;

        var words = Word().Matches(text);
        var result = new StringBuilder(text.Length);
        var copied = 0;
        Dictionary<(string, bool), Found?> single = []; // one Latin word is searched up to twice: alone and against a pair

        Found? FindWord(string word, bool name, bool loose = false) =>
            name ? Find(word, name, english: true, names, loose)
            : single.TryGetValue((word, loose), out var known) ? known
            : single[(word, loose)] = Find(word, name, english: true, names, loose);

        void Put(int start, int end, string spoken, Found found)
        {
            var replacement = found.Screen is not null && found.Name is { } seen && end + 1 < text.Length && text[end] == '.' && char.IsAsciiLetterLower(text[end + 1])
                ? seen // "cruzade.com"
                : Cased(found.Replacement, spoken, found.English, StartsSentence(text, start) || InTitle(text, start, end));
            log?.Add(new Match(spoken, replacement, Math.Round(found.Score, 2), found.English, found.Screen));
            result.Append(text, copied, start - copied).Append(replacement);
            copied = end;
            while (replacement.EndsWith('+') && copied < text.Length && text[copied] == '+')
            {
                copied++; // "Charb++" → "C++", as Terms does
            }
        }

        // "Lazy Trasur", "Creat Token": a Latin word next to an English word, one space apart.
        bool EnglishNeighbour(int k) =>
            !IsCyrillic(words[k].Value)
            && ((k > 0 && Spaced(words[k - 1], words[k]) && !IsCyrillic(words[k - 1].Value) && IsEnglish(words[k - 1].Value))
                || (k + 1 < words.Count && Spaced(words[k], words[k + 1]) && !IsCyrillic(words[k + 1].Value) && IsEnglish(words[k + 1].Value)));
        bool Spaced(System.Text.RegularExpressions.Match a, System.Text.RegularExpressions.Match b) =>
            b.Index == a.Index + a.Length + 1 && text[a.Index + a.Length] == ' ';

        // A Latin word becomes a Cyrillic term only inside Russian speech and in its number: "Это Plagens" →
        // "плагины", but "Stells Extra Plagin для …" stays.
        bool RussianAround(int k, int n, string spoken, string replacement) =>
            !(k > 0 && Spaced(words[k - 1], words[k]) && !IsCyrillic(words[k - 1].Value))
            && !(k + n < words.Count && Spaced(words[k + n - 1], words[k + n]) && !IsCyrillic(words[k + n].Value))
            && (replacement[^1] is not ('ы' or 'и') || spoken.EndsWith('s'));

        for (var i = 0; i < words.Count;)
        {
            var n = 0;
            Found? found = null;
            // Two Latin words the model split ("Frount End") first, then one word.
            for (var span = Math.Min(2, words.Count - i); span >= 1 && found is null; span--)
            {
                var spoken = string.Concat(Enumerable.Range(i, span).Select(k => words[k].Value));
                if ((IsCyrillic(spoken) ? span == 1 : _latinOn) && Eligible(text, words, i, span, out var name)
                    && (span == 1 ? FindWord(spoken, name, EnglishNeighbour(i)) : Find(spoken, name, english: false, names)) is { } match
                    && (span == 1 || !Enumerable.Range(i, span).Any(k => CloserEnglish(words[k].Value, match.Score, (w, n) => FindWord(w, n))))
                    && (IsCyrillic(spoken) || !IsCyrillic(match.Replacement) || RussianAround(i, span, spoken, match.Replacement)))
                {
                    found = match;
                    n = span;
                }
            }

            if (found is null && names is not null && FindShort(words[i], names) is { } shortMatch)
            {
                found = shortMatch;
                n = 1;
            }

            if (found is { } f)
            {
                var start = words[i].Index;
                var end = words[i + n - 1].Index + words[i + n - 1].Length;
                Put(start, end, text[start..end], f);
                i += n;
                continue;
            }

            // "Фронтент-дизайн", "Frountent-design": each part on its own.
            var word = words[i];
            if (word.Value.Contains('-', StringComparison.Ordinal))
            {
                var at = word.Index;
                foreach (var part in word.Value.Split('-'))
                {
                    if ((IsCyrillic(part) || _latinOn) && Eligible(part, StartsSentence(text, at) && at == word.Index, out var name)
                        && FindWord(part, name) is { } match)
                    {
                        Put(at, at + part.Length, part, match);
                    }

                    at += part.Length + 1;
                }
            }

            i++;
        }

        return copied == 0 ? text : result.Append(text, copied, text.Length - copied).ToString();
    }

    // "Beautifull day" is a garbled English word and a word, not a term split in two ("Frount End": the term,
    // as close as "front").
    private bool CloserEnglish(string word, double score, Func<string, bool, Found?> find) =>
        !IsEnglish(word) && find(word, false) is { English: true } english && english.Score > score;

    /// <param name="Name">The screen name as seen ("cruzade" for "Cruzade"), kept before ".com".</param>
    private readonly record struct Found(string Replacement, double Score, bool English, string? Screen = null, string? Name = null);

    private bool Eligible(string text, MatchCollection words, int i, int n, out bool name)
    {
        name = false;
        if (Enumerable.Range(i, n).Sum(k => words[k].Value.Count(char.IsLetter)) < MinLetters)
        {
            return false;
        }

        var known = 0;
        for (var k = i; k < i + n; k++)
        {
            var word = words[k].Value;
            if (word.Length < 2 || SelfLearning.IsCommonWord(word) || IsIdentifier(word))
            {
                return false; // an ordinary word, code ("TextRules", "APPDATA")
            }

            if (Terms.IsTermForm(word) || (!IsCyrillic(word) && IsEnglish(word)))
            {
                known++; // a term already ("Deep" of "Deep Learning") or an English word
            }

            name |= IsCyrillic(word) && char.IsUpper(word[0]) && !StartsSentence(text, words[k].Index);

            if (k > i)
            {
                var gap = text.AsSpan(words[k - 1].Index + words[k - 1].Length, words[k].Index - words[k - 1].Index - words[k - 1].Length);
                if (gap.ContainsAnyExcept(' ', ' '))
                {
                    return false; // "Bek, End" is two words
                }
            }
        }

        return known < n; // "water", "Docker"; "Bek End", "Deep Seak" have an unknown word
    }

    private bool Eligible(string part, bool sentenceStart, out bool name)
    {
        name = IsCyrillic(part) && char.IsUpper(part[0]) && !sentenceStart;
        return part.Length >= MinLetters && !SelfLearning.IsCommonWord(part) && !Terms.IsTermForm(part)
            && (IsCyrillic(part) || !(IsEnglish(part) || IsIdentifier(part)));
    }

    // The model writes a word in lower case or with a capital; a capital inside is code or an acronym.
    private static bool IsIdentifier(string word) => !IsCyrillic(word) && word.AsSpan(1).IndexOfAnyInRange('A', 'Z') >= 0;

    /// <param name="english">Also English words as candidates: one Latin word, not two the model split.</param>
    /// <param name="names">Screen names by skeleton (<see cref="ScreenIndex"/>).</param>
    /// <param name="loose">Next to an English word: see <see cref="LooseLetters"/>.</param>
    private Found? Find(string spoken, bool name, bool english, Dictionary<(char, int), List<Candidate>>? names = null, bool loose = false)
    {
        var cyrillic = IsCyrillic(spoken);
        var key = cyrillic ? Normal(spoken) : Skeleton(spoken);
        if (key.Length < 4)
        {
            return null;
        }

        var threshold = name ? 1 : cyrillic ? Threshold : LatinThreshold;
        var floor = threshold - Margin;
        var letters = LettersOf(spoken);
        var vowels = !cyrillic && key.Length <= ShortKey ? VowelSkeleton(spoken) : null; // see ShortKey
        var skeleton = cyrillic ? Skeleton(spoken) : key;
        var screenVowels = skeleton.Length <= ShortKey ? VowelSkeleton(spoken) : null;
        double ByVowels(Candidate c) =>
            c.Screen is not null ? (screenVowels is null ? 1 : Similarity(screenVowels, c.Vowels))
            : vowels is null || c.English ? 1 : Similarity(vowels, c.Vowels);
        // The best score per target; candidates under the floor can neither win nor stand in the way.
        List<(Candidate C, double Score)> hits = [];
        Scan(cyrillic ? _cyrillic : _latin, key, cyrillic);
        // Screen names by sound, a Cyrillic word too: "ремоушн" ~ "Remotion".
        if (names is not null && skeleton.Length >= 4)
        {
            Scan(names, skeleton, false);
        }

        if (english && !cyrillic && letters.Length > 0)
        {
            var least = loose ? LooseLetters : LatinThreshold;
            var bonus = loose ? 0 : TermBonus; // next to an English word, an English word is as likely as a term
            var spread = (int)((1 - least) / least * letters.Length); // the most length a score at the floor allows
            var mask = Mask(letters);
            for (var length = letters.Length - spread; length <= letters.Length + spread; length++)
            {
                if (_latinEnglish.TryGetValue((letters[0], length), out var bucket))
                {
                    foreach (var c in bucket)
                    {
                        // A garbled English word is also close in letters: "promis" ~ "promise", not "sherpa" ~ "sharp".
                        // Letters only one of the two has need an edit each.
                        var edits = Math.Max(BitOperations.PopCount(mask & ~c.Letters), BitOperations.PopCount(c.Letters & ~mask));
                        // Next to an English word, a word that only lost letters on the very same sound skeleton:
                        // "Lazy Trasur" ~ "treasure", "Creat Token" ~ "create" (not "Vender" ~ "vendor", "Pigon" ~ "pigeon").
                        if (edits <= (1 - (loose ? LooseLetters : LatinThreshold)) * Math.Max(length, letters.Length)
                            && Similarity(letters, c.Source, LooseLetters) is var byLetters and >= LooseLetters
                            && Similarity(key, c.Key, floor) is var sound
                            && (byLetters >= LatinThreshold || (loose && sound >= 1 && IsSubsequence(letters, c.Source))))
                        {
                            Hit(c, sound - bonus);
                        }
                    }
                }
            }
        }

        void Scan(Dictionary<(char, int), List<Candidate>> index, string key, bool letters)
        {
            var first = Class(key[0]);
            for (var length = Math.Max(4, key.Length - 3); length <= key.Length + 3; length++)
            {
                if (Math.Abs(length - key.Length) > (1 - floor) * Math.Max(length, key.Length) || !index.TryGetValue((first, length), out var bucket))
                {
                    continue;
                }

                foreach (var c in bucket)
                {
                    // The first sound is heard best: "Vender" is not "Finder"; h/ch ("Hrome", "Chrome") alike.
                    if ((letters || c.Key[0] == key[0] || (c.Key[0] is 'к' or 'х' && key[0] is 'к' or 'х'))
                        && Similarity(key, c.Key, floor) is var score
                        && (c.Screen is null || (!c.ShortOnly && ScreenAllowed(c, score, cyrillic))))
                    {
                        Hit(c, score);
                    }
                }
            }
        }

        void Hit(Candidate c, double score)
        {
            if (score < floor)
            {
                return;
            }

            var at = hits.FindIndex(h => h.C.Target == c.Target);
            if (at < 0)
            {
                hits.Add((c, score));
            }
            else if (score > hits[at].Score || (score == hits[at].Score && vowels is not null && ByVowels(c) > ByVowels(hits[at].C)))
            {
                hits[at] = (c, score);
            }
        }

        if (hits.Count == 0)
        {
            return null;
        }

        // The term itself in other case ("Deepl": DeepL, not "деплой" of "диплей") wins over any look-alike.
        if (!cyrillic && hits.FirstOrDefault(h => !h.C.English && LettersOf(h.C.Target) == letters) is { C: not null } same)
        {
            hits = [same];
        }

        var top = hits.Max(h => h.Score);
        var window = hits.Where(h => h.Score >= top - Margin).ToList();
        // Several about as close: the most frequent of the closest wins if every other one is rarer.
        var tied = window.Where(h => h.Score >= top - Tie).ToList();
        // Next to an English word an English word ranks by its own level: "Creat Token" is "create", not "Krita".
        int Rank(Candidate c) => loose && c.English ? c.Rank - EnglishRank : c.Rank;
        var rank = tied.Min(h => Rank(h.C));
        var winners = tied.Where(h => Rank(h.C) == rank).ToList();
        if (winners.Count != 1 || window.Any(h => h != winners[0] && Rank(h.C) <= rank))
        {
            return null; // two candidates about as close
        }

        var (best, bestScore) = winners[0];
        if (best.English)
        {
            bestScore += loose ? 0 : TermBonus;
            if (bestScore < EnglishThreshold || best.Rank - EnglishRank >= RareLevel)
            {
                return null; // an English word only when close and common
            }
        }

        if (bestScore < threshold
            || ByVowels(best) < threshold)
        {
            return null; // nothing close enough; a short word must match by vowels too
        }

        // "фронтанде" ~ "фронтенде": the case form carries over to the term.
        var replacement = best.Ending is { Length: > 0 } ending
            ? Declension.Inflect(best.Target, best.Source, ending) ?? best.Target
            : best.Target;
        var target = cyrillic ? Normal(best.Target) : "";
        if (LettersOf(replacement) == LettersOf(spoken))
        {
            // The term in other case or spacing ("Deepl", "Coin marketcap") gets its spelling; "Notepad" of
            // "Notepad++" stays.
            return !cyrillic && !best.English && replacement.Any(char.IsUpper) && replacement != spoken
                && string.Equals(replacement.Replace(" ", "", StringComparison.Ordinal), spoken.Replace(" ", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase)
                ? new Found(replacement, bestScore, false, best.Screen, best.Source)
                : null;
        }

        if (target.Length > 0 && key.StartsWith(target, StringComparison.Ordinal))
        {
            return null; // another form of the term ("нейросети")
        }

        return new Found(replacement, bestScore, best.English, best.Screen, best.Source);
    }

    // A short name ("HAPP") only on an exact skeleton while it is on screen.
    // A Cyrillic word only against one-word names in a window now: thousands of remembered names come too close
    // to ordinary words by sound ("курсор" ~ "Coursera", "конвейер" ~ "Canva Pro").
    // A remembered name from MemoryThreshold: a name seen a week ago is a weaker hint than one on screen.
    private static bool ScreenAllowed(Candidate c, double score, bool cyrillic) =>
        (LettersOf(c.Source).Length >= MinLetters || (c.Screen == Window && score >= 1))
        && (c.Screen == Window || score >= MemoryThreshold)
        && (!cyrillic || (c.Screen == Window && !c.Source.Contains(' ', StringComparison.Ordinal) && score >= CyrillicScreenThreshold));

    /// <summary>Screen names indexed by skeleton, as <see cref="_latin"/>; a name's own spelling is not searched.</summary>
    private Dictionary<(char, int), List<Candidate>> ScreenIndex(IReadOnlyCollection<ScreenName> screen)
    {
        if (_screenCache.Count > 4 * ScreenMemory.Limit)
        {
            _screenCache.Clear();
        }

        var index = new Dictionary<(char, int), List<Candidate>>();
        var onScreen = screen.Where(s => s.OnScreen).Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var s in screen)
        {
            if (!s.OnScreen && onScreen.Contains(s.Name))
            {
                continue;
            }

            foreach (var c in _screenCache.GetOrAdd((s.Name, s.OnScreen), static (k, self) => self.ScreenCandidates(k.Item1, k.Item2), this))
            {
                var key = (Class(c.Key[0]), c.Key.Length);
                if (!index.TryGetValue(key, out var bucket))
                {
                    index[key] = bucket = [];
                }

                bucket.Add(c); // a name twice is harmless: hits keep the best per target
            }
        }

        return index;
    }

    private Candidate[] ScreenCandidates(string name, bool onScreen)
    {
        // A term is searched anyway; on screen it also serves the short-word rule ("Дипл" with DeepL open).
        // Only a whole term: "Sber" of "Sber Kandinsky" is written "Сбер".
        var shortOnly = IsKnown(name);
        if (shortOnly && (!onScreen || !_targets.Contains(name) || name.Contains(' ', StringComparison.Ordinal) || name.Split(' ').All(IsEnglish)))
        {
            return [];
        }

        // A name seen only in lower case (a domain: "cruzade.com") is written as a name.
        var target = name.Any(char.IsUpper) ? name : char.ToUpperInvariant(name[0]) + name[1..];

        // English reading too: "Remotion" is said "ремоушн", not "ремотион".
        var english = name.ToLowerInvariant()
            .Replace("tion", "шн", StringComparison.Ordinal).Replace("sion", "жн", StringComparison.Ordinal)
            .Replace("ture", "чер", StringComparison.Ordinal);
        return new[] { Skeleton(name), Skeleton(english) }.Distinct().Where(k => k.Length >= 2)
            .Select(k => new Candidate(k, name, target, onScreen ? WindowRank : MemoryRank, Screen: onScreen ? Window : Memory, ShortOnly: shortOnly))
            .ToArray();
    }

    // A word under MinLetters ("Хап", "Hap") is a screen name only on the exact skeleton and vowels of one name
    // in a window now; never an ordinary, English or term word.
    private Found? FindShort(System.Text.RegularExpressions.Match word, Dictionary<(char, int), List<Candidate>> names)
    {
        var spoken = word.Value;
        var letters = spoken.Count(char.IsLetter);
        if (letters is < 3 or >= MinLetters || SelfLearning.IsCommonWord(spoken) || IsIdentifier(spoken)
            || Terms.IsTermForm(spoken) || (!IsCyrillic(spoken) && IsEnglish(spoken)) || spoken.Contains('-', StringComparison.Ordinal))
        {
            return null;
        }

        var key = Skeleton(spoken);
        if (key.Length < 3 || !names.TryGetValue((Class(key[0]), key.Length), out var bucket))
        {
            return null;
        }

        // An acronym ("хап" → "HAPP") or a term ("Дипл" → "DeepL") on screen: other short names come too close to
        // Russian words and brands written in Cyrillic ("трее" ~ "Trae", "Сбер" ~ "Sber"). A Cyrillic word no longer
        // than the name, a Latin one of the acronym's length (short Latin words are code and English: "diff" ~ "Dify").
        var vowels = VowelSkeleton(spoken);
        var cyrillic = IsCyrillic(spoken);
        var targets = bucket.Where(c => c.Screen == Window && c.Key == key && c.Vowels == vowels
                && (cyrillic
                    ? letters <= LettersOf(c.Source).Length && (c.ShortOnly || c.Source.All(char.IsAsciiLetterUpper))
                    : letters == c.Source.Length && c.Source.All(char.IsAsciiLetterUpper)))
            .Select(c => c.Target).Distinct(StringComparer.Ordinal).ToList();
        return targets.Count == 1 && LettersOf(targets[0]) != LettersOf(spoken)
            ? new Found(targets[0], 1, false, Window, bucket.First(c => c.Target == targets[0]).Source)
            : null;
    }

    private static bool IsSubsequence(string part, string whole)
    {
        var at = 0;
        foreach (var c in whole)
        {
            if (at < part.Length && part[at] == c)
            {
                at++;
            }
        }

        return at == part.Length;
    }

    private static string LettersOf(string s) => string.Concat(s.Where(char.IsLetter)).ToLowerInvariant();

    // Cyrillic compared letter by letter, with spellings a model mixes up merged: ё/э → е, no ь/ъ, no doubles.
    private static string Normal(string word)
    {
        var sb = new StringBuilder(word.Length);
        foreach (var ch in word.ToLowerInvariant())
        {
            var c = ch switch { 'ё' or 'э' => 'е', _ => ch };
            if (c is >= 'а' and <= 'я' && c is not ('ь' or 'ъ') && (sb.Length == 0 || sb[^1] != c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    // Same rule as Terms: a Cyrillic capital is the speaker's, a Latin one is the model's (kept at a sentence start).
    // An English word is written in lower case, as a Cyrillic term.
    private static string Cased(string replacement, string spoken, bool english, bool sentenceStart) =>
        replacement.Length > 0 && char.IsUpper(spoken[0]) && char.IsLower(replacement[0]) && (english || IsCyrillic(replacement))
            && (sentenceStart || IsCyrillic(spoken))
            ? char.ToUpperInvariant(replacement[0]) + replacement[1..]
            : replacement;

    // "Lazy Trasur": an English word between capitalised Latin words keeps its capital ("Lazy Treasure").
    private static bool InTitle(string text, int start, int end)
    {
        static bool Capital(ReadOnlySpan<char> word) => word.Length > 0 && char.IsAsciiLetterUpper(word[0]);
        var before = text.AsSpan(0, start);
        var after = text.AsSpan(end);
        if (before.EndsWith(" ") && !before.EndsWith("  "))
        {
            var prev = before[..^1];
            var from = prev.Length;
            while (from > 0 && char.IsAsciiLetter(prev[from - 1]))
            {
                from--;
            }

            if (Capital(prev[from..]))
            {
                return true;
            }
        }

        if (after.StartsWith(" "))
        {
            var next = after[1..];
            var to = 0;
            while (to < next.Length && char.IsAsciiLetter(next[to]))
            {
                to++;
            }

            return Capital(next[..to]);
        }

        return false;
    }

    private static bool StartsSentence(string text, int index)
    {
        var before = text.AsSpan(0, index).TrimEnd();
        return before.IsEmpty || ".!?…".Contains(before[^1]);
    }

    private static bool IsCyrillic(string s) => s.Any(c => c is >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё');

    private static readonly (string Latin, string Cyrillic)[] Digraphs =
    [
        ("sch", "ш"), ("tch", "ч"), ("chr", "кр"), ("ign", "айн"), ("sh", "ш"), ("ch", "ч"), ("zh", "ж"), ("kh", "х"),
        ("ph", "ф"), ("th", "т"), ("ck", "к"), ("qu", "кв"), ("oo", "у"), ("ee", "и"),
        ("ce", "се"), ("ci", "си"), ("cy", "си"),
    ];

    private static readonly Dictionary<char, string> Letters = new()
    {
        ['a'] = "а", ['b'] = "б", ['c'] = "к", ['d'] = "д", ['e'] = "е", ['f'] = "ф", ['g'] = "г", ['h'] = "х",
        ['i'] = "и", ['j'] = "дж", ['k'] = "к", ['l'] = "л", ['m'] = "м", ['n'] = "н", ['o'] = "о", ['p'] = "п",
        ['q'] = "к", ['r'] = "р", ['s'] = "с", ['t'] = "т", ['u'] = "у", ['v'] = "в", ['w'] = "в", ['x'] = "кс",
        ['y'] = "и", ['z'] = "з",
    };

    /// <summary>
    /// The sound skeleton: Latin read as Cyrillic, every vowel run one "а", no ь/ъ/й, no doubled
    /// consonants, no final vowel, a devoiced final consonant ("Frount End" and "фронтенд" → "франтант").
    /// </summary>
    public static string Skeleton(string word) => Key(word, vowels: false);

    // The skeleton with vowel groups kept (а/о/я, е/и/ы/э/ё, у/ю): a short skeleton matches too much by consonants
    // alone ("rowid" ~ "ревит", "middl" ~ "мудл").
    private static string VowelSkeleton(string word) => Key(word, vowels: true);

    private static string Key(string word, bool vowels)
    {
        var s = word.ToLowerInvariant();
        foreach (var (latin, cyrillic) in Digraphs)
        {
            s = s.Replace(latin, cyrillic, StringComparison.Ordinal);
        }

        var cyr = new StringBuilder(s.Length + 4);
        foreach (var ch in s)
        {
            cyr.Append(Letters.TryGetValue(ch, out var t) ? t : ch.ToString());
        }

        var sb = new StringBuilder(cyr.Length);
        foreach (var ch in cyr.ToString())
        {
            if ("ьъй".Contains(ch, StringComparison.Ordinal) || !(ch is >= 'а' and <= 'я' or 'ё'))
            {
                continue;
            }

            var vowel = "аеёиоуыэюя".Contains(ch, StringComparison.Ordinal);
            var c = !vowel ? ch : !vowels ? 'а' : ch switch { 'а' or 'о' or 'я' => 'а', 'у' or 'ю' => 'у', _ => 'е' };
            if (sb.Length == 0 || (sb[^1] != c && !(vowel && IsVowelGroup(sb[^1]))))
            {
                sb.Append(c);
            }
        }

        if (sb.Length > 1 && IsVowelGroup(sb[^1]))
        {
            sb.Length--;
        }

        if (sb.Length > 0 && Devoiced.TryGetValue(sb[^1], out var voiceless))
        {
            sb[^1] = voiceless;
        }

        return sb.ToString();
    }

    private static bool IsVowelGroup(char c) => c is 'а' or 'е' or 'у';

    private static readonly Dictionary<char, char> Devoiced = new() { ['б'] = 'п', ['в'] = 'ф', ['г'] = 'к', ['д'] = 'т', ['ж'] = 'ш', ['з'] = 'с' };

    // Sounds a model mixes up: a swap inside one class costs half.
    private static char Class(char c) => c switch
    {
        'б' or 'в' or 'п' or 'ф' => 'б',
        'д' or 'т' => 'д',
        'г' or 'к' or 'х' => 'г',
        'з' or 'с' or 'ц' => 'з',
        'ж' or 'ш' or 'щ' or 'ч' => 'ж',
        _ => c,
    };

    /// <summary>1 − weighted edit distance / longer length, on skeletons; 0 once it is surely under <paramref name="floor"/>.</summary>
    public static double Similarity(string a, string b, double floor = 0)
    {
        var longest = Math.Max(a.Length, b.Length);
        if (longest == 0)
        {
            return 0;
        }

        // In half steps: a swap inside one class costs 1, another edit 2.
        var limit = (int)Math.Floor(2 * (1 - floor) * longest + 1e-9);
        Span<char> ca = a.Length < 64 ? stackalloc char[a.Length] : new char[a.Length];
        Span<char> cb = b.Length < 64 ? stackalloc char[b.Length] : new char[b.Length];
        Span<int> bag = stackalloc int[64];
        for (var i = 0; i < a.Length; i++)
        {
            ca[i] = Class(a[i]);
            bag[Slot(ca[i])]++;
        }

        for (var j = 0; j < b.Length; j++)
        {
            cb[j] = Class(b[j]);
            bag[Slot(cb[j])]--;
        }

        // Each edit across classes changes the class counts by one at most: a cheap lower bound.
        int extra = 0, missing = 0;
        foreach (var n in bag)
        {
            extra += Math.Max(n, 0);
            missing += Math.Max(-n, 0);
        }

        if (2 * Math.Max(extra, missing) > limit)
        {
            return 0;
        }

        Span<int> prev = b.Length < 64 ? stackalloc int[b.Length + 1] : new int[b.Length + 1];
        Span<int> cur = b.Length < 64 ? stackalloc int[b.Length + 1] : new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            prev[j] = 2 * j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = 2 * i;
            var rowMin = cur[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : ca[i - 1] == cb[j - 1] ? 1 : 2;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 2, prev[j] + 2), prev[j - 1] + cost);
                rowMin = Math.Min(rowMin, cur[j]);
            }

            if (rowMin > limit)
            {
                return 0; // the distance only grows from here
            }

            var swap = prev;
            prev = cur;
            cur = swap;
        }

        return 1 - prev[b.Length] / (2.0 * longest);
    }

    private static ulong Mask(string s)
    {
        var mask = 0UL;
        foreach (var c in s)
        {
            mask |= 1UL << Slot(char.ToLowerInvariant(c));
        }

        return mask;
    }

    // A counting slot: Cyrillic а–я, Latin a–z, the rest shared.
    private static int Slot(char c) => c switch
    {
        >= 'а' and <= 'я' => c - 'а',
        >= 'a' and <= 'z' => 32 + c - 'a',
        _ => 63,
    };
}
