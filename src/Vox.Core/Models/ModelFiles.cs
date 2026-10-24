using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace Vox.Core.Models;

/// <summary>A model file or download that does not match its pin. <see cref="Exception.Message"/> is for the user.</summary>
public sealed class ModelIntegrityException : Exception
{
    public ModelIntegrityException()
    {
    }

    public ModelIntegrityException(string message)
        : base(message)
    {
    }

    public ModelIntegrityException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Checks an installed model against <see cref="SpeechModel.Files"/> before sherpa-onnx loads it (D036).
/// The files stay open for reading only, so nobody can change, rename or replace them, or any folder
/// above them, until the caller has loaded the model and disposes the result.
/// </summary>
public static partial class ModelFiles
{
    private const int BufferSize = 1 << 20;

    /// <summary>Opens and checks every pinned file of the model; throws <see cref="ModelIntegrityException"/> on any mismatch.</summary>
    /// <param name="rejectLinks">Refuse files reached through a junction or symbolic link: in a folder other
    /// users can write to, a link could be pointed elsewhere after the check (<see cref="IsSharedFolder"/>).</param>
    public static IDisposable OpenVerified(SpeechModel model, string modelsRoot, bool rejectLinks)
    {
        var dir = ModelCatalog.DirectoryOf(model, modelsRoot);
        var opened = new List<FileStream>(model.Files.Count);
        try
        {
            foreach (var pin in model.Files)
            {
                opened.Add(OpenPinned(Path.GetFullPath(Path.Combine(dir, pin.Path)), pin, rejectLinks));
            }

            return new HeldFiles(opened);
        }
        catch
        {
            opened.ForEach(f => f.Dispose());
            throw;
        }
    }

    /// <summary>True when every pinned file is there and matches.</summary>
    public static bool IsIntact(SpeechModel model, string modelsRoot, bool rejectLinks)
    {
        try
        {
            using var held = OpenVerified(model, modelsRoot, rejectLinks);
            return true;
        }
        catch (ModelIntegrityException)
        {
            return false;
        }
    }

    /// <summary>The models folder is under %ProgramData% (<see cref="AppPaths.Models"/>), where every user can create files.</summary>
    public static bool IsSharedFolder(string modelsRoot)
    {
        var shared = Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        return shared.Length > 0
            && Path.GetFullPath(modelsRoot).StartsWith(shared + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Lowercase hex SHA-256 of the rest of the stream.</summary>
    internal static string Sha256Of(Stream stream)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            sha.AppendData(buffer, 0, read);
        }

        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    private static FileStream OpenPinned(string path, PinnedFile pin, bool rejectLinks)
    {
        FileStream file;
        try
        {
            // FileShare.Read: an open writer makes this fail, and no writer can start until we close.
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ModelIntegrityException($"Не получилось открыть файл модели {pin.Path}: его нет или он занят.", e);
        }

        try
        {
            if (rejectLinks && OperatingSystem.IsWindows() && !string.Equals(FinalPath(file.SafeFileHandle), path, StringComparison.OrdinalIgnoreCase))
            {
                throw new ModelIntegrityException($"Файл модели {pin.Path} лежит не в папке модели, а за ссылкой на другую папку.");
            }

            if (file.Length != pin.Size || Sha256Of(file) != pin.Sha256)
            {
                throw new ModelIntegrityException($"Файл модели {pin.Path} изменён или повреждён.");
            }

            return file;
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    /// <summary>The path the open file really has, with every junction and link resolved; null if unknown.</summary>
    [SupportedOSPlatform("windows")]
    private static unsafe string? FinalPath(SafeFileHandle file)
    {
        var buffer = new char[512];
        while (true)
        {
            uint length;
            fixed (char* chars = buffer)
            {
                length = GetFinalPathNameByHandleW(file, chars, (uint)buffer.Length, 0); // normalized, drive letter
            }

            if (length == 0)
            {
                return null;
            }

            if (length < buffer.Length)
            {
                var path = new string(buffer, 0, (int)length);
                return path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal) ? @"\\" + path[8..]
                    : path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..]
                    : path;
            }

            buffer = new char[length]; // too small: length is the size needed
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static unsafe partial uint GetFinalPathNameByHandleW(SafeFileHandle file, char* path, uint length, uint flags);

    private sealed class HeldFiles(List<FileStream> files) : IDisposable
    {
        public void Dispose() => files.ForEach(f => f.Dispose());
    }
}
