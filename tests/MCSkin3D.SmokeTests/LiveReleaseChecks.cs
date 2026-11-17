using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MCSkin3D.UpdateSystem;

// Explicit opt-in online release/real-process test. Never used by normal offline builds.
internal static class LiveReleaseChecks
{
    internal static int Run(string[] args)
    {
        try { RunAsync(args).GetAwaiter().GetResult(); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static async Task RunAsync(string[] args)
    {
        int option = Array.IndexOf(args, "--verify-release");
        if (args.Length < option + 3) throw new ArgumentException("Usage: --verify-release <existing-package.zip> <new-isolated-test-directory> [--local-package <new-package.zip>]");
        string originalPackage = Path.GetFullPath(args[option + 1]);
        string target = Path.GetFullPath(args[option + 2]);
        if (Directory.Exists(target)) throw new IOException("Release verification requires a new, isolated directory.");
        Directory.CreateDirectory(target);
        ZipFile.ExtractToDirectory(originalPackage, target);
        if (!File.Exists(Path.Combine(target, "MCSkin3D.exe"))) throw new InvalidDataException("Original package has no root executable.");
        File.WriteAllText(Path.Combine(target, "settings.ini"), "[GlobalSettings]\r\nAutoUpdate=False\r\nLanguageFile=en\r\nPreviewLightLevel=6\r\nLegacyKey=keep\r\n");
        Directory.CreateDirectory(Path.Combine(target, "Skins", "Updater verification"));
        string customSkin = Path.Combine(target, "Skins", "Updater verification", "my-skin.png");
        File.Copy(Directory.GetFiles(Path.Combine(target, "Skins"), "*.png", SearchOption.AllDirectories).First(), customSkin);
        string customPalette = Path.Combine(target, "Swatches", "keep-this-custom-file.txt");
        File.WriteAllText(customPalette, "Do not overwrite this user's file.");
        string skinHash = UpdateInstaller.HashFile(customSkin);
        string job;
        int localOption = Array.IndexOf(args, "--local-package");
        if (localOption >= 0)
        {
            job = Path.Combine(target, UpdateInstaller.StagingPrefix + Guid.NewGuid().ToString("N"));
            string payload = Path.Combine(job, "payload");
            UpdateInstaller.ExtractPackage(Path.GetFullPath(args[localOption + 1]), payload, default);
            var manifest = UpdateInstaller.ReadManifest(payload);
            UpdateInstaller.ValidatePayload(payload, Version.Parse(manifest.Version));
            Console.WriteLine("PASS published package manifest, executable version and file checksums");
        }
        else
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MCSkin3D-ReleaseVerification/2.0.0.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            var checker = new UpdateChecker(client, new Version(1, 7, 2, 0));
            if (!await checker.CheckForUpdatesAsync(true)) throw new InvalidOperationException("Live release check failed: " + checker.LastError);
            Console.WriteLine("PASS GitHub latest release discovery: " + checker.LatestRelease.TagName + " / " + checker.LatestRelease.Asset.Name);
            if (string.IsNullOrWhiteSpace(checker.LatestRelease.Body)) throw new InvalidDataException("Release notes are missing.");
            var upToDate = new UpdateChecker(client, checker.LatestRelease.Version);
            if (await upToDate.CheckForUpdatesAsync() || !upToDate.LastCheckSucceeded)
                throw new InvalidOperationException("An installed current release must not update itself.");
            var skipped = new UpdateChecker(client, new Version(1, 7, 2, 0), () => checker.LatestRelease.Version.ToString());
            if (await skipped.CheckForUpdatesAsync() || !await skipped.CheckForUpdatesAsync(true))
                throw new InvalidOperationException("Live skip/manual bypass behavior failed.");
            using var prepared = await UpdateInstaller.PrepareAsync(checker.LatestRelease, target, client, null, default);
            prepared.HandedOff = true;
            job = prepared.DirectoryPath;
            Console.WriteLine("PASS real release download, size, SHA-256 and complete staged payload validation");
        }

        var sentinelStart = new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = false, CreateNoWindow = true };
        sentinelStart.ArgumentList.Add("--update-parent");
        sentinelStart.ArgumentList.Add(job);
        using var sentinel = Process.Start(sentinelStart);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await sentinel.WaitForExitAsync(timeout.Token);
        while (!File.Exists(Path.Combine(job, "result.txt"))) await Task.Delay(100, timeout.Token);
        if (sentinel.ExitCode != 0 || !File.Exists(Path.Combine(job, "ready")) || File.ReadAllText(Path.Combine(job, "result.txt")) != "success")
            throw new InvalidOperationException("Real updater helper did not complete successfully: " + job);
        if (UpdateInstaller.HashFile(customSkin) != skinHash || File.ReadAllText(customPalette) != "Do not overwrite this user's file." ||
            !File.ReadAllLines(Path.Combine(target, "settings.ini")).Contains("AutoUpdate=False"))
            throw new InvalidOperationException("The update changed user skin, palette or settings data.");
        if (UpdateInstaller.HashFile(Path.Combine(target, "MCSkin3D.exe")) != UpdateInstaller.HashFile(Path.Combine(job, "payload", "MCSkin3D.exe")))
            throw new InvalidOperationException("The helper did not install the expected executable.");
        if (UpdateInstaller.ReadManifest(target).Files.Count != 2) throw new InvalidOperationException("Expected the Minimal manifest.");
        Console.WriteLine("PASS production handoff, verified Minimal installation and original user data");

        Process restarted = null;
        for (int attempt = 0; attempt < 100 && restarted == null; attempt++)
        {
            foreach (var process in Process.GetProcessesByName("MCSkin3D"))
            {
                try
                {
                    if (process.MainModule.FileName.Equals(Path.Combine(target, "MCSkin3D.exe"), StringComparison.OrdinalIgnoreCase))
                    { restarted = process; break; }
                }
                catch { }
                process.Dispose();
            }
            if (restarted == null) await Task.Delay(100);
        }
        if (restarted == null) throw new InvalidOperationException("The updated editor was not restarted.");
        using (restarted) await WaitForEditorAndCloseAsync(restarted);
        var preferences = File.ReadAllLines(Path.Combine(target, "settings.ini"));
        if (!new[] { "AutoUpdate=False", "PreviewLightLevel=6", "LegacyKey=keep", "SkippedUpdateVersion=" }.All(preferences.Contains))
            throw new InvalidOperationException("Old settings were not migrated with their values intact.");

        // The same Minimal payload must also boot with no previous installation or settings.
        string fresh = Path.Combine(job, "fresh-test");
        string source = Path.Combine(job, "payload");
        Directory.CreateDirectory(Path.Combine(fresh, "Languages"));
        foreach (string file in new[] { "MCSkin3D.exe", "Languages/English.lang", "update-manifest.json" })
            File.Copy(Path.Combine(source, file), Path.Combine(fresh, file));
        using var firstLaunch = Process.Start(new ProcessStartInfo(Path.Combine(fresh, "MCSkin3D.exe"))
            { WorkingDirectory = fresh, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        await WaitForEditorAndCloseAsync(firstLaunch);
        if (!File.ReadAllLines(Path.Combine(fresh, "settings.ini")).Contains("PreviewLightLevel=15") ||
            !File.Exists(Path.Combine(fresh, "Swatches", "Quick Picks.swtch")))
            throw new InvalidOperationException("Minimal did not initialize its settings and palette.");
        using var starter = new System.Drawing.Bitmap(Path.Combine(fresh, "Skins", "Steve.png"));
        if (starter.Width != 64 || starter.Height != 64) throw new InvalidDataException("Invalid starter skin.");
        Console.WriteLine("PASS Minimal starts from three files, generates settings, starter skin and palette");
        Console.WriteLine("Release verification passed. Retained isolated files and backup: " + target);
    }

    private static async Task WaitForEditorAndCloseAsync(Process editor)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            while (true)
            {
                editor.Refresh();
                if (editor.HasExited) throw new InvalidOperationException("The test editor exited during startup.");
                if (editor.MainWindowTitle.Contains("MCSkin3D") && editor.MainWindowTitle.Contains("2.0.0.0")) break;
                await Task.Delay(100, timeout.Token);
            }
            Console.WriteLine("PASS editor opened: " + editor.MainWindowTitle);
            if (!editor.CloseMainWindow()) throw new InvalidOperationException("Could not close the isolated test editor cleanly.");
            await editor.WaitForExitAsync(timeout.Token);
        }
        finally { if (!editor.HasExited) editor.Kill(); }
    }
}
