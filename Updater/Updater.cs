using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MCSkin3D.UpdateSystem
{
    public sealed class UpdateAsset
    {
        public string Name { get; init; }
        public string DownloadUrl { get; init; }
        public long Size { get; init; }
        // GitHub's digest without the "sha256:" prefix. Older assets may lack one.
        public string Sha256 { get; init; }
    }

    public sealed class UpdateRelease
    {
        public System.Version Version { get; init; }
        public string TagName { get; init; }
        public string Name { get; init; }
        public string Body { get; init; }
        public string HtmlUrl { get; init; }
        public UpdateAsset Asset { get; init; }
    }

    public sealed class UpdateChecker
    {
        public const string ReleasesUrl = "https://github.com/karilaa-dev/mcskin3d/releases";
        public const string LatestReleaseApiUrl = "https://api.github.com/repos/karilaa-dev/mcskin3d/releases/latest";
        private static readonly HttpClient SharedClient = CreateClient();
        private readonly HttpClient client;
        private readonly System.Version currentVersion;
        private readonly Func<string> skippedVersion;
        private readonly SemaphoreSlim checkGate = new SemaphoreSlim(1, 1);

        public bool LastCheckSucceeded { get; private set; }
        public string LastError { get; private set; }
        public UpdateRelease LatestRelease { get; private set; }

        public UpdateChecker() : this(SharedClient, typeof(UpdateChecker).Assembly.GetName().Version,
            () => GlobalSettings.SkippedUpdateVersion) { }

        // The injected client is owned by the caller. The GitHub endpoint remains fixed.
        public UpdateChecker(HttpClient client, System.Version currentVersion,
            Func<string> skippedVersion = null)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.currentVersion = Normalize(currentVersion ?? throw new ArgumentNullException(nameof(currentVersion)));
            this.skippedVersion = skippedVersion ?? (() => null);
        }

        private static HttpClient CreateClient()
        {
            var result = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            result.DefaultRequestHeaders.UserAgent.ParseAdd("MCSkin3D/" + typeof(UpdateChecker).Assembly.GetName().Version);
            result.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            result.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            return result;
        }

        public bool CheckForUpdates() => CheckForUpdatesAsync().GetAwaiter().GetResult();
        public Task<bool> CheckForUpdatesAsync() => CheckForUpdatesAsync(false);

        public async Task<bool> CheckForUpdatesAsync(bool ignoreSkippedVersion)
        {
            await checkGate.WaitAsync().ConfigureAwait(false);
            try
            {
                LastCheckSucceeded = false;
                LastError = null;
                LatestRelease = null;
                using var response = await client.GetAsync(LatestReleaseApiUrl).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    LastCheckSucceeded = true;
                    return false;
                }
                response.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                var root = json.RootElement;
                if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
                {
                    LastCheckSucceeded = true;
                    return false;
                }
                string tag = root.GetProperty("tag_name").GetString();
                if (!TryParseVersion(tag, out var latest))
                    throw new InvalidDataException("The latest release has an unsupported version tag.");
                if (latest <= currentVersion)
                {
                    LastCheckSucceeded = true;
                    return false;
                }
                string releaseUrl = ReleasesUrl + "/tag/" + Uri.EscapeDataString(tag);
                string htmlUrl = OptionalString(root, "html_url");
                if (htmlUrl != null && htmlUrl != releaseUrl)
                    throw new InvalidDataException("The release page is outside this application's GitHub repository.");
                LatestRelease = new UpdateRelease
                {
                    Version = latest, TagName = tag,
                    Name = OptionalString(root, "name") ?? "MCSkin3D " + latest,
                    Body = OptionalString(root, "body") ?? "No release notes were provided.",
                    HtmlUrl = releaseUrl, Asset = ReadAsset(root, tag)
                };
                LastCheckSucceeded = true;
                return ignoreSkippedVersion || !TryParseVersion(skippedVersion(), out var skipped) || skipped != latest;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException ||
                ex is JsonException || ex is InvalidOperationException || ex is InvalidDataException ||
                ex is System.Collections.Generic.KeyNotFoundException || ex is FormatException || ex is OverflowException)
            {
                LatestRelease = null;
                LastCheckSucceeded = false;
                LastError = ex is TaskCanceledException ? "The update check timed out. Please try again." : ex.Message;
                return false;
            }
            finally { checkGate.Release(); }
        }

        private static UpdateAsset ReadAsset(JsonElement root, string tag)
        {
            string versionText = tag[0] == 'v' || tag[0] == 'V' ? tag.Substring(1) : tag;
            string versionedName = "MCSkin3D-" + versionText + "-win-x64-minimal.zip";
            UpdateAsset selected = null;
            foreach (var entry in root.GetProperty("assets").EnumerateArray())
            {
                string name = entry.GetProperty("name").GetString();
                if (name != versionedName) continue;
                if (selected != null)
                    throw new InvalidDataException("The release contains duplicate Minimal update packages.");
                string expectedUrl = ReleasesUrl + "/download/" + Uri.EscapeDataString(tag) + "/" + Uri.EscapeDataString(name);
                if (entry.GetProperty("browser_download_url").GetString() != expectedUrl)
                    throw new InvalidDataException("The update download is outside this application's GitHub release.");
                long size = entry.GetProperty("size").GetInt64();
                if (size <= 0 || size > 1024L * 1024 * 1024)
                    throw new InvalidDataException("The update package size is invalid.");
                if (OptionalString(entry, "state") is string state && state != "uploaded")
                    throw new InvalidDataException("The update package has not finished uploading.");
                string digest = OptionalString(entry, "digest");
                string sha256 = null;
                if (!string.IsNullOrEmpty(digest))
                {
                    if (!digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 71)
                        throw new InvalidDataException("The update package has an invalid SHA-256 digest.");
                    sha256 = digest.Substring(7).ToLowerInvariant();
                    foreach (char c in sha256)
                        if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                            throw new InvalidDataException("The update package has an invalid SHA-256 digest.");
                }
                selected = new UpdateAsset { Name = name, DownloadUrl = expectedUrl, Size = size, Sha256 = sha256 };
            }
            return selected ?? throw new InvalidDataException("The latest release does not include the Minimal Windows x64 package.");
        }

        private static string OptionalString(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

        public static bool TryParseVersion(string text, out System.Version version)
        {
            version = null;
            if (string.IsNullOrEmpty(text)) return false;
            if (text[0] == 'v' || text[0] == 'V') text = text.Substring(1);
            // Numeric tags only; prerelease labels and whitespace must never become upgrades.
            foreach (char c in text)
                if ((c < '0' || c > '9') && c != '.') return false;
            if (!System.Version.TryParse(text, out var parsed)) return false;
            version = Normalize(parsed);
            return true;
        }

        private static System.Version Normalize(System.Version version) => new System.Version(
            version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
    }
}
