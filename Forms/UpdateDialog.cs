using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MCSkin3D.UpdateSystem;

namespace MCSkin3D.Forms
{
    /// <summary>Shows release notes and prepares an update; the caller owns closing the editor.</summary>
    internal sealed class UpdateDialog : Form
    {
        private readonly UpdateRelease release;
        private readonly Func<UpdateRelease, IProgress<int>, CancellationToken, Task<PreparedUpdate>> prepare;
        private readonly Action saveSettings;
        private readonly CheckBox skip = new() { Name = "skipUpdate", Text = "Skip this update", AutoSize = true };
        private readonly Button update = new() { Name = "installUpdate", Text = "Update and restart", AutoSize = true };
        private readonly Button close = new() { Name = "closeUpdate", Text = "Close", AutoSize = true };
        private readonly Label status = new() { Name = "updateStatus", AutoSize = true, Dock = DockStyle.Fill };
        private readonly ProgressBar progress = new() { Name = "updateProgress", Dock = DockStyle.Fill, Height = 18, Visible = false };
        private CancellationTokenSource cancellation;
        private bool busy;
        private bool closeRequested;
        private bool settingsSaveFailed;

        // On OK the caller takes ownership. Other close paths clean up the staged download.
        public PreparedUpdate PreparedUpdate { get; private set; }

        public UpdateDialog(UpdateRelease release)
            : this(release, UpdateInstaller.PrepareAsync, UpdateInstaller.IsSupportedInstallation, GlobalSettings.Save) { }

        internal UpdateDialog(UpdateRelease release,
            Func<UpdateRelease, IProgress<int>, CancellationToken, Task<PreparedUpdate>> prepare,
            bool supportedInstallation, Action saveSettings = null)
        {
            this.release = release ?? throw new ArgumentNullException(nameof(release));
            this.prepare = prepare ?? throw new ArgumentNullException(nameof(prepare));
            this.saveSettings = saveSettings ?? GlobalSettings.Save;
            Text = "MCSkin3D update available";
            Name = "updateDialog";
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(660, 530);
            MinimumSize = new Size(480, 400);
            Padding = new Padding(16);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 7; i++)
                layout.RowStyles.Add(new RowStyle(i == 2 ? SizeType.Percent : SizeType.AutoSize, i == 2 ? 100 : 0));
            var heading = new Label
            {
                Name = "releaseTitle", Text = string.IsNullOrWhiteSpace(release.Name) ? "MCSkin3D " + release.Version : release.Name,
                AutoSize = true, Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 8)
            };
            layout.Controls.Add(heading, 0, 0);
            layout.Controls.Add(new Label
            {
                Name = "releaseVersions", Text = "Installed: " + typeof(UpdateDialog).Assembly.GetName().Version +
                    "     Available: " + release.Version,
                AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12)
            }, 0, 1);
            var notes = new RichTextBox
            {
                Name = "releaseNotes", Text = string.IsNullOrWhiteSpace(release.Body) ? "No release notes were provided." : release.Body,
                ReadOnly = true, DetectUrls = false, WordWrap = true, Dock = DockStyle.Fill,
                BackColor = SystemColors.Window, BorderStyle = BorderStyle.FixedSingle, ScrollBars = RichTextBoxScrollBars.Vertical,
                AccessibleName = "Release notes", Margin = new Padding(0, 0, 0, 12), TabIndex = 0
            };
            layout.Controls.Add(notes, 0, 2);
            status.Margin = new Padding(0, 0, 0, 8);
            status.AccessibleName = "Update status";
            layout.Controls.Add(status, 0, 3);
            progress.Margin = new Padding(0, 0, 0, 8);
            progress.AccessibleName = "Download progress";
            layout.Controls.Add(progress, 0, 4);
            skip.Checked = IsThisVersionSkipped();
            skip.AccessibleName = skip.Text;
            skip.Margin = new Padding(0, 0, 0, 10);
            skip.TabIndex = 1;
            skip.CheckedChanged += (_, _) => { settingsSaveFailed = false; close.Text = "Close"; };
            layout.Controls.Add(skip, 0, 5);
            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
                WrapContents = true, Margin = Padding.Empty
            };
            close.Margin = new Padding(8, 0, 0, 0);
            update.Margin = new Padding(8, 0, 0, 0);
            close.AccessibleName = close.Text;
            update.AccessibleName = update.Text;
            close.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
            update.Click += async (_, _) => await PrepareUpdateAsync();
            footer.Controls.Add(close);
            footer.Controls.Add(update);
            if (IsReleasePage(release.HtmlUrl))
            {
                var website = new Button { Name = "viewRelease", Text = "View on GitHub", AutoSize = true, Margin = Padding.Empty };
                website.AccessibleName = website.Text;
                website.Click += (_, _) =>
                {
                    try { Process.Start(new ProcessStartInfo(release.HtmlUrl) { UseShellExecute = true }); }
                    catch (Exception ex) { ShowError("Could not open the release page. " + ex.Message); }
                };
                footer.Controls.Add(website);
            }
            layout.Controls.Add(footer, 0, 6);
            Controls.Add(layout);
            AcceptButton = update;
            CancelButton = close;
            update.Enabled = supportedInstallation && release.Asset != null && !string.IsNullOrEmpty(release.Asset.Sha256);
            status.Text = !supportedInstallation
                ? "This build cannot install updates automatically. Download the new edition from GitHub."
                : release.Asset == null
                    ? "This release has no compatible download. Open GitHub for the available editions."
                    : string.IsNullOrEmpty(release.Asset.Sha256)
                        ? "This download has no verification checksum. Open GitHub to download it manually."
                        : "Your skins and settings will be preserved. The editor will close and restart after download.";
        }

        private bool IsThisVersionSkipped() =>
            UpdateChecker.TryParseVersion(GlobalSettings.SkippedUpdateVersion, out var version) && version == release.Version;

        private static bool IsReleasePage(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
            uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
            uri.AbsolutePath.StartsWith("/karilaa-dev/mcskin3d/releases/tag/", StringComparison.Ordinal);

        private async Task PrepareUpdateAsync()
        {
            if (busy || !update.Enabled) return;
            busy = true;
            closeRequested = false;
            update.Enabled = skip.Enabled = false;
            progress.Visible = true;
            progress.Value = 0;
            status.ForeColor = SystemColors.ControlText;
            status.Text = "Downloading and verifying the update...";
            cancellation = new CancellationTokenSource();
            try
            {
                PreparedUpdate ??= await prepare(release, new Progress<int>(value =>
                {
                    if (IsDisposed || Disposing || !busy) return;
                    progress.Value = Math.Clamp(value, 0, 100);
                    status.Text = value >= 100 ? "Verifying and preparing the update..." : "Downloading update: " + progress.Value + "%";
                }), cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (PreparedUpdate == null) throw new InvalidOperationException("The update was not prepared.");
                skip.Checked = false;
                status.Text = "Ready to install. Close the editor to continue.";
            }
            catch (OperationCanceledException)
            {
                PreparedUpdate?.Dispose();
                PreparedUpdate = null;
                status.Text = "Download canceled.";
            }
            catch (Exception ex)
            {
                ShowError("The update could not be prepared. " + ex.Message);
            }
            finally
            {
                busy = false;
                cancellation.Dispose();
                cancellation = null;
                update.Enabled = skip.Enabled = true;
                close.Enabled = true;
                progress.Visible = false;
            }
            if (closeRequested)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
            else if (PreparedUpdate != null)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy)
            {
                e.Cancel = true;
                DialogResult = DialogResult.None;
                closeRequested = true;
                close.Enabled = false;
                status.Text = "Canceling download...";
                cancellation.Cancel();
                return;
            }
            // After a persistence error, Close still offers a way to leave the dialog.
            if (!(settingsSaveFailed && DialogResult != DialogResult.OK))
            {
                string previous = GlobalSettings.SkippedUpdateVersion;
                string next = skip.Checked ? release.Version.ToString() : IsThisVersionSkipped() ? "" : previous;
                if (!string.Equals(previous, next, StringComparison.Ordinal))
                {
                    try
                    {
                        GlobalSettings.SkippedUpdateVersion = next;
                        saveSettings();
                    }
                    catch (Exception ex)
                    {
                        GlobalSettings.SkippedUpdateVersion = previous;
                        settingsSaveFailed = true;
                        close.Text = "Close without saving";
                        ShowError("Could not save the update preference. " + ex.Message);
                        DialogResult = DialogResult.None;
                        e.Cancel = true;
                        return;
                    }
                }
            }
            base.OnFormClosing(e);
        }

        private void ShowError(string message)
        {
            status.ForeColor = SystemColors.ControlText;
            status.Text = message;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                cancellation?.Cancel();
                if (DialogResult != DialogResult.OK)
                {
                    PreparedUpdate?.Dispose();
                    PreparedUpdate = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
