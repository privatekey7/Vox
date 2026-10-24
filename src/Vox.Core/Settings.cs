using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vox.Core;

/// <summary>Where Vox keeps its data: %APPDATA%\vox — the same folder as Vox 1.x, so an upgrade keeps everything.</summary>
/// <param name="sharedRoot">Fallback for models when <paramref name="root"/> has non-ASCII characters.</param>
/// <param name="localRoot">%LOCALAPPDATA%\vox: crash dumps (WER LocalDumps in vox.iss).</param>
public sealed class AppPaths(string root, string sharedRoot, string? localRoot = null)
{
    public static AppPaths Default { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vox"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Vox"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vox"));

    public string Root { get; } = root;

    public string? LocalRoot { get; } = localRoot;

    /// <summary>
    /// sherpa-onnx opens model files through narrow-character APIs on Windows, so a profile path
    /// like C:\Users\Иван breaks loading. Such users get the models in %ProgramData%\Vox instead.
    /// Every user can write there, so model files are trusted only when they match their pins (D036).
    /// </summary>
    public string Models => Path.Combine(SharedModels ? sharedRoot : Root, "models");

    /// <summary>The models are in the folder shared by this PC's users with non-ASCII profile paths.</summary>
    public bool SharedModels => !Root.All(char.IsAscii);

    public string Logs => Path.Combine(Root, "logs");
    public string LogFile => Path.Combine(Logs, "vox.log");
    public string Database => Path.Combine(Root, "vox_data.db");
    public string ScreenNames => Path.Combine(Root, "screen_names.json");
    public string Recordings => Path.Combine(Root, "recordings");
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

    // Tied to this computer: another PC has other microphones. Texts in the log are this PC's developer's
    // choice: a transfer file must not turn them on elsewhere.
    private static readonly string[] LocalKeys = ["microphone", "log_texts"];

    private Settings(string path, JsonObject json)
    {
        _path = path;
        _json = json;
    }

    /// <summary>The settings to move to another PC (<see cref="Storage.DataTransfer"/>), without device ones.</summary>
    public string ForTransfer()
    {
        var copy = (JsonObject)_json.DeepClone();
        foreach (var key in LocalKeys)
        {
            copy.Remove(key);
        }

        return copy.ToJsonString();
    }

    /// <summary>
    /// Takes the settings moved from another PC: only the known keys with values of the right kind (security audit
    /// L1); this computer's device settings stay, and nothing that reads the screen or keeps audio is switched on.
    /// </summary>
    public void Merge(string json)
    {
        foreach (var (key, value) in Transferable(json).Where(p => !IsKeptOff(p.Key, p.Value)))
        {
            _json[key] = value.DeepClone();
        }
    }

    /// <summary>The <see cref="PrivacyKeys"/> that <paramref name="json"/> has on and <see cref="Merge"/> leaves off here.</summary>
    public IReadOnlyList<string> PrivacyKeptOff(string json) =>
        [.. Transferable(json).Where(p => IsKeptOff(p.Key, p.Value)).Select(p => p.Key)];

    private bool IsKeptOff(string key, JsonValue value) => PrivacyKeys.Contains(key) && value.GetValue<bool>() && !GetBool(key, false);

    /// <summary>What an import never switches on: the user turns these on here, knowing what they do.</summary>
    public static IReadOnlyList<string> PrivacyKeys { get; } = ["screen_text", "save_recordings"];

    private static readonly string[] BoolKeys = ["sounds", "learn_corrections", "screen_hints", "restore_clipboard", .. PrivacyKeys];
    private static readonly string[] StringKeys = ["active_model", "language"];

    // The keys of a transfer file Vox takes, each with a value of its kind; the rest is dropped.
    private static List<(string Key, JsonValue Value)> Transferable(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject other)
        {
            throw new InvalidDataException("settings are not a JSON object");
        }

        var taken = new List<(string, JsonValue)>();
        foreach (var (key, node) in other)
        {
            if (node is not JsonValue value)
            {
                continue;
            }

            var ok = BoolKeys.Contains(key) ? value.GetValueKind() is JsonValueKind.True or JsonValueKind.False
                : StringKeys.Contains(key) ? value.TryGetValue<string>(out var s) && s.Length <= 100 && Text.SafeText.HasNoHiddenChars(s)
                : key == "hotkey" && value.TryGetValue<string>(out var h) && Hotkeys.IsKnown(h);
            if (ok)
            {
                taken.Add((key, value));
            }
        }

        return taken;
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

    /// <summary>Use names from open windows (their titles) to spell what the model misheard.</summary>
    public bool ScreenHints
    {
        get => GetBool("screen_hints", true);
        set => _json["screen_hints"] = value;
    }

    /// <summary>
    /// Also take names from the text of the active window (owner, 23.10). Off by default: reading what is on
    /// screen may look alarming to a user who did not turn it on.
    /// </summary>
    public bool ScreenText
    {
        get => GetBool("screen_text", false);
        set => _json["screen_text"] = value;
    }

    /// <summary>Save each dictation's audio and text for checking recognition (<see cref="Audio.RecordingStore"/>).</summary>
    public bool SaveRecordings
    {
        get => GetBool("save_recordings", false);
        set => _json["save_recordings"] = value;
    }

    /// <summary>
    /// Developer setting: write the texts of dictations and fixes into vox.log (Raw/Final for tools/Vox.Replay).
    /// Off by default: the log then has only sizes and counts (<see cref="Diagnostics.LogText"/>).
    /// </summary>
    public bool LogTexts
    {
        get => GetBool("log_texts", false);
        set => _json["log_texts"] = value;
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

    /// <summary>
    /// Whether the hotkey should be hidden from other apps. Ctrl/Shift/Alt pass through — they are
    /// part of the user's shortcuts; the rest (Caps Lock toggling, F5 reloading a page) are not.
    /// </summary>
    public static bool Swallow(int virtualKey) => virtualKey is < 0xA0 or > 0xA5;

    /// <summary>
    /// Ctrl+S, the user's "remember this fix" (owner, 23.10). Only Ctrl: Ctrl+Shift+S, Ctrl+Alt+S
    /// (AltGr is Ctrl+Alt) and Win combinations are other shortcuts.
    /// </summary>
    public static bool IsSave(int virtualKey, bool ctrl, bool shift, bool alt, bool win) =>
        virtualKey == 0x53 && ctrl && !shift && !alt && !win;

    /// <summary>
    /// A key that edits text (security audit L5, 25.10): a character, Backspace, Delete, paste, cut, undo. Only these
    /// count as the user's own typing for self-learning; Space, Enter, arrows and other shortcuts do not, so a page
    /// script cannot wait for any key and pass its own change off as the user's fix.
    /// </summary>
    /// <remarks>AltGr is Ctrl+Alt: it types characters on many layouts. Text from AutoHotkey or the on-screen
    /// keyboard comes as VK_PACKET, from an IME as VK_PROCESSKEY.</remarks>
    public static bool IsEditing(int virtualKey, bool ctrl, bool shift, bool alt, bool win)
    {
        var character = virtualKey is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x60 and <= 0x6F
            or >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDF or 0xE2;
        return !win && (virtualKey switch
        {
            0x08 or 0x2E => true, // Backspace, Delete (Ctrl too: a whole word)
            0xE5 or 0xE7 => true, // VK_PROCESSKEY, VK_PACKET
            0x2D => shift && !ctrl && !alt, // Shift+Insert: paste
            0x56 or 0x58 or 0x59 or 0x5A when ctrl && !alt => true, // Ctrl+V, X, Y, Z (with Shift: plain paste, redo)
            _ => character && (ctrl == alt), // plain or AltGr; Ctrl+A or Alt+F are shortcuts
        });
    }
}
