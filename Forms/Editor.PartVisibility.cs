using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MCSkin3D.Languages;

namespace MCSkin3D
{
    public partial class Editor
    {
        private readonly Dictionary<ModelPart, CheckBox> _partButtons = new();
        private ToolStripButton _partVisibilityButton;
        private FlowLayoutPanel _partVisibilityPanel;
        private GroupBox _mainLayerGroup, _outerLayerGroup;

        private void InitializePartVisibility()
        {
            _partVisibilityButton = new ToolStripButton
            {
                Name = "partVisibilityButton", Image = chestArmorToolStripMenuItem.Image,
                DisplayStyle = ToolStripItemDisplayStyle.Image, CheckOnClick = true,
                Overflow = ToolStripItemOverflow.Never
            };
            toolStrip1.Items.Insert(toolStrip1.Items.IndexOf(toolStripSeparator9) + 1, _partVisibilityButton);
            _partVisibilityPanel = new FlowLayoutPanel
            {
                Name = "partVisibilityPanel", Dock = DockStyle.Right, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Visible = false
            };
            var tips = new ToolTip(components);
            int iconSize = LogicalToDeviceUnits(16);
            var icons = new ImageList(components) { ImageSize = new Size(iconSize, iconSize), ColorDepth = ColorDepth.Depth32Bit };
            _mainLayerGroup = CreateLayerGroup(tips, icons, ModelPart.Head, ModelPart.LeftArm, ModelPart.Chest,
                ModelPart.RightArm, ModelPart.LeftLeg, ModelPart.RightLeg);
            _outerLayerGroup = CreateLayerGroup(tips, icons, ModelPart.Helmet, ModelPart.LeftArmArmor, ModelPart.ChestArmor,
                ModelPart.RightArmArmor, ModelPart.LeftLegArmor, ModelPart.RightLegArmor);
            treeView2.Visible = false;
            _partVisibilityPanel.Controls.AddRange(new Control[] { _mainLayerGroup, _outerLayerGroup, treeView2 });
            _partVisibilityPanel.ClientSizeChanged += (_, _) => FitPartVisibilityHeight();
            splitContainer4.Panel1.Controls.Add(_partVisibilityPanel);
            _partVisibilityButton.CheckedChanged += (_, _) =>
            {
                _partVisibilityPanel.Visible = _partVisibilityButton.Checked;
                if (_partVisibilityButton.Checked && !_opening) toolStripButton1.PerformClick();
                UpdateToolOptionsHeight();
                FitPartVisibilityHeight();
            };
        }

        private GroupBox CreateLayerGroup(ToolTip tips, ImageList icons, ModelPart head, ModelPart leftArm, ModelPart chest,
            ModelPart rightArm, ModelPart leftLeg, ModelPart rightLeg)
        {
            var group = new GroupBox { Padding = new Padding(2, 0, 2, 1), Margin = new Padding(2, 1, 2, 1) };
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 3, Margin = Padding.Empty
            };
            for (int i = 0; i < grid.ColumnCount; ++i)
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 1));
            for (int i = 0; i < grid.RowCount; ++i)
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 1));

            AddPart(head, 2, 0);
            AddPart(rightArm, 0, 1);
            AddPart(chest, 2, 1);
            AddPart(leftArm, 4, 1);
            AddPart(rightLeg, 1, 2);
            AddPart(leftLeg, 3, 2);
            group.Controls.Add(grid);
            return group;

            void AddPart(ModelPart part, int column, int row)
            {
                var item = _partItems[(int)part];
                icons.Images.Add(item.Name, item.Image);
                var button = new CheckBox
                {
                    Name = "show" + part, Appearance = Appearance.Button, AutoCheck = false,
                    ImageList = icons, ImageKey = item.Name, Dock = DockStyle.Fill,
                    Margin = Padding.Empty, AccessibleName = item.Text, UseVisualStyleBackColor = true
                };
                button.Click += (_, _) => ToggleVisiblePart(part);
                tips.SetToolTip(button, item.Text);
                item.TextChanged += (_, _) =>
                {
                    button.AccessibleName = item.Text;
                    tips.SetToolTip(button, item.Text);
                };
                _partButtons.Add(part, button);
                grid.Controls.Add(button, column, row);
                grid.SetColumnSpan(button, 2);
            }
        }

        private void FitPartVisibilityHeight()
        {
            int available = _partVisibilityPanel.ClientSize.Height;
            foreach (var group in new[] { _mainLayerGroup, _outerLayerGroup })
                group.Height = Math.Clamp(available - group.Margin.Vertical, 0,
                    group.Font.Height + group.Padding.Vertical + LogicalToDeviceUnits(62));
            treeView2.Height = Math.Clamp(available - treeView2.Margin.Vertical, 0, LogicalToDeviceUnits(88));
        }

        private void RefreshPartVisibilityLabels()
        {
            _partVisibilityButton.Text = _partVisibilityButton.AccessibleName = LanguageLoader.GetStringWithFallback("M_VISIBLEPARTS");
            SetLayerCaption(_mainLayerGroup, "VP_MAIN_LAYER");
            SetLayerCaption(_outerLayerGroup, "VP_OUTER_LAYER");
        }

        private void SetLayerCaption(GroupBox group, string key)
        {
            group.Text = LanguageLoader.GetStringWithFallback(key);
            group.Width = Math.Max(LogicalToDeviceUnits(66) + group.Padding.Horizontal,
                TextRenderer.MeasureText(group.Text, group.Font).Width + LogicalToDeviceUnits(24));
        }
    }
}
