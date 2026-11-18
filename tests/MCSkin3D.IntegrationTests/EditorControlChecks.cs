using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MCSkin3D;
using OpenTK.Mathematics;

internal static partial class Program
{
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    private static void ViewportControls()
    {
        Run("Character-shaped visibility controls", LayerVisibilityControls);
        Run("Orientation cube hover and camera input", OrientationCubeInput);
    }

    private static void ExplorerArrows()
    {
        var tree = Field<SkinTreeView>(editor, "treeView1");
        var split = Field<SplitContainer>(editor, "splitContainer1");
        var selected = tree.SelectedNode;
        int distance = split.SplitterDistance, height = tree.ItemHeight;
        var root = new FolderNode("Arrow check");
        var folder = new FolderNode(new string('w', 100));
        root.Nodes.Add(folder);
        folder.Nodes.Add(new FolderNode("child"));
        tree.Nodes.Add(root);
        try
        {
            Check(tree.Parent is SplitterPanel && !Controls(tree.Parent).OfType<TabControl>().Any(),
                "The skin explorer has no tab strip");
            root.Expand();
            foreach (int offset in new[] { 0, 11 })
            {
                split.SplitterDistance = distance + 54 - offset;
                tree.ItemHeight = height + offset;
                folder.EnsureVisible();
                tree.Refresh();
                Call(editor.hScrollBar1, "OnScroll", new ScrollEventArgs(ScrollEventType.ThumbPosition, offset));
                tree.Refresh();
                Check(tree.ScrollPosition.X == offset, "The explorer uses the horizontal scrollbar's new position");
                var arrow = (Rectangle)Call(tree, "ArrowBounds", folder);
                var center = new Point(arrow.Left + arrow.Width / 2, arrow.Top + arrow.Height / 2);
                Click(new Point(arrow.Left - 2, center.Y));
                Check(!folder.IsExpanded, "Clicking the indentation beside the rendered arrow leaves the folder closed");
                Click(center);
                Check(folder.IsExpanded && tree.SelectedNode == folder, "A native click on the resized, scrolled arrow opens its folder");
                Click(center);
                Check(!folder.IsExpanded, "The same arrow closes the folder");
            }
        }
        finally
        {
            root.Remove();
            tree.ItemHeight = height;
            split.SplitterDistance = distance;
            tree.SelectedNode = selected;
            tree.Refresh();
        }

        void Click(Point p)
        {
            var position = (IntPtr)((p.Y << 16) | p.X);
            // The native tree can wait for button-up inside its button-down handler.
            PostMessage(tree.Handle, 0x0201, (IntPtr)1, position);
            PostMessage(tree.Handle, 0x0202, IntPtr.Zero, position);
            Application.DoEvents();
        }
    }

    private static void LayerVisibilityControls()
    {
        var skin = Field<Skin>(editor, "_lastSkin");
        var tree = Field<TreeView>(editor, "treeView1");
        var player = WorkspaceFixture(skin, "layer-control-player", "Players/Steve", 64, 64, Color.Teal);
        var button = Field<ToolStripButton>(editor, "_partVisibilityButton");
        var panel = Field<FlowLayoutPanel>(editor, "_partVisibilityPanel");
        var size = editor.ClientSize;
        var originalTool = editor.SelectedTool;
        bool originalLighting = GlobalSettings.MinecraftLighting;
        try
        {
            tree.SelectedNode = player;
            Check(!panel.Visible && !button.Checked, "The two layers start folded into one toolbar button");
            var toolbar = button.Owner;
            Check(toolbar.Items[toolbar.Items.IndexOf(button) - 1].Name == "toolStripSeparator9" &&
                toolbar.Items[toolbar.Items.IndexOf(button) + 1].Name == "toolStripDropDownButton1" &&
                button.Alignment == ToolStripItemAlignment.Left && button.Image == Field<ToolStripMenuItem>(editor, "chestArmorToolStripMenuItem").Image,
                "Layer visibility uses the outer chest icon at the old parts position before the model selector");
            button.PerformClick();
            var buttons = Field<Dictionary<ModelPart, CheckBox>>(editor, "_partButtons");
            var items = Field<ToolStripMenuItem[]>(editor, "_partItems");
            foreach (int width in new[] { size.Width, tree.Width + panel.Width + editor.LogicalToDeviceUnits(220) })
            {
                button.PerformClick();
                editor.ClientSize = new Size(width, size.Height);
                var viewport = editor.Renderer.RectangleToScreen(editor.Renderer.ClientRectangle);
                button.PerformClick();
                Check(viewport == editor.Renderer.RectangleToScreen(editor.Renderer.ClientRectangle),
                    "Opening layer visibility preserves the tool-options height and preview bounds at " + width);
                Check(button.Visible && button.Placement == ToolStripItemPlacement.Main && panel.Visible &&
                    panel.Right == panel.Parent.ClientSize.Width && buttons.Values.All(b =>
                        b.Visible && panel.RectangleToScreen(panel.ClientRectangle).Contains(b.RectangleToScreen(b.ClientRectangle))),
                    "Both character layouts remain visible at the right of the tool options after resizing");
                foreach (var parts in new[] {
                    new[] { ModelPart.Head, ModelPart.RightArm, ModelPart.Chest, ModelPart.LeftArm, ModelPart.RightLeg, ModelPart.LeftLeg },
                    new[] { ModelPart.Helmet, ModelPart.RightArmArmor, ModelPart.ChestArmor, ModelPart.LeftArmArmor, ModelPart.RightLegArmor, ModelPart.LeftLegArmor } })
                {
                    var b = parts.Select(p => buttons[p].Bounds).ToArray();
                    Check(b[0].X == b[2].X && b[0].Bottom <= b[2].Top && b[1].Top == b[2].Top &&
                        b[2].Top == b[3].Top && b[1].Right <= b[2].Left && b[2].Right <= b[3].Left &&
                        b[4].Top >= b[2].Bottom && b[5].Top == b[4].Top && b[4].Right <= b[5].Left,
                        "The character's right arm and leg appear on the viewer's left in both layers");
                }
                foreach (var tool in Field<List<ToolIndex>>(editor, "_tools").Where(t => t.Tool is CameraTool or PencilTool))
                    foreach (bool lighting in new[] { false, true })
                    {
                        button.PerformClick();
                        tool.MenuItem.PerformClick();
                        Call(editor, "SetMinecraftLighting", lighting);
                        viewport = editor.Renderer.RectangleToScreen(editor.Renderer.ClientRectangle);
                        button.PerformClick();
                        Check(viewport == editor.Renderer.RectangleToScreen(editor.Renderer.ClientRectangle) &&
                            buttons.Values.All(b => b.Height > 0 && panel.RectangleToScreen(panel.ClientRectangle)
                                .Contains(b.RectangleToScreen(b.ClientRectangle))),
                            $"Both layers fit the existing {tool.Tool.GetType().Name} row at {width}px with lighting {lighting}");
                    }
            }
            foreach (var (part, toggle) in buttons)
            {
                bool wasVisible = toggle.Checked;
                Call(toggle, "OnClick", EventArgs.Empty);
                int index = Editor.CurrentModel.Meshes.FindIndex(mesh => mesh.Part == part);
                Check(toggle.Checked != wasVisible && items[(int)part].Checked == toggle.Checked &&
                    Editor.CurrentModel.PartsEnabled[index] == toggle.Checked, "Toggling " + part + " updates menu and model");
                items[(int)part].PerformClick();
                Check(toggle.Checked == wasVisible, "The menu synchronizes " + part + " in the layer panel");
            }
            using var layout = new Bitmap(panel.Width, panel.Height);
            panel.DrawToBitmap(layout, panel.ClientRectangle);
            layout.Save(Path.Combine(scratch, "layer-visibility.png"));
            tree.SelectedNode = WorkspaceFixture(skin, "layer-control-legacy", "Players/Steve (Minimal)", 64, 32, Color.Teal);
            Check(buttons[ModelPart.Helmet].Enabled && !buttons[ModelPart.ChestArmor].Enabled,
                "Legacy skins expose only the available outer parts");
            tree.SelectedNode = WorkspaceFixture(skin, "layer-control-cape", "Players/Cape", 64, 32, Color.Teal);
            Check(Field<TreeView>(editor, "treeView2").Visible && !buttons[ModelPart.Head].Visible,
                "Non-player models expose their named meshes in the same panel");
        }
        finally
        {
            if (button.Checked) button.PerformClick();
            originalTool.MenuItem.PerformClick();
            Call(editor, "SetMinecraftLighting", originalLighting);
            editor.ClientSize = size;
            tree.SelectedNode = skin;
        }
    }

    private static void OrientationCubeInput()
    {
        var mode = Field<ViewMode>(editor, "_currentViewMode");
        var skin = Field<Skin>(editor, "_lastSkin");
        bool dirty = skin.Dirty;
        var tool = editor.SelectedTool;
        bool infiniteMouse = GlobalSettings.InfiniteMouse;
        try
        {
            GlobalSettings.InfiniteMouse = false;
            Field<List<ToolIndex>>(editor, "_tools").Single(t => t.Tool is PencilTool).MenuItem.PerformClick();
            foreach (var view in new[] { ViewMode.Perspective, ViewMode.Hybrid })
            {
                Call(editor, "SetViewMode", view);
                editor.PerformResetCamera();
                var bounds = (Rectangle)Call(editor, "CubeBounds");
                var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                var corner = new Point(center.X + bounds.Width / 5, center.Y - bounds.Height / 5);
                var edge = new Point(corner.X, center.Y);
                Call(editor, "rendererControl_MouseLeave", editor.Renderer, EventArgs.Empty);
                using var plain = (Bitmap)Call(editor, "CopyScreenToBitmap");
                Hover(corner);
                using var hovered = (Bitmap)Call(editor, "CopyScreenToBitmap");
                Check(plain.GetPixel(corner.X, corner.Y) != hovered.GetPixel(corner.X, corner.Y) &&
                    plain.GetPixel(center.X, center.Y) == hovered.GetPixel(center.X, center.Y), "Only the hovered corner highlights in " + view);
                Hover(edge);
                using var edgeFrame = (Bitmap)Call(editor, "CopyScreenToBitmap");
                Check(plain.GetPixel(edge.X, edge.Y) != edgeFrame.GetPixel(edge.X, edge.Y) &&
                    plain.GetPixel(corner.X, corner.Y) == edgeFrame.GetPixel(corner.X, corner.Y) &&
                    plain.GetPixel(center.X, center.Y) == edgeFrame.GetPixel(center.X, center.Y), "The hover stripe isolates the edge in " + view);
                Hover(center);
                using var face = (Bitmap)Call(editor, "CopyScreenToBitmap");
                Check(plain.GetPixel(center.X, center.Y) != face.GetPixel(center.X, center.Y), "A hovered face highlights in " + view);
                Click(corner);
                float pitch = Field<float>(editor, "_3DRotationX"), yaw = Field<float>(editor, "_3DRotationY");
                Check(Math.Abs(pitch - 215.2644f) < .001f && yaw == -45 && !editor.GetPick(center.X, center.Y, out _),
                    "The upper-right front corner snaps to an isometric view and blocks skin picking");
                // From this corner, the original front face lies down and left of center.
                Click(new Point(center.X - bounds.Width / 6, center.Y + bounds.Height / 12));
                Check(Field<float>(editor, "_3DRotationX") == 180 && Field<float>(editor, "_3DRotationY") == 0 &&
                    !Field<bool>(editor, "_mouseIsDown") && skin.Dirty == dirty, "Clicking Front restores the straight-on camera without a stroke");
                Click(edge);
                Check(Field<float>(editor, "_3DRotationX") == 180 && Field<float>(editor, "_3DRotationY") == -45,
                    "A completed edge click snaps halfway between its two faces");
                editor.PerformResetCamera();
                Press(corner);
                var moved = new Point(corner.X - 15, corner.Y + 10);
                Drag(moved);
                float draggedYaw = Field<float>(editor, "_3DRotationY");
                Check(Math.Abs(draggedYaw + 15 * editor.ToolScale) < .001f &&
                    Math.Abs(Field<float>(editor, "_3DRotationX") - 180 - 10 * editor.ToolScale) < .001f,
                    "Dragging from the cube orbits from the original camera with the pencil selected");
                Release(moved);
                Check(draggedYaw == Field<float>(editor, "_3DRotationY") && skin.Dirty == dirty,
                    "Releasing a cube drag keeps the orbit angle without painting or snapping");
                editor.PerformResetCamera();
                Press(edge);
                Drag(moved);
                Drag(edge);
                Release(edge);
                Check(Math.Abs(Field<float>(editor, "_3DRotationY")) < .001f, "Returning a drag to its original edge does not turn it into a click");
                Press(edge);
                editor.Renderer.Capture = false;
                Release(edge);
                Check(Math.Abs(Field<float>(editor, "_3DRotationY")) < .001f, "Losing capture cancels a pending cube click");
                if (view == ViewMode.Perspective)
                {
                    var position = (IntPtr)((edge.Y << 16) | edge.X);
                    SendMessage(editor.Renderer.Handle, 0x0201, (IntPtr)1, position);
                    Check(editor.Renderer.Capture && Math.Abs(Field<float>(editor, "_3DRotationY")) < .001f,
                        "The viewport captures a native cube press without moving the camera");
                    SendMessage(editor.Renderer.Handle, 0x0202, IntPtr.Zero, position);
                    Check(!editor.Renderer.Capture && Field<float>(editor, "_3DRotationY") == -45,
                        "The native release completes the cube click and releases capture");
                    foreach (int side in new[] { 0, 90, 180, -90 })
                        foreach (int pitchAngle in new[] { 135, 180, 225 }) CheckSnap(pitchAngle, side);
                    CheckSnap(90, 0);
                    CheckSnap(270, 0);
                    foreach (int yawAngle in new[] { -135, -45, 45, 135 })
                        foreach (float pitchAngle in new[] { 144.7356f, 180, 215.2644f }) CheckSnap(pitchAngle, yawAngle);
                }

                void CheckSnap(float expectedPitch, float expectedYaw)
                {
                    SetField(editor, "_3DRotationX", expectedPitch + 3);
                    SetField(editor, "_3DRotationY", expectedYaw + 3);
                    Call(editor, "CalculateMatrices");
                    Click(center);
                    Check(Math.Abs(Field<float>(editor, "_3DRotationX") - expectedPitch) < .001f &&
                        Math.Abs(Math.IEEERemainder(Field<float>(editor, "_3DRotationY") - expectedYaw, 360)) < .001,
                        $"Clicking a near-aligned cube target snaps to pitch {expectedPitch}, yaw {expectedYaw}");
                }
            }
            Call(editor, "SetViewMode", ViewMode.Orthographic);
            Check((Vector3)Call(editor, "PickCube", editor.Renderer.Width - 60, 60) == Vector3.Zero, "2D input passes through the cube location");
        }
        finally
        {
            GlobalSettings.InfiniteMouse = infiniteMouse;
            tool.MenuItem.PerformClick();
            Call(editor, "rendererControl_MouseLeave", editor.Renderer, EventArgs.Empty);
            Call(editor, "SetViewMode", mode);
            editor.PerformResetCamera();
        }

        void Hover(Point p) => Call(editor, "rendererControl_MouseMove", editor.Renderer, new MouseEventArgs(MouseButtons.None, 0, p.X, p.Y, 0));
        void Drag(Point p) => Call(editor, "rendererControl_MouseMove", editor.Renderer, new MouseEventArgs(MouseButtons.Left, 0, p.X, p.Y, 0));
        void Press(Point p) => Call(editor, "rendererControl_MouseDown", editor.Renderer, new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
        void Release(Point p) => Call(editor, "rendererControl_MouseUp", editor.Renderer, new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
        void Click(Point p)
        {
            float yaw = Field<float>(editor, "_3DRotationY"), pitch = Field<float>(editor, "_3DRotationX");
            Press(p);
            Check(Field<float>(editor, "_3DRotationY") == yaw && Field<float>(editor, "_3DRotationX") == pitch,
                "Pressing a cube target leaves the camera in place until release");
            Release(p);
        }
    }
}
