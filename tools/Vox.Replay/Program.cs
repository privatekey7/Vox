using System.Text.RegularExpressions;
using Vox.Core.Models;
using Vox.Core.Text;

// Replays dictations through the text pipeline: `dotnet run --project tools/Vox.Replay -- input.txt`.
// Input is vox.log itself ([STT] Raw / [STT] Final lines), or plain lines: one Raw per line,
// optionally followed by a tab and the logged Final.
// Output (UTF-8, tab-separated): Raw → Final; with --diff only lines where the new Final differs
// from the one in the log: Raw, logged Final, new Final.
// Options: --model gigaam|parakeet (default gigaam), --dict file (user dictionary: wrong<TAB>correct).
var files = new List<string>();
var model = ModelCatalog.GigaAm;
Dictionary<string, string>? dict = null;
var diff = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--model":
            var id = args[++i];
            model = ModelCatalog.All.FirstOrDefault(m => m.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Unknown model {id}; known: {string.Join(", ", ModelCatalog.All.Select(m => m.Id))}");
            break;
        case "--dict":
            dict = File.ReadLines(args[++i]).Select(l => l.Split('\t')).Where(p => p.Length == 2)
                .ToDictionary(p => p[0].Trim().ToLowerInvariant(), p => p[1].Trim());
            break;
        case "--diff":
            diff = true;
            break;
        default:
            files.Add(args[i]);
            break;
    }
}

if (files.Count == 0)
{
    Console.Error.WriteLine("usage: Vox.Replay [--model gigaam|parakeet] [--dict file] [--diff] input.txt|vox.log ...");
    return 2;
}

Console.OutputEncoding = System.Text.Encoding.UTF8;
var processor = new PostProcessor(model);
var logLine = new Regex(@"\[STT\] (Raw|Final): ""(.*)""\s*$");
var otherLogLine = new Regex(@"^(\d\d:\d\d:\d\d\.\d{3} |\s*===)");
int total = 0, changed = 0;
foreach (var file in files)
{
    string? raw = null;
    foreach (var line in File.ReadLines(file))
    {
        var m = logLine.Match(line);
        if (!m.Success)
        {
            if (!otherLogLine.IsMatch(line) && line.Trim().Length > 0)
            {
                var fields = line.Split('\t'); // a plain line: Raw[<TAB>Final]
                Emit(fields[0].Trim(), fields.Length > 1 ? fields[1] : null);
            }

            continue;
        }

        if (m.Groups[1].Value == "Raw")
        {
            if (raw is not null)
            {
                Emit(raw, null);
            }

            raw = m.Groups[2].Value;
        }
        else if (raw is not null)
        {
            Emit(raw, m.Groups[2].Value);
            raw = null;
        }
    }

    if (raw is not null)
    {
        Emit(raw, null);
    }
}

Console.Error.WriteLine(diff ? $"{changed} of {total} dictations differ from the log" : $"{total} dictations");
return 0;

void Emit(string raw, string? logged)
{
    total++;
    var final = processor.Process(raw, dict).Trim();
    if (!diff)
    {
        Console.WriteLine($"{raw}\t{final}");
    }
    else if (logged is not null && logged.Trim() != final)
    {
        changed++;
        Console.WriteLine($"{raw}\t{logged.Trim()}\t{final}");
    }
}
