using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MCSkin3D;
using MCSkin3D.Controls;
using MCSkin3D.Forms;
using MCSkin3D.Languages;
using MCSkin3D.Services;

internal static class SkinMoveChecks
{
    internal static void Run(Action<bool, string> check, string scratch)
    {
        var savedDirectories = GlobalSettings.SkinDirectories;
        string root = Path.Combine(scratch, "move-library");
        Directory.CreateDirectory(root);
        try
        {
            GlobalSettings.SkinDirectories = new[] { root + Path.DirectorySeparatorChar };
            using var tree = new SkinTreeView();
            string source = Path.Combine(root, "Explorer.png");
            File.WriteAllText(source, "original pixels and metadata");
            using var skin = new Skin(source) { Dirty = true, Head = new Bitmap(32, 32) };
            var undo = skin.Undo;
            tree.Nodes.Add(skin);
            tree.SelectedNode = skin;
            var session = new SkinMoveSession(tree, skin);
            string destination = session.CreateFolder(root, "Destination");
            session.CreateFolder(root, "Alpha");
            check(session.GetFolders(root).Select(Path.GetFileName).SequenceEqual(new[] { "Alpha", "Destination" }), "Browser lists only folders alphabetically");
            check(!session.CanMoveTo(root) && session.CanMoveTo(destination), "Source folder disables moving");
            session.Move(root, skin.Name);
            check(File.Exists(source) && tree.SelectedNode == skin, "Same-folder request is harmless");

            foreach (string name in new[] { "", " ", "..", "../escape", "a/b", "a\\b", "a:", "CON", "nul.txt", "LPT1", "trailing.", " trailing", "trailing ", new string('x', 252) })
                Reject(() => session.DestinationFile(destination, name), check, "Invalid skin name: " + name);
            Reject(() => session.CreateFolder(root, "../escape"), check, "New folder cannot escape root");
            Reject(() => session.CreateFolder(root, "Destination"), check, "Existing folder name rejected");
            Reject(() => session.ValidateDirectory(scratch), check, "Parent outside library rejected");
            string sibling = root + "-outside";
            Directory.CreateDirectory(sibling);
            Reject(() => session.ValidateDirectory(sibling), check, "Prefix sibling outside library rejected");
            string outsideLink = Path.Combine(root, "Outside link");
            string insideLink = Path.Combine(root, "Inside link");
            CreateJunction(outsideLink, sibling);
            CreateJunction(insideLink, destination);
            try
            {
                Reject(() => session.ValidateDirectory(outsideLink), check, "Junction outside library rejected");
                check(!session.GetFolders(root).Contains(outsideLink), "Outside junction omitted from browser");
                check(session.ValidateDirectory(insideLink) == insideLink, "Junction inside library is browsable");
            }
            finally
            {
                // Remove only the junction entries, never recurse into their targets.
                Directory.Delete(outsideLink);
                Directory.Delete(insideLink);
            }
            Reject(() => session.Move(Path.Combine(root, "deleted"), "Explorer"), check, "Missing destination rejected");
            check(File.Exists(source) && skin.Parent == null && skin.Dirty, "Failed move leaves source and edits unchanged");

            string conflict = Path.Combine(destination, "Explorer.png");
            File.WriteAllText(conflict, "existing destination");
            Reject(() => session.Move(destination, "Explorer"), check, "Collision never overwrites destination");
            check(File.ReadAllText(conflict) == "existing destination" && File.Exists(source), "Collision preserves both files");
            using (var locked = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None))
                Reject(() => session.Move(destination, "Renamed"), check, "Locked source leaves tree unchanged");
            check(skin.Parent == null && skin.Name == "Explorer" && tree.SelectedNode == skin, "Failure retains name, parent and selection");

            // Canceling the browser does not move a skin; folders created beforehand stay in both views.
            using (var dialog = new MoveSkinDialog(session))
            {
                dialog.Show();
                Application.DoEvents();
                check(!Find<Button>(dialog, "moveButton").Enabled && !Find<Button>(dialog, "upButton").Enabled, "Dialog starts at library root with Move and Up disabled");
                check(Find<TextBox>(dialog, "location").ReadOnly, "Destination location is read-only");
                check(Find<TextBox>(dialog, "location").Text == "Skins" && Find<TreeView>(dialog, "folders").SelectedNode.Text == "Skins", "Library root has a relative path and highlighted tree node");
                dialog.Close();
            }
            check(File.Exists(source) && Directory.Exists(destination) && tree.Nodes.Cast<TreeNode>().Any(n => n.Name == "Destination"), "Cancel preserves source and already-created folders");
            CheckBrowser(tree, session, destination, check);

            session.Move(destination, "Renamed");
            check(!File.Exists(source) && File.ReadAllText(skin.File.FullName) == "original pixels and metadata", "Renamed move preserves file contents");
            check(skin.Name == "Renamed" && skin.Parent.Name == "Destination" && tree.SelectedNode == skin && ReferenceEquals(skin.Undo, undo) && skin.Dirty, "Move preserves skin instance, edits, undo and selection");
            var moved = new SkinMoveSession(tree, skin);
            using (var nestedDialog = new MoveSkinDialog(moved))
            {
                nestedDialog.Show();
                var folderTree = Find<TreeView>(nestedDialog, "folders");
                check(folderTree.SelectedNode.Text == "Destination" && folderTree.SelectedNode.Parent.Text == "Skins" && folderTree.SelectedNode.Parent.IsExpanded, "Opening a nested source folder reveals its ancestors and selects it");
                check(!Find<Button>(nestedDialog, "moveButton").Enabled, "Nested source folder cannot move to itself");
                nestedDialog.Close();
            }
            moved.Move(root, "Returned");
            check(skin.Parent == null && skin.Name == "Returned" && skin.File.Exists, "Move back to library root");

            // Existing drag-and-drop keeps automatic renaming, including repeated conflicts.
            File.WriteAllText(Path.Combine(destination, "Returned.png"), "do not overwrite");
            string suffix = " - " + LanguageLoader.GetStringWithFallback("C_MOVED");
            File.WriteAllText(Path.Combine(destination, "Returned" + suffix + ".png"), "also preserve");
            var destinationNode = tree.Nodes.Cast<TreeNode>().Single(n => n.Name == "Destination");
            tree.MoveSkin(skin, destinationNode.Nodes, Path.Combine(destination, "Returned.png"), true);
            check(skin.Name == "Returned" + suffix + suffix && skin.File.Exists, "Automatic move suffix updates the skin's filename");

            // External folders become usable without rebuilding or replacing loaded skins.
            string external = Path.Combine(root, "External", "Nested");
            Directory.CreateDirectory(external);
            new SkinMoveSession(tree, skin).Move(external, "ExternalSkin");
            check(skin.Parent.Name == "Nested" && skin.Parent.Parent.Name == "External" && skin.File.Exists, "Externally-created destination gets synchronized into tree");
            string externalSource = skin.File.FullName;
            File.Delete(externalSource);
            Reject(() => new SkinMoveSession(tree, skin).Move(root, "MissingSource"), check, "Missing source fails without changing tree");
            check(skin.Parent.Name == "Nested" && skin.Name == "ExternalSkin", "Missing source retains node location");

            // Both roots have the same leaf name: path resolution must use the captured absolute root.
            string secondRoot = Path.Combine(scratch, "second", "move-library");
            Directory.CreateDirectory(secondRoot);
            GlobalSettings.SkinDirectories = new[] { root, secondRoot };
            using var multiTree = new SkinTreeView();
            var firstNode = new FolderNode("move-library", root);
            var secondNode = new FolderNode("move-library", secondRoot);
            multiTree.Nodes.AddRange(new TreeNode[] { firstNode, secondNode });
            string multiSource = Path.Combine(secondRoot, "Multi.png");
            File.WriteAllText(multiSource, "second library");
            using var multiSkin = new Skin(multiSource) { Head = new Bitmap(32, 32) };
            secondNode.Nodes.Add(multiSkin);
            var multi = new SkinMoveSession(multiTree, multiSkin);
            check(multi.RootPath == secondRoot && multiSkin.File.FullName == multiSource, "Multiple libraries retain their absolute root paths");
            Reject(() => multi.Move(root, "Multi"), check, "Move cannot cross library roots");
            string child = multi.CreateFolder(secondRoot, "Child");
            multi.Move(child, "Multi");
            check(multiSkin.File.Exists && multiSkin.Parent.Parent == secondNode, "Move inside second library succeeds");
        }
        finally { GlobalSettings.SkinDirectories = savedDirectories; }
    }

    private static T Find<T>(Control control, string name) where T : Control => (T)control.Controls.Find(name, true).Single();

    private static void CheckBrowser(SkinTreeView tree, SkinMoveSession session, string destination, Action<bool, string> check)
    {
        using var dialog = new MoveSkinDialog(session);
        dialog.Show();
        Application.DoEvents();
        string snapshots = Path.Combine(AppContext.BaseDirectory, "move-dialog-checks");
        Directory.CreateDirectory(snapshots);
        Capture(dialog, Path.Combine(snapshots, "normal.png"));
        var folders = Find<TreeView>(dialog, "folders");
        folders.SelectedNode = folders.Nodes[0].Nodes.Cast<TreeNode>().Single(n => n.Text == "Destination");
        check(Find<TextBox>(dialog, "location").Text == Path.Combine("Skins", "Destination") && Find<Button>(dialog, "moveButton").Enabled, "Folder activation opens destination and enables Move here");
        check(tree.SelectedNode == session.Skin, "Browsing does not change editor selection");
        var sibling = folders.Nodes[0].Nodes.Cast<TreeNode>().Single(n => n.Text == "Alpha");
        check(sibling.IsVisible, "Sibling folders remain visible while selecting a destination");
        sibling.Expand();
        check(folders.SelectedNode.Text == "Destination" && Find<TextBox>(dialog, "location").Text == Path.Combine("Skins", "Destination"), "Expanding a different branch does not change the destination");
        check(folders.SelectedNode.Text == "Destination" && folders.SelectedNode.Parent == folders.Nodes[0] && folders.Nodes[0].IsExpanded, "Selected destination remains visible beneath its expanded parent");

        // A canceled rename returns to the same browser; invalid input stays in the prompt.
        WithPrompt(() => Find<Button>(dialog, "moveButton").PerformClick(), prompt =>
        {
            Find<TextBox>(prompt, "nameInput").Text = "CON";
            Find<Button>(prompt, "okButton").PerformClick();
            check(prompt.Visible && Find<Label>(prompt, "validation").Text.Length > 0, "Invalid replacement name keeps prompt open with validation");
            Capture(prompt, Path.Combine(snapshots, "rename-validation.png"));
            Find<TextBox>(prompt, "nameInput").Text = session.Skin.Name;
            Find<Button>(prompt, "okButton").PerformClick();
            check(prompt.Visible && Find<Label>(prompt, "validation").Text == LanguageLoader.GetStringWithFallback("D_MOVE_NAME_EXISTS"), "Conflicting replacement name remains rejected");
            Find<Button>(prompt, "cancelButton").PerformClick();
        });
        check(dialog.Visible && Find<TextBox>(dialog, "location").Text == Path.Combine("Skins", "Destination") && session.Skin.Parent == null, "Cancel rename returns to browser without moving");

        WithPrompt(() => Find<Button>(dialog, "newFolderButton").PerformClick(), prompt =>
        {
            Find<TextBox>(prompt, "nameInput").Text = "Created in dialog";
            Find<Button>(prompt, "okButton").PerformClick();
        });
        string created = Path.Combine(destination, "Created in dialog");
        check(folders.SelectedNode.Text == "Created in dialog" && folders.SelectedNode.Parent.Text == "Destination" && folders.SelectedNode.Parent.IsExpanded, "New destination is selected beneath its expanded ancestors");
        check(Find<Label>(dialog, "error").Text == LanguageLoader.GetStringWithFallback("D_MOVE_EMPTY"), "Empty folder message is localized");
        check(Directory.Exists(created) && Find<TextBox>(dialog, "location").Text == Path.Combine("Skins", "Destination", "Created in dialog") && tree.Nodes.Find("Created in dialog", true).Length == 1, "New folder is created, synchronized and opened");
        dialog.Size = dialog.MinimumSize;
        Application.DoEvents();
        Capture(dialog, Path.Combine(snapshots, "compact.png"));
        CheckLayout(dialog, check);
        dialog.Scale(new SizeF(1.5f, 1.5f));
        Application.DoEvents();
        Capture(dialog, Path.Combine(snapshots, "scaled.png"));
        CheckLayout(dialog, check);

        // Revalidation happens when Move is clicked, even if the destination disappeared.
        Directory.Delete(created);
        Find<Button>(dialog, "moveButton").PerformClick();
        check(dialog.Visible && Find<Label>(dialog, "error").Text.Contains(LanguageLoader.GetStringWithFallback("D_MOVE_MISSING")), "Removed destination displays an error and leaves dialog open");
        Find<Button>(dialog, "upButton").PerformClick();
        check(Find<TextBox>(dialog, "location").Text == Path.Combine("Skins", "Destination"), "Up recovers from a removed destination");
        check(folders.SelectedNode.Text == "Destination", "Up selects the parent in the tree");
        Find<Button>(dialog, "cancelButton").PerformClick();

        // Complete a real conflict prompt on a separate skin, preserving the main fixture.
        string file = Path.Combine(session.RootPath, "Prompt.png");
        File.WriteAllText(file, "prompt source");
        File.WriteAllText(Path.Combine(destination, "Prompt.png"), "keep existing");
        using var skin = new Skin(file) { Head = new Bitmap(32, 32) };
        tree.Nodes.Add(skin);
        using var moveDialog = new MoveSkinDialog(new SkinMoveSession(tree, skin));
        moveDialog.Show();
        var moveFolders = Find<TreeView>(moveDialog, "folders");
        moveFolders.SelectedNode = moveFolders.Nodes[0].Nodes.Cast<TreeNode>().Single(n => n.Text == "Destination");
        WithPrompt(() => Find<Button>(moveDialog, "moveButton").PerformClick(), prompt =>
        {
            Find<TextBox>(prompt, "nameInput").Text = "Prompt renamed";
            Find<Button>(prompt, "okButton").PerformClick();
        });
        check(moveDialog.DialogResult == DialogResult.OK && !File.Exists(file) && skin.Name == "Prompt renamed" && skin.File.Exists, "Conflict prompt completes move using entered basename");
        check(File.ReadAllText(Path.Combine(destination, "Prompt.png")) == "keep existing", "Dialog move preserves existing destination skin");
        skin.Remove();
        tree.SelectedNode = session.Skin;
        Console.WriteLine("Move dialog render artifacts: " + snapshots);
    }

    private static void WithPrompt(Action open, Action<Form> interact)
    {
        Exception failure = null;
        bool found = false;
        using var timer = new System.Windows.Forms.Timer { Interval = 50 };
        timer.Tick += (_, _) =>
        {
            var prompt = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Name == "moveNamePrompt");
            if (prompt == null) return;
            timer.Stop();
            found = true;
            try { interact(prompt); }
            catch (Exception ex) { failure = ex; prompt.Close(); }
        };
        timer.Start();
        open();
        if (failure != null) throw failure;
        if (!found) throw new InvalidOperationException("Expected name prompt to open");
    }

    private static void Capture(Form dialog, string path)
    {
        using var bitmap = new Bitmap(dialog.Width, dialog.Height);
        dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
        bitmap.Save(path);
    }

    private static void CheckLayout(Form dialog, Action<bool, string> check)
    {
        foreach (string name in new[] { "location", "folders", "upButton", "moveButton", "cancelButton", "newFolderButton" })
        {
            var control = Find<Control>(dialog, name);
            check(control.Parent.ClientRectangle.Contains(control.Bounds), name + " stays inside its layout at " + dialog.Size);
        }
        check(Find<TreeView>(dialog, "folders").Height >= 80, "Folder list remains usable at compact and scaled sizes");
        var status = Find<Label>(dialog, "error");
        check(dialog.Controls.Find("currentFolderName", true).Length == 0, "Redundant current folder heading is removed");
        check(status.Height >= status.PreferredHeight, "Status message remains visible after resizing");
        var folders = Find<TreeView>(dialog, "folders");
        check(folders.SelectedNode != null && !folders.HideSelection && folders.ShowLines, "Folder tree keeps its selection and hierarchy visible");
    }

    private static void CreateJunction(string path, string target)
    {
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("$ErrorActionPreference = 'Stop'; New-Item -ItemType Junction -Path '" + path.Replace("'", "''") + "' -Target '" + target.Replace("'", "''") + "' | Out-Null");
        using var process = Process.Start(start);
        string errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Could not create test junction: " + errors);
    }
    private static void Reject(Action action, Action<bool, string> check, string message)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException) { check(true, message); return; }
        check(false, message);
    }

    // Optional isolated dialog for live mouse/keyboard and layout verification.
    internal static void ShowInteractive(string scratch)
    {
        string root = Path.Combine(scratch, "Skins");
        Directory.CreateDirectory(Path.Combine(root, "Characters"));
        Directory.CreateDirectory(Path.Combine(root, "Favorites"));
        Directory.CreateDirectory(Path.Combine(root, "Projects", "Work in progress"));
        GlobalSettings.SkinDirectories = new[] { root + Path.DirectorySeparatorChar };
        using var tree = new SkinTreeView();
        string source = Path.Combine(root, "Explorer.png");
        using (var png = new Bitmap(64, 64)) png.Save(source);
        File.Copy(source, Path.Combine(root, "Favorites", "Explorer.png"));
        using var skin = new Skin(source) { Head = new Bitmap(32, 32) };
        tree.Nodes.Add(skin);
        tree.SelectedNode = skin;
        using var dialog = new MoveSkinDialog(new SkinMoveSession(tree, skin));
        Application.Run(dialog);
        Console.WriteLine("Dialog result: " + dialog.DialogResult + "; skin: " + skin.File.FullName);
    }
}
