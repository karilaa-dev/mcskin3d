using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MCSkin3D;
using MCSkin3D.Forms;
using MCSkin3D.UpdateSystem;

internal static class UpdateDialogChecks
{
    internal static void Run(Action<bool, string> check)
    {
        string original = GlobalSettings.SkippedUpdateVersion;
        try
        {
            GlobalSettings.SkippedUpdateVersion = "";
            var release = Release();
            int saves = 0;
            using (var dialog = Dialog(release, () => saves++))
            {
                dialog.Show();
                check(Control<RichTextBox>(dialog, "releaseNotes").Text.Contains("<script>literal notes</script>") &&
                    !Control<RichTextBox>(dialog, "releaseNotes").DetectUrls,
                    "Release notes remain selectable plain text without active HTML or links");
                foreach (Size size in new[] { new Size(660, 530), new Size(480, 400), new Size(1000, 700) })
                {
                    dialog.Size = size;
                    dialog.PerformLayout();
                    var notes = Control<RichTextBox>(dialog, "releaseNotes");
                    check(notes.Height > 50 && notes.Width > 200, "Changelog retains usable space when resizing update dialog");
                    foreach (string name in new[] { "installUpdate", "closeUpdate", "skipUpdate" })
                    {
                        var control = dialog.Controls.Find(name, true).Single();
                        Rectangle bounds = dialog.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
                        check(dialog.ClientRectangle.Contains(bounds), name + " remains inside the update dialog");
                    }
                }
                Control<CheckBox>(dialog, "skipUpdate").Checked = true;
                Control<Button>(dialog, "closeUpdate").PerformClick();
                check(saves == 1 && GlobalSettings.SkippedUpdateVersion == "2.0.0.0",
                    "Close saves the selected skipped version exactly once");
            }

            GlobalSettings.SkippedUpdateVersion = "2.0.0";
            using (var dialog = Dialog(release, () => saves++))
            {
                dialog.Show();
                check(Control<CheckBox>(dialog, "skipUpdate").Checked, "Previously skipped three-component version is checked");
                Control<CheckBox>(dialog, "skipUpdate").Checked = false;
                dialog.Close();
                check(GlobalSettings.SkippedUpdateVersion == "", "Unchecking skip clears this release's preference");
            }
            GlobalSettings.SkippedUpdateVersion = "3.0.0.0";
            using (var dialog = Dialog(release, () => saves++))
            {
                dialog.Show();
                dialog.Close();
                check(GlobalSettings.SkippedUpdateVersion == "3.0.0.0", "Closing another release preserves the saved skipped version");
            }

            foreach (var item in new[] { (supported: false, release), (supported: true, release: Release(false)) })
            {
                using var dialog = new UpdateDialog(item.release, (_, _, _) => Task.FromResult<PreparedUpdate>(null), item.supported, () => { });
                check(!Control<Button>(dialog, "installUpdate").Enabled &&
                    Control<Button>(dialog, "viewRelease") != null,
                    "Unsupported installation or missing checksum keeps manual download available");
            }

            GlobalSettings.SkippedUpdateVersion = "";
            using (var dialog = Dialog(release, () => throw new System.IO.IOException("Read-only test folder")))
            {
                dialog.Show();
                Control<CheckBox>(dialog, "skipUpdate").Checked = true;
                dialog.Close();
                check(dialog.Visible && GlobalSettings.SkippedUpdateVersion == "" &&
                    Control<Label>(dialog, "updateStatus").Text.Contains("Could not save"),
                    "Preference save failure stays visible and restores the in-memory value");
                Control<Button>(dialog, "closeUpdate").PerformClick();
                check(dialog.IsDisposed, "Close without saving exits after a preference persistence error");
            }

            using (var dialog = new UpdateDialog(release, (_, _, _) => Task.FromException<PreparedUpdate>(
                new System.IO.IOException("Disconnected test download")), true, () => { }))
            {
                dialog.Show();
                Control<Button>(dialog, "installUpdate").PerformClick();
                PumpUntil(() => Control<Button>(dialog, "installUpdate").Enabled);
                check(dialog.Visible && Control<Label>(dialog, "updateStatus").Text.Contains("Disconnected test download") &&
                    dialog.PreparedUpdate == null, "Download failure keeps the editor dialog open with a retry and useful error");
                dialog.Close();
            }

            // A successful dialog transfers its staged download to the editor, which can
            // still cancel shutdown for unsaved work and dispose it without installing.
            var preparedRoot = Directory.CreateTempSubdirectory("MCSkin3D-UpdateDialog-");
            try
            {
                File.WriteAllText(Path.Combine(preparedRoot.FullName, "MCSkin3D.exe"), "Test installation");
                string job = Path.Combine(preparedRoot.FullName, UpdateInstaller.StagingPrefix + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(job);
                using var prepared = new PreparedUpdate(job);
                GlobalSettings.SkippedUpdateVersion = "2.0.0.0";
                using (var dialog = new UpdateDialog(release, (_, _, _) => Task.FromResult(prepared), true, () => saves++))
                {
                    dialog.Show();
                    Control<Button>(dialog, "installUpdate").PerformClick();
                    PumpUntil(() => dialog.IsDisposed);
                    check(dialog.DialogResult == DialogResult.OK && ReferenceEquals(dialog.PreparedUpdate, prepared) &&
                        Directory.Exists(job) && GlobalSettings.SkippedUpdateVersion == "",
                        "Successful preparation clears skip and transfers staging ownership without installing or deleting it");
                }
                prepared.Dispose();
                check(!Directory.Exists(job), "Caller can discard a prepared update when editor shutdown is canceled");
            }
            finally { Directory.Delete(preparedRoot.FullName, true); }

            var pending = new TaskCompletionSource<PreparedUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken token = default;
            using (var dialog = new UpdateDialog(release, (_, _, cancellation) => { token = cancellation; return pending.Task; }, true, () => { }))
            {
                dialog.Show();
                Control<Button>(dialog, "installUpdate").PerformClick();
                check(!Control<Button>(dialog, "installUpdate").Enabled, "Downloading disables repeated update requests");
                Control<Button>(dialog, "closeUpdate").PerformClick();
                check(token.IsCancellationRequested && dialog.Visible && !dialog.IsDisposed,
                    "Close cancels an active download and waits for it to settle");
                pending.SetCanceled(token);
                PumpUntil(() => dialog.IsDisposed);
                check(dialog.PreparedUpdate == null, "Canceled download closes without an install request");
            }
        }
        finally { GlobalSettings.SkippedUpdateVersion = original; }
    }

    private static UpdateDialog Dialog(UpdateRelease release, Action save) =>
        new(release, (_, _, _) => Task.FromResult<PreparedUpdate>(null), true, save);

    private static UpdateRelease Release(bool checksum = true) => new()
    {
        Version = new Version(2, 0, 0, 0), TagName = "v2.0.0.0", Name = "MCSkin3D 2.0.0.0",
        Body = "# Changes\n- Automatic updates\n<script>literal notes</script>\n" + string.Join("\n", Enumerable.Repeat("Additional release note", 50)),
        HtmlUrl = "https://github.com/karilaa-dev/mcskin3d/releases/tag/v2.0.0.0",
        Asset = new UpdateAsset
        {
            Name = "MCSkin3D-2.0.0.0-win-x64-minimal.zip", Size = 100,
            DownloadUrl = "https://github.com/karilaa-dev/mcskin3d/releases/download/v2.0.0.0/MCSkin3D-2.0.0.0-win-x64-minimal.zip",
            Sha256 = checksum ? new string('a', 64) : null
        }
    };

    private static T Control<T>(Form dialog, string name) where T : Control => (T)dialog.Controls.Find(name, true).Single();

    private static void PumpUntil(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Update dialog did not finish its pending operation.");
            Application.DoEvents();
            Thread.Sleep(1);
        }
    }
}
