using Vox.Core.Diagnostics;

namespace Vox.Core.Tests;

public sealed class DictationLogTests
{
    [Fact]
    public void Reads_a_crlf_log_with_old_and_new_lines()
    {
        // Regression (coordinator's run on the owner's log): CRLF and 1.x lines were taken as dictations.
        var log = "2026-10-01 12:00:00,123 [INIT] Vox 1.3 started\r\n"
            + "2026-10-01 12:00:05,001 [STT] Raw: 'залей на гитхаб'\r\n"
            + "2026-10-01 12:00:05,002 [STT] Final: ' залей на GitHub'\r\n"
            + "=== Vox 2.0.0 started ===\r\n"
            + "12:00:00.000 [DL] https://github.com/x\r\n"
            + "12:00:01.000 [STT] Raw: \"раз \"два\"\"\r\n"
            + "12:00:01.001 [STT] Final: \" Раз \"два\".\"\r\n"
            + "12:00:02.000 [STT] Raw: \"без финала\"\r\n";

        var dictations = DictationLog.Read(log.Split('\n')).ToList();

        Assert.Equal(
            [new("залей на гитхаб", " залей на GitHub"), new("раз \"два\"", " Раз \"два\"."), new LoggedDictation("без финала", null)],
            dictations);
    }

    [Fact]
    public void Reads_plain_lines_with_an_optional_final() =>
        Assert.Equal([new("один", null), new LoggedDictation("два", "Два.")],
            DictationLog.Read(["один\r", "", "два\tДва."]).ToList());
}
