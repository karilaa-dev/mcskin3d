using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using MCSkin3D;
using MCSkin3D.Services;
using Paril.Imaging;

internal static partial class Program
{
    private static void PlayerModelSelection()
    {
        var tree = Field<TreeView>(editor, "treeView1");
        var original = Field<Skin>(editor, "_lastSkin");
        var siblings = original.Parent?.Nodes ?? tree.Nodes;
        var notice = Field<ToolStripStatusLabel>(editor, "_modelNotice");
        var timer = Field<System.Windows.Forms.Timer>(editor, "_modelNoticeTimer");
        int interval = timer.Interval;
        string PathFor(string name) => Path.Combine(original.Directory.FullName, name + ".png");
        Skin OpenNode(string path)
        {
            var skin = new Skin(path);
            siblings.Add(skin);
            Check(skin.SetImages(), "Load the skin thumbnail and saved model");
            return skin;
        }
        string Template(string name, string model)
        {
            string path = PathFor(name);
            File.WriteAllBytes(path, Convert.FromBase64String(ModelLoader.GetModelForPath(model).DefaultTexture));
            return path;
        }
        try
        {
            string path = Template("model-check-local", "Players/Alex");
            PNGMetadata.WriteMetadata(path, new() { ["Model"] = "Players/Steve", ["Artist"] = "Example" });
            var local = OpenNode(path);
            Check(local.Model.Name == "Steve" && !local.ModelChecked, "Loading thumbnails leaves a legacy skin unchecked");
            tree.SelectedNode = local;
            Check(local.Model.Name == "Alex" && !local.Dirty && Editor.CurrentModel == local.Model, "First selection corrects the model without marking pixels dirty");
            var saved = PNGMetadata.ReadMetadata(path);
            Check(saved["Model"] == "Players/Alex" && saved["ModelChecked"] == "1" && saved["Artist"] == "Example", "First selection persists the result and preserves other PNG metadata");
            Check(notice.Visible && notice.Text.Contains(local.Name) && notice.Text.Contains("Alex"), "Automatic correction identifies the skin and model");
            Check(timer.Enabled, "Automatic model notice has an active timeout");
            timer.Interval = 1;
            var elapsed = Stopwatch.StartNew();
            while (notice.Visible && elapsed.ElapsedMilliseconds < 1000) { Application.DoEvents(); Thread.Sleep(1); }
            Check(!notice.Visible, "The notice dismisses without a click");
            timer.Interval = interval;
            editor.SetModel(ModelLoader.GetModelForPath("Players/Steve"));
            Check(!local.Dirty, "Remembering a model on its own requires no pixel save");
            tree.SelectedNode = original;
            local.Remove();
            local.Dispose();
            var reopened = OpenNode(path);
            tree.SelectedNode = reopened;
            Check(reopened.Model.Name == "Steve" && reopened.ModelChecked && !notice.Visible, "Manual override survives reopening without a save or another notification");
            reopened.CommitChanges(GlobalDirtiness.CurrentSkin, true);
            reopened.Resize(128, 128);
            Check(PNGMetadata.ReadMetadata(path)["ModelChecked"] == "1", "Saving and changing resolution retain the decision");

            var matching = OpenNode(Template("model-check-matching", "Players/Steve"));
            tree.SelectedNode = matching;
            Check(matching.ModelChecked && !notice.Visible, "A matching first selection is remembered silently");
            string capePath = Template("model-check-cape", "Players/Alex (Minimal)");
            PNGMetadata.WriteMetadata(capePath, new() { ["Model"] = "Players/Cape" });
            var cape = OpenNode(capePath);
            tree.SelectedNode = cape;
            Check(cape.Model.Path == "Players/Cape" && !notice.Visible, "The player-arm check respects a non-player model");

            foreach (string model in new[] { "Players/Alex (Minimal)", "Players/Steve (Minimal)", "Players/Steve" })
            {
                string texture = Template("model-check-" + ModelLoader.GetModelForPath(model).Name, model);
                bool legacy = model.EndsWith("(Minimal)");
                PNGMetadata.WriteMetadata(texture, new() { ["Model"] = PlayerSkinModel.PathFor(!model.Contains("Alex"), legacy) });
                var skin = OpenNode(texture);
                tree.SelectedNode = skin;
                Check(skin.Model.Path == model && skin.ModelChecked, "Detects the shipped texture for " + model);
            }

            // A wide-arm texture explicitly reported as slim must still use the profile's model.
            string wide = Template("model-check-wide", "Players/Steve");
            string profilePath = PathFor("model-check-profile-priority");
            new DownloadedSkin(File.ReadAllBytes(wide), "Players/Alex").SaveNew(profilePath);
            var imported = OpenNode(profilePath);
            Check(imported.ModelChecked && imported.Model.Path == "Players/Alex", "Import has already recorded the profile's decision");
            byte[] before = File.ReadAllBytes(profilePath);
            tree.SelectedNode = imported;
            Check(imported.Model.Path == "Players/Alex" && before.AsSpan().SequenceEqual(File.ReadAllBytes(profilePath)), "Opening a nickname import skips the pixel check");

            string lockedPath = Template("model-check-read-only", "Players/Alex");
            File.SetAttributes(lockedPath, FileAttributes.ReadOnly);
            try
            {
                var locked = OpenNode(lockedPath);
                tree.SelectedNode = locked;
                Check(locked.Model.Path == "Players/Alex", "Read-only PNGs can still switch model");
                editor.SetModel(ModelLoader.GetModelForPath("Players/Steve"));
                tree.SelectedNode = original;
                locked.Remove();
                locked.Dispose();
                var lockedAgain = OpenNode(lockedPath);
                Check(lockedAgain.ModelChecked && lockedAgain.Model.Path == "Players/Steve", "Read-only model override survives a fresh document");
            }
            finally { File.SetAttributes(lockedPath, FileAttributes.Normal); }

            using var blank = new Bitmap(128, 128);
            Check(PlayerSkinModel.Detect(blank) == null, "A blank skin keeps its chosen model");
            blank.SetPixel(2 * 45, 2 * 24, Color.Coral);
            Check(PlayerSkinModel.Detect(blank) == "Players/Alex", "Scaled arm UVs identify a slim HD skin");
            blank.SetPixel(2 * 46 + 1, 2 * 54 + 1, Color.Coral);
            Check(PlayerSkinModel.Detect(blank) == "Players/Steve", "HD markers on the other arm identify a wide skin");
        }
        finally { timer.Interval = interval; tree.SelectedNode = original; }
    }
}
