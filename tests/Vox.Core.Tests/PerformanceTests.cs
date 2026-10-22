using System.Diagnostics;
using Vox.Core.Models;
using Vox.Core.Text;

namespace Vox.Core.Tests;

/// <summary>Coordinator's budget: the text pipeline stays under 5 ms per dictation as the dictionary grows.</summary>
public sealed class PerformanceTests
{
    [Fact]
    public void Pipeline_takes_under_5_ms_per_dictation()
    {
        const string raw = "Слушай, открой клод код и напиши промт для агента, чтобы он задеплоил бэк энд на верцель, "
            + "потом проверь гитхаб экшенс, запусти докер компоуз и сравни джипити пять с клод сонет четыре и пять, "
            + "а результат сохрани в ноушн и отправь в телеграм. Э-э, и ещё — посмотри логи в графане за 1—2 часа.";
        var pp = new PostProcessor(ModelCatalog.GigaAm);
        pp.Process(raw); // load and index the dictionary once
        var sw = Stopwatch.StartNew();
        const int runs = 200;
        for (var i = 0; i < runs; i++)
        {
            pp.Process(raw);
        }

        var perDictation = sw.Elapsed.TotalMilliseconds / runs;
        Assert.True(perDictation < 5, $"{perDictation:F2} ms per dictation");
    }
}
