using System.Globalization;
using System.Text;

namespace Vox.App.Services;

/// <summary>
/// Plain-text log at %APPDATA%\vox\logs\vox.log (the windowed app has no console). Rotates at 5 MB,
/// keeping one previous file. Never logs whole dictations' worth of personal data beyond what
/// Vox 1.x did, so old and new logs read the same way.
/// </summary>
internal static class Log
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private static readonly Lock Gate = new();
    private static string? _path;

    public static string? FilePath => _path;

    public static void Init(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
        {
            File.Move(path, path + ".1", overwrite: true);
        }

        _path = path;
        Write($"\n=== Vox {typeof(Log).Assembly.GetName().Version?.ToString(3)} started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
    }

    public static void Info(string message) =>
        Write($"{DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)} {message}");

    public static void Error(string message, Exception e) => Info($"[ERROR] {message}: {e}");

    private static void Write(string line)
    {
        if (_path is null)
        {
            return;
        }

        lock (Gate)
        {
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
