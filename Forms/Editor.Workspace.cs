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
        private ToolStripMenuItem _skinLayersMenu;
        private ToolStripButton _skinLayersButton;
        private ToolStripButton _minecraftLightingButton;
        private ToolStripMenuItem _skinLayerSettingsMenu;
        private ToolStripButton _officialCapesButton;
        private Label _officialCapeLabel;
        private readonly Dictionary<string, Skin> _officialCapeDocuments = new Dictionary<string, Skin>();
        private readonly Dictionary<Button, string> _workspaceButtons = new Dictionary<Button, string>();
        private readonly Dictionary<Label, string> _workspaceLabels = new Dictionary<Label, string>();
        private Button _editSkinButton, _editCapeButton, _capeButton, _elytraButton;
        private FlowLayoutPanel _toolOptionsArea;
        private bool _layingOutToolOptions;
        private GroupBox _wingSpreadGroup;
        private TrackBar _wingSpreadSlider;
        private FlowLayoutPanel _lightingOptionsColumn;
        private Panel _brightnessGroup;
        private TrackBar _brightnessSlider;
        private Label _brightnessValue;
        private CheckBox _lightSurroundingsToggle;
        private Panel _lightAngleGroup;
        private TrackBar _lightAngleSlider;
        private NumericUpDown _lightAngleValue;
        private Button _lightAngleDefault;

        private sealed class LibraryChoice
        {
            public Skin Document;
            public override string ToString() => Document == null ? LanguageLoader.GetStringWithFallback("W_SELECT") :
                Document.FullPath + (Document.Dirty ? " *" : "");
        }

        private void InitializeWorkspace()
        {
            _skinLayersMenu = new ToolStripMenuItem { Name = "skinLayers3DMenu", Checked = GlobalSettings.SkinLayers3D };
            _skinLayersMenu.Click += (_, _) => SetSkinLayers3D(!GlobalSettings.SkinLayers3D);
            viewToolStripMenuItem.DropDownItems.Add(_skinLayersMenu);
            _skinLayersButton = new ToolStripButton { Name = "skinLayers3DButton", DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = Properties.Resources.skin_layers, Overflow = ToolStripItemOverflow.Never };
            _skinLayersButton.Click += (_, _) => SetSkinLayers3D(!GlobalSettings.SkinLayers3D);
            _minecraftLightingButton = new ToolStripButton { Name = "minecraftLightingButton", DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = Properties.Resources.minecraft_lighting, Overflow = ToolStripItemOverflow.Never };
            _minecraftLightingButton.Click += (_, _) => SetMinecraftLighting(!GlobalSettings.MinecraftLighting);
            _skinLayerSettingsMenu = new ToolStripMenuItem { Name = "skinLayerSettingsMenu" };
            _skinLayerSettingsMenu.Click += (_, _) =>
            {
                using var dialog = new Forms.SkinLayerSettingsDialog(SkinLayerOptions.Current);
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                FinishStroke();
                dialog.Options.Save();
                dialog.Rendering.Save();
                SetSampleMenuItem(dialog.Rendering.Samples);
                GlobalSettings.Save();
                RebuildScene();
                RefreshEditingView();
            };
            optionsToolStripMenuItem.DropDownItems.Add(_skinLayerSettingsMenu);
            _combinedButton = new ToolStripButton { Name = "combinedPreviewButton", DisplayStyle = ToolStripItemDisplayStyle.Text };
            _combinedMenu = new ToolStripMenuItem { Name = "combinedPreviewMenu" };
            _combinedButton.Click += (_, _) => SetCombinedMode(!CombinedEnabled);
            _combinedMenu.Click += (_, _) => SetCombinedMode(!CombinedEnabled);
            toolStrip1.Items.Add(new ToolStripSeparator());
            toolStrip1.Items.Add(_minecraftLightingButton);
            toolStrip1.Items.Add(_skinLayersButton);
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
            _toolOptionsArea = new FlowLayoutPanel { Name = "toolOptionsArea", Dock = DockStyle.Fill,
                WrapContents = true, AutoScroll = true, Padding = new Padding(0) };
            splitContainer4.Panel1.Controls.Add(_toolOptionsArea);
            _wingSpreadGroup = PreviewOptionsGroup("elytraOptions", out var wingRow);
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
            wingRow.Controls.Add(folded);
            wingRow.Controls.Add(_wingSpreadSlider);
            wingRow.Controls.Add(spread);
            _toolOptionsArea.Controls.Add(_wingSpreadGroup);
            _lightingOptionsColumn = new FlowLayoutPanel { Name = "lightingOptionsColumn", AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, Margin = new Padding(3) };
            _brightnessGroup = PreviewLightingRow("brightnessOptions", "SL_BRIGHTNESS", out var brightnessRow);
            _brightnessSlider = new TrackBar { Name = "brightnessSlider", Minimum = 0, Maximum = 15,
                SmallChange = 1, LargeChange = 1, TickStyle = TickStyle.None, AutoSize = false,
                Size = new Size(90, 23), Margin = new Padding(0) };
            _brightnessValue = new Label { Name = "brightnessValue", AutoSize = true, MinimumSize = new Size(24, 23),
                TextAlign = ContentAlignment.MiddleRight, Margin = new Padding(0) };
            _lightSurroundingsToggle = new CheckBox { Name = "lightSurroundingsToggle", AutoSize = true,
                Margin = new Padding(8, 3, 0, 0) };
            _lightSurroundingsToggle.CheckedChanged += (_, _) =>
            {
                if (!_syncWorkspace) SetPreviewLightSurroundings(_lightSurroundingsToggle.Checked);
            };
            _brightnessSlider.ValueChanged += (_, _) =>
            {
                if (!_syncWorkspace) SetPreviewLightLevel(_brightnessSlider.Value);
            };
            brightnessRow.Controls.Add(_brightnessSlider);
            brightnessRow.Controls.Add(_brightnessValue);
            brightnessRow.Controls.Add(_lightSurroundingsToggle);
            _lightingOptionsColumn.Controls.Add(_brightnessGroup);
            _lightAngleGroup = PreviewLightingRow("lightAngleOptions", "SL_LIGHT_ANGLE", out var lightAngleRow);
            _lightAngleSlider = new TrackBar { Name = "lightAngleSlider", Minimum = -90, Maximum = 90,
                SmallChange = 1, LargeChange = 15, TickStyle = TickStyle.None, AutoSize = false,
                Size = new Size(140, 23), Margin = new Padding(0, 0, 4, 0) };
            _lightAngleValue = new NumericUpDown { Name = "lightAngleValue", Minimum = -90, Maximum = 90,
                Width = 52, TextAlign = HorizontalAlignment.Right, Margin = new Padding(0, 1, 0, 0) };
            var degrees = new Label { Name = "lightAngleDegrees", Text = "\u00b0", AutoSize = true,
                Margin = new Padding(0, 4, 3, 0) };
            _lightAngleDefault = new Button { Name = "lightAngleDefault", AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0) };
            _workspaceButtons.Add(_lightAngleDefault, "SL_LIGHT_ANGLE_DEFAULT");
            _lightAngleSlider.ValueChanged += (_, _) =>
            {
                if (!_syncWorkspace) SetPreviewLightAngle(_lightAngleSlider.Value);
            };
            _lightAngleValue.ValueChanged += (_, _) =>
            {
                if (!_syncWorkspace) SetPreviewLightAngle((int)_lightAngleValue.Value);
            };
            _lightAngleDefault.Click += (_, _) => SetPreviewLightAngle(PreviewRenderOptions.Defaults.LightAngle);
            lightAngleRow.Controls.Add(_lightAngleSlider);
            lightAngleRow.Controls.Add(_lightAngleValue);
            lightAngleRow.Controls.Add(degrees);
            lightAngleRow.Controls.Add(_lightAngleDefault);
            _lightingOptionsColumn.Controls.Add(_lightAngleGroup);
            _brightnessGroup.Anchor = _lightAngleGroup.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _toolOptionsArea.Controls.Add(_lightingOptionsColumn);
            InitializeFirstPerson();
            _toolOptionsArea.Layout += (_, _) => UpdateToolOptionsHeight();
            panel4.Controls.Add(_workspaceBar);
            // Place the wrapping bar below the existing viewport toolbar, including in the popout.
            panel4.Controls.SetChildIndex(_workspaceBar, panel4.Controls.IndexOf(toolStrip1));
            RefreshWorkspaceControls();
        }

        private static GroupBox PreviewOptionsGroup(string name, out FlowLayoutPanel row)
        {
            var group = new GroupBox { Name = name, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(0, 64), Margin = new Padding(3), Padding = new Padding(6, 3, 6, 3) };
            row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0) };
            group.Controls.Add(row);
            return group;
        }

        private Panel PreviewLightingRow(string name, string labelKey, out FlowLayoutPanel row)
        {
            var panel = new Panel { Name = name, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0), Padding = new Padding(3, 2, 3, 2) };
            row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0) };
            var label = new Label { AutoSize = true, MinimumSize = new Size(85, 23),
                TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, 4, 0) };
            _workspaceLabels.Add(label, labelKey);
            row.Controls.Add(label);
            panel.Controls.Add(row);
            return panel;
        }

        private void UpdateToolOptionsHeight()
        {
            if (_toolOptionsArea == null || _layingOutToolOptions) return;
            _layingOutToolOptions = true;
            try
            {
                splitContainer4.Panel1Collapsed = !_opening;
                if (!_opening) return;
                _selectedTool?.OptionsPanel?.ArrangeOptions(_toolOptionsArea.ClientSize.Width);
                foreach (var group in new Control[] { _wingSpreadGroup, _brightnessGroup, _lightAngleGroup, _firstPersonGroup })
                {
                    if (group == null) continue;
                    var row = group.Controls[0];
                    row.MaximumSize = new Size(Math.Max(190, Math.Min(460,
                        _toolOptionsArea.ClientSize.Width - group.Margin.Horizontal - group.Padding.Horizontal - 8)), 0);
                    row.PerformLayout();
                    group.PerformLayout();
                }
                _lightingOptionsColumn?.PerformLayout();
                // FlowLayoutPanel raises Layout before arranging its children, and
                // nested AutoSize groups underreport wrapped preferred heights.
                // Measure rows from the groups' actual sizes, including margins.
                int preferred = _toolOptionsArea.Padding.Vertical + 4;
                int usedWidth = 0, flowRowHeight = 0;
                int contentWidth = _toolOptionsArea.ClientSize.Width - _toolOptionsArea.Padding.Horizontal;
                foreach (Control control in _toolOptionsArea.Controls)
                {
                    if (!control.Visible) continue;
                    int width = control.Width + control.Margin.Horizontal;
                    int controlHeight = control.Height + control.Margin.Vertical;
                    if (usedWidth > 0 && usedWidth + width > contentWidth)
                    {
                        preferred += flowRowHeight;
                        usedWidth = flowRowHeight = 0;
                    }
                    usedWidth += width;
                    flowRowHeight = Math.Max(flowRowHeight, controlHeight);
                }
                preferred += flowRowHeight;
                int available = Math.Max(0, splitContainer4.Height - splitContainer4.Panel2MinSize - splitContainer4.SplitterWidth);
                // Reserve one row for legacy tool panels (designed at a 13-pixel
                // font); first-person camera controls can use their compact height.
                int rowHeight = IsFirstPersonView && _selectedTool?.OptionsPanel == null ? 0 :
                    (int)Math.Ceiling(74 * Font.Height / 13d * DeviceDpi / 96d);
                int height = Math.Min(Math.Max(rowHeight, preferred), available);
                if (splitContainer4.SplitterDistance != height) splitContainer4.SplitterDistance = height;
                splitContainer4.Panel1.PerformLayout();
            }
            finally { _layingOutToolOptions = false; }
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
            _skinLayersButton.Text = _skinLayersMenu.Text = LanguageLoader.GetStringWithFallback("M_3D_SKIN_LAYERS");
            _skinLayersButton.AccessibleName = _skinLayersButton.Text;
            _skinLayerSettingsMenu.Text = LanguageLoader.GetStringWithFallback("SL_SETTINGS") + "…";
            _skinLayersButton.ToolTipText = _skinLayersMenu.ToolTipText = LanguageLoader.GetStringWithFallback("T_3D_SKIN_LAYERS");
            _skinLayersButton.Checked = _skinLayersMenu.Checked = GlobalSettings.SkinLayers3D;
            _minecraftLightingButton.Text = LanguageLoader.GetStringWithFallback("M_MINECRAFT_LIGHTING");
            _minecraftLightingButton.AccessibleName = _minecraftLightingButton.Text;
            _minecraftLightingButton.ToolTipText = LanguageLoader.GetStringWithFallback("T_MINECRAFT_LIGHTING");
            _minecraftLightingButton.Checked = GlobalSettings.MinecraftLighting;
            _combinedButton.Text = _combinedMenu.Text = LanguageLoader.GetStringWithFallback("W_COMBINED");
            _officialCapesButton.Text = LanguageLoader.GetStringWithFallback("W_OFFICIAL_CAPES");
            _officialCapesButton.Visible = !CombinedEnabled;
            _officialCapeLabel.Text = _workspaceCape?.ReferenceCape?.Name ?? "";
            _combinedButton.Checked = _combinedMenu.Checked = CombinedEnabled;
            _workspaceBar.SuspendLayout();
            _toolOptionsArea.SuspendLayout();
            foreach (Control control in _workspaceBar.Controls) control.Visible = CombinedEnabled;
            _officialCapeLabel.Visible = CombinedEnabled && _workspaceCape?.IsReadOnly == true;
            bool has3DView = _currentViewMode != ViewMode.Orthographic;
            RefreshFirstPersonControls();
            _minecraftLightingButton.Visible = has3DView;
            bool showBrightness = has3DView && GlobalSettings.MinecraftLighting;
            _lightingOptionsColumn.Visible = IsFirstPersonView || showBrightness;
            // Keep the same compact controls and wrapping in first person. Hiding
            // Lighting controls would resize the viewport and move the projected hand.
            foreach (var group in new[] { _brightnessGroup, _lightAngleGroup })
            {
                group.Visible = IsFirstPersonView || showBrightness;
                group.Enabled = showBrightness;
            }
            _workspaceBar.Visible = CombinedEnabled;
            _brightnessGroup.Text = LanguageLoader.GetStringWithFallback("SL_BRIGHTNESS");
            _lightAngleGroup.Text = LanguageLoader.GetStringWithFallback("SL_LIGHT_ANGLE");
            _wingSpreadGroup.Text = LanguageLoader.GetStringWithFallback("W_ELYTRA");
            foreach (var pair in _workspaceLabels) pair.Key.Text = LanguageLoader.GetStringWithFallback(pair.Value);
            foreach (var pair in _workspaceButtons) pair.Key.Text = LanguageLoader.GetStringWithFallback(pair.Value);
            _editSkinButton.Enabled = _workspaceSkin != null;
            _editCapeButton.Enabled = _workspaceCape != null && !_workspaceCape.IsReadOnly;
            if (_workspaceCape?.IsReadOnly == true) _editCapeButton.Text = LanguageLoader.GetStringWithFallback("W_READ_ONLY");
            _editSkinButton.BackColor = ActiveEditingTarget == EditingTarget.Skin ? SystemColors.GradientActiveCaption : SystemColors.Control;
            _editCapeButton.BackColor = ActiveEditingTarget == EditingTarget.Cape ? SystemColors.GradientActiveCaption : SystemColors.Control;
            _capeButton.BackColor = AccessoryPresentation == AccessoryView.Cape ? SystemColors.GradientActiveCaption : SystemColors.Control;
            _elytraButton.BackColor = AccessoryPresentation == AccessoryView.Elytra ? SystemColors.GradientActiveCaption : SystemColors.Control;
            _wingSpreadGroup.Visible = has3DView && _currentViewMode != ViewMode.FirstPerson && CombinedEnabled && AccessoryPresentation == AccessoryView.Elytra;
            _wingSpreadSlider.AccessibleName = LanguageLoader.GetStringWithFallback("W_WING_SPREAD");
            _brightnessSlider.AccessibleName = LanguageLoader.GetStringWithFallback("SL_BRIGHTNESS");
            _brightnessSlider.AccessibleDescription = LanguageLoader.GetStringWithFallback("SL_BRIGHTNESS_HELP");
            _lightSurroundingsToggle.Text = _lightSurroundingsToggle.AccessibleName = LanguageLoader.GetStringWithFallback("SL_SURROUNDINGS");
            _lightSurroundingsToggle.AccessibleDescription = LanguageLoader.GetStringWithFallback("SL_SURROUNDINGS_HELP");
            _lightAngleSlider.AccessibleName = _lightAngleGroup.Text;
            _lightAngleSlider.AccessibleDescription = LanguageLoader.GetStringWithFallback("SL_LIGHT_ANGLE_HELP");
            _lightAngleValue.AccessibleName = _lightAngleGroup.Text;
            _lightAngleValue.AccessibleDescription = _lightAngleSlider.AccessibleDescription;
            _syncWorkspace = true;
            try
            {
                _wingSpreadSlider.Value = (int)Math.Round(WingSpread * 100);
                _brightnessSlider.Value = PreviewRenderOptions.Current.LightLevel;
                _brightnessValue.Text = _brightnessSlider.Value.ToString();
                _lightSurroundingsToggle.Checked = PreviewRenderOptions.Current.LightSurroundings;
                SyncLightAngleControls();
            }
            finally { _syncWorkspace = false; }
            _workspaceBar.ResumeLayout(true);
            _toolOptionsArea.ResumeLayout(true);
            UpdateToolOptionsHeight();
            RefreshLibraryChoices(_skinSelector, EditingTarget.Skin);
            RefreshLibraryChoices(_capeSelector, EditingTarget.Cape);
        }

        public void SetSkinLayers3D(bool enabled)
        {
            FinishStroke();
            GlobalSettings.SkinLayers3D = enabled;
            RebuildScene();
            RefreshEditingView();
        }

        private void SetMinecraftLighting(bool enabled)
        {
            FinishStroke();
            GlobalSettings.MinecraftLighting = enabled;
            RefreshWorkspaceControls();
            Renderer?.Invalidate();
        }

        internal void SetPreviewLightLevel(int level)
        {
            FinishStroke();
            GlobalSettings.PreviewLightLevel = Math.Clamp(level, 0, 15);
            _syncWorkspace = true;
            try
            {
                _brightnessSlider.Value = GlobalSettings.PreviewLightLevel;
                _brightnessValue.Text = _brightnessSlider.Value.ToString();
            }
            finally { _syncWorkspace = false; }
            Renderer?.Invalidate();
        }

        internal void SetPreviewLightAngle(int angle)
        {
            FinishStroke();
            GlobalSettings.PreviewLightAngle = PreviewRenderOptions.NormalizeLightAngle(angle);
            _syncWorkspace = true;
            try { SyncLightAngleControls(); }
            finally { _syncWorkspace = false; }
            Renderer?.Invalidate();
        }

        internal void SetPreviewLightSurroundings(bool enabled)
        {
            FinishStroke();
            GlobalSettings.PreviewLightSurroundings = enabled;
            _syncWorkspace = true;
            try { _lightSurroundingsToggle.Checked = enabled; }
            finally { _syncWorkspace = false; }
            Renderer?.Invalidate();
        }

        private void SyncLightAngleControls()
        {
            _lightAngleSlider.Value = PreviewRenderOptions.Current.LightAngle;
            _lightAngleValue.Value = _lightAngleSlider.Value;
            _lightAngleDefault.Enabled = _lightAngleSlider.Value != PreviewRenderOptions.Defaults.LightAngle;
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
            RebuildWorkspaceScene();
            PrepareFirstPersonScene();
            if (CombinedEnabled || GlobalSettings.SkinLayers3D) CaptureSceneLayers();
        }

        private void RebuildWorkspaceScene()
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
        }

        private void CaptureSceneLayers()
        {
            foreach (var item in _scene)
            {
                if (item.Document.GLImage == null) item.Document.Create();
                item.CaptureTexture(item.Document == _lastSkin ? GlobalDirtiness.CurrentSkin : item.Document.GLImage);
                item.UpdateSkinLayers();
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
            var rendering = PreviewRenderOptions.Current;
            foreach (var item in _scene)
            {
                var texture = item.Document == _lastSkin ? activeTexture : item.Document.GLImage;
                if (texture == null) continue;
                item.CaptureTexture(texture);
                item.UpdateSkinLayers();
                bool useLayers = item.UseSkinLayers;
                bool lighting = rendering.MinecraftLighting && PreviewItem.Role(item.Document) != null;
                for (int i = 0; i < item.Model.Meshes.Count; ++i)
                {
                    var mesh = item.Model.Meshes[i];
                    if (!IsMeshInView(item, mesh)) continue;
                    bool visible = item.Model.PartsEnabled[i];
                    if (!visible && !GlobalSettings.Ghost) continue;
                    SkinLayerGeometry layer = null;
                    if (useLayers && item.SkinLayers.TryGetValue(mesh, out layer))
                    {
                        mesh = layer.Mesh;
                        mesh.Matrix = item.Model.Meshes[i].Matrix;
                    }
                    mesh.Texture = texture;
                    if (layer == null) mesh.HasTransparency = item.HasTransparency(mesh);
                    mesh.LastDrawTransparent = mesh.DrawTransparent;
                    mesh.DrawTransparent = !visible && GlobalSettings.Ghost;
                    bool lightingChanged = mesh.SetLighting(lighting, rendering.LightLevel, rendering.LightAngle);
                    if (mesh.UserData == null)
                    {
                        mesh.UserData = MeshRenderer.CreateUserData(mesh);
                        MeshRenderer.UpdateUserData(mesh);
                    }
                    else if (mesh.LastDrawTransparent != mesh.DrawTransparent || lightingChanged) MeshRenderer.UpdateUserData(mesh);
                    MeshRenderer.AddMesh(mesh);
                }
            }
        }
    }
}
