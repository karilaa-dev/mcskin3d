using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MCSkin3D.UpdateSystem;

internal static class UpdateCheckerChecks
{
    internal static void Run(Action<bool, string> check) => RunAsync(check).GetAwaiter().GetResult();

    private static async Task RunAsync(Action<bool, string> check)
    {
        foreach (var text in new[] { "v2.0.0.0", "V2.0.0", "2.0" })
            check(UpdateChecker.TryParseVersion(text, out var value) && value == new Version(2, 0, 0, 0), "Numeric version normalization: " + text);
        foreach (var text in new[] { "", "v", "vv2.0", " v2.0", "v2.0 ", "2.0-beta", "2.0+build", "2.-1", "2.0.0.0.1", "2.0/evil", "2.999999999999" })
            check(!UpdateChecker.TryParseVersion(text, out _), "Reject unsupported version: " + text);

        using var handler = new FakeHandler();
        using var client = new HttpClient(handler);
        string skipped = null;
        var checker = new UpdateChecker(client, new Version(1, 7, 2), () => skipped);
        handler.Body = JsonSerializer.Serialize(Release());
        check(await checker.CheckForUpdatesAsync().ConfigureAwait(false), "New release is available");
        check(checker.LastCheckSucceeded && checker.LastError == null, "Successful request clears error");
        check(checker.LatestRelease.Version == new Version(2, 0, 0, 0), "Release version normalized to four components");
        check(checker.LatestRelease.Body == "## Changes\n- New preview & updater", "Release notes are retained as untrusted plain text");
        check(checker.LatestRelease.Asset.Name == "MCSkin3D-2.0.0.0-win-x64.zip" && checker.LatestRelease.Asset.Sha256 == new string('a', 64), "Portable asset and normalized SHA-256 digest selected");

        skipped = "v2.0.0";
        check(!await checker.CheckForUpdatesAsync().ConfigureAwait(false) && checker.LastCheckSucceeded && checker.LatestRelease != null, "Skipped release suppresses automatic prompt but retains release metadata");
        check(await checker.CheckForUpdatesAsync(true).ConfigureAwait(false), "Manual request bypasses skipped version");
        skipped = "1.9.0.0";
        check(await checker.CheckForUpdatesAsync().ConfigureAwait(false), "Skipping one version does not suppress another");

        foreach (var current in new[] { new Version(2, 0, 0), new Version(2, 0, 0, 0), new Version(2, 0, 1) })
        {
            var upToDate = new UpdateChecker(client, current);
            check(!await upToDate.CheckForUpdatesAsync().ConfigureAwait(false) && upToDate.LastCheckSucceeded && upToDate.LatestRelease == null, "Equal or older release is not an upgrade: " + current);
        }
        handler.Body = JsonSerializer.Serialize(Release("v1.7.10"));
        check(await new UpdateChecker(client, new Version(1, 7, 9)).CheckForUpdatesAsync().ConfigureAwait(false), "Version ordering is numeric rather than lexical");

        var portableAsset = Asset("v2.0.0.0", false);
        var lightAsset = Asset("v2.0.0.0", true);
        handler.Body = JsonSerializer.Serialize(Release(assets: new[] { portableAsset, lightAsset }));
        check(await checker.CheckForUpdatesAsync().ConfigureAwait(false) && checker.LatestRelease.Asset.Name == "MCSkin3D-2.0.0.0-win-x64.zip", "Portable package is selected even when a retired lightweight package is present");
        handler.Body = JsonSerializer.Serialize(Release(assets: new[] { lightAsset }));
        check(!await checker.CheckForUpdatesAsync().ConfigureAwait(false) && !checker.LastCheckSucceeded && checker.LatestRelease == null, "A release with only a lightweight package is not installable");
        handler.Body = JsonSerializer.Serialize(Release(assets: new[] { portableAsset }));
        check(await checker.CheckForUpdatesAsync().ConfigureAwait(false), "Portable-only release is installable");
        var legacy = Asset("v2.0.0.0", false, "MCSkin3D-win-x64.zip");
        handler.Body = JsonSerializer.Serialize(Release(assets: new[] { legacy }));
        check(await checker.CheckForUpdatesAsync().ConfigureAwait(false), "Existing unversioned archive name is supported");
        handler.Body = JsonSerializer.Serialize(Release(assets: new[] { portableAsset, legacy }));
        check(!await checker.CheckForUpdatesAsync().ConfigureAwait(false) && !checker.LastCheckSucceeded, "Ambiguous packages are rejected");

        var noDigest = Asset("v2.0.0.0", false);
        noDigest["digest"] = null;
        handler.Body = JsonSerializer.Serialize(Release(assets: new[] { noDigest }));
        check(await checker.CheckForUpdatesAsync().ConfigureAwait(false) && checker.LatestRelease.Asset.Sha256 == null, "Missing legacy digest is explicitly represented for installer policy");
        foreach (string digest in new[] { "md5:" + new string('a', 64), "sha256:short", "sha256:" + new string('z', 64) })
        {
            var broken = Asset("v2.0.0.0", false);
            broken["digest"] = digest;
            handler.Body = JsonSerializer.Serialize(Release(assets: new[] { broken }));
            check(!await checker.CheckForUpdatesAsync().ConfigureAwait(false) && !checker.LastCheckSucceeded, "Malformed digest rejected: " + digest);
        }
        foreach (string url in new[] {
            "http://github.com/karilaa-dev/mcskin3d/releases/download/v2.0.0.0/MCSkin3D-2.0.0.0-win-x64.zip",
            "https://github.com/another/repository/releases/download/v2.0.0.0/MCSkin3D-2.0.0.0-win-x64.zip",
            "https://github.com.evil.example/karilaa-dev/mcskin3d/releases/download/v2.0.0.0/MCSkin3D-2.0.0.0-win-x64.zip",
            "https://github.com/karilaa-dev/mcskin3d/releases/download/v1.0.0/MCSkin3D-2.0.0.0-win-x64.zip",
            "https://github.com/karilaa-dev/mcskin3d/releases/download/v2.0.0.0/MCSkin3D-2.0.0.0-win-x64.zip?redirect=evil"
        })
        {
            var broken = Asset("v2.0.0.0", false);
            broken["browser_download_url"] = url;
            handler.Body = JsonSerializer.Serialize(Release(assets: new[] { broken }));
            check(!await checker.CheckForUpdatesAsync().ConfigureAwait(false) && !checker.LastCheckSucceeded && checker.LatestRelease == null, "Untrusted asset URL rejected: " + url);
        }
        var maliciousPage = Release();
        maliciousPage["html_url"] = "https://evil.example/release";
        handler.Body = JsonSerializer.Serialize(maliciousPage);
        check(!await checker.CheckForUpdatesAsync().ConfigureAwait(false) && !checker.LastCheckSucceeded, "Untrusted release page URL rejected");
        foreach (long size in new[] { 0L, -1L, 1024L * 1024 * 1024 + 1 })
        {
            var broken = Asset("v2.0.0.0", false);
            broken["size"] = size;
            handler.Body = JsonSerializer.Serialize(Release(assets: new[] { broken }));
            check(!await checker.CheckForUpdatesAsync().ConfigureAwait(false) && !checker.LastCheckSucceeded, "Invalid package size rejected");
        }
        foreach (string flag in new[] { "draft", "prerelease" })
        {
            var hidden = Release();
            hidden[flag] = true;
            handler.Body = JsonSerializer.Serialize(hidden);
            check(!await checker.CheckForUpdatesAsync().ConfigureAwait(false) && checker.LastCheckSucceeded && checker.LatestRelease == null, "Unstable release ignored: " + flag);
        }
        foreach (string badJson in new[] { "not json", "{}", "{\"draft\":false,\"prerelease\":false,\"tag_name\":\"v2.0-beta\"}", "[]" })
        {
            handler.Body = badJson;
            check(!await checker.CheckForUpdatesAsync().ConfigureAwait(false) && !checker.LastCheckSucceeded && !string.IsNullOrEmpty(checker.LastError), "Malformed response produces a useful failure");
        }
        handler.StatusCode = HttpStatusCode.NotFound;
        check(!await checker.CheckForUpdatesAsync().ConfigureAwait(false) && checker.LastCheckSucceeded && checker.LatestRelease == null && checker.LastError == null, "No published release is successful without a notification");
        foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.InternalServerError })
        {
            handler.StatusCode = status;
            check(!await checker.CheckForUpdatesAsync().ConfigureAwait(false) && !checker.LastCheckSucceeded && checker.LatestRelease == null, "HTTP error does not reuse stale release: " + status);
        }
        handler.Failure = new HttpRequestException("Offline");
        check(!await checker.CheckForUpdatesAsync().ConfigureAwait(false) && checker.LastError == "Offline", "Offline failure is caught and exposed");
        handler.Failure = new TaskCanceledException();
        check(!await checker.CheckForUpdatesAsync().ConfigureAwait(false) && checker.LastError.Contains("timed out", StringComparison.Ordinal), "Timeout is caught and explained");
        handler.Failure = null;
        handler.StatusCode = HttpStatusCode.OK;
        handler.Body = JsonSerializer.Serialize(Release());
        check(await checker.CheckForUpdatesAsync().ConfigureAwait(false) && checker.LastError == null, "Successful retry clears previous failure");

        handler.Blocker = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int before = handler.Requests;
        Task<bool> first = checker.CheckForUpdatesAsync();
        Task<bool> second = checker.CheckForUpdatesAsync(true);
        check(!first.IsCompleted && !second.IsCompleted && handler.Requests == before + 1, "Concurrent startup/manual checks share a serialized request gate");
        handler.Blocker.SetResult(true);
        check(await first.ConfigureAwait(false) && await second.ConfigureAwait(false) && handler.Requests == before + 2 && handler.MaximumActive == 1, "Serialized checks both complete without overlapping HTTP requests");
    }

    private static Dictionary<string, object> Release(string tag = "v2.0.0.0", Dictionary<string, object>[] assets = null) => new Dictionary<string, object>
    {
        ["tag_name"] = tag, ["name"] = "MCSkin3D " + tag, ["draft"] = false, ["prerelease"] = false,
        ["body"] = "## Changes\n- New preview & updater", ["html_url"] = UpdateChecker.ReleasesUrl + "/tag/" + tag,
        ["assets"] = assets ?? new[] { Asset(tag, false) }
    };

    private static Dictionary<string, object> Asset(string tag, bool light, string filename = null)
    {
        filename ??= "MCSkin3D-" + tag.TrimStart('v', 'V') + (light ? "-win-x64-light.zip" : "-win-x64.zip");
        return new Dictionary<string, object>
        {
            ["name"] = filename, ["size"] = 12345L, ["state"] = "uploaded", ["digest"] = "sha256:" + new string('A', 64),
            ["browser_download_url"] = UpdateChecker.ReleasesUrl + "/download/" + tag + "/" + filename
        };
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        internal string Body;
        internal HttpStatusCode StatusCode = HttpStatusCode.OK;
        internal Exception Failure;
        internal TaskCompletionSource<bool> Blocker;
        internal int Requests, MaximumActive;
        private int active;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get || request.RequestUri.AbsoluteUri != UpdateChecker.LatestReleaseApiUrl)
                throw new InvalidOperationException("Unexpected update endpoint.");
            Requests++;
            active++;
            MaximumActive = Math.Max(MaximumActive, active);
            try
            {
                if (Blocker != null) await Blocker.Task.ConfigureAwait(false);
                if (Failure != null) throw Failure;
                return new HttpResponseMessage(StatusCode) { Content = new StringContent(Body ?? "", Encoding.UTF8, "application/json") };
            }
            finally { active--; }
        }
    }
}
