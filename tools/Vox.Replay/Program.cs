using Vox.Core.Audio;
using Vox.Core.Diagnostics;
using Vox.Core.Models;
using Vox.Core.Text;
using Vox.Engine;

// Replays dictations through the text pipeline: `dotnet run --project tools/Vox.Replay -- input.txt`.
// Input is vox.log itself ([STT] Raw / [STT] Final lines: only with Settings → "Писать тексты диктовок в лог"),
// recordings.tsv (its raw and final columns), or plain lines: one Raw per line, optionally followed by a tab
// and the logged Final.
// Output (UTF-8, tab-separated): Raw → Final; with --diff only lines where the new Final differs
// from the one in the log: Raw, logged Final, new Final.
// Options: --model gigaam|parakeet (default gigaam), --dict file (user dictionary: wrong<TAB>correct).
// Audio: --audio <folder> takes the saved recordings (recordings.tsv + WAV, Settings → "Сохранять записи
// диктовок") through the model and the whole pipeline: file, recorded Raw, new Raw, new Final; models from
// --models <folder> or VOX_TEST_MODELS (the extracted archives). --model qwen3: Qwen3-ASR, only compared.
// --compare: every installed model (GigaAM, Parakeet, Qwen3-ASR) in columns: Final and seconds per recording,
// speed per model at the end. --hotwords file: Qwen3-ASR prompt words, one per line. --threads N.
// --contested: one JSON line per dictation, Final with the words no list knows and their closest terms (tools/llm-fix).
var files = new List<string>();
var model = ModelCatalog.GigaAm;
Dictionary<string, string>? dict = null;
var diff = false;
string? audio = null;
var compare = false;
var contested = false;
string? hotwords = null;
int? threads = null;
var modelsRoot = Environment.GetEnvironmentVariable("VOX_TEST_MODELS");
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--model":
            var id = args[++i];
            model = ModelCatalog.Experimental.FirstOrDefault(m => m.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Unknown model {id}; known: {string.Join(", ", ModelCatalog.Experimental.Select(m => m.Id))}");
            break;
        case "--dict":
            dict = File.ReadLines(args[++i]).Select(l => l.Split('\t')).Where(p => p.Length == 2)
                .ToDictionary(p => p[0].Trim().ToLowerInvariant(), p => p[1].Trim());
            break;
        case "--diff":
            diff = true;
            break;
        case "--audio":
            audio = args[++i];
            break;
        case "--models":
            modelsRoot = args[++i];
            break;
        case "--compare":
            compare = true;
            break;
        case "--contested":
            contested = true;
            break;
        case "--hotwords":
            hotwords = string.Join(", ", File.ReadLines(args[++i]).Select(l => l.Trim()).Where(l => l.Length > 0));
            break;
        case "--threads":
            threads = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            break;
        default:
            files.Add(args[i]);
            break;
    }
}

if (files.Count == 0 && audio is null)
{
    Console.Error.WriteLine("usage: Vox.Replay [--model gigaam|parakeet] [--dict file] [--diff] input.txt|vox.log ...");
    Console.Error.WriteLine("       Vox.Replay --audio recordings [--model gigaam|parakeet|qwen3 | --compare] [--models folder] [--dict file] [--hotwords file] [--threads N]");
    return 2;
}

Console.OutputEncoding = System.Text.Encoding.UTF8;
var processor = new PostProcessor(model);
if (audio is not null)
{
    return compare ? CompareAudio(audio) : ReplayAudio(audio);
}

int total = 0, changed = 0;
foreach (var file in files)
{
    foreach (var d in DictationLog.Read(File.ReadLines(file)))
    {
        Emit(d.Raw, d.Final);
    }
}

Console.Error.WriteLine(diff ? $"{changed} of {total} dictations differ from the log" : $"{total} dictations");
return 0;

void Emit(string raw, string? logged)
{
    total++;
    var fuzzy = new List<FuzzyTerms.Match>();
    var final = processor.Process(raw, dict, fuzzy).Trim();
    foreach (var f in fuzzy)
    {
        // To stderr, as the app logs it: the stdout columns stay as they are.
        Console.Error.WriteLine($"{(f.English ? "[EN]" : "[FUZZY]")} \"{f.Spoken}\" → \"{f.Replacement}\" {f.Score.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}\t{raw}");
    }

    if (contested)
    {
        Console.WriteLine(Contested.Line(raw, final));
    }
    else if (!diff)
    {
        Console.WriteLine($"{raw}\t{final}");
    }
    else if (logged is not null && logged.Trim() != final)
    {
        changed++;
        Console.WriteLine($"{raw}\t{logged.Trim()}\t{final}");
    }
}

int ReplayAudio(string folder)
{
    if (modelsRoot is null)
    {
        Console.Error.WriteLine("Models folder: --models or VOX_TEST_MODELS");
        return 2;
    }

    var recordings = RecordingStore.Read(folder);
    using var recognizer = Recognizers.Create(model, modelsRoot, threads, hotwords);
    int rawChanged = 0, finalChanged = 0, missing = 0;
    var sw = System.Diagnostics.Stopwatch.StartNew();
    Console.WriteLine("file\trecorded raw\tnew raw\tnew final");
    foreach (var r in recordings)
    {
        var path = Path.Combine(folder, r.File);
        if (!File.Exists(path))
        {
            missing++;
            continue;
        }

        var (samples, rate) = Wav.Read(path);
        var raw = recognizer.Transcribe(samples, rate);
        var final = raw.Length == 0 ? "" : processor.Process(raw, dict).Trim();
        rawChanged += raw != r.Raw ? 1 : 0;
        finalChanged += final != r.Final.Trim() ? 1 : 0;
        Console.WriteLine($"{r.File}\t{r.Raw}\t{raw}\t{final}");
    }

    Console.Error.WriteLine($"{recordings.Count - missing} recordings through {model.Id} in {sw.Elapsed.TotalSeconds:F0}s: " +
        $"Raw differs in {rawChanged}, Final in {finalChanged}{(missing > 0 ? $", {missing} WAV files missing" : "")}");
    return 0;
}

int CompareAudio(string folder)
{
    if (modelsRoot is null)
    {
        Console.Error.WriteLine("Models folder: --models or VOX_TEST_MODELS");
        return 2;
    }

    var recordings = RecordingStore.Read(folder).Where(r => File.Exists(Path.Combine(folder, r.File))).ToList();
    var audios = recordings.Select(r => Wav.Read(Path.Combine(folder, r.File))).ToList();
    var seconds = audios.Sum(a => a.Samples.Length / (double)a.SampleRate);
    var models = ModelCatalog.Experimental.Where(m => ModelCatalog.IsInstalled(m, modelsRoot)).ToList();
    var results = new List<(string Final, double Seconds)[]>();
    foreach (var m in models)
    {
        // One model in memory at a time; the first decode (warm-up) is not timed.
        using var recognizer = Recognizers.Create(m, modelsRoot, threads, hotwords);
        var pp = new PostProcessor(m);
        recognizer.Transcribe(audios[0].Samples, audios[0].SampleRate);
        results.Add([.. audios.Select(a =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var raw = recognizer.Transcribe(a.Samples, a.SampleRate);
            var elapsed = sw.Elapsed.TotalSeconds;
            return (raw.Length == 0 ? "" : pp.Process(raw, dict).Trim(), elapsed);
        })]);
        var total = results[^1].Sum(r => r.Seconds);
        Console.Error.WriteLine($"{m.Id}: {total:F1}s for {seconds:F1}s of audio, {total / seconds:F3}s per second");
    }

    var inv = System.Globalization.CultureInfo.InvariantCulture;
    Console.WriteLine(string.Join('\t', ["file", "recorded raw", .. models.SelectMany(m => new[] { m.Id, "s" })]));
    for (var i = 0; i < recordings.Count; i++)
    {
        Console.WriteLine(string.Join('\t', [recordings[i].File, recordings[i].Raw,
            .. results.SelectMany(r => new[] { r[i].Final, r[i].Seconds.ToString("F2", inv) })]));
    }

    return 0;
}
