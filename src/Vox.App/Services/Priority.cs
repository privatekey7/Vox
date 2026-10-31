using System.Diagnostics;

namespace Vox.App.Services;

/// <summary>
/// Vox above other programs while the model recognises: a video render on all cores (owner, 30.10, Remotion on the
/// CPU) made recognition three times slower. The model runs well under a second, so the render hardly notices.
/// </summary>
internal static class Priority
{
    /// <summary>Raises the process priority until the result is disposed; the priority before is restored.</summary>
    public static IDisposable AboveNormal()
    {
        var process = Process.GetCurrentProcess();
        var before = process.PriorityClass;
        if (before != ProcessPriorityClass.Normal)
        {
            return process; // set by the user (Task Manager): leave it
        }

        process.PriorityClass = ProcessPriorityClass.AboveNormal;
        return new Restore(process);
    }

    private sealed class Restore(Process process) : IDisposable
    {
        public void Dispose()
        {
            process.PriorityClass = ProcessPriorityClass.Normal;
            process.Dispose();
        }
    }
}
