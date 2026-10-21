using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vox.Core;

/// <summary>Where Vox keeps its data: %APPDATA%\vox — the same folder as Vox 1.x, so an upgrade keeps everything.</summary>
/// <param name="sharedRoot">Fallback for models when <paramref name="root"/> has non-ASCII characters.</param>
public sealed class AppPaths(string root, string sharedRoot)
{
    public static AppPaths Default { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vox"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Vox"));

    public string Root { get; } = root;

    /// <summary>
    /// sherpa-onnx opens model files through narrow-character APIs on Windows, so a profile path
    /// like C:\Users\Иван breaks loading. Such users get the models in %ProgramData%\Vox instead.
    /// </summary>
    public string Models => Path.Combine(Root.All(char.IsAscii) ? Root : sharedRoot, "models");
    public string Logs => Path.Combine(Root, "logs");
    public string LogFile => Path.Combine(Logs, "vox.log");
    public string Database => Path.Combine(Root, "vox_data.db");
    public string Config => Path.Combine(Root, "config.json");
}

/// <summary>
/// User settings in config.json. Keys written by Vox 1.x (<c>hotkey</c>, <c>active_model</c>,
/// <c>language</c>) are read as-is; unknown keys are preserved on save.
/// </summary>
public sealed class Settings
{
    public const string DefaultHotkey = "right ctrl";

    private readonly string _path;
    private readonly JsonObject _json;

    private Settings(string path, JsonObject json)
    {
        _path = path;
        _json = json;
    }

    /// <summary>Hotkey name in Vox 1.x notation ("right ctrl", "caps lock", "f9", …).</summary>
    public string Hotkey
    {
        get => Hotkeys.IsKnown(GetString("hotkey")) ? GetString("hotkey")! : DefaultHotkey;
        set => _json["hotkey"] = value;
    }

    public string? ActiveModel
    {
        get => GetString("active_model");
        set => _json["active_model"] = value;
    }

    public string? Language
    {
        get => GetString("language");
        set => _json["language"] = value;
    }

    /// <summary>Microphone device id; null = the Windows default microphone.</summary>
    public string? Microphone
    {
        get => GetString("microphone");
        set => _json["microphone"] = value;
    }

    public bool Sounds
    {
        get => GetBool("sounds", true);
        set => _json["sounds"] = value;
    }

    /// <summary>Learn dictionary entries from the user's own fixes after a dictation.</summary>
    public bool LearnCorrections
    {
        get => GetBool("learn_corrections", true);
        set => _json["learn_corrections"] = value;
    }

    /// <summary>Put the user's own clipboard back after pasting a dictation.</summary>
    public bool RestoreClipboard
    {
        get => GetBool("restore_clipboard", true);
        set => _json["restore_clipboard"] = value;
    }

    public static Settings Load(string path)
    {
        JsonObject json;
        try
        {
            json = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? [];
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            json = []; // missing or corrupt → defaults
        }

        return new Settings(path, json);
    }

    /// <summary>Atomic write: a crash mid-save never leaves a half-written config.</summary>
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, _json.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }));
        File.Move(tmp, _path, overwrite: true);
    }

    private string? GetString(string key) =>
        _json[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private bool GetBool(string key, bool fallback) =>
        _json[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;
}

/// <summary>Hotkeys the user can choose, in Vox 1.x notation, with Windows virtual-key codes.</summary>
public static class Hotkeys
{
    public static IReadOnlyList<(string Name, string Display, int VirtualKey)> All { get; } =
    [
        ("right ctrl", "Правый Ctrl", 0xA3),
        ("ctrl", "Левый Ctrl", 0xA2),
        ("right shift", "Правый Shift", 0xA1),
        ("shift", "Левый Shift", 0xA0),
        ("right alt", "Правый Alt", 0xA5),
        ("alt", "Левый Alt", 0xA4),
        ("caps lock", "Caps Lock", 0x14),
        ("scroll lock", "Scroll Lock", 0x91),
        .. Enumerable.Range(1, 12).Select(i => ($"f{i}", $"F{i}", 0x70 + i - 1)),
    ];

    public static bool IsKnown(string? name) => All.Any(h => h.Name == name);

    public static string Display(string name) => All.FirstOrDefault(h => h.Name == name).Display ?? name;

    public static int VirtualKey(string name) => All.First(h => h.Name == name).VirtualKey;
}
