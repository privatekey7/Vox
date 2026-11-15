using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vox.Core.Updates;

public enum UpdateStatus
{
    /// <summary>GitHub's latest release is this build or an older one.</summary>
    UpToDate,

    /// <summary>A newer release is out.</summary>
    Available,

    /// <summary>No answer Vox can rely on (no network, an HTTP error, a tag that is not a version, garbage).</summary>
    Unknown,
}

/// <summary>The result of one check.</summary>
/// <param name="Latest">The version of GitHub's latest release, when the answer had one.</param>
/// <param name="Detail">For the log, why the status is <see cref="UpdateStatus.Unknown"/>. Nothing of the
/// user's and nothing taken from the answer: a fixed phrase, an HTTP status or an error kind.</param>
public sealed record UpdateCheck(UpdateStatus Status, Version? Latest = null, string Detail = "")
{
    /// <summary>The release page to open in the browser; only for a newer release.</summary>
    public Uri? Page => Status == UpdateStatus.Available && Latest is not null ? UpdateChecker.ReleasePage(Latest) : null;
}

/// <summary>
/// Learns whether a newer Vox is out (docs/adr/037): one anonymous GET to GitHub for the latest release, and a
/// comparison of its tag with this build. Nothing is downloaded or installed, and nothing about the user is
/// sent. Any failure is <see cref="UpdateStatus.Unknown"/>, never an exception: Vox works offline.
/// </summary>
public sealed partial class UpdateChecker(HttpClient http, Version current)
{
    /// <summary>The only address Vox asks. Without a token: 60 requests an hour per IP, Vox makes one a day.</summary>
    public static readonly Uri LatestRelease = new("https://api.github.com/repos/privatekey7/Vox/releases/latest");

    private const string ReleaseTagPage = "https://github.com/privatekey7/Vox/releases/tag/v";

    // A real answer is 2–20 KB (the release notes are in it). More than this is not GitHub's answer.
    private const int MaxAnswerBytes = 1024 * 1024;

    /// <summary>How long one check may take, connection and answer together.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <exception cref="OperationCanceledException">Only when <paramref name="ct"/> is cancelled (Vox is closing).</exception>
    public async Task<UpdateCheck> CheckAsync(CancellationToken ct = default)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(Timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestRelease);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Vox", Format(current))); // GitHub refuses requests without one
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Unknown($"http {(int)response.StatusCode}"); // 403 and 429: the hourly limit; 404: no release yet
            }

            var answer = await ReadAnswerAsync(response.Content, limit.Token).ConfigureAwait(false);
            return answer is null ? Unknown("the answer is too big") : Read(answer, current);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Unknown("timeout");
        }
        catch (HttpRequestException e)
        {
            return Unknown($"network: {e.HttpRequestError}");
        }
        catch (IOException)
        {
            return Unknown("network: the connection broke");
        }
    }

    /// <summary>The release page of a version: built by Vox, never taken from GitHub's answer.</summary>
    public static Uri ReleasePage(Version version) => new(ReleaseTagPage + Format(version));

    /// <summary>
    /// The one kind of address Vox opens in the browser for an update: the page of a vX.Y.Z release of
    /// privatekey7/Vox. Compared as written (no "..", no query), so nothing else can pass for it.
    /// </summary>
    public static bool IsReleasePage([NotNullWhen(true)] Uri? page) =>
        page is { IsAbsoluteUri: true }
        && page.OriginalString.StartsWith(ReleaseTagPage, StringComparison.Ordinal)
        && TryParseTag(page.OriginalString[(ReleaseTagPage.Length - 1)..], out _);

    /// <summary>
    /// A release tag of Vox: exactly <c>vX.Y.Z</c>. Anything else is not an app release: <c>models-1</c> mirrors
    /// the speech models, <c>v2.1.0-rc1</c> is a pre-release.
    /// </summary>
    public static bool TryParseTag(string? tag, out Version version)
    {
        var match = tag is null ? Match.Empty : PlainVersion().Match(tag);
        version = match.Success ? new Version(Number(match, 1), Number(match, 2), Number(match, 3)) : new Version();
        return match.Success;
    }

    /// <summary>GitHub's answer for the latest release, compared with <paramref name="current"/>.</summary>
    internal static UpdateCheck Read(byte[] answer, Version current)
    {
        try
        {
            using var json = JsonDocument.Parse(answer);
            var release = json.RootElement;
            if (release.ValueKind != JsonValueKind.Object
                || !release.TryGetProperty("tag_name", out var tagName) || tagName.ValueKind != JsonValueKind.String)
            {
                return Unknown("the answer is not a release");
            }

            if (!IsFalseOrAbsent(release, "draft", out var draft) || !IsFalseOrAbsent(release, "prerelease", out var prerelease))
            {
                return Unknown("the answer is not a release");
            }

            if (draft || prerelease)
            {
                return Unknown("the latest release is a draft or a pre-release");
            }

            if (!TryParseTag(tagName.GetString(), out var latest))
            {
                return Unknown("the latest release's tag is not vX.Y.Z");
            }

            return new UpdateCheck(latest > Normalize(current) ? UpdateStatus.Available : UpdateStatus.UpToDate, latest);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            // InvalidOperationException: a tag with half of a surrogate pair parses as JSON, but is not a string .NET reads.
            return Unknown("the answer is not JSON");
        }
    }

    // True when the flag is a JSON boolean or missing; `value` is the flag.
    private static bool IsFalseOrAbsent(JsonElement release, string name, out bool value)
    {
        value = false;
        if (!release.TryGetProperty(name, out var flag))
        {
            return true;
        }

        value = flag.ValueKind == JsonValueKind.True;
        return flag.ValueKind is JsonValueKind.True or JsonValueKind.False;
    }

    // The whole answer, or null when it is longer than MaxAnswerBytes (not read further).
    private static async Task<byte[]?> ReadAnswerAsync(HttpContent content, CancellationToken ct)
    {
        if (content.Headers.ContentLength > MaxAnswerBytes)
        {
            return null;
        }

        using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var answer = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (answer.Length + read > MaxAnswerBytes)
            {
                return null;
            }

            answer.Write(buffer, 0, read);
        }

        return answer.ToArray();
    }

    private static UpdateCheck Unknown(string detail) => new(UpdateStatus.Unknown, Detail: detail);

    private static int Number(Match match, int group) => int.Parse(match.Groups[group].ValueSpan, System.Globalization.CultureInfo.InvariantCulture);

    private static string Format(Version version) => Normalize(version).ToString(3);

    // An assembly version is 2.0.0.0, a tag's is 2.0.0: compare and print three parts.
    private static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));

    // SemVer numbers: no leading zeros; nine digits fit an int. \z, not $: "$" lets a trailing newline through.
    [GeneratedRegex(@"\Av(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\z", RegexOptions.CultureInvariant)]
    private static partial Regex PlainVersion();
}

/// <summary>When to check and when to tell: at most one request a day and one notice per version.</summary>
public static class UpdateSchedule
{
    /// <summary>The first check waits for the start to finish: the model loads, the tray appears.</summary>
    public static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>After a check without an answer (no network, GitHub's limit) Vox tries again sooner than in a day.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromHours(1);

    /// <summary>
    /// How long to wait before the next check. <paramref name="lastChecked"/> is the last check that got an
    /// answer (saved in config.json), so restarting Vox ten times a day still makes one request.
    /// </summary>
    public static TimeSpan UntilNext(DateTimeOffset? lastChecked, DateTimeOffset now)
    {
        if (lastChecked is not { } last || last > now)
        {
            return StartDelay; // never, or the clock went back: do not wait for a time that may never come
        }

        var left = last + Interval - now;
        return left > StartDelay ? left : StartDelay;
    }

    /// <summary>
    /// Whether the tray should say "a new version is out": once per version. <paramref name="notified"/> is the
    /// version Vox announced last ("2.1.0", from config.json).
    /// </summary>
    public static bool ShouldNotify(UpdateCheck check, string? notified) =>
        check is { Status: UpdateStatus.Available, Latest: { } latest }
        && !(UpdateChecker.TryParseTag("v" + notified, out var told) && told >= latest);
}
