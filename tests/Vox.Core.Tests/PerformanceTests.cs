using System.Diagnostics;
using Vox.Core.Models;
using Vox.Core.Text;

namespace Vox.Core.Tests;

/// <summary>
/// Timing tests run alone, after the others: tests on the same cores made them fail on a busy machine.
/// </summary>
[CollectionDefinition(nameof(Timing), DisableParallelization = true)]
public sealed class Timing
{
    /// <summary>
    /// The fastest of several batches, in ms per run: a busy machine slows some batches, a real regression
    /// slows them all.
    /// </summary>
    public static double BestMs(Action action, int runs, int batches = 5)
    {
        action(); // warm up: the dictionary is loaded and skeletons are cached
        var best = double.MaxValue;
        for (var b = 0; b < batches; b++)
        {
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < runs; i++)
            {
                action();
            }

            best = Math.Min(best, sw.Elapsed.TotalMilliseconds / runs);
        }

        return best;
    }
}

/// <summary>Coordinator's budget: the text pipeline stays under 5 ms per dictation as the dictionary grows.</summary>
[Collection(nameof(Timing))]
public sealed class PerformanceTests
{
    [Fact]
    public void Pipeline_takes_under_5_ms_per_dictation()
    {
        const string raw = "Слушай, открой клод код и напиши промт для агента, чтобы он задеплоил бэк энд на верцель, "
            + "потом проверь гитхаб экшенс, запусти докер компоуз и сравни джипити пять с клод сонет четыре и пять, "
            + "а результат сохрани в ноушн и отправь в телеграм. Э-э, и ещё — посмотри логи в графане за 1—2 часа.";
        var pp = new PostProcessor(ModelCatalog.GigaAm);
        var perDictation = Timing.BestMs(() => pp.Process(raw), runs: 40);
        Assert.True(perDictation < 5, $"{perDictation:F2} ms per dictation");
    }

    [Fact]
    public void Matching_with_a_full_memory_is_fast()
    {
        var names = Terms.LoadPairs().Select(p => p[1]).Where(t => t.All(char.IsAsciiLetter) && t.Length >= 3).Distinct()
            .Select((n, i) => n + "x" + (char)('a' + (i % 26))).Take(ScreenMemory.Limit).ToList(); // unknown names
        ScreenName[] screen = [.. names.Select(n => new ScreenName(n, false)), .. ScreenNamesTests.OnScreen("Sunwhisper Settings - Google Chrome")];
        const string text = "Открой санвиспер, проверь фронтенд и Bek End в нашем проекте, потом напиши отчёт";
        var perMatch = Timing.BestMs(() => FuzzyTerms.Apply(text, null, screen), runs: 4);
        Assert.True(perMatch < 10, $"{perMatch:0.0} ms");
    }
}
