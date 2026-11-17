using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MCSkin3D.UpdateSystem;

internal static class UpdateInstallerChecks
{
    private static readonly Version PackageVersion = typeof(UpdateChecker).Assembly.GetName().Version;

    internal static void Run(Action<bool, string> check, string scratch)
    {
        string root = Path.Combine(scratch, "installer-checks");
        Directory.CreateDirectory(root);
        Preservation(check, root);
        PreservationAcrossUpdates(check, root);
        Rollback(check, root);
        LockAndDowngrade(check, root);
        AtomicSettings(check, root);
        ArchiveValidation(check, root);
        ManifestValidation(check, root);
        Downloads(check, root).GetAwaiter().GetResult();
    }

    private static void Preservation(Action<bool, string> check, string root)
    {
        string target = Path.Combine(root, "preservation");
        Write(target, "MCSkin3D.exe", "original application");
        Write(target, UpdateInstaller.EnglishCatalog, "factory English");
        Write(target, "Models/custom.xml", "user model");
        Write(target, "Swatches/custom.swtch", "user palette");
        Write(target, "Skins/player.png", "user skin");
        Write(target, "settings.ini", "user preferences");
        WriteManifest(target, "1.7.2.0");
        string job = NewJob(target);
        CreatePayload(Path.Combine(job, "payload"));
        WriteManifest(Path.Combine(job, "payload"), PackageVersion.ToString());
        var before = Snapshot(target);
        UpdateInstaller.ApplyPreparedUpdate(job);
        foreach (var file in before.Where(p => p.Key != "MCSkin3D.exe" && p.Key != UpdateInstaller.ManifestName &&
            p.Key.Replace('\\', '/') != UpdateInstaller.EnglishCatalog))
            check(UpdateInstaller.HashFile(Path.Combine(target, file.Key)) == file.Value, "Minimal preserves " + file.Key);
        check(Read(target, UpdateInstaller.EnglishCatalog) == "new English", "Factory translation updates");
        check(UpdateInstaller.ReadManifest(target).Files.Count == 2, "Installed manifest contracts to the two managed files");
        check(Read(Path.Combine(job, "backup"), "MCSkin3D.exe") == "original application", "Executable backup retained");
    }

    private static void PreservationAcrossUpdates(Action<bool, string> check, string root)
    {
        string target = Path.Combine(root, "custom-translation");
        Write(target, "MCSkin3D.exe", "original executable");
        Write(target, UpdateInstaller.EnglishCatalog, "factory English");
        WriteManifest(target, "1.7.2.0");
        Write(target, UpdateInstaller.EnglishCatalog, "new English");
        for (int i = 0; i < 2; i++)
        {
            string job = NewJob(target);
            string payload = Path.Combine(job, "payload");
            CreatePayload(payload);
            if (i == 1) Write(payload, UpdateInstaller.EnglishCatalog, "third English");
            WriteManifest(payload, PackageVersion.ToString());
            UpdateInstaller.ApplyPreparedUpdate(job);
            check(Read(target, UpdateInstaller.EnglishCatalog) == "new English", "Customized translation survives successive updates");
        }
    }

    private static void Rollback(Action<bool, string> check, string root)
    {
        string target = Path.Combine(root, "rollback");
        Write(target, "MCSkin3D.exe", "original executable bytes");
        Write(target, "settings.ini", "untouched preferences");
        WriteManifest(target, "1.7.2.0");
        var before = Snapshot(target);
        string job = NewJob(target);
        string payload = Path.Combine(job, "payload");
        CreatePayload(payload);
        WriteManifest(payload, PackageVersion.ToString());
        Expect<IOException>(() => UpdateInstaller.ApplyPreparedUpdate(job, count =>
        {
            if (count == 3) throw new IOException("Injected failure after all replacements");
        }), check, "Failed install reports rollback");
        var after = Snapshot(target);
        check(before.Count == after.Count && before.All(p => after.TryGetValue(p.Key, out var hash) && p.Value == hash),
            "Rollback restores the original executable and manifest and removes the newly added catalog");
    }
    private static void LockAndDowngrade(Action<bool, string> check, string root)
    {
        string target = Path.Combine(root, "lock-and-downgrade");
        Write(target, "MCSkin3D.exe", "original executable");
        WriteManifest(target, "1.7.2.0");
        string job = NewJob(target);
        string payload = Path.Combine(job, "payload");
        CreatePayload(payload);
        WriteManifest(payload, PackageVersion.ToString());
        foreach (var share in new[] { FileShare.ReadWrite, FileShare.None })
        {
            using var competing = new FileStream(Path.Combine(target, ".mcskin3d-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, share);
            Expect<IOException>(() => UpdateInstaller.ApplyPreparedUpdate(job), check, "An active editor or installer lock prevents replacement: " + share);
            check(Read(target, "MCSkin3D.exe") == "original executable" && !Directory.Exists(Path.Combine(job, "backup")), "Contended installation does not change or back up any application file");
        }
        WriteManifest(target, "99.0.0.0");
        Expect<IOException>(() => UpdateInstaller.ApplyPreparedUpdate(job), check, "A stale prepared update cannot downgrade a newer installed manifest");
        check(Read(target, "MCSkin3D.exe") == "original executable" && UpdateInstaller.ReadManifest(target).Version == "99.0.0.0", "Rejected downgrade leaves installed executable and manifest unchanged");
    }

    private static void AtomicSettings(Action<bool, string> check, string root)
    {
        string target = Path.Combine(root, "atomic-settings.ini");
        var settings = new Paril.Settings.Settings();
        settings.Structures.Add(typeof(SettingsFixture));
        SettingsFixture.FailRead = false;
        SettingsFixture.StoredValue = "original settings";
        settings.Save(target);
        byte[] original = File.ReadAllBytes(target);
        SettingsFixture.StoredValue = "changed settings";
        SettingsFixture.FailRead = true;
        try
        {
            Expect<System.Reflection.TargetInvocationException>(() => settings.Save(target), check, "A failed setting getter reports save failure");
            check(original.SequenceEqual(File.ReadAllBytes(target)), "Serialization failure preserves the existing settings file byte-for-byte");
        }
        finally { SettingsFixture.FailRead = false; }
        bool replacementFailed = false;
        using (var locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { settings.Save(target); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { replacementFailed = true; }
        }
        check(replacementFailed, "A locked destination reports atomic replacement failure");
        check(original.SequenceEqual(File.ReadAllBytes(target)), "Replacement failure preserves the original settings");
        check(!Directory.EnumerateFiles(root, "atomic-settings.ini.tmp-*").Any(), "Failed atomic save removes its temporary file");
        settings.Save(target);
        check(File.ReadAllText(target).Contains("Value=changed settings", StringComparison.Ordinal), "Successful retry commits the entire new settings file");
    }

    public static class SettingsFixture
    {
        public static bool FailRead;
        public static string StoredValue;
        [Paril.Settings.Savable]
        public static string Value => FailRead ? throw new InvalidOperationException("Injected settings serialization failure") : StoredValue;
    }
    private static void ArchiveValidation(Action<bool, string> check, string root)
    {
        var entries = new[]
        {
            new[] { "../outside.txt" }, new[] { "folder/../../outside.txt" }, new[] { "..\\outside.txt" },
            new[] { "C:/outside.txt" }, new[] { "/outside.txt" }, new[] { "file.txt:stream" },
            new[] { "NUL.txt" }, new[] { "folder/trailing. " }, new[] { "a//b.txt" },
            new[] { "same.txt", "same.txt" }, new[] { "same.txt", "SAME.TXT" }
        };
        for (int index = 0; index < entries.Length; index++)
        {
            string archive = Path.Combine(root, "bad-path-" + index + ".zip");
            CreateArchive(archive, entries[index]);
            Expect<InvalidDataException>(() => UpdateInstaller.ExtractPackage(archive, Path.Combine(root, "bad-path-" + index), CancellationToken.None), check, "Reject unsafe or duplicate archive paths: " + string.Join(", ", entries[index]));
        }
        string symbolic = Path.Combine(root, "symbolic.zip");
        using (var zip = ZipFile.Open(symbolic, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("linked-file");
            entry.ExternalAttributes = unchecked((int)0xA0000000);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("../../outside.txt");
        }
        Expect<InvalidDataException>(() => UpdateInstaller.ExtractPackage(symbolic, Path.Combine(root, "symbolic"), CancellationToken.None), check, "Archive symbolic links are rejected");
        check(!File.Exists(Path.Combine(root, "outside.txt")), "Traversal entries never create a file outside their extraction folder");
    }

    private static void ManifestValidation(Action<bool, string> check, string root)
    {
        string payload = Path.Combine(root, "manifest-payload");
        CreatePayload(payload);
        WriteManifest(payload, PackageVersion.ToString());
        check(UpdateInstaller.ValidatePayload(payload, PackageVersion).Files.Count == 2, "Valid complete file manifest and executable version pass verification");
        Write(payload, UpdateInstaller.EnglishCatalog, "tampered resource");
        Expect<InvalidDataException>(() => UpdateInstaller.ValidatePayload(payload, PackageVersion), check, "Payload file hash mismatch rejected");
        WriteManifest(payload, PackageVersion.ToString());
        Write(payload, "unexpected.txt", "unlisted file");
        Expect<InvalidDataException>(() => UpdateInstaller.ValidatePayload(payload, PackageVersion), check, "Unlisted payload file rejected");
        File.Delete(Path.Combine(payload, "unexpected.txt"));
        Expect<InvalidDataException>(() => UpdateInstaller.ValidatePayload(payload, new Version(99, 0, 0, 0)), check, "Release and manifest version mismatch rejected");
        Write(payload, "Skins/unexpected.png", "extra");
        WriteManifest(payload, PackageVersion.ToString());
        Expect<InvalidDataException>(() => UpdateInstaller.ValidatePayload(payload, PackageVersion), check, "Even a listed extra file is outside the Minimal payload");
        File.Delete(Path.Combine(payload, "Skins/unexpected.png"));
        WriteManifest(payload, "99.0.0.0");
        Expect<InvalidDataException>(() => UpdateInstaller.ValidatePayload(payload, new Version(99, 0, 0, 0)), check, "Executable version must match manifest and release");
        var duplicate = new Dictionary<string, string> { ["file.txt"] = new string('a', 64), ["FILE.TXT"] = new string('a', 64) };
        File.WriteAllText(Path.Combine(payload, UpdateInstaller.ManifestName), JsonSerializer.Serialize(new { Version = PackageVersion.ToString(), Edition = "portable", Files = duplicate }));
        Expect<InvalidDataException>(() => UpdateInstaller.ReadManifest(payload), check, "Manifest case-colliding paths rejected");
        var escaped = new Dictionary<string, string> { ["../escape.txt"] = new string('a', 64) };
        File.WriteAllText(Path.Combine(payload, UpdateInstaller.ManifestName), JsonSerializer.Serialize(new { Version = PackageVersion.ToString(), Edition = "portable", Files = escaped }));
        Expect<InvalidDataException>(() => UpdateInstaller.ReadManifest(payload), check, "Manifest traversal path rejected");
    }

    private static async Task Downloads(Action<bool, string> check, string root)
    {
        string target = Path.Combine(root, "downloads");
        Write(target, "MCSkin3D.exe", "old executable");
        Write(target, "settings.ini", "user preference sentinel");
        string source = Path.Combine(root, "download-source");
        CreatePayload(source);
        WriteManifest(source, PackageVersion.ToString());
        string archive = Path.Combine(root, "valid-download.zip");
        ZipFile.CreateFromDirectory(source, archive);
        byte[] bytes = File.ReadAllBytes(archive);
        string filename = "MCSkin3D-" + PackageVersion + "-win-x64-minimal.zip";
        var release = new UpdateRelease
        {
            Version = PackageVersion, TagName = "v" + PackageVersion,
            Asset = new UpdateAsset { Name = filename, Size = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)), DownloadUrl = UpdateChecker.ReleasesUrl + "/download/v" + PackageVersion + "/" + filename }
        };
        using var handler = new DownloadHandler(() => new ByteArrayContent(bytes));
        using var client = new HttpClient(handler);
        var progress = new InlineProgress();
        string job;
        using (var prepared = await UpdateInstaller.PrepareAsync(release, target, client, progress, CancellationToken.None).ConfigureAwait(false))
        {
            job = prepared.DirectoryPath;
            check(progress.Last == 100 && File.Exists(Path.Combine(job, "payload", "MCSkin3D.exe")), "Valid download is hashed, extracted and staged with completed progress");
            check(Read(target, "MCSkin3D.exe") == "old executable" && Read(target, "settings.ini") == "user preference sentinel", "Preparation does not alter the running installation");
        }
        check(!Directory.Exists(job), "Abandoned prepared update removes its own staging folder");
        byte[] corrupt = (byte[])bytes.Clone();
        corrupt[corrupt.Length - 1] ^= 1;
        handler.Content = () => new ByteArrayContent(corrupt);
        await ExpectAsync<InvalidDataException>(() => UpdateInstaller.PrepareAsync(release, target, client, null, CancellationToken.None), check, "Corrupt download is rejected before installation").ConfigureAwait(false);
        check(!Directory.EnumerateDirectories(target, UpdateInstaller.StagingPrefix + "*").Any(), "Corrupt download leaves no abandoned staging folder");
        handler.Content = () => new ByteArrayContent(bytes.Take(bytes.Length - 1).ToArray());
        await ExpectAsync<InvalidDataException>(() => UpdateInstaller.PrepareAsync(release, target, client, null, CancellationToken.None), check, "Truncated content length rejected").ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource();
        handler.Content = () => new StreamContent(new CancellingStream(bytes, cancellation));
        await ExpectAsync<OperationCanceledException>(() => UpdateInstaller.PrepareAsync(release, target, client, null, cancellation.Token), check, "Cancelling a partially downloaded update aborts preparation").ConfigureAwait(false);
        check(!Directory.EnumerateDirectories(target, UpdateInstaller.StagingPrefix + "*").Any() && Read(target, "MCSkin3D.exe") == "old executable", "Cancellation cleans staged bytes and leaves the installed executable unchanged");
    }

    private static string NewJob(string target)
    {
        string job = Path.Combine(target, UpdateInstaller.StagingPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(job);
        return job;
    }
    private static void CreatePayload(string payload)
    {
        Directory.CreateDirectory(payload);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "MCSkin3D.exe"), Path.Combine(payload, "MCSkin3D.exe"));
        Write(payload, UpdateInstaller.EnglishCatalog, "new English");
    }
    private static void WriteManifest(string directory, string version)
    {
        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(file => Path.GetFileName(file) != UpdateInstaller.ManifestName)
            .ToDictionary(file => Path.GetRelativePath(directory, file).Replace('\\', '/'), UpdateInstaller.HashFile);
        File.WriteAllText(Path.Combine(directory, UpdateInstaller.ManifestName), JsonSerializer.Serialize(new UpdateManifest { Version = version, Files = files }));
    }
    private static void Write(string directory, string relative, string text)
    {
        string path = Path.Combine(directory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text);
    }
    private static string Read(string directory, string relative) => File.ReadAllText(Path.Combine(directory, relative));
    private static Dictionary<string, string> Snapshot(string target) => Directory.GetFiles(target, "*", SearchOption.AllDirectories)
        .Where(path => !Path.GetRelativePath(target, path).StartsWith(".mcskin3d-update", StringComparison.Ordinal))
        .ToDictionary(path => Path.GetRelativePath(target, path), UpdateInstaller.HashFile);
    private static void CreateArchive(string path, IEnumerable<string> names)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (string name in names)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write("untrusted archive content");
        }
    }
    private static void Expect<T>(Action action, Action<bool, string> check, string message) where T : Exception
    {
        try { action(); }
        catch (T) { check(true, message); return; }
        check(false, message);
    }
    private static async Task ExpectAsync<T>(Func<Task<PreparedUpdate>> action, Action<bool, string> check, string message) where T : Exception
    {
        try { using var prepared = await action().ConfigureAwait(false); }
        catch (T) { check(true, message); return; }
        check(false, message);
    }
    private sealed class InlineProgress : IProgress<int>
    {
        internal int Last;
        public void Report(int value) { Last = value; }
    }
    private sealed class DownloadHandler : HttpMessageHandler
    {
        internal Func<HttpContent> Content;
        internal DownloadHandler(Func<HttpContent> content) { Content = content; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = Content() });
    }
    private sealed class CancellingStream : MemoryStream
    {
        private readonly CancellationTokenSource cancellation;
        internal CancellingStream(byte[] bytes, CancellationTokenSource cancellation) : base(bytes) { this.cancellation = cancellation; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await base.ReadAsync(buffer.Slice(0, Math.Min(buffer.Length, 64)), cancellationToken).ConfigureAwait(false);
            cancellation.Cancel();
            return read;
        }
    }
}
