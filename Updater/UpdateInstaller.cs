using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MCSkin3D.UpdateSystem
{
    internal sealed class PreparedUpdate : IDisposable
    {
        internal PreparedUpdate(string directory) { DirectoryPath = directory; }
        internal string DirectoryPath { get; }
        internal bool HandedOff { get; set; }
        public void Dispose()
        {
            if (!HandedOff) UpdateInstaller.RemoveStaging(DirectoryPath);
        }
    }

    internal sealed class UpdateManifest
    {
        public string Version { get; set; }
        public string Edition { get; set; }
        public Dictionary<string, string> Files { get; set; }
        // Installation provenance: once a path belongs to the user, future packages must leave it alone.
        public List<string> PreservedFiles { get; set; } = new List<string>();
    }

    internal static class UpdateInstaller
    {
        internal const string ManifestName = "update-manifest.json";
        internal const string StagingPrefix = ".mcskin3d-update-";
        private const long MaximumDownload = 512L * 1024 * 1024;
        private const long MaximumExpanded = 1024L * 1024 * 1024;
        private static readonly HttpClient DownloadClient = CreateDownloadClient();
        internal static PreparedUpdate PendingUpdate { get; set; }
        [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "An empty Assembly.Location intentionally identifies single-file published installations.")]
        public static bool IsSupportedInstallation =>
            string.IsNullOrEmpty(typeof(UpdateInstaller).Assembly.Location) &&
            File.Exists(Path.Combine(AppContext.BaseDirectory, ManifestName)) &&
            File.Exists(Path.Combine(AppContext.BaseDirectory, "update-channel.txt"));

        private static HttpClient CreateDownloadClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MCSkin3D/2.0.0.0");
            return client;
        }

        public static Task<PreparedUpdate> PrepareAsync(UpdateRelease release, IProgress<int> progress,
            CancellationToken cancellationToken) => PrepareAsync(release, AppContext.BaseDirectory,
                DownloadClient, progress, cancellationToken);

        internal static async Task<PreparedUpdate> PrepareAsync(UpdateRelease release, string targetDirectory,
            HttpClient client, IProgress<int> progress, CancellationToken cancellationToken)
        {
            var asset = release?.Asset ?? throw new InvalidDataException("This release has no compatible update package.");
            if (!IsHash(asset.Sha256) || asset.Size <= 0 || asset.Size > MaximumDownload)
                throw new InvalidDataException("The release is missing a valid SHA-256 checksum or package size. Download it from GitHub manually.");
            var expectedUrl = "https://github.com/karilaa-dev/mcskin3d/releases/download/" +
                Uri.EscapeDataString(release.TagName) + "/" + Uri.EscapeDataString(asset.Name);
            if (!string.Equals(asset.DownloadUrl, expectedUrl, StringComparison.Ordinal))
                throw new InvalidDataException("The update package is not from this project's GitHub release.");
            string target = Path.GetFullPath(targetDirectory);
            EnsureNoLinks(target, target);
            string job = Path.Combine(target, StagingPrefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(job); // Also verifies this portable installation is writable.
            var prepared = new PreparedUpdate(job);
            try
            {
                string archive = Path.Combine(job, "package.zip");
                using (var response = await client.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength is long length && length != asset.Size)
                        throw new InvalidDataException("The update download size does not match GitHub's release.");
                    using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    using var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
                    {
                        total += read;
                        if (total > asset.Size) throw new InvalidDataException("The update download exceeds its expected size.");
                        hash.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        progress?.Report((int)(total * 90 / asset.Size));
                    }
                    if (total != asset.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The update download failed its SHA-256 or size check. Please try again.");
                }
                await Task.Run(() =>
                {
                    ExtractPackage(archive, Path.Combine(job, "payload"), cancellationToken);
                    ValidatePayload(Path.Combine(job, "payload"), release.Version, ReadEdition(target), cancellationToken);
                }, cancellationToken).ConfigureAwait(false);
                progress?.Report(100);
                return prepared;
            }
            catch { prepared.Dispose(); throw; }
        }

        internal static string ReadEdition(string directory)
        {
            string value = File.ReadAllText(Path.Combine(directory, "update-channel.txt")).Trim();
            if (value != "portable" && value != "lightweight") throw new InvalidDataException("Unknown installed update edition.");
            return value;
        }

        internal static void ExtractPackage(string archivePath, string payload, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(payload);
            using var archive = ZipFile.OpenRead(archivePath);
            if (archive.Entries.Count == 0 || archive.Entries.Count > 10000)
                throw new InvalidDataException("The update archive has an invalid number of files.");
            long total = 0;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string name = entry.FullName.Replace('\\', '/');
                bool directory = name.EndsWith("/", StringComparison.Ordinal);
                if (directory) name = name.TrimEnd('/');
                string destination = SafePath(payload, name);
                if (!names.Add(name) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 ||
                    (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("The update archive contains duplicate paths or symbolic links.");
                total = checked(total + entry.Length);
                if (entry.Length > MaximumExpanded || total > MaximumExpanded)
                    throw new InvalidDataException("The expanded update archive is too large.");
                if (directory) { Directory.CreateDirectory(destination); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                using var input = entry.Open();
                using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
                var buffer = new byte[81920];
                long written = 0;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    written += read;
                    if (written > entry.Length) throw new InvalidDataException("An archive entry exceeds its declared size.");
                    output.Write(buffer, 0, read);
                }
                if (written != entry.Length) throw new InvalidDataException("An archive entry is incomplete.");
            }
        }

        internal static UpdateManifest ValidatePayload(string payload, System.Version expectedVersion, string edition,
            CancellationToken cancellationToken = default)
        {
            var manifest = ReadManifest(payload);
            if (!UpdateChecker.TryParseVersion(manifest.Version, out var version) || version != expectedVersion ||
                manifest.Edition != edition || !manifest.Files.ContainsKey("MCSkin3D.exe") ||
                !manifest.Files.ContainsKey("update-channel.txt"))
                throw new InvalidDataException("The update package version or edition does not match the release.");
            var actual = Directory.GetFiles(payload, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(payload, file).Replace('\\', '/'))
                .Where(file => file != ManifestName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!actual.SetEquals(manifest.Files.Keys))
                throw new InvalidDataException("The update package does not match its file manifest.");
            foreach (var item in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!HashFile(SafePath(payload, item.Key)).Equals(item.Value, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("An update file failed verification: " + item.Key);
            }
            if (ReadEdition(payload) != edition ||
                !UpdateChecker.TryParseVersion(FileVersionInfo.GetVersionInfo(Path.Combine(payload, "MCSkin3D.exe")).ProductVersion, out var exeVersion) ||
                exeVersion != expectedVersion)
                throw new InvalidDataException("The executable version does not match the release.");
            return manifest;
        }

        internal static UpdateManifest ReadManifest(string directory)
        {
            string file = Path.Combine(directory, ManifestName);
            if (new FileInfo(file).Length > 4 * 1024 * 1024) throw new InvalidDataException("Update manifest is too large.");
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(File.ReadAllText(file));
            if (manifest?.Files == null || manifest.Files.Count == 0 || manifest.Files.Count > 10000)
                throw new InvalidDataException("Invalid update file manifest.");
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in manifest.Files)
            {
                SafePath(directory, item.Key);
                if (item.Key.Contains('\\') || item.Key.Equals(ManifestName, StringComparison.OrdinalIgnoreCase) ||
                    !IsHash(item.Value) || !files.TryAdd(item.Key, item.Value))
                    throw new InvalidDataException("Invalid update manifest entry.");
            }
            manifest.Files = files;
            manifest.PreservedFiles ??= new List<string>();
            if (manifest.PreservedFiles.Count > 10000) throw new InvalidDataException("Too many preserved update paths.");
            foreach (string path in manifest.PreservedFiles) SafePath(directory, path);
            return manifest;
        }

        internal static string SafePath(string root, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains('\\'))
                throw new InvalidDataException("Unsafe update path.");
            foreach (string part in relative.Split('/'))
            {
                string stem = part.Split('.')[0].ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(part) || part == "." || part == ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                    part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    stem is "CON" or "PRN" or "AUX" or "NUL" ||
                    (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3])))
                    throw new InvalidDataException("Unsafe update path.");
            }
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(fullRoot, relative));
            if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update path escapes its folder.");
            EnsureNoLinks(root, path);
            return path;
        }

        private static void EnsureNoLinks(string root, string path)
        {
            string current = Path.GetFullPath(path);
            string boundary = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            while (current != null)
            {
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Updates cannot pass through symbolic links or junctions.");
                if (current.TrimEnd(Path.DirectorySeparatorChar).Equals(boundary, StringComparison.OrdinalIgnoreCase)) break;
                current = Path.GetDirectoryName(current);
            }
        }

        internal static string HashFile(string file)
        {
            using var stream = File.OpenRead(file);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        private static bool IsHash(string hash) => hash?.Length == 64 && hash.All(Uri.IsHexDigit);

        internal static FileStream AcquireApplicationLock()
        {
            string path = Path.Combine(AppContext.BaseDirectory, ".mcskin3d-update.lock");
            var timer = Stopwatch.StartNew();
            while (true)
            {
                try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite); }
                catch (IOException) when (timer.Elapsed < TimeSpan.FromSeconds(20)) { Thread.Sleep(100); }
            }
        }

        private sealed class UpdateBlockedException : IOException
        {
            public UpdateBlockedException(string message, Exception inner = null) : base(message, inner) { }
        }

        internal static void LaunchPendingUpdate()
        {
            var update = PendingUpdate;
            PendingUpdate = null;
            if (update == null) return;
            Process helper = null;
            try
            {
                using var parent = Process.GetCurrentProcess();
                var start = new ProcessStartInfo(Path.Combine(update.DirectoryPath, "payload", "MCSkin3D.exe"))
                { UseShellExecute = false, WorkingDirectory = update.DirectoryPath, CreateNoWindow = true };
                start.ArgumentList.Add("--apply-update");
                start.ArgumentList.Add(update.DirectoryPath);
                start.ArgumentList.Add(parent.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                start.ArgumentList.Add(parent.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
                helper = Process.Start(start) ?? throw new IOException("Could not start the update installer.");
                var startup = Stopwatch.StartNew();
                while (!File.Exists(Path.Combine(update.DirectoryPath, "ready")))
                {
                    if (helper.HasExited) throw new IOException("The update installer exited before it was ready. Check that the required .NET Desktop Runtime is installed.");
                    if (startup.Elapsed > TimeSpan.FromSeconds(15)) throw new IOException("The update installer did not start in time.");
                    Thread.Sleep(50);
                }
                update.HandedOff = true;
            }
            catch (Exception ex)
            {
                // The parent is still alive, so this helper cannot have started replacing files.
                try { if (helper != null && !helper.HasExited) { helper.Kill(); helper.WaitForExit(5000); } } catch { }
                Log(ex.ToString());
                MessageBox.Show("The update installer could not start. Your existing installation is unchanged.\n\n" + ex.Message,
                    "MCSkin3D update", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Restart(AppContext.BaseDirectory);
            }
            finally { helper?.Dispose(); update.Dispose(); }
        }

        internal static bool TryRunHelper(string[] args)
        {
            if (args.Length == 0 || args[0] != "--apply-update") return false;
            string target = null;
            string job = null;
            bool parentExited = false;
            try
            {
                if (args.Length != 4 || !int.TryParse(args[2], out int parentId) || !long.TryParse(args[3], out long parentStarted))
                    throw new InvalidDataException("Invalid update installer arguments.");
                job = Path.GetFullPath(args[1]);
                target = ValidateJob(job);
                // The verified new executable runs as the helper, without loading the editor or user settings.
                if (!Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar)
                    .Equals(Path.Combine(job, "payload"), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The update installer must run from its staging folder.");
                File.WriteAllText(Path.Combine(job, "ready"), "ready");
                try
                {
                    using var parent = Process.GetProcessById(parentId);
                    if (parent.StartTime.ToUniversalTime().Ticks == parentStarted && !parent.WaitForExit(120000))
                        throw new UpdateBlockedException("The editor is still running. The update was not installed.");
                }
                catch (ArgumentException) { } // Parent already exited; process IDs may also have been reused.
                parentExited = true;
                ApplyPreparedUpdate(job);
                File.WriteAllText(Path.Combine(job, "result.txt"), "success");
                Restart(target);
            }
            catch (Exception ex)
            {
                Log(ex.ToString());
                if (job != null && target != null)
                {
                    try { File.WriteAllText(Path.Combine(job, "result.txt"), ex.ToString()); } catch { }
                }
                MessageBox.Show("The update could not be installed. Your files and any available backup have been kept.\n\n" +
                    ex.Message + "\n\nSee %LOCALAPPDATA%\\MCSkin3D\\Logs\\update.log for details.",
                    "MCSkin3D update", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (target != null && parentExited && ex is not UpdateBlockedException) Restart(target);
            }
            return true;
        }

        private static void Restart(string target)
        {
            Process.Start(new ProcessStartInfo(Path.Combine(target, "MCSkin3D.exe"))
            { UseShellExecute = false, WorkingDirectory = target });
        }

        private static string ValidateJob(string job)
        {
            string name = Path.GetFileName(job);
            if (!name.StartsWith(StagingPrefix, StringComparison.Ordinal) ||
                !Guid.TryParseExact(name.Substring(StagingPrefix.Length), "N", out _))
                throw new InvalidDataException("Invalid update staging folder.");
            string target = Path.GetDirectoryName(job);
            EnsureNoLinks(target, job);
            if (!File.Exists(Path.Combine(target, "MCSkin3D.exe"))) throw new InvalidDataException("The original installation is missing.");
            return target;
        }

        internal static void ApplyPreparedUpdate(string job, Action<int> afterFileInstalled = null)
        {
            string target = ValidateJob(job);
            string payload = Path.Combine(job, "payload");
            var proposed = ReadManifest(payload);
            if (!UpdateChecker.TryParseVersion(proposed.Version, out var version)) throw new InvalidDataException("Invalid update version.");
            var manifest = ValidatePayload(payload, version, ReadEdition(target));
            using var updateLock = AcquireInstallerLock(target);
            // Catch another editor instance (Windows holds the running executable open).
            using (new FileStream(Path.Combine(target, "MCSkin3D.exe"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            UpdateManifest previous = null;
            if (File.Exists(Path.Combine(target, ManifestName))) previous = ReadManifest(target);
            if ((previous != null && UpdateChecker.TryParseVersion(previous.Version, out var installedVersion) && version < installedVersion) ||
                (UpdateChecker.TryParseVersion(FileVersionInfo.GetVersionInfo(Path.Combine(target, "MCSkin3D.exe")).ProductVersion,
                    out var installedExeVersion) && version < installedExeVersion))
                throw new UpdateBlockedException("A newer version is already installed. This staged update was not applied.");
            var preserved = new HashSet<string>(previous?.PreservedFiles ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var installedManifest = new UpdateManifest
            {
                Version = manifest.Version, Edition = manifest.Edition,
                Files = new Dictionary<string, string>(previous?.Files ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
            };
            foreach (var item in manifest.Files) installedManifest.Files[item.Key] = item.Value;
            var installed = new List<(string Path, bool Existed)>();
            string backup = Path.Combine(job, "backup");
            try
            {
                foreach (string relative in manifest.Files.Keys.Concat(new[] { ManifestName }))
                {
                    // Do not even resolve user folders; they may legitimately be junctions to an external library.
                    if (IsUserData(relative)) continue;
                    string destination = SafePath(target, relative);
                    bool exists = File.Exists(destination);
                    bool applicationFile = relative is "MCSkin3D.exe" or "update-channel.txt" or ManifestName;
                    // User libraries and preferences are never installed, changed, or removed by an update.
                    if (!applicationFile)
                    {
                        if (preserved.Contains(relative)) continue;
                        bool wasBundled = previous?.Files.TryGetValue(relative, out _) == true;
                        if (!exists && wasBundled) continue; // Respect intentional deletion of an old bundled asset.
                        if (exists && (!wasBundled || !HashFile(destination).Equals(previous.Files[relative], StringComparison.OrdinalIgnoreCase)))
                        {
                            preserved.Add(relative);
                            continue; // Keep this ownership decision across every subsequent update.
                        }
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    string incoming = Path.Combine(job, "incoming");
                    if (relative == ManifestName)
                    {
                        installedManifest.PreservedFiles = preserved.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
                        File.WriteAllText(incoming, JsonSerializer.Serialize(installedManifest));
                    }
                    else File.Copy(SafePath(payload, relative), incoming, true);
                    if (exists)
                    {
                        string saved = SafePath(backup, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(saved));
                        if (File.Exists(saved)) throw new IOException("This update already has a recovery backup; prepare a fresh download.");
                        File.Replace(incoming, destination, saved);
                    }
                    else File.Move(incoming, destination, false); // Never overwrite a file created since the absence check.
                    installed.Add((relative, exists));
                    afterFileInstalled?.Invoke(installed.Count);
                }
                Log("Installed " + version + ". Recovery backup retained at " + backup);
            }
            catch (Exception installError)
            {
                var errors = new List<Exception> { installError };
                foreach (var item in installed.AsEnumerable().Reverse())
                {
                    try
                    {
                        string destination = SafePath(target, item.Path);
                        if (item.Existed) File.Copy(SafePath(backup, item.Path), destination, true);
                        else File.Delete(destination); // Only a new file created by this failed transaction.
                    }
                    catch (Exception rollbackError) { errors.Add(rollbackError); }
                }
                if (errors.Count > 1)
                    throw new AggregateException("The update failed and recovery needs attention. Backups are in " + backup, errors);
                throw new IOException("The update failed; replaced files were restored. " + installError.Message, installError);
            }
        }

        private static FileStream AcquireInstallerLock(string target)
        {
            try
            {
                return new FileStream(Path.Combine(target, ".mcskin3d-update.lock"), FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex) { throw new UpdateBlockedException("Another editor or installer is using this folder. Close it before trying the update again.", ex); }
        }

        private static bool IsUserData(string relative)
        {
            string first = relative.Split('/')[0];
            return first.Equals("Skins", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("Swatches", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("Data", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("settings.ini", StringComparison.OrdinalIgnoreCase) ||
                first.StartsWith(".", StringComparison.Ordinal) || first.Equals("__updateFiles", StringComparison.OrdinalIgnoreCase);
        }

        internal static void RemoveStaging(string job)
        {
            // Only generated download staging is eligible. Handed-off jobs retain payload and recovery backups.
            try
            {
                string full = Path.GetFullPath(job);
                ValidateJob(full);
                if (Directory.Exists(Path.Combine(full, "backup"))) return;
                Directory.Delete(full, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void Log(string message)
        {
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MCSkin3D", "Logs");
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "update.log"), DateTimeOffset.Now + " " + message + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
