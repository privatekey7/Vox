using System.Net;
using System.Text;
using Vox.Core.Updates;

namespace Vox.Core.Tests;

/// <summary>
/// "A new version is out" (docs/adr/037): one GET to GitHub, no download, no install. No test here goes to
/// the network: GitHub is a stub handler.
/// </summary>
public sealed class UpdateCheckerTests : IDisposable
{
    private static readonly Version Current = new(2, 0, 0);
    private readonly string _dir = Directory.CreateTempSubdirectory("vox-updates-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // ---- The answer of GitHub ------------------------------------------------------------

    [Fact]
    public async Task Newer_release_is_reported_with_its_page()
    {
        var check = await Check(Release("v2.1.0"));

        Assert.Equal(UpdateStatus.Available, check.Status);
        Assert.Equal(new Version(2, 1, 0), check.Latest);
        Assert.Equal("https://github.com/privatekey7/Vox/releases/tag/v2.1.0", check.Page?.AbsoluteUri);
    }

    [Theory]
    [InlineData("v2.0.0")] // the same
    [InlineData("v1.2.0")] // older: a release was deleted, or this is a build ahead of the releases
    [InlineData("v1.99.99")]
    public async Task Same_or_older_release_means_up_to_date(string tag)
    {
        var check = await Check(Release(tag));

        Assert.Equal(UpdateStatus.UpToDate, check.Status);
        Assert.Null(check.Page);
    }

    [Theory]
    [InlineData("2.9.0", "v2.10.0", true)] // numbers, not strings: 10 > 9
    [InlineData("2.10.0", "v2.9.0", false)]
    [InlineData("2.0.9", "v2.0.10", true)]
    [InlineData("2.0.0", "v3.0.0", true)]
    [InlineData("2.5.7", "v2.5.7", false)]
    public async Task Versions_compare_by_semver(string current, string tag, bool newer)
    {
        var check = await new UpdateChecker(new HttpClient(new GitHub(Release(tag))), Version.Parse(current)).CheckAsync();

        Assert.Equal(newer ? UpdateStatus.Available : UpdateStatus.UpToDate, check.Status);
    }

    [Theory]
    [InlineData("models-1")] // the repository also has a release that mirrors the speech models
    [InlineData("v2.1")]
    [InlineData("v2.1.0.1")]
    [InlineData("2.1.0")]
    [InlineData("V2.1.0")]
    [InlineData("v2.1.0-rc1")] // a pre-release by its tag
    [InlineData("v2.1.0+build5")]
    [InlineData("v02.1.0")]
    [InlineData(" v2.1.0")]
    [InlineData("v2.1.0\n")]
    [InlineData("v2.1.x")]
    [InlineData("v99999999999.0.0")]
    [InlineData("")]
    public async Task Tag_that_is_not_a_plain_version_is_ignored(string tag)
    {
        var check = await Check(Release(tag));

        Assert.Equal(UpdateStatus.Unknown, check.Status);
        Assert.Null(check.Latest);
        Assert.Null(check.Page);
    }

    [Theory]
    [InlineData("true", "false")]
    [InlineData("false", "true")]
    public async Task Draft_and_pre_release_are_ignored(string draft, string prerelease)
    {
        var check = await Check(Release("v2.1.0", draft, prerelease));

        Assert.Equal(UpdateStatus.Unknown, check.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>rate limit</html>")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"v2.1.0\"")]
    [InlineData("{}")]
    [InlineData("{\"tag_name\": 210}")]
    [InlineData("{\"tag_name\": null}")]
    [InlineData("{\"tag_name\": [\"v2.1.0\"]}")]
    [InlineData("{\"tag_name\": \"v2.1.0\", \"draft\": \"no\"}")]
    [InlineData("{\"tag_name\": \"v2.1.0\"")] // cut
    [InlineData("{\"tag_name\": \"v2.1.0\\uD800\"}")] // half of a surrogate pair: not a string .NET can read
    [InlineData("{\"tag_name\": \"\\uDC00\"}")]
    public async Task Garbage_in_the_answer_is_ignored(string body)
    {
        var check = await Check(body);

        Assert.Equal(UpdateStatus.Unknown, check.Status);
        Assert.NotEqual("", check.Detail);
    }

    [Fact]
    public async Task Answer_without_draft_and_prerelease_fields_is_read()
    {
        var check = await Check("{\"tag_name\": \"v2.1.0\"}");

        Assert.Equal(UpdateStatus.Available, check.Status);
    }

    [Fact]
    public async Task Huge_answer_is_not_read_to_the_end()
    {
        var body = "{\"tag_name\": \"v2.1.0\", \"body\": \"" + new string('a', 2 * 1024 * 1024) + "\"}";

        var check = await Check(body);

        Assert.Equal(UpdateStatus.Unknown, check.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)] // the hourly limit of GitHub for this IP
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.NotFound)] // no releases yet
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotModified)]
    public async Task Http_error_is_ignored_even_with_a_release_in_its_body(HttpStatusCode status)
    {
        var check = await Check(Release("v2.1.0"), status);

        Assert.Equal(UpdateStatus.Unknown, check.Status);
        Assert.Equal($"http {(int)status}", check.Detail);
    }

    [Fact]
    public async Task No_internet_gives_no_error()
    {
        var github = new GitHub(Release("v2.1.0")) { Fail = new HttpRequestException(HttpRequestError.NameResolutionError) };

        var check = await new UpdateChecker(new HttpClient(github), Current).CheckAsync();

        Assert.Equal(UpdateStatus.Unknown, check.Status);
        Assert.Contains("NameResolutionError", check.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Broken_connection_while_reading_gives_no_error()
    {
        var github = new GitHub(Release("v2.1.0")) { Fail = new IOException("connection reset") };

        var check = await new UpdateChecker(new HttpClient(github), Current).CheckAsync();

        Assert.Equal(UpdateStatus.Unknown, check.Status);
    }

    [Fact]
    public async Task Silent_server_is_given_up_on_after_the_timeout()
    {
        var github = new GitHub(Release("v2.1.0")) { Hang = true };
        var checker = new UpdateChecker(new HttpClient(github), Current) { Timeout = TimeSpan.FromMilliseconds(50) };

        var check = await checker.CheckAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(UpdateStatus.Unknown, check.Status);
        Assert.Equal("timeout", check.Detail);
    }

    [Fact]
    public void Timeout_is_about_ten_seconds() =>
        Assert.Equal(TimeSpan.FromSeconds(10), new UpdateChecker(new HttpClient(new GitHub("")), Current).Timeout);

    [Fact]
    public async Task Closing_vox_stops_the_check()
    {
        var github = new GitHub(Release("v2.1.0")) { Hang = true };
        using var closing = new CancellationTokenSource();
        var check = new UpdateChecker(new HttpClient(github), Current).CheckAsync(closing.Token);

        await closing.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    // ---- What Vox sends ------------------------------------------------------------------

    [Fact]
    public async Task One_anonymous_get_with_the_version_and_nothing_else()
    {
        var github = new GitHub(Release("v2.1.0"));
        var http = new HttpClient(github);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Vox/2.0"); // the app's shared client names itself too

        await new UpdateChecker(http, new Version(2, 0, 0)).CheckAsync();

        var request = Assert.Single(github.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.github.com/repos/privatekey7/Vox/releases/latest", request.Url);
        Assert.Equal("Vox/2.0.0", request.UserAgent);
        Assert.False(request.HasBody);
        Assert.Equal(["Accept", "User-Agent"], request.Headers); // no token, no cookie, nothing about the user
    }

    [Fact]
    public async Task Page_never_comes_from_the_answer()
    {
        // html_url is not trusted: the page is built from the checked tag.
        var body = "{\"tag_name\": \"v2.1.0\", \"html_url\": \"https://evil.example/Vox/releases/tag/v2.1.0\"}";

        var check = await Check(body);

        Assert.Equal("https://github.com/privatekey7/Vox/releases/tag/v2.1.0", check.Page?.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://github.com/privatekey7/Vox/releases/tag/v2.1.0", true)]
    [InlineData("https://github.com/privatekey7/Vox/releases/tag/v12.0.34", true)]
    [InlineData("https://github.com/privatekey7/Vox/releases/tag/models-1", false)]
    [InlineData("https://github.com/privatekey7/Vox/releases/download/v2.1.0/vox-setup.exe", false)] // Vox downloads nothing
    [InlineData("https://github.com/privatekey7/Vox/releases/tag/v2.1.0/../../../../evil", false)]
    [InlineData("https://github.com/privatekey7/Vox/releases/tag/v2.1.0?x=1", false)]
    [InlineData("http://github.com/privatekey7/Vox/releases/tag/v2.1.0", false)]
    [InlineData("https://github.com/privatekey7/Vox/releases", false)]
    [InlineData("https://github.com/privatekey7/Vox/releases/../../../evil/repo", false)]
    [InlineData("https://github.com/privatekey7/Vox/releases/%2e%2e/%2e%2e/evil", false)]
    [InlineData("https://github.com/privatekey7/Voxx/releases/tag/v2.1.0", false)]
    [InlineData("https://github.com/someone/Vox/releases/tag/v2.1.0", false)]
    [InlineData("https://github.com.evil.example/privatekey7/Vox/releases/tag/v2.1.0", false)]
    [InlineData("https://github.com@evil.example/privatekey7/Vox/releases/tag/v2.1.0", false)]
    [InlineData("https://evil.example/?https://github.com/privatekey7/Vox/releases/", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("C:\\Windows\\System32\\calc.exe", false)]
    [InlineData("releases/tag/v2.1.0", false)]
    public void Only_the_releases_of_vox_may_be_opened(string address, bool allowed)
    {
        Uri.TryCreate(address, UriKind.RelativeOrAbsolute, out var page);

        Assert.Equal(allowed, UpdateChecker.IsReleasePage(page));
    }

    [Fact]
    public void No_page_is_not_a_release_page() => Assert.False(UpdateChecker.IsReleasePage(null));

    [Fact]
    public void Page_of_a_version_passes_its_own_check() =>
        Assert.True(UpdateChecker.IsReleasePage(UpdateChecker.ReleasePage(new Version(12, 0, 34))));

    // ---- Once a day ----------------------------------------------------------------------

    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public void First_check_waits_a_little_after_start() =>
        Assert.Equal(UpdateSchedule.StartDelay, UpdateSchedule.UntilNext(lastChecked: null, Now));

    [Fact]
    public void Start_delay_keeps_out_of_the_way_of_the_start() =>
        Assert.InRange(UpdateSchedule.StartDelay, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(5));

    [Fact]
    public void Checked_an_hour_ago_waits_for_the_rest_of_the_day() =>
        Assert.Equal(TimeSpan.FromHours(23), UpdateSchedule.UntilNext(Now.AddHours(-1), Now));

    [Theory]
    [InlineData(-24)]
    [InlineData(-25)]
    [InlineData(-24 * 400)]
    public void Checked_a_day_ago_or_more_is_due_at_start(int hours) =>
        Assert.Equal(UpdateSchedule.StartDelay, UpdateSchedule.UntilNext(Now.AddHours(hours), Now));

    [Fact]
    public void Check_is_never_sooner_than_the_start_delay() =>
        Assert.Equal(UpdateSchedule.StartDelay, UpdateSchedule.UntilNext(Now.AddHours(-24).AddSeconds(1), Now));

    [Fact]
    public void Clock_moved_back_does_not_put_the_check_off_for_long()
    {
        // The last check "in the future": the PC clock was wrong then or is wrong now.
        Assert.Equal(UpdateSchedule.StartDelay, UpdateSchedule.UntilNext(Now.AddDays(30), Now));
    }

    [Fact]
    public void Day_is_twenty_four_hours_and_a_failed_check_is_retried_sooner()
    {
        Assert.Equal(TimeSpan.FromHours(24), UpdateSchedule.Interval);
        Assert.InRange(UpdateSchedule.RetryDelay, TimeSpan.FromMinutes(30), TimeSpan.FromHours(6));
    }

    // ---- Once per version ----------------------------------------------------------------

    [Theory]
    [InlineData(null, true)] // never told
    [InlineData("", true)]
    [InlineData("2.0.5", true)] // told about an older one
    [InlineData("2.1.0", false)] // told about this one
    [InlineData("2.2.0", false)] // told about a newer one that was then withdrawn
    [InlineData("garbage", true)]
    [InlineData("v2.1.0", true)] // not what Vox writes: treated as garbage
    public void New_version_is_announced_once(string? notified, bool announce)
    {
        var check = new UpdateCheck(UpdateStatus.Available, new Version(2, 1, 0));

        Assert.Equal(announce, UpdateSchedule.ShouldNotify(check, notified));
    }

    [Fact]
    public void Nothing_new_is_never_announced()
    {
        Assert.False(UpdateSchedule.ShouldNotify(new UpdateCheck(UpdateStatus.UpToDate, new Version(2, 0, 0)), notified: null));
        Assert.False(UpdateSchedule.ShouldNotify(new UpdateCheck(UpdateStatus.Unknown, Detail: "http 403"), notified: null));
    }

    // ---- Settings ------------------------------------------------------------------------

    [Fact]
    public void Old_config_without_the_field_checks_for_updates()
    {
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, "{\"hotkey\": \"right ctrl\", \"sounds\": false}"); // written by Vox 2.0

        var settings = Settings.Load(path);

        Assert.True(settings.CheckUpdates);
        Assert.Null(settings.UpdateNotified);
        Assert.Null(settings.UpdateChecked);
    }

    [Fact]
    public void Update_settings_survive_a_restart()
    {
        var path = Path.Combine(_dir, "config.json");
        var settings = Settings.Load(path);
        settings.CheckUpdates = false;
        settings.UpdateNotified = "2.1.0";
        settings.UpdateChecked = Now;
        settings.Save();

        var loaded = Settings.Load(path);

        Assert.False(loaded.CheckUpdates);
        Assert.Equal("2.1.0", loaded.UpdateNotified);
        Assert.Equal(Now, loaded.UpdateChecked);
        Assert.Contains("\"check_updates\": false", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"yesterday\"")]
    [InlineData("12345")]
    [InlineData("null")]
    [InlineData("{}")]
    public void Damaged_time_of_the_last_check_reads_as_never(string value)
    {
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, $"{{\"update_checked\": {value}, \"check_updates\": \"maybe\"}}");

        var settings = Settings.Load(path);

        Assert.Null(settings.UpdateChecked);
        Assert.True(settings.CheckUpdates);
    }

    [Fact]
    public void Update_settings_stay_on_this_computer()
    {
        // Whether this PC goes online, and what it was told about, is not another PC's business.
        var here = Settings.Load(Path.Combine(_dir, "here.json"));
        here.CheckUpdates = false;
        here.UpdateNotified = "2.1.0";
        here.UpdateChecked = Now;

        var transfer = here.ForTransfer();

        Assert.DoesNotContain("update", transfer, StringComparison.Ordinal);

        here.Merge("{\"check_updates\": true, \"update_notified\": \"9.9.9\", \"update_checked\": \"2000-01-01T00:00:00+00:00\"}");

        Assert.False(here.CheckUpdates);
        Assert.Equal("2.1.0", here.UpdateNotified);
        Assert.Equal(Now, here.UpdateChecked);
    }

    // ---- Helpers -------------------------------------------------------------------------

    private static Task<UpdateCheck> Check(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new UpdateChecker(new HttpClient(new GitHub(body, status)), Current).CheckAsync();

    // The fields of GET /repos/{owner}/{repo}/releases/latest that matter, and a few that do not.
    private static string Release(string tag, string draft = "false", string prerelease = "false") =>
        $$"""
        {
          "url": "https://api.github.com/repos/privatekey7/Vox/releases/1",
          "html_url": "https://github.com/privatekey7/Vox/releases/tag/{{System.Text.Json.JsonEncodedText.Encode(tag)}}",
          "id": 1,
          "tag_name": "{{System.Text.Json.JsonEncodedText.Encode(tag)}}",
          "name": "Vox",
          "draft": {{draft}},
          "prerelease": {{prerelease}},
          "assets": [{ "name": "vox-setup.exe", "browser_download_url": "https://github.com/privatekey7/Vox/releases/download/x/vox-setup.exe" }],
          "body": "Что нового"
        }
        """;

    private sealed record Seen(HttpMethod Method, string Url, string UserAgent, bool HasBody, IReadOnlyList<string> Headers);

    private sealed class GitHub(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<Seen> Requests { get; } = [];

        public Exception? Fail { get; init; }

        public bool Hang { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new Seen(request.Method, request.RequestUri!.AbsoluteUri, request.Headers.UserAgent.ToString(),
                request.Content is not null, [.. request.Headers.Select(h => h.Key).Order(StringComparer.Ordinal)]));
            if (Hang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (Fail is not null)
            {
                throw Fail;
            }

            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
