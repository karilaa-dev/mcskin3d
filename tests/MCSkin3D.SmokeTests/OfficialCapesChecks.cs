using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;
using System.Xml.Linq;
using MCSkin3D;
using MCSkin3D.Forms;
using Paril.Components;

internal static class OfficialCapesChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run(Action<bool, string> check)
    {
        var capes = OfficialCapeCatalog.All;
        using var manifestStream = OfficialCapeCatalog.OpenResource("catalog.xml");
        var manifest = XDocument.Load(manifestStream).Root.Elements("cape").ToDictionary(e => (string)e.Attribute("id"));
        check(capes.Count == 51 && capes.Select(c => c.Id).Distinct().Count() == capes.Count,
            "Official catalog includes current, personal and historical capes without duplicate identities");
        check(capes.All(c => c.Id != "test"), "Developer API-testing textures are excluded from official cosmetics");
        check(capes.SequenceEqual(capes.OrderByDescending(c => c.Released).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)),
            "Official capes use release chronology from newest to oldest, with stable ties");
        check(capes.First().Released.Year == 2026 && capes.Last().Id == "mojang-classic", "Newest and oldest releases anchor the gallery");
        check(capes.Single(c => c.Id == "pan").Released.Year == 2020 && capes.Single(c => c.Id == "founders").Released.Year == 2019,
            "Earlier Bedrock release dates take precedence over later Java availability");
        foreach (var cape in capes)
        {
            using var original = OfficialCapeCatalog.OpenResource(cape.Id + ".png");
            check(Convert.ToHexString(SHA256.HashData(original)).Equals((string)manifest[cape.Id].Attribute("sha256"), StringComparison.OrdinalIgnoreCase),
                cape.Name + " matches its source-verified texture checksum");
            using var image = cape.OpenTexture();
            check(image.Width >= 64 && image.Width * 32 == image.Height * 64, cape.Name + " has a complete compatible PNG texture");
            using var document = new Skin(cape);
            check(document.IsReadOnly && !document.Dirty && document.Size == image.Size && document.Parent == null,
                cape.Name + " is a read-only reference outside the library");
        }

        using var reference = new Skin(capes[0]);
        using var editable = new Skin("Editable.png");
        var history = new EditorHistory();
        history.Add(editable, new Change()); history.Undo(_ => { });
        bool Blocked(Action action) { try { action(); return false; } catch (InvalidOperationException) { return true; } }
        check(Blocked(() => history.Add(reference, new Change())) && history.CanRedo,
            "A read-only edit is rejected before it can discard the editor's redo branch");
        check(Blocked(() => reference.Resize(128, 64)) && Blocked(() => reference.ChangeName("Renamed", true)) &&
            Blocked(reference.Delete) && Blocked(() => reference.MoveTo("NeverWritten.png")) && Blocked(() => { _ = reference.File; }),
            "Official references reject resizing, renaming, deleting, moving and file access");
        reference.CommitChanges(null, false); reference.CommitChanges(null, true);
        check(!reference.Dirty, "Switching and saving cannot commit working pixels to an official reference");

        using var editor = new Editor();
        typeof(Editor).GetField("_lastSkin", Private).SetValue(editor, reference);
        foreach (var mode in new[] { ViewMode.Orthographic, ViewMode.Perspective, ViewMode.Hybrid })
        {
            typeof(Editor).GetField("_currentViewMode", Private).SetValue(editor, mode);
            check(!editor.GetPick(10, 10, out _), "Read-only reference cannot be picked for editing in " + mode);
        }
        typeof(Editor).GetField("_workspaceCape", Private).SetValue(editor, reference);
        typeof(Editor).GetMethod("RefreshWorkspaceControls", Private).Invoke(editor, null);
        var edit = (Button)typeof(Editor).GetField("_editCapeButton", Private).GetValue(editor);
        check(!edit.Enabled && edit.Text == "Read-only", "Official cape disables editing and exposes its read-only state");
        var selector = (ComboBox)typeof(Editor).GetField("_capeSelector", Private).GetValue(editor);
        check(selector.Items.Count == 1 && selector.SelectedIndex == 0,
            "Official references never appear in the custom cape dropdown, even when equipped");
        var capeName = (Label)typeof(Editor).GetField("_officialCapeLabel", Private).GetValue(editor);
        check(capeName.Text == reference.ReferenceCape.Name, "Chosen official cape remains identified outside the custom selector");
        var options = (ToolStripMenuItem)typeof(Editor).GetField("optionsToolStripMenuItem", Private).GetValue(editor);
        check(!options.DropDownItems.Cast<ToolStripItem>().Any(i => i.Name == "officialCapeSettingsMenu"),
            "Cape selection is absent from application settings");
        var toolbar = (ToolStripButton)typeof(Editor).GetField("_officialCapesButton", Private).GetValue(editor);
        check(toolbar.Available, "Official Capes button is accessible before entering combined mode");
        string previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            string file = Path.Combine(AppContext.BaseDirectory, "Models/Players/Cape.xml");
            var model = new MCSkin3D.Models.ModelFormatTCN().Load(file);
            model.File = new FileInfo(file);
            editable.Model = model; editable.Size = new Size(128, 64);
            var tree = (TreeView)typeof(Editor).GetField("treeView1", Private).GetValue(editor);
            // Populate without selecting a node or activating an OpenGL context.
            tree.Nodes.Add(editable);
            typeof(Editor).GetMethod("RefreshWorkspaceControls", Private).Invoke(editor, null);
            check(selector.Items.Count == 2 && selector.SelectedIndex == 0 && selector.Items[1].ToString().Contains("Editable"),
                "A compatible custom cape remains selectable while an official cape is equipped");
            typeof(Editor).GetField("_workspaceCape", Private).SetValue(editor, editable);
            typeof(Editor).GetMethod("RefreshWorkspaceControls", Private).Invoke(editor, null);
            check(selector.SelectedIndex == 1 && edit.Enabled && capeName.Text == "",
                "A custom cape becomes selected and editable without an official selection label");
            typeof(Editor).GetField("_workspaceCape", Private).SetValue(editor, reference);
            tree.Nodes.Remove(editable);
        }
        finally { Directory.SetCurrentDirectory(previousDirectory); }
        typeof(Editor).GetField("_lastSkin", Private).SetValue(editor, editable);
        editor.SetEditingTarget(EditingTarget.Cape);
        check(typeof(Editor).GetField("_lastSkin", Private).GetValue(editor) == editable,
            "Edit Cape cannot activate a protected reference over the editable skin");

        using var dialog = new OfficialCapeDialog();
        var list = (ListView)typeof(OfficialCapeDialog).GetField("list", Private).GetValue(dialog);
        var search = (TextBox)typeof(OfficialCapeDialog).GetField("search", Private).GetValue(dialog);
        check(list.Items.Cast<ListViewItem>().Select(i => (OfficialCape)i.Tag).SequenceEqual(capes), "Gallery initially shows all capes in chronological order");
        search.Text = "MiNeCoN";
        check(list.Items.Count == 5 && list.Items[0].Text == "MineCon 2016", "Gallery search is case-insensitive and preserves release order");
        search.Text = "not a cape";
        check(list.Items.Count == 0 && dialog.SelectedCape == null, "Empty gallery searches cannot select a stale cape");
        search.Text = "";
        dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(-30000, -30000);
        dialog.Show();
        foreach (var size in new[] { new Size(720, 580), new Size(400, 380) })
        {
            dialog.ClientSize = size; dialog.PerformLayout();
            using var screenshot = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
            screenshot.Save(Path.Combine(AppContext.BaseDirectory, "official-capes-" + size.Width + ".png"));
            check(list.Width > 300 && list.Height > 120, "Official gallery remains browsable at " + size.Width + "px");
        }
        dialog.Close();
        PreviousSettings(check);
    }

    private static void PreviousSettings(Action<bool, string> check)
    {
        string path = Path.Combine(Path.GetTempPath(), "MCSkin3D-OldCapeSettings-" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            File.WriteAllText(path, "[GlobalSettings]\nHiddenOfficialCapes=" +
                string.Join(",", OfficialCapeCatalog.All.Select(c => c.Id)) + "\nCapeAccountNickname=Test_Player\n");
            var settings = new Paril.Settings.Settings();
            settings.Structures.Add(typeof(GlobalSettings));
            settings.Load(path);
            using var gallery = new OfficialCapeDialog();
            var list = (ListView)typeof(OfficialCapeDialog).GetField("list", Private).GetValue(gallery);
            check(list.Items.Count == OfficialCapeCatalog.All.Count,
                "Upgrading from Hide All and account-sync preferences still shows the entire gallery");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private sealed class Change : IUndoable
    {
        public string Action => "Edit";
        public void Undo(object obj) { }
        public void Redo(object obj) { }
    }
}
