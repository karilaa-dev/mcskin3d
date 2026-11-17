using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MCSkin3D.Languages;
using MCSkin3D.Properties;
using Paril.OpenGL;

namespace MCSkin3D
{
    public partial class Editor
    {
        private ToolStripButton _firstPersonButton;
        private ToolStripMenuItem _firstPersonMenu;
        private GroupBox _firstPersonGroup;
        private ComboBox _firstPersonHand;
        private Model _firstPersonModel, _firstPersonSource;

        internal bool IsFirstPersonView => _currentViewMode == ViewMode.FirstPerson;

        private bool CanShowFirstPerson => PreviewItem.Role(CombinedEnabled ? _workspaceSkin : _lastSkin) == EditingTarget.Skin;

        private void InitializeFirstPerson()
        {
            _firstPersonButton = new ToolStripButton { Name = "firstPersonButton",
                DisplayStyle = ToolStripItemDisplayStyle.Image, Image = Resources.first_person };
            _firstPersonMenu = new ToolStripMenuItem { Name = "firstPersonMenu", Image = Resources.first_person };
            _firstPersonButton.Click += (_, _) => SetViewMode(ViewMode.FirstPerson);
            _firstPersonMenu.Click += (_, _) => SetViewMode(ViewMode.FirstPerson);
            toolStrip1.Items.Insert(toolStrip1.Items.IndexOf(hybridToolStripButton) + 1, _firstPersonButton);
            modeToolStripMenuItem.DropDownItems.Add(_firstPersonMenu);

            _firstPersonGroup = PreviewOptionsGroup("firstPersonOptions", out var row);
            _firstPersonGroup.MinimumSize = Size.Empty;
            _firstPersonHand = new ComboBox { Name = "firstPersonHand", DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 100, Margin = new Padding(2, 1, 0, 0) };
            _firstPersonHand.SelectedIndexChanged += (_, _) =>
            {
                if (!_syncWorkspace) SetFirstPersonLeftHand(_firstPersonHand.SelectedIndex == 1);
            };
            row.Controls.Add(_firstPersonHand);
            _toolOptionsArea.Controls.Add(_firstPersonGroup);
        }

        private void RefreshFirstPersonControls()
        {
            if (_firstPersonButton == null) return;
            bool active = _currentViewMode == ViewMode.FirstPerson;
            _firstPersonButton.Text = _firstPersonMenu.Text = _firstPersonGroup.Text = LanguageLoader.GetStringWithFallback("FP_VIEW");
            _firstPersonButton.AccessibleName = _firstPersonButton.Text;
            _firstPersonButton.ToolTipText = _firstPersonMenu.ToolTipText = LanguageLoader.GetStringWithFallback("FP_HELP");
            _firstPersonButton.Enabled = _firstPersonMenu.Enabled = CanShowFirstPerson;
            _firstPersonButton.Checked = _firstPersonMenu.Checked = active;
            _firstPersonGroup.Visible = active;
            _firstPersonHand.AccessibleName = LanguageLoader.GetStringWithFallback("FP_HAND");
            bool syncing = _syncWorkspace;
            _syncWorkspace = true;
            try
            {
                string right = LanguageLoader.GetStringWithFallback("FP_RIGHT"), left = LanguageLoader.GetStringWithFallback("FP_LEFT");
                if (_firstPersonHand.Items.Count != 2 || (string)_firstPersonHand.Items[0] != right || (string)_firstPersonHand.Items[1] != left)
                {
                    _firstPersonHand.Items.Clear();
                    _firstPersonHand.Items.AddRange(new object[] { right, left });
                }
                _firstPersonHand.SelectedIndex = GlobalSettings.FirstPersonLeftHand ? 1 : 0;
            }
            finally { _syncWorkspace = syncing; }
        }

        internal void SetFirstPersonLeftHand(bool leftHand)
        {
            FinishStroke();
            GlobalSettings.FirstPersonLeftHand = leftHand;
            RefreshFirstPersonControls();
            _isValidPick = false;
            _pickPosition = new Point(-1, -1);
            Renderer?.Invalidate();
        }

        private void PrepareFirstPersonScene()
        {
            if (_currentViewMode != ViewMode.FirstPerson) return;
            var item = _scene.FirstOrDefault(i => PreviewItem.Role(i.Document) == EditingTarget.Skin);
            if (item == null)
            {
                SetViewMode(ViewMode.Perspective);
                return;
            }
            if (_firstPersonSource != item.Model)
            {
                _firstPersonSource = item.Model;
                _firstPersonModel = PreviewItem.Clone(item.Model);
                // Share only the visibility choices, never mesh transforms or UVs.
                _firstPersonModel.PartsEnabled = item.Model.PartsEnabled;
                FirstPersonPreview.Pose(_firstPersonModel);
            }
            item.Model = _firstPersonModel;
            item.IsFirstPerson = true;
        }

        private bool IsMeshInView(PreviewItem item, Mesh mesh) => _currentViewMode != ViewMode.FirstPerson ||
            (item.Model == _firstPersonModel && FirstPersonPreview.IsArm(mesh.Part, GlobalSettings.FirstPersonLeftHand));
    }
}
