using System.Globalization;
using System.Text;
using Vox.Core.Diagnostics;

namespace Vox.App.Services;

/// <summary>
/// Plain-text log at %APPDATA%\vox\logs\vox.log (the windowed app has no console). Rotates at 5 MB,
/// keeping one previous file. The user's text (dictations, their fixes, names from window contents) goes in
/// only with <see cref="Texts"/> on, the developer setting; otherwise its size (<see cref="LogText"/>).
/// </summary>
internal static class Log
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private static readonly Lock Gate = new();
    private static string? _path;

    public static string? FilePath => _path;

    /// <summary>Settings → "Писать тексты диктовок в лог" (<see cref="Vox.Core.Settings.LogTexts"/>); off by default.</summary>
    public static bool Texts { get; set; }

    /// <summary>Shown where the app points the user at the log, only while it holds texts.</summary>
    public static string TextsWarning =>
        Texts ? "\n\nВ логе есть тексты ваших диктовок. Не отправляйте его тому, кому не стали бы их показывать." : "";

    /// <summary>"Raw: "текст"" or "Raw (5 chars, 1 word)", see <see cref="LogText.Field"/>.</summary>
    public static string Field(string label, string? text) => LogText.Field(label, text, Texts);

    /// <summary>"\"a\" → \"b\"" or "1 word → 1 word", see <see cref="LogText.Pair"/>.</summary>
    public static string Pair(string from, string to) => LogText.Pair(from, to, Texts);

    /// <summary>
    /// Deletes vox.log and vox.log.1 (clearing the history); logging goes on in a fresh file. It starts with a
    /// banner, so the next start still tells this run's normal exit from a crash.
    /// </summary>
    public static void Clear()
    {
        if (_path is null)
        {
            return;
        }

        lock (Gate)
        {
            File.Delete(_path + ".1");
            File.Delete(_path);
        }

        Write($"=== Vox {typeof(Log).Assembly.GetName().Version?.ToString(3)} log cleared with the history at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
    }

    public static void Init(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var crashed = PreviousRunCrashed(path);
        if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
        {
            File.Move(path, path + ".1", overwrite: true);
        }

        _path = path;
        Write($"\n=== Vox {typeof(Log).Assembly.GetName().Version?.ToString(3)} started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        if (crashed)
        {
            Info("[INIT] The previous run ended without a normal exit (crash, kill or power loss) — see its last lines in vox.log (or vox.log.1); "
                + @"crash dumps, if any: %LOCALAPPDATA%\vox\crashes");
        }
    }

    /// <summary>A run that ended normally logged "[EXIT] Bye" after its start banner.</summary>
    private static bool PreviousRunCrashed(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            file.Seek(Math.Max(0, file.Length - 64 * 1024), SeekOrigin.Begin);
            var tail = new StreamReader(file, Encoding.UTF8).ReadToEnd();
            var start = tail.LastIndexOf("=== Vox", StringComparison.Ordinal);
            return start >= 0 && !tail.AsSpan(start).Contains("[EXIT] Bye", StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>No more writes (before «Удалить мои данные» deletes the log folder).</summary>
    public static void Stop()
    {
        lock (Gate)
        {
            _path = null;
        }
    }

    public static void Info(string message) =>
        Write($"{DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)} {message}");

    public static void Error(string message, Exception e) => Info($"[ERROR] {message}: {e}");

    private static void Write(string line)
    {
        lock (Gate)
        {
            if (_path is null)
            {
                return;
            }

            try
            {
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Logging must never take Vox down.
            }
        }
    }
}
