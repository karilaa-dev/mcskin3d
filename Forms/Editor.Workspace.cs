using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MCSkin3D.Languages;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using Paril.Components;
using Paril.Drawing;
using Paril.OpenGL;

namespace MCSkin3D
{
    public partial class Editor
    {
        public EditorHistory History { get; } = new EditorHistory();
        public bool CombinedEnabled { get; private set; }
        public EditingTarget ActiveEditingTarget { get; private set; }
        public AccessoryView AccessoryPresentation { get; private set; }
        public float WingSpread { get; private set; }
        private Skin _workspaceSkin, _workspaceCape;
        private Model _playerPreview, _playerSource, _capePreview, _elytraPreview;
        private readonly List<PreviewItem> _scene = new List<PreviewItem>();
        private RectangleF _skinSheet, _capeSheet;
        private bool _syncWorkspace, _activatingDocument;
        private MouseButtons _strokeButton;
        private FlowLayoutPanel _workspaceBar;
        private ComboBox _skinSelector, _capeSelector;
        private ToolStripButton _combinedButton;
        private ToolStripMenuItem _combinedMenu;
        private ToolStripButton _officialCapesButton;
        private Label _officialCapeLabel;
        private readonly Dictionary<string, Skin> _officialCapeDocuments = new Dictionary<string, Skin>();
        private readonly Dictionary<Button, string> _workspaceButtons = new Dictionary<Button, string>();
        private readonly Dictionary<Label, string> _workspaceLabels = new Dictionary<Label, string>();
        private Button _editSkinButton, _editCapeButton, _capeButton, _elytraButton;
        private FlowLayoutPanel _wingSpreadGroup;
        private TrackBar _wingSpreadSlider;

        private sealed class LibraryChoice
        {
            public Skin Document;
            public override string ToString() => Document == null ? LanguageLoader.GetStringWithFallback("W_SELECT") :
                Document.FullPath + (Document.Dirty ? " *" : "");
        }

        private void InitializeWorkspace()
        {
            _combinedButton = new ToolStripButton { Name = "combinedPreviewButton", DisplayStyle = ToolStripItemDisplayStyle.Text };
            _combinedMenu = new ToolStripMenuItem { Name = "combinedPreviewMenu" };
            _combinedButton.Click += (_, _) => SetCombinedMode(!CombinedEnabled);
            _combinedMenu.Click += (_, _) => SetCombinedMode(!CombinedEnabled);
            toolStrip1.Items.Add(new ToolStripSeparator());
            toolStrip1.Items.Add(_combinedButton);
            viewToolStripMenuItem.DropDownItems.Add(_combinedMenu);
            _officialCapesButton = new ToolStripButton { Name = "officialCapesButton", DisplayStyle = ToolStripItemDisplayStyle.Text };
            _officialCapesButton.Click += (_, _) => ShowOfficialCapes();
            toolStrip1.Items.Add(_officialCapesButton);
            _workspaceBar = new FlowLayoutPanel
            {
                Name = "workspaceBar", Dock = DockStyle.Top, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
                Padding = new Padding(4), Visible = false
            };
            _skinSelector = AddLibrarySelector(EditingTarget.Skin);
            _capeSelector = AddLibrarySelector(EditingTarget.Cape);
            AddWorkspaceButton("W_OFFICIAL_CAPES", ShowOfficialCapes);
            _officialCapeLabel = new Label { AutoSize = true, MaximumSize = new Size(180, 0), Margin = new Padding(4, 7, 4, 0) };
            _workspaceBar.Controls.Add(_officialCapeLabel);
            _editSkinButton = AddWorkspaceButton("W_EDIT_SKIN", () => SetEditingTarget(EditingTarget.Skin));
            _editCapeButton = AddWorkspaceButton("W_EDIT_CAPE", () => SetEditingTarget(EditingTarget.Cape));
            _capeButton = AddWorkspaceButton("W_CAPE", () => SetAccessoryView(AccessoryView.Cape));
            _elytraButton = AddWorkspaceButton("W_ELYTRA", () => SetAccessoryView(AccessoryView.Elytra));
            GroupWorkspaceButtons(_editSkinButton, _editCapeButton);
            GroupWorkspaceButtons(_capeButton, _elytraButton);
            _wingSpreadGroup = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false, Margin = new Padding(0) };
            var folded = new Label { AutoSize = true, Margin = new Padding(4, 7, 0, 0) };
            var spread = new Label { AutoSize = true, Margin = new Padding(0, 7, 4, 0) };
            _workspaceLabels.Add(folded, "W_FOLDED");
            _workspaceLabels.Add(spread, "W_SPREAD");
            _wingSpreadSlider = new TrackBar { Name = "wingSpreadSlider", Minimum = 0, Maximum = 100,
                SmallChange = 1, LargeChange = 10, TickStyle = TickStyle.None, AutoSize = false,
                Size = new Size(140, 28), Margin = new Padding(0) };
            _wingSpreadSlider.ValueChanged += (_, _) =>
            {
                if (!_syncWorkspace) SetWingSpread(_wingSpreadSlider.Value / 100f);
            };
            _wingSpreadGroup.Controls.Add(folded);
            _wingSpreadGroup.Controls.Add(_wingSpreadSlider);
            _wingSpreadGroup.Controls.Add(spread);
            _workspaceBar.Controls.Add(_wingSpreadGroup);
            panel4.Controls.Add(_workspaceBar);
            // Place the wrapping bar below the existing viewport toolbar, including in the popout.
            panel4.Controls.SetChildIndex(_workspaceBar, panel4.Controls.IndexOf(toolStrip1));
            RefreshWorkspaceControls();
        }

        private void GroupWorkspaceButtons(params Button[] buttons)
        {
            var group = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false, Margin = new Padding(0) };
            foreach (var button in buttons) group.Controls.Add(button);
            _workspaceBar.Controls.Add(group);
        }

        private Button AddWorkspaceButton(string key, Action action)
        {
            var button = new Button { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Standard };
            button.Click += (_, _) => action();
            _workspaceButtons.Add(button, key);
            _workspaceBar.Controls.Add(button);
            return button;
        }

        private ComboBox AddLibrarySelector(EditingTarget target)
        {
            var group = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false, Margin = new Padding(0) };
            var label = new Label { AutoSize = true, Margin = new Padding(4, 7, 2, 0) };
            _workspaceLabels.Add(label, target == EditingTarget.Skin ? "W_SKIN" : "W_CAPE");
            group.Controls.Add(label);
            var selector = new ComboBox { Width = 180, DropDownWidth = 340, DropDownStyle = ComboBoxStyle.DropDownList };
            selector.DropDown += (_, _) => RefreshLibraryChoices(selector, target);
            selector.SelectionChangeCommitted += (_, _) =>
            {
                if (!_syncWorkspace && selector.SelectedItem is LibraryChoice choice)
                    SetWorkspaceDocument(target, choice.Document);
            };
            group.Controls.Add(selector);
            _workspaceBar.Controls.Add(group);
            return selector;
        }

        private IEnumerable<Skin> LibraryDocuments(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
                if (node is Skin skin) yield return skin;
                else foreach (var child in LibraryDocuments(node.Nodes)) yield return child;
        }

        private void RefreshLibraryChoices(ComboBox selector, EditingTarget target)
        {
            _syncWorkspace = true;
            try
            {
                var selected = target == EditingTarget.Skin ? _workspaceSkin : _workspaceCape;
                selector.Items.Clear();
                selector.Items.Add(new LibraryChoice());
                foreach (var document in LibraryDocuments(treeView1.Nodes).Where(s => !s.IsReadOnly && PreviewItem.Role(s) == target))
                    selector.Items.Add(new LibraryChoice { Document = document });
                selector.SelectedIndex = 0;
                for (int i = 1; i < selector.Items.Count; ++i)
                {
                    var choice = (LibraryChoice)selector.Items[i];
                    if (selected != null && choice.Document == selected) selector.SelectedIndex = i;
                }
            }
            finally { _syncWorkspace = false; }
        }

        private void RefreshWorkspaceControls()
        {
            if (_workspaceBar == null) return;
            _combinedButton.Text = _combinedMenu.Text = LanguageLoader.GetStringWithFallback("W_COMBINED");
            _officialCapesButton.Text = LanguageLoader.GetStringWithFallback("W_OFFICIAL_CAPES");
            _officialCapesButton.Visible = !CombinedEnabled;
            _officialCapeLabel.Text = _workspaceCape?.ReferenceCape?.Name ?? "";
            _officialCapeLabel.Visible = _workspaceCape?.IsReadOnly == true;
            _combinedButton.Checked = _combinedMenu.Checked = CombinedEnabled;
            _workspaceBar.Visible = CombinedEnabled;
            foreach (var pair in _workspaceLabels) pair.Key.Text = LanguageLoader.GetStringWithFallback(pair.Value);
            foreach (var pair in _workspaceButtons) pair.Key.Text = LanguageLoader.GetStringWithFallback(pair.Value);
            _editSkinButton.Enabled = _workspaceSkin != null;
            _editCapeButton.Enabled = _workspaceCape != null && !_workspaceCape.IsReadOnly;
            if (_workspaceCape?.IsReadOnly == true) _editCapeButton.Text = LanguageLoader.GetStringWithFallback("W_READ_ONLY");
            _editSkinButton.BackColor = ActiveEditingTarget == EditingTarget.Skin ? SystemColors.GradientActiveCaption : SystemColors.Control;
            _editCapeButton.BackColor = ActiveEditingTarget == EditingTarget.Cape ? SystemColors.GradientActiveCaption : SystemColors.Control;
            _capeButton.BackColor = AccessoryPresentation == AccessoryView.Cape ? SystemColors.GradientActiveCaption : SystemColors.Control;
            _elytraButton.BackColor = AccessoryPresentation == AccessoryView.Elytra ? SystemColors.GradientActiveCaption : SystemColors.Control;
            _wingSpreadGroup.Visible = AccessoryPresentation == AccessoryView.Elytra;
            _wingSpreadSlider.AccessibleName = LanguageLoader.GetStringWithFallback("W_WING_SPREAD");
            _syncWorkspace = true;
            try { _wingSpreadSlider.Value = (int)Math.Round(WingSpread * 100); }
            finally { _syncWorkspace = false; }
            RefreshLibraryChoices(_skinSelector, EditingTarget.Skin);
            RefreshLibraryChoices(_capeSelector, EditingTarget.Cape);
        }

        public void SetCombinedMode(bool enabled)
        {
            FinishStroke();
            CombinedEnabled = enabled;
            if (enabled && PreviewItem.Role(_lastSkin) is EditingTarget role)
            {
                if (role == EditingTarget.Skin) _workspaceSkin = _lastSkin;
                else _workspaceCape = _lastSkin;
                ActiveEditingTarget = role;
            }
            else if (enabled)
            {
                ActivateDocument(_workspaceSkin ?? _workspaceCape, false);
                ActiveEditingTarget = _workspaceSkin != null ? EditingTarget.Skin : EditingTarget.Cape;
            }
            if (!enabled && _lastSkin?.IsReadOnly == true) ActivateDocument(_workspaceSkin, false);
            RebuildScene();
            RefreshWorkspaceControls();
            FitWorkspace();
            RefreshEditingView();
        }

        private Skin GetOfficialCapeDocument(OfficialCape cape)
        {
            if (!_officialCapeDocuments.TryGetValue(cape.Id, out var document))
                _officialCapeDocuments.Add(cape.Id, document = new Skin(cape));
            return document;
        }

        public void PreviewOfficialCape(OfficialCape cape)
        {
            ArgumentNullException.ThrowIfNull(cape);
            if (!CombinedEnabled) SetCombinedMode(true);
            SetWorkspaceDocument(EditingTarget.Cape, GetOfficialCapeDocument(cape));
        }

        private void ShowOfficialCapes()
        {
            using var dialog = new Forms.OfficialCapeDialog(_workspaceCape?.ReferenceCape);
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedCape != null)
                PreviewOfficialCape(dialog.SelectedCape);
        }

        private void DisposeOfficialCapes()
        {
            if (_officialCapeDocuments.Values.Any(d => d.GLImage != null)) Renderer.MakeCurrent();
            foreach (var document in _officialCapeDocuments.Values) document.Dispose();
            _officialCapeDocuments.Clear();
        }

        public void SetWorkspaceDocument(EditingTarget target, Skin document)
        {
            if (document != null && PreviewItem.Role(document) != target) throw new ArgumentException("Incompatible preview document.", nameof(document));
            FinishStroke();
            if (_lastSkin != null) _lastSkin.CommitChanges(GlobalDirtiness.CurrentSkin, false);
            var previous = target == EditingTarget.Skin ? _workspaceSkin : _workspaceCape;
            if (target == EditingTarget.Skin) _workspaceSkin = document;
            else _workspaceCape = document;
            if (ActiveEditingTarget == target || _lastSkin == null || (_lastSkin.IsReadOnly && target == EditingTarget.Skin && document != null))
            {
                ActiveEditingTarget = target;
                var active = document?.IsReadOnly == true ? _workspaceSkin ?? document :
                    document ?? (target == EditingTarget.Skin ? _workspaceCape : _workspaceSkin);
                ActivateDocument(active, false);
                if (_lastSkin != null) ActiveEditingTarget = PreviewItem.Role(_lastSkin).Value;
            }
            RebuildScene();
            ReleaseUnpinnedTexture(previous);
            RefreshEditingView();
        }

        public void SetEditingTarget(EditingTarget target)
        {
            var document = target == EditingTarget.Skin ? _workspaceSkin : _workspaceCape;
            if (document == null || document.IsReadOnly) return;
            ActiveEditingTarget = target;
            ActivateDocument(document, false);
        }

        public void SetAccessoryView(AccessoryView view)
        {
            FinishStroke();
            AccessoryPresentation = view;
            RebuildScene();
            RefreshEditingView();
        }

        public void SetWingSpread(float spread)
        {
            if (!float.IsFinite(spread)) throw new ArgumentOutOfRangeException(nameof(spread));
            spread = Math.Clamp(spread, 0, 1);
            if (WingSpread == spread) return;
            FinishStroke();
            WingSpread = spread;
            if (CombinedEnabled && AccessoryPresentation == AccessoryView.Elytra)
                foreach (var item in _scene)
                    if (item.Document == _workspaceCape)
                        PreviewItem.PoseAccessory(item.Model, AccessoryView.Elytra, WingSpread);
            // A drag only changes existing mesh transforms; keep texture buffers, camera,
            // document metadata and history untouched while updating picking immediately.
            _isValidPick = false;
            _pickPosition = new Point(-1, -1);
            RefreshWorkspaceControls();
            Renderer?.Invalidate();
        }

        private Model EditingModel
        {
            get
            {
                foreach (var item in _scene) if (item.Document == _lastSkin) return item.Model;
                return _lastSkin?.Model;
            }
        }

        private void ReleaseUnpinnedTexture(Skin document)
        {
            if (document != null && document != _lastSkin && document != _workspaceSkin && document != _workspaceCape &&
                !document.Dirty && document.GLImage != null) document.Unload();
        }

        private void RebuildScene()
        {
            _scene.Clear();
            if (!CombinedEnabled)
            {
                if (_lastSkin != null) _scene.Add(new PreviewItem { Document = _lastSkin, Model = _lastSkin.Model });
                return;
            }
            if (_workspaceSkin != null && _playerSource != _workspaceSkin.Model)
            {
                _playerSource = _workspaceSkin.Model;
                _playerPreview = PreviewItem.Clone(_playerSource);
            }
            LayoutWorkspaceSheets();
            if (_workspaceSkin != null)
                _scene.Add(new PreviewItem { Document = _workspaceSkin, Model = _playerPreview, Sheet = _skinSheet });
            if (_workspaceCape != null)
            {
                _capePreview ??= PreviewItem.Clone(ModelLoader.GetModelForPath("Players/Cape"));
                _elytraPreview ??= PreviewItem.Clone(ModelLoader.GetModelForPath("Items/Elytra"));
                var model = AccessoryPresentation == AccessoryView.Cape ? _capePreview : _elytraPreview;
                PreviewItem.PoseAccessory(model, AccessoryPresentation, WingSpread);
                _scene.Add(new PreviewItem { Document = _workspaceCape, Model = model, Sheet = _capeSheet });
            }
            foreach (var item in _scene)
            {
                if (item.Document.GLImage == null) item.Document.Create();
                using var pixels = new ColorGrabber(item.Document == _lastSkin ? GlobalDirtiness.CurrentSkin : item.Document.GLImage,
                    item.Document.Width, item.Document.Height);
                pixels.Load();
                item.CaptureAlpha(pixels);
            }
        }

        private void LayoutWorkspaceSheets()
        {
            float skinHeight = _workspaceSkin?.Model.DefaultHeight ?? 64;
            const float sheetGap = 2;
            float top = -(32 + skinHeight + sheetGap) / 2;
            _capeSheet = new RectangleF(-32, top, 64, 32);
            _skinSheet = new RectangleF(-32, _capeSheet.Bottom + sheetGap, 64, skinHeight);
        }

        private void ReconcileWorkspaceModel()
        {
            if (!CombinedEnabled || _lastSkin == null) return;
            var role = PreviewItem.Role(_lastSkin);
            if (role == null)
            {
                CombinedEnabled = false;
                if (_workspaceSkin == _lastSkin) _workspaceSkin = null;
                if (_workspaceCape == _lastSkin) _workspaceCape = null;
                return;
            }
            var previousSkin = _workspaceSkin;
            var previousCape = _workspaceCape;
            if (role == EditingTarget.Skin)
            {
                if (_workspaceCape == _lastSkin) _workspaceCape = null;
                _workspaceSkin = _lastSkin;
            }
            else
            {
                if (_workspaceSkin == _lastSkin) _workspaceSkin = null;
                _workspaceCape = _lastSkin;
            }
            ActiveEditingTarget = role.Value;
            ReleaseUnpinnedTexture(previousSkin);
            ReleaseUnpinnedTexture(previousCape);
        }

        private void RefreshEditingView()
        {
            _isValidPick = false;
            _pickPosition = new Point(-1, -1);
            RefreshWorkspaceControls();
            FillPartList();
            CheckUndo();
            SetCanSave(_lastSkin?.Dirty == true);
            CalculateMatrices();
            Renderer.Invalidate();
        }

        private void FitWorkspace(bool reset3D = true)
        {
            if (!CombinedEnabled) return;
            var viewport = GetViewport2D();
            _2DCamOffsetX = _2DCamOffsetY = 0;
            _2DZoom = Math.Max(.25f, Math.Min((viewport.Width - 32) / 72f, (viewport.Height - 32) / (_skinSheet.Bottom - _capeSheet.Top + 16)));
            if (reset3D) _3DZoom = -100;
        }

        private void FinishStroke()
        {
            if (!_mouseIsDown || _lastSkin == null) return;
            rendererControl_MouseUp(Renderer, new MouseEventArgs(_strokeButton, 1, _mousePoint.X, _mousePoint.Y, 0));
        }

        private void ActivateDocument(Skin document, bool assignWorkspace = true)
        {
            if (_activatingDocument) return;
            FinishStroke();
            Renderer.MakeCurrent();
            _activatingDocument = true;
            try
            {
                var previous = _lastSkin;
                var previousSkin = _workspaceSkin;
                var previousCape = _workspaceCape;
                if (_lastSkin != null)
                {
                    _lastSkin.CommitChanges(GlobalDirtiness.CurrentSkin, false);
                }
                if (CombinedEnabled && assignWorkspace && document != null)
                {
                    var role = PreviewItem.Role(document);
                    if (role == null) CombinedEnabled = false;
                    else
                    {
                        ActiveEditingTarget = role.Value;
                        if (role == EditingTarget.Skin) _workspaceSkin = document; else _workspaceCape = document;
                    }
                }
                _lastSkin = document;
                if (document != null)
                {
                    if (document.GLImage == null) document.Create();
                    using (var grabber = new ColorGrabber(document.GLImage, document.Width, document.Height))
                    {
                        grabber.Load();
                        grabber.Texture = GlobalDirtiness.CurrentSkin; grabber.Save();
                        grabber.Texture = _previewPaint; grabber.Save();
                    }
                    treeView1.SelectedNode = document.IsReadOnly ? null : document;
                    if (!document.IsReadOnly) document.EnsureVisible();
                    SetModel(document.Model);
                }
                else treeView1.SelectedNode = null;
                RebuildScene();
                ReleaseUnpinnedTexture(previous);
                ReleaseUnpinnedTexture(previousSkin);
                ReleaseUnpinnedTexture(previousCape);
                RefreshEditingView();
                VerifySelectionButtons();
            }
            finally { _activatingDocument = false; }
        }

        public void RecordEdit(Skin document, IUndoable change)
        {
            History.Add(document, change);
            CheckUndo();
        }

        internal void ForgetDocumentHistory(Skin document)
        {
            History.Remove(document);
            CheckUndo();
        }

        internal void PrepareDocumentResize(Skin document)
        {
            FinishStroke();
            Renderer.MakeCurrent();
            if (document == _lastSkin) document.CommitChanges(GlobalDirtiness.CurrentSkin, false);
        }

        private void RemoveWorkspaceDocument(Skin document)
        {
            History.Remove(document);
            if (_workspaceSkin == document) _workspaceSkin = null;
            if (_workspaceCape == document) _workspaceCape = null;
            if (_lastSkin == document) _lastSkin = null;
            RebuildScene();
            RefreshEditingView();
        }

        private void DrawWorkspace2D()
        {
            bool checker = true;
            foreach (var item in _scene)
            {
                DrawPlayer2D(item.Document == _lastSkin ? _previewPaint : item.Document.GLImage, item.Document, item.Model, item.Sheet, checker);
                checker = false;
            }
            if (checker) DrawPlayer2D(null, null);
            GL.PushMatrix();
            GL.Translate(_currentViewport.Width / 2f, _currentViewport.Height / 2f, 0);
            GL.Scale(_2DZoom, _2DZoom, 1);
            GL.Translate(_2DCamOffsetX, _2DCamOffsetY, 0);
            DrawSheetLabel(_capeSheet, _workspaceCape, EditingTarget.Cape, "W_CAPE");
            DrawSheetLabel(_skinSheet, _workspaceSkin, EditingTarget.Skin, "W_SKIN");
            GL.PopMatrix();
        }

        private void DrawSheetLabel(RectangleF rect, Skin document, EditingTarget target, string key)
        {
            TextureGL.Unbind();
            GL.Color4(document != null && !document.IsReadOnly && ActiveEditingTarget == target ? Color.Gold : Color.DimGray);
            GL.LineWidth(2);
            GL.Begin(PrimitiveType.LineLoop);
            GL.Vertex2(rect.Left, rect.Top); GL.Vertex2(rect.Right, rect.Top);
            GL.Vertex2(rect.Right, rect.Bottom); GL.Vertex2(rect.Left, rect.Bottom);
            GL.End();
            GL.LineWidth(1);
            GL.Enable(EnableCap.Blend);
            GL.Color4(Color.White);
            string label = LanguageLoader.GetStringWithFallback(key);
            if (document == null) label += ": " + LanguageLoader.GetStringWithFallback("W_SELECT");
            else if (document.IsReadOnly) label += " (" + LanguageLoader.GetStringWithFallback("W_READ_ONLY") + ")";
            // The viewport font is an ASCII atlas; native controls above contain full localized names.
            label = new string(label.Select(c => c >= 32 && c < 127 ? c : '?').ToArray());
            // Labels sit outside the combined sheets so even a zoomed-out two-pixel
            // separator stays clear, without text covering either editable texture.
            float labelY = target == EditingTarget.Cape ? rect.Top - 12 / _2DZoom : rect.Bottom + 2 / _2DZoom;
            DrawStringWithinRectangle(_font, new RectangleF(rect.Left, labelY, rect.Width, 10 / _2DZoom), label, 8 / _2DZoom, 8 / _2DZoom);
            GL.Disable(EnableCap.Blend);
            GL.Color4(Color.White);
        }

        private void QueueSceneMeshes(Texture activeTexture)
        {
            foreach (var item in _scene)
            {
                var texture = item.Document == _lastSkin ? activeTexture : item.Document.GLImage;
                if (texture == null) continue;
                using (var pixels = new ColorGrabber(texture, item.Document.Width, item.Document.Height))
                {
                    pixels.Load();
                    if (CombinedEnabled) item.CaptureAlpha(pixels);
                    for (int i = 0; i < item.Model.Meshes.Count; ++i)
                    {
                        var mesh = item.Model.Meshes[i];
                        bool visible = item.Model.PartsEnabled[i];
                        if (!visible && !GlobalSettings.Ghost) continue;
                        mesh.Texture = texture;
                        mesh.HasTransparency = false;
                        if (!mesh.IsSolid)
                            foreach (var face in mesh.Faces)
                            {
                                var rect = Rectangle.Intersect(face.TexCoordsToInteger(item.Document.Width, item.Document.Height), new Rectangle(0, 0, item.Document.Width, item.Document.Height));
                                for (int y = rect.Top; y < rect.Bottom && !mesh.HasTransparency; ++y)
                                    for (int x = rect.Left; x < rect.Right; ++x)
                                        if (pixels[x, y].Alpha != 255) { mesh.HasTransparency = true; break; }
                                if (mesh.HasTransparency) break;
                            }
                        mesh.LastDrawTransparent = mesh.DrawTransparent;
                        mesh.DrawTransparent = !visible && GlobalSettings.Ghost;
                        if (mesh.UserData == null)
                        {
                            mesh.UserData = MeshRenderer.CreateUserData(mesh);
                            MeshRenderer.UpdateUserData(mesh);
                        }
                        else if (mesh.LastDrawTransparent != mesh.DrawTransparent) MeshRenderer.UpdateUserData(mesh);
                        MeshRenderer.AddMesh(mesh);
                    }
                }
            }
        }
    }
}
