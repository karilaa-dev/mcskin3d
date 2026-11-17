using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MCSkin3D;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using Paril.OpenGL;

internal static partial class Program
{
    private static void RenderQuality()
    {
        var tree = Field<TreeView>(editor, "treeView1");
        var original = Field<Skin>(editor, "_lastSkin");
        var savedRendering = PreviewRenderOptions.Current;
        var savedLayers = SkinLayerOptions.Current;
        bool was3D = GlobalSettings.SkinLayers3D;
        try
        {
            var skin = WorkspaceFixture(original, "lighting-quality", "Players/Alex", 64, 64, Color.FromArgb(211, 235, 234));
            using (var solid = new Bitmap(64, 64))
            {
                using (var graphics = Graphics.FromImage(solid)) graphics.Clear(Color.FromArgb(211, 235, 234));
                solid.Save(skin.File.FullName);
            }
            skin.SetImages(false);
            tree.SelectedNode = skin;
            Field<List<ToolIndex>>(editor, "_tools").Single(t => t.Tool is CameraTool).MenuItem.PerformClick();
            Field<ToolStripMenuItem>(editor, "grassToolStripMenuItem").Checked = false;
            Call(editor, "SetViewMode", ViewMode.Perspective);
            editor.PerformResetCamera();
            typeof(Editor).GetField("_3DRotationX", Private).SetValue(editor, 195f);
            typeof(Editor).GetField("_3DRotationY", Private).SetValue(editor, 25f);
            Call(editor, "CalculateMatrices");
            PreviewRenderOptions.Defaults.Save();
            SkinLayerOptions.Defaults.Save();
            editor.SetSkinLayers3D(true);
            var brightnessSlider = Field<TrackBar>(editor, "_brightnessSlider");
            var brightnessValue = Field<Label>(editor, "_brightnessValue");
            var lightingButton = Field<ToolStripButton>(editor, "_minecraftLightingButton");
            Check(lightingButton.Available && lightingButton.Checked && lightingButton.Text == "Minecraft Lighting" &&
                lightingButton.Owner == Field<ToolStrip>(editor, "toolStrip1"),
                "Viewport toolbar exposes the enabled, localized Minecraft Lighting toggle");
            var bar = Field<FlowLayoutPanel>(editor, "_workspaceBar");
            var optionsArea = Field<FlowLayoutPanel>(editor, "_toolOptionsArea");
            Check(brightnessSlider.Visible && brightnessSlider.Value == 8 && brightnessValue.Text == "8 / 15",
                "Main preview exposes a live brightness slider with a level 8 default");
            Check(!bar.Visible && optionsArea.Contains(brightnessSlider),
                "Single-skin brightness lives in the tool-options area, outside the library toolbar");
            Check(brightnessSlider.Minimum == 0 && brightnessSlider.Maximum == 15 && brightnessSlider.SmallChange == 1,
                "Brightness exposes exactly Minecraft's 16 integer light levels");
            editor.SetPreviewLightLevel(6);
            editor.SetSkinLayers3D(false);
            Check(!brightnessSlider.Visible && !bar.Visible, "Disabling 3D layers hides brightness and the empty toolbar");
            Check(!lightingButton.Available, "Disabling 3D layers hides the Minecraft Lighting toggle");
            editor.SetSkinLayers3D(true);
            Check(brightnessSlider.Visible && brightnessSlider.Value == 6, "Enabling 3D layers restores the saved brightness");
            Check(lightingButton.Available && lightingButton.Checked, "Enabling 3D layers restores the lighting toggle state");
            editor.SetPreviewLightLevel(8);
            var item = Field<List<PreviewItem>>(editor, "_scene").Single();
            for (int i = 0; i < item.Model.Meshes.Count; ++i)
                item.Model.PartsEnabled[i] = !item.Model.Meshes[i].IsArmor;
            using (var frame = (Bitmap)Call(editor, "CopyScreenToBitmap"))
                CheckHeadColors(frame, item.Model.Meshes.Single(m => m.Part == ModelPart.Head), "Base skin");
            Array.Fill(item.Model.PartsEnabled, true);
            Call(editor, "CalculateMatrices");
            using var native = (Bitmap)Call(editor, "CopyScreenToBitmap");
            var helmet = item.SkinLayers.Values.Single(l => l.Mesh.Part == ModelPart.Helmet);
            CheckHeadColors(native, helmet.Mesh, "Raised skin");
            var face = helmet.Mesh.Faces.Single(f => f.Normal.Z < -.99f);
            Point location = QualityFacePoint(helmet.Mesh, face);
            Check(editor.GetPick(location.X, location.Y, out var sourcePixel), "Quality fixture has a paintable raised pixel");
            var geometry = helmet.Mesh;
            var target = Field<PreviewRenderTarget>(editor, "_previewRenderTarget");
            foreach (var quality in new[] { (50, 0), (100, 2), (100, 4), (100, 8), (150, 4), (200, 0) })
            {
                (PreviewRenderOptions.Defaults with { Resolution = quality.Item1, Samples = quality.Item2 }).Save();
                using var frame = (Bitmap)Call(editor, "CopyScreenToBitmap");
                frame.Save(Path.Combine(scratch, $"quality-{quality.Item1}-{quality.Item2}.png"));
                Check(target.Width == (int)Math.Round(editor.Renderer.Width * quality.Item1 / 100d) &&
                    target.Height == (int)Math.Round(editor.Renderer.Height * quality.Item1 / 100d), "Resolution changes GPU render dimensions: " + quality);
                Check(target.Samples == Math.Min(quality.Item2, GL.GetInteger(GetPName.MaxSamples)), "MSAA sample count is applied live: " + quality);
                Check(GL.GetInteger(GetPName.ReadFramebufferBinding) == 0 && GL.GetInteger(GetPName.DrawFramebufferBinding) == 0 &&
                    GL.GetError() == ErrorCode.NoError, "Resolve and rescale restore framebuffer state: " + quality);
                Check(ReferenceEquals(geometry, helmet.Mesh) && editor.GetPick(location.X, location.Y, out var pixel) && pixel == sourcePixel,
                    "Quality preserves skin geometry and editing coordinates: " + quality);
                CheckHeadColors(frame, helmet.Mesh, "Quality " + quality);
                int buffer = Field<int>(target, "framebuffer");
                using var repeat = (Bitmap)Call(editor, "CopyScreenToBitmap");
                Check(buffer != 0 && buffer == Field<int>(target, "framebuffer"), "Steady frames reuse GPU targets: " + quality);
                Check(FrameDifference(native, frame, new Rectangle(Point.Empty, native.Size)) > 0, "Quality changes actual rasterized pixels: " + quality);
            }
            // Exercise a real edit while rendering to a scaled multisample target.
            (PreviewRenderOptions.Defaults with { Resolution = 150, Samples = 4 }).Save();
            MCSkin3D.Brushes.BrushBox.SelectedItem = MCSkin3D.Brushes.BrushList.First(b => b.Width == 1 && b.Height == 1);
            editor.ColorPanel.SelectedColor = MCSkin3D.lemon42.ColorManager.FromRGBA(255, 0, 255, 255);
            var tools = Field<List<ToolIndex>>(editor, "_tools");
            tools.Single(t => t.Tool is PencilTool).MenuItem.PerformClick();
            // Tool groups can wrap and resize the viewport; pick in its current layout.
            location = QualityFacePoint(helmet.Mesh, face);
            Check(editor.GetPick(location.X, location.Y, out sourcePixel), "Paint target follows the tool-options layout");
            ClickLayer(location);
            Check(ReadDocumentPixel(skin, sourcePixel) == new ColorPixel(255, 0, 255, 255).RGBA,
                "Painting with scaled MSAA writes exact RGB to the picked source texel");
            Call(editor, "PerformUndo");
            tools.Single(t => t.Tool is CameraTool).MenuItem.PerformClick();
            location = QualityFacePoint(helmet.Mesh, face);
            Check(ReadDocumentPixel(skin, sourcePixel) == new ColorPixel(211, 235, 234, 255).RGBA,
                "Undo restores the original texture independently of preview lighting");
            PreviewRenderOptions.Defaults.Save();
            using (var reset = (Bitmap)Call(editor, "CopyScreenToBitmap"))
            {
                int changed = FrameDifference(native, reset, new Rectangle(Point.Empty, native.Size));
                if (changed != 0)
                {
                    native.Save(Path.Combine(scratch, "quality-native.png")); reset.Save(Path.Combine(scratch, "quality-reset.png"));
                    Log($"Reset changed {changed} pixels; remaining framebuffer {Field<int>(target, "framebuffer")}");
                }
                Check(changed == 0 && Field<int>(target, "framebuffer") == 0,
                    "Reset restores the original frame and releases quality buffers");
            }

            foreach (int brightness in Enumerable.Range(0, 16))
            {
                brightnessSlider.Value = brightness;
                Check(GlobalSettings.PreviewLightLevel == brightness && brightnessValue.Text == brightness + " / 15",
                    "Main slider immediately updates brightness and its 0-15 level label");
                using var frame = (Bitmap)Call(editor, "CopyScreenToBitmap");
                CheckHeadColors(frame, helmet.Mesh, "Brightness " + brightness, brightness);
                Check(ReferenceEquals(geometry, helmet.Mesh), "Changing brightness updates cached colors without rebuilding the skin");
            }
            lightingButton.PerformClick();
            using (var settings = new MCSkin3D.Forms.SkinLayerSettingsDialog(SkinLayerOptions.Current))
            {
                Check(!settings.Rendering.MinecraftLighting, "Advanced settings reflect changes made by the toolbar toggle");
                typeof(Button).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(
                    settings.Controls.Find("resetDefaults", true).Single(), new object[] { EventArgs.Empty });
                settings.Rendering.Save();
            }
            Call(editor, "RefreshWorkspaceControls");
            Check(brightnessSlider.Value == 8 && brightnessValue.Text == "8 / 15", "Applying settings Reset synchronizes the main slider to level 8");
            Check(lightingButton.Checked && lightingButton.Available, "Applying settings Reset synchronizes the lighting toggle");
            SaveBrightnessBar(optionsArea, "brightness-toolbar");
            SaveBrightnessBar(lightingButton.Owner, "minecraft-lighting-toolbar");
            var beforeToggle = PreviewRenderOptions.Current;
            lightingButton.PerformClick();
            Check(!lightingButton.Checked && lightingButton.Available &&
                PreviewRenderOptions.Current == (beforeToggle with { MinecraftLighting = false }),
                "Toolbar toggle disables lighting while preserving brightness and quality preferences");
            Check(!brightnessSlider.Visible && !bar.Visible, "Turning off directional shading hides its brightness slider");
            using (var unlit = (Bitmap)Call(editor, "CopyScreenToBitmap"))
            {
                var unlitLocation = QualityFacePoint(helmet.Mesh, face);
                Check(NearColor(unlit.GetPixel(unlitLocation.X, unlitLocation.Y), Color.FromArgb(211, 235, 234)), "Unlit mode displays exact texture RGB");
            }
            lightingButton.PerformClick();
            Check(lightingButton.Checked && GlobalSettings.MinecraftLighting && ReferenceEquals(geometry, helmet.Mesh),
                "Toolbar toggle restores lighting without rebuilding skin geometry");
            Check(brightnessSlider.Visible && brightnessSlider.Value == 8, "Enabling shading restores brightness at its retained value");
            var client = editor.MeshRenderer;
            typeof(Editor).GetProperty("MeshRenderer", Private).SetValue(editor, new ImmediateRenderer());
            try
            {
                using var immediate = (Bitmap)Call(editor, "CopyScreenToBitmap");
                Check(FrameDifference(native, immediate, new Rectangle(Point.Empty, native.Size)) == 0, "Both renderers produce the same Minecraft lighting");
            }
            finally { typeof(Editor).GetProperty("MeshRenderer", Private).SetValue(editor, client); }
            foreach (var mode in new[] { ViewMode.Orthographic, ViewMode.Hybrid })
            {
                Call(editor, "SetViewMode", mode);
                Check(brightnessSlider.Visible == (mode == ViewMode.Hybrid) && !bar.Visible,
                    "Brightness is visible only when the view includes 3D: " + mode);
                Check(lightingButton.Available == (mode == ViewMode.Hybrid) && lightingButton.Checked,
                    "Lighting toggle follows the 3D view while retaining its state: " + mode);
                PreviewRenderOptions.Defaults.Save();
                using var before = (Bitmap)Call(editor, "CopyScreenToBitmap");
                (PreviewRenderOptions.Defaults with { Resolution = 200, Samples = 4 }).Save();
                using var after = (Bitmap)Call(editor, "CopyScreenToBitmap");
                Check(FrameDifference(before, after, editor.GetViewport2D()) == 0, "2D texture remains pixel-identical at high quality: " + mode);
            }
            editor.SetCombinedMode(true);
            editor.SetAccessoryView(AccessoryView.Elytra);
            var wingSlider = Field<TrackBar>(editor, "_wingSpreadSlider");
            Check(brightnessSlider.Visible && wingSlider.Visible,
                "Combined elytra view exposes both sliders together");
            Check(optionsArea.Controls.Cast<Control>().Where(c => c.Visible).All(c => c.Right <= optionsArea.Width && c.Bottom <= optionsArea.Height),
                "Combined preview controls wrap without clipping brightness");
            SaveBrightnessBar(optionsArea, "brightness-and-elytra-toolbar");
            var originalSize = editor.ClientSize;
            foreach (int width in new[] { 1500, 1000, 800 })
            {
                editor.ClientSize = new Size(width, 950);
                foreach (var tool in tools)
                {
                    tool.MenuItem.PerformClick();
                    optionsArea.PerformLayout();
                    Check(brightnessSlider.Visible && wingSlider.Visible &&
                        (tool.OptionsPanel == null || optionsArea.Controls.Contains(tool.OptionsPanel)),
                        "Tool switching preserves preview options: " + tool.Tool.GetType().Name);
                    var visible = optionsArea.Controls.Cast<Control>().Where(c => c.Visible).ToArray();
                    if (visible.Any(c => c.Right > optionsArea.ClientSize.Width || c.Bottom > optionsArea.ClientSize.Height))
                    {
                        Log("Tool layout " + optionsArea.ClientSize + ": " + string.Join("; ", visible.Select(c => c.Name + " " + c.Bounds)));
                        SaveBrightnessBar(optionsArea, "tool-layout-overflow");
                    }
                    Check(visible.All(c => c.Right <= optionsArea.ClientSize.Width && c.Bottom <= optionsArea.ClientSize.Height) &&
                        visible.SelectMany((c, i) => visible.Skip(i + 1).Select(other => c.Bounds.IntersectsWith(other.Bounds))).All(overlap => !overlap),
                        "Tool and preview groups fit without overlap at width " + width + ": " + tool.Tool.GetType().Name);
                    if (tool.Tool is FloodFillTool) SaveBrightnessBar(optionsArea, "fill-preview-options-" + width);
                }
            }
            editor.ClientSize = originalSize;
            tools.Single(t => t.Tool is FloodFillTool).MenuItem.PerformClick();
            var expand = Field<ToolStripButton>(editor, "toolStripButton1");
            expand.PerformClick();
            Check(!brightnessSlider.Visible && !wingSlider.Visible, "Collapsing tool options hides both preview controls");
            expand.PerformClick();
            Check(brightnessSlider.Visible && wingSlider.Visible, "Expanding tool options restores both preview controls");
            editor.SetPreviewLightLevel(6);
            editor.SetWingSpread(.63f);
            Field<ToolStripButton>(editor, "orthographicToolStripButton").PerformClick();
            Check(!brightnessSlider.Visible && !wingSlider.Visible && bar.Visible,
                "Texture view hides both 3D sliders while keeping combined document controls");
            SaveBrightnessBar(optionsArea, "texture-view-toolbar");
            Field<ToolStripMenuItem>(editor, "perspectiveToolStripMenuItem").PerformClick();
            Check(brightnessSlider.Visible && wingSlider.Visible && brightnessSlider.Value == 6 && wingSlider.Value == 63,
                "Returning to perspective restores both sliders without losing their values");
            editor.SetAccessoryView(AccessoryView.Cape);
            Check(!wingSlider.Visible && brightnessSlider.Visible, "Cape mode hides only the elytra slider");
            editor.SetAccessoryView(AccessoryView.Elytra);
            editor.SetSkinLayers3D(false);
            Check(!brightnessSlider.Visible && wingSlider.Visible, "Elytra controls remain available with 3D skin layers disabled");
            editor.SetSkinLayers3D(true);
            editor.SetCombinedMode(false);
            Check(!wingSlider.Visible && brightnessSlider.Visible, "Leaving combined mode hides the elytra slider");
        }
        finally
        {
            savedRendering.Save(); savedLayers.Save();
            editor.SetSkinLayers3D(was3D);
            tree.SelectedNode = original;
            Call(editor, "SetViewMode", ViewMode.Perspective);
            editor.PerformResetCamera();
        }
    }

    private static Point QualityFacePoint(Mesh mesh, Face face)
    {
        var p = face.Vertices.Aggregate(Vector3.Zero, (sum, v) => sum + v) / face.Vertices.Length;
        var clip = Vector4.TransformRow(new Vector4(Vector3.TransformPosition(p, mesh.Matrix), 1), Field<Matrix4>(editor, "_projectionMatrix"));
        return new Point((int)((clip.X / clip.W + 1) * editor.Renderer.Width / 2), (int)((1 - clip.Y / clip.W) * editor.Renderer.Height / 2));
    }

    private static void CheckHeadColors(Bitmap frame, Mesh mesh, string label, int brightness = 8)
    {
        // Reference RGBA8 lightmap texels from Mojang's lightmap.fsh at
        // sky levels 0..15, block light 0, Minecraft Overworld noon, gamma 1, ambient #0a0a0a.
        int[] lightmap = { 38, 53, 69, 86, 104, 122, 141, 160, 179, 198, 215, 231, 244, 252, 255, 255 };
        // Independent coefficients from the fixed vanilla Overworld light directions.
        Color Lit(double shade) => Color.FromArgb((int)Math.Round(211 * shade * lightmap[brightness] / 255),
            (int)Math.Round(235 * shade * lightmap[brightness] / 255), (int)Math.Round(234 * shade * lightmap[brightness] / 255));
        var front = Lit(.739549875);
        var side = Lit(.49701425);
        foreach (var sample in new[] { (new Vector3(0, 0, -1), front),
            (new Vector3(-1, 0, 0), side), (new Vector3(0, -1, 0), Lit(1)) })
        {
            var face = mesh.Faces.Single(f => Vector3.Dot(f.Normal, sample.Item1) > .99f);
            var point = QualityFacePoint(mesh, face);
            var color = frame.GetPixel(point.X, point.Y);
            Check(NearColor(color, sample.Item2), $"{label} {sample.Item1} uses Minecraft RGB: {color} expected {sample.Item2}");
        }
    }

    private static void SaveBrightnessBar(Control bar, string name)
    {
        bar.PerformLayout();
        using var bitmap = new Bitmap(bar.Width, bar.Height);
        bar.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(Path.Combine(scratch, name + ".png"));
    }

    private static bool NearColor(Color a, Color b) => Math.Abs(a.R - b.R) <= 1 && Math.Abs(a.G - b.G) <= 1 && Math.Abs(a.B - b.B) <= 1;
    private static int FrameDifference(Bitmap a, Bitmap b, Rectangle area)
    {
        int changed = 0;
        for (int y = area.Top; y < Math.Min(a.Height, area.Bottom); ++y)
            for (int x = area.Left; x < Math.Min(a.Width, area.Right); ++x)
                if (a.GetPixel(x, y).ToArgb() != b.GetPixel(x, y).ToArgb()) ++changed;
        return changed;
    }
}
