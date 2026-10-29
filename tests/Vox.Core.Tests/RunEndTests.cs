using Vox.Core.Diagnostics;

namespace Vox.Core.Tests;

public sealed class RunEndTests
{
    private const string Start = "\n=== Vox 2.0.0 started at 2026-10-28 12:24:53 ===\n12:24:54.950 [HOOK] Keyboard hook installed\n";

    [Fact]
    public void A_run_that_said_bye_ended_normally() =>
        Assert.True(RunEnd.EndedNormally(Start + "12:30:00.000 " + RunEnd.Bye + "\n"));

    [Fact]
    public void A_run_closed_by_a_Windows_shutdown_ended_normally() =>
        Assert.True(RunEnd.EndedNormally(Start + "12:30:00.000 " + RunEnd.SessionEnding + "\n"));

    [Fact]
    public void A_run_without_an_exit_line_did_not_end_normally() =>
        Assert.False(RunEnd.EndedNormally(Start + "12:30:00.000 [POWER] Going to sleep\n"));

    [Fact]
    public void Only_the_last_run_counts() =>
        Assert.False(RunEnd.EndedNormally(Start + "12:30:00.000 " + RunEnd.Bye + "\n" + Start));

    [Fact]
    public void A_log_without_runs_is_not_a_crash() =>
        Assert.True(RunEnd.EndedNormally("12:00:00.000 [INIT] cleared\n"));
}
