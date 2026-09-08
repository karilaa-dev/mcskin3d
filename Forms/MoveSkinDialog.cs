using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MCSkin3D.Languages;
using MCSkin3D.Services;

namespace MCSkin3D.Forms
{
    internal sealed class MoveSkinDialog : Form
    {
        private readonly SkinMoveSession session;
        private readonly TextBox location;
        private readonly TreeView folders;
        private readonly HashSet<TreeNode> loadedFolders = new HashSet<TreeNode>();
        private readonly Button up;
        private readonly Button move;
        private readonly Label error;
        private readonly ImageList folderImages;
        private string currentFolder;
        private bool updatingTree;

        internal MoveSkinDialog(SkinMoveSession session)
        {
            this.session = session;
            Text = L("M_MOVE_TO");
            Name = "moveSkinDialog";
            AutoScaleMode = AutoScaleMode.Font;
            Font = SystemFonts.MessageBoxFont;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(600, 440);
            MinimumSize = new Size(460, 380);
            Padding = new Padding(12);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(layout);
            layout.Controls.Add(new Label { Text = string.Format(L("D_MOVE_SKIN"), session.Skin.Name), AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(3, 0, 3, 10) }, 0, 0);

            var navigation = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0, 0, 0, 8) };
            navigation.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            navigation.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            up = ButtonFor("D_MOVE_UP", "upButton");
            up.Click += (_, _) => Navigate(Path.GetDirectoryName(currentFolder));
            navigation.Controls.Add(up, 0, 0);
            location = new TextBox { Name = "location", ReadOnly = true, Dock = DockStyle.Fill, AccessibleName = L("D_MOVE_LOCATION"), Margin = new Padding(6) };
            navigation.Controls.Add(location, 1, 0);
            var newFolder = ButtonFor("M_NEWFOLDER", "newFolderButton");
            newFolder.Click += (_, _) => CreateFolder();
            navigation.Controls.Add(newFolder, 2, 0);
            layout.Controls.Add(navigation, 0, 1);

            folderImages = new ImageList { ImageSize = new Size(20, 20), ColorDepth = ColorDepth.Depth32Bit };
            folderImages.Images.Add(Properties.Resources.Folder_32x32);
            folders = new TreeView
            {
                Name = "folders", Dock = DockStyle.Fill, HideSelection = false, ImageList = folderImages,
                ImageIndex = 0, SelectedImageIndex = 0, ShowLines = true, ShowRootLines = true,
                ShowPlusMinus = true, ShowNodeToolTips = true,
                AccessibleName = L("D_MOVE_FOLDERS")
            };
            folders.BeforeExpand += (_, e) =>
            {
                try { LoadChildren(e.Node); }
                catch (Exception ex) when (Expected(ex)) { e.Cancel = true; ShowError(ex); }
            };
            folders.BeforeSelect += (_, e) =>
            {
                if (updatingTree) return;
                if (!(e.Node.Tag is string)) { e.Cancel = true; return; }
                try { SelectDestination(e.Node); }
                catch (Exception ex) when (Expected(ex)) { e.Cancel = true; ShowError(ex); }
            };
            folders.AfterSelect += (_, e) =>
            {
                if (updatingTree) return;
                e.Node.Expand();
                e.Node.EnsureVisible();
            };
            folders.Nodes.Add(CreateFolderNode(session.RootPath, L("D_MOVE_ROOT")));
            layout.Controls.Add(folders, 0, 2);
            error = new Label { Name = "error", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, MinimumSize = new Size(0, Font.Height + 4), ForeColor = SystemColors.HotTrack, Margin = new Padding(3, 8, 3, 8) };
            layout.SizeChanged += (_, _) => error.MaximumSize = new Size(Math.Max(100, layout.ClientSize.Width - 6), 0);
            layout.Controls.Add(error, 0, 3);

            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var cancel = ButtonFor("D_MOVE_CANCEL", "cancelButton");
            cancel.DialogResult = DialogResult.Cancel;
            move = ButtonFor("D_MOVE_HERE", "moveButton");
            move.Click += (_, _) => MoveSkin();
            actions.Controls.Add(cancel);
            actions.Controls.Add(move);
            layout.Controls.Add(actions, 0, 4);
            CancelButton = cancel;
            currentFolder = session.SourceFolder;
            UpdateLocation();
            up.Enabled = !string.Equals(currentFolder, session.RootPath, StringComparison.OrdinalIgnoreCase);
            move.Enabled = false;
            // Tree navigation never moves the file; moving requires the explicit action button.
            Navigate(session.SourceFolder);
            Shown += (_, _) => folders.Focus();
        }

        private static string L(string key) => LanguageLoader.GetStringWithFallback(key);
        private static Button ButtonFor(string key, string name) => new Button
        {
            Name = name, Text = L(key), AutoSize = true, MinimumSize = new Size(85, 29), Padding = new Padding(6, 2, 6, 2)
        };

        private TreeNode CreateFolderNode(string path, string text = null)
        {
            var node = new TreeNode(text ?? Path.GetFileName(path))
            {
                Name = path, Tag = path,
                ToolTipText = RelativeLocation(path)
            };
            // Load only expanded branches, including when a library contains junctions.
            node.Nodes.Add(new TreeNode());
            return node;
        }

        private void LoadChildren(TreeNode node, bool refresh = false)
        {
            if (!refresh && loadedFolders.Contains(node)) return;
            string[] children = session.GetFolders((string)node.Tag);
            var paths = new HashSet<string>(children, StringComparer.OrdinalIgnoreCase);
            bool previousUpdating = updatingTree;
            updatingTree = true;
            folders.BeginUpdate();
            try
            {
                foreach (TreeNode child in node.Nodes.Cast<TreeNode>().ToArray())
                    if (!(child.Tag is string path) || !paths.Contains(path)) child.Remove();
                for (int index = 0; index < children.Length; index++)
                    if (!node.Nodes.ContainsKey(children[index])) node.Nodes.Insert(index, CreateFolderNode(children[index]));
                loadedFolders.Add(node);
            }
            finally { folders.EndUpdate(); updatingTree = previousUpdating; }
        }

        private string RelativeLocation(string path)
        {
            string relativePath = Path.GetRelativePath(session.RootPath, path);
            return relativePath == "." ? L("D_MOVE_ROOT") : Path.Combine(L("D_MOVE_ROOT"), relativePath);
        }

        private void UpdateLocation()
        {
            location.Text = RelativeLocation(currentFolder);
            location.SelectionStart = 0;
        }

        private void SelectDestination(TreeNode node)
        {
            string path = session.ValidateDirectory((string)node.Tag);
            bool canMove = session.CanMoveTo(path);
            LoadChildren(node);
            currentFolder = path;
            UpdateLocation();
            up.Enabled = !string.Equals(currentFolder, session.RootPath, StringComparison.OrdinalIgnoreCase);
            move.Enabled = canMove;
            error.Text = node.Nodes.Count == 0 ? L("D_MOVE_EMPTY") : "";
        }

        private void Navigate(string path)
        {
            try
            {
                path = session.ValidateDirectory(path);
                TreeNode node = folders.Nodes[0];
                string relative = Path.GetRelativePath(session.RootPath, path);
                if (relative != ".")
                    foreach (string part in relative.Split(Path.DirectorySeparatorChar))
                    {
                        LoadChildren(node);
                        string childPath = Path.Combine((string)node.Tag, part);
                        if (!node.Nodes.ContainsKey(childPath)) LoadChildren(node, true);
                        node.Expand();
                        node = node.Nodes[childPath] ?? throw new DirectoryNotFoundException(L("D_MOVE_MISSING"));
                    }
                SelectDestination(node);
                updatingTree = true;
                try
                {
                    folders.SelectedNode = node;
                    node.Expand();
                    node.EnsureVisible();
                }
                finally { updatingTree = false; }
                folders.Focus();
            }
            catch (Exception ex) when (Expected(ex)) { ShowError(ex); }
        }

        private void CreateFolder()
        {
            string created = null;
            using (var prompt = new NamePrompt(L("M_NEWFOLDER"), L("D_MOVE_FOLDER_NAME"), L("M_NEWFOLDER"), name => created = session.CreateFolder(currentFolder, name)))
                if (prompt.ShowDialog(this) == DialogResult.OK) Navigate(created);
        }

        private void MoveSkin()
        {
            string name = session.Skin.Name;
            bool rename = false;
            while (true)
            {
                try
                {
                    string destination = session.DestinationFile(currentFolder, name);
                    if (rename || SkinMoveSession.Exists(destination))
                    {
                        using (var prompt = new NamePrompt(L("M_MOVE_TO"), L("D_MOVE_RENAME"), name, candidate =>
                        {
                            string file = session.DestinationFile(currentFolder, candidate);
                            if (SkinMoveSession.Exists(file)) throw new IOException(L("D_MOVE_NAME_EXISTS"));
                            name = candidate;
                        }))
                            if (prompt.ShowDialog(this) != DialogResult.OK) return;
                    }
                    session.Move(currentFolder, name);
                    DialogResult = DialogResult.OK;
                    Close();
                    return;
                }
                catch (SkinMoveConflictException)
                {
                    // A new collision between validation and File.Move also needs a new name.
                    rename = true;
                }
                catch (Exception ex) when (Expected(ex)) { ShowError(ex); return; }
            }
        }

        private static bool Expected(Exception ex) => ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException;
        private void ShowError(Exception ex) => error.Text = string.Format(L("D_MOVE_ERROR"), ex.Message);

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                folderImages?.Dispose();
            }
        }

        private sealed class NamePrompt : Form
        {
            internal NamePrompt(string title, string message, string initial, Action<string> submit)
            {
                Text = title;
                Name = "moveNamePrompt";
                AutoScaleMode = AutoScaleMode.Font;
                Font = SystemFonts.MessageBoxFont;
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MinimizeBox = MaximizeBox = false;
                ShowInTaskbar = false;
                ClientSize = new Size(440, 215);
                Padding = new Padding(12);
                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                Controls.Add(layout);
                layout.Controls.Add(new Label { Text = message, AutoSize = true, Dock = DockStyle.Fill, MaximumSize = new Size(410, 0) }, 0, 0);
                var input = new TextBox { Name = "nameInput", Text = initial, Dock = DockStyle.Fill, AccessibleName = message };
                layout.Controls.Add(input, 0, 1);
                var validation = new Label { Name = "validation", AutoSize = true, Dock = DockStyle.Fill, MaximumSize = new Size(410, 0), ForeColor = SystemColors.HotTrack };
                layout.Controls.Add(validation, 0, 2);
                var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
                var cancel = ButtonFor("D_MOVE_CANCEL", "cancelButton");
                cancel.DialogResult = DialogResult.Cancel;
                var ok = ButtonFor("D_MOVE_OK", "okButton");
                ok.Click += (_, _) =>
                {
                    try { submit(input.Text); DialogResult = DialogResult.OK; Close(); }
                    catch (Exception ex) when (Expected(ex)) { validation.Text = ex.Message; input.Focus(); input.SelectAll(); }
                };
                actions.Controls.Add(cancel);
                actions.Controls.Add(ok);
                layout.Controls.Add(actions, 0, 3);
                AcceptButton = ok;
                CancelButton = cancel;
                Shown += (_, _) => { input.Focus(); input.SelectAll(); };
            }
        }
    }
}
