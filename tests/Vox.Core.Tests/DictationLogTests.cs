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

    [Fact]
    public void Reads_raw_and_final_from_recordings_tsv_skipping_empty_dictations() =>
        Assert.Equal([new LoggedDictation("сделай комет", "Сделай коммит.")],
            DictationLog.Read([
                "file\ttime\tmodel\traw\tfinal\tnote\r",
                "20261022-193005-123.wav\t2026-10-22 19:30:05\tgigaam-v3-rnnt-punct\tсделай комет\tСделай коммит.\t",
                "20261022-193006-000.wav\t2026-10-22 19:30:06\tgigaam-v3-rnnt-punct\t\t\tno speech",
            ]).ToList());

    // Security audit M4: by default the log has no dictation text, and Vox.Replay must not take a size for one.
    [Fact]
    public void A_log_without_texts_has_sizes_only_and_no_dictations_to_replay()
    {
        var raw = LogText.Field("Raw", "переведи 500 рублей маме", full: false);
        var final = LogText.Field("Final", " Переведи 500 рублей маме.", full: false);
        var fix = LogText.Pair("кубернетис", "Kubernetes", full: false);

        Assert.Equal("Raw (24 chars, 4 words)", raw);
        Assert.Equal("Final (26 chars, 4 words)", final);
        Assert.Equal("1 word → 1 word", fix);
        Assert.Empty(DictationLog.Read([$"12:00:01.000 [STT] {raw}", $"12:00:01.001 [STT] {final}"]));
    }

    [Fact]
    public void A_log_with_texts_reads_back_as_before()
    {
        var lines = new[]
        {
            $"12:00:01.000 [STT] {LogText.Field("Raw", "залей на гитхаб", full: true)}",
            $"12:00:01.001 [STT] {LogText.Field("Final", " Залей на GitHub.", full: true)}",
        };

        Assert.Equal([new LoggedDictation("залей на гитхаб", " Залей на GitHub.")], DictationLog.Read(lines).ToList());
        Assert.Equal("\"кубер\" → \"Kubernetes\"", LogText.Pair("кубер", "Kubernetes", full: true));
    }
}
