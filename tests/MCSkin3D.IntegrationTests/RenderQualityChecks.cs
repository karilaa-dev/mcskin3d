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
    private static void BenchmarkLayerRendering()
    {
        var tree = Field<TreeView>(editor, "treeView1");
        var original = Field<Skin>(editor, "_lastSkin");
        var rendering = PreviewRenderOptions.Current;
        bool layers = GlobalSettings.SkinLayers3D;
        bool benchmark = GlobalSettings.RenderBenchmark;
        bool screenshot = Field<bool>(editor, "_screenshotMode");
        var paint = (Action<object, PaintEventArgs>)typeof(Editor).GetMethod("rendererControl_Paint", Private)
            .CreateDelegate(typeof(Action<object, PaintEventArgs>), editor);
        try
        {
            PreviewRenderOptions.Defaults.Save();
            Field<List<ToolIndex>>(editor, "_tools").Single(t => t.Tool is CameraTool).MenuItem.PerformClick();
            Call(editor, "SetViewMode", ViewMode.Perspective);
            typeof(Editor).GetField("_screenshotMode", Private).SetValue(editor, true);
            GlobalSettings.RenderBenchmark = true;
            foreach (int size in new[] { 64, 128 })
            {
                var skin = WorkspaceFixture(original, "render-benchmark-" + size, "Players/Alex", size, size, Color.CornflowerBlue);
                using (var image = new Bitmap(size, size))
                {
                    using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.CornflowerBlue);
                    foreach (var mesh in skin.Model.Meshes.Where(m => m.IsArmor))
                        foreach (var face in mesh.Faces)
                        {
                            var rect = face.TexCoordsToInteger(size, size);
                            for (int y = rect.Top; y < rect.Bottom; ++y)
                                for (int x = rect.Left; x < rect.Right; ++x)
                                    image.SetPixel(x, y, (x + y) % 3 == 0 ? Color.Coral : (x + y) % 7 == 0 ? Color.FromArgb(128, Color.White) : Color.Transparent);
                        }
                    image.Save(skin.File.FullName);
                }
                skin.SetImages(false); tree.SelectedNode = skin;
                editor.SetSkinLayers3D(true); editor.PerformResetCamera();
                typeof(Editor).GetField("_screenshotMode", Private).SetValue(editor, true);
                for (int i = 0; i < 20; i++) { paint(editor.Renderer, null); GL.Finish(); }
                Editor._compileTimer.Reset(); Editor._batchTimer.Reset();
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                var timer = System.Diagnostics.Stopwatch.StartNew();
                const int frames = 200;
                for (int i = 0; i < frames; i++) { paint(editor.Renderer, null); GL.Finish(); }
                timer.Stop();
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                Log($"3D layers {size}: {timer.Elapsed.TotalMilliseconds / frames:F3} ms/frame, " +
                    $"{Editor._compileTimer.Elapsed.TotalMilliseconds / frames:F3} ms preparation, " +
                    $"{Editor._batchTimer.Elapsed.TotalMilliseconds / frames:F3} ms submission, {allocated / frames} managed bytes/frame");
                using var frame = (Bitmap)Call(editor, "CopyScreenToBitmap");
                frame.Save(Path.Combine(scratch, "render-benchmark-" + size + ".png"));
                Check(GL.GetError() == ErrorCode.NoError, "Benchmark renders without GL errors at size " + size);
            }
        }
        finally
        {
            GlobalSettings.RenderBenchmark = benchmark;
            typeof(Editor).GetField("_screenshotMode", Private).SetValue(editor, screenshot);
            rendering.Save(); editor.SetSkinLayers3D(layers); tree.SelectedNode = original;
        }
    }

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
            var surroundings = Field<CheckBox>(editor, "_lightSurroundingsToggle");
            var angleSlider = Field<TrackBar>(editor, "_lightAngleSlider");
            var angleValue = Field<NumericUpDown>(editor, "_lightAngleValue");
            var angleDefault = Field<Button>(editor, "_lightAngleDefault");
            var lightingButton = Field<ToolStripButton>(editor, "_minecraftLightingButton");
            Check(lightingButton.Available && lightingButton.Checked && lightingButton.Text == "Minecraft Lighting" &&
                lightingButton.Owner == Field<ToolStrip>(editor, "toolStrip1"),
                "Viewport toolbar exposes the enabled, localized Minecraft Lighting toggle");
            var bar = Field<FlowLayoutPanel>(editor, "_workspaceBar");
            var optionsArea = Field<FlowLayoutPanel>(editor, "_toolOptionsArea");
            Check(brightnessSlider.Visible && brightnessSlider.Value == 8 && brightnessValue.Text == "8",
                "Main preview exposes a live brightness slider with a level 8 default");
            Check(!bar.Visible && optionsArea.Contains(brightnessSlider),
                "Single-skin brightness lives in the tool-options area, outside the library toolbar");
            Check(angleSlider.Visible && optionsArea.Contains(angleSlider) && angleSlider.Minimum == -90 && angleSlider.Maximum == 90 &&
                angleSlider.Value == 0 && angleValue.Visible && angleValue.Minimum == -90 && angleValue.Maximum == 90 &&
                angleValue.Value == 0 && angleDefault.Text == "Default" && !angleDefault.Enabled,
                "Tool options expose synchronized angle slider and numeric input with a localized Default button");
            var angleGroup = Field<Control>(editor, "_lightAngleGroup");
            var brightnessGroup = Field<Control>(editor, "_brightnessGroup");
            var lightingColumn = Field<FlowLayoutPanel>(editor, "_lightingOptionsColumn");
            Check(brightnessSlider.Width < angleSlider.Width && Math.Abs(angleGroup.Height - brightnessGroup.Height) <= 2 &&
                lightingColumn.Height + lightingColumn.Margin.Vertical <= optionsArea.ClientSize.Height,
                "Both compact lighting rows fit within the normal tool-options height with more slider space reserved for the angle");
            Check(lightingColumn.Parent == optionsArea && brightnessGroup.Parent == lightingColumn && angleGroup.Parent == lightingColumn &&
                brightnessGroup.Left == angleGroup.Left && brightnessGroup.Bottom <= angleGroup.Top,
                "Brightness and lighting angle stay stacked in one left-aligned tool-options column");
            Check(brightnessGroup.Contains(surroundings) && surroundings.Visible && surroundings.Checked &&
                !Controls(editor).Any(control => control.Name == "gameBrightnessInput"),
                "Brightness contains enabled surroundings lighting without a separate Game brightness control");
            Check(brightnessSlider.Minimum == 0 && brightnessSlider.Maximum == 15 && brightnessSlider.SmallChange == 1,
                "Brightness exposes exactly Minecraft's 16 integer light levels");
            int toolOptionsHeight = optionsArea.Height;
            var viewportBeforeToggle = editor.Renderer.RectangleToScreen(editor.Renderer.ClientRectangle);
            lightingButton.PerformClick();
            editor.PerformLayout(); optionsArea.PerformLayout();
            Check(!lightingColumn.Visible && optionsArea.Height == toolOptionsHeight &&
                editor.Renderer.RectangleToScreen(editor.Renderer.ClientRectangle) == viewportBeforeToggle,
                "Hiding the two lighting rows preserves the normal camera tool-options and viewport height");
            lightingButton.PerformClick();
            editor.PerformLayout(); optionsArea.PerformLayout();
            Check(lightingColumn.Visible && optionsArea.Height == toolOptionsHeight &&
                editor.Renderer.RectangleToScreen(editor.Renderer.ClientRectangle) == viewportBeforeToggle,
                "Showing the two lighting rows preserves the normal camera tool-options and viewport height");
            editor.SetPreviewLightLevel(6);
            editor.SetSkinLayers3D(false);
            Check(brightnessSlider.Visible && brightnessSlider.Value == 6 && !bar.Visible,
                "Regular skin view retains the brightness slider and its saved level");
            Check(lightingButton.Available && lightingButton.Checked, "Regular skin view exposes the enabled Minecraft Lighting toggle");
            RegularSkinLighting();
            editor.SetPreviewLightLevel(6);
            editor.SetSkinLayers3D(true);
            Check(brightnessSlider.Visible && brightnessSlider.Value == 6, "Enabling 3D layers retains the saved brightness");
            Check(lightingButton.Available && lightingButton.Checked, "Enabling 3D layers retains the lighting toggle state");
            editor.SetPreviewLightLevel(8);
            var item = Field<List<PreviewItem>>(editor, "_scene").Single();
            for (int i = 0; i < item.Model.Meshes.Count; ++i)
                item.Model.PartsEnabled[i] = !item.Model.Meshes[i].IsArmor;
            using (var frame = (Bitmap)Call(editor, "CopyScreenToBitmap"))
                CheckHeadColors(frame, item.Model.Meshes.Single(m => m.Part == ModelPart.Head), "Base skin");
            Array.Fill(item.Model.PartsEnabled, true);
            Call(editor, "CalculateMatrices");
            using var native = (Bitmap)Call(editor, "CopyScreenToBitmap");
            var previewTexture = Field<Texture>(editor, "_previewPaint");
            long previewVersion = previewTexture.ContentVersion;
            for (int i = 0; i < 3; i++)
            {
                using var repeat = (Bitmap)Call(editor, "CopyScreenToBitmap");
                Check(FrameDifference(native, repeat, new Rectangle(Point.Empty, native.Size)) == 0 && previewTexture.ContentVersion == previewVersion,
                    "Steady camera frames reuse the preview texture without changing rendered pixels");
            }
            var helmet = item.SkinLayers.Values.Single(l => l.Mesh.Part == ModelPart.Helmet);
            CheckHeadColors(native, helmet.Mesh, "Raised skin");
            CheckLightingAngle(native, helmet.Mesh, "Raised skin");
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
            typeof(Editor).GetField("_mousePoint", Private).SetValue(editor, location);
            Call(editor, "SetPreview");
            using (var hover = new ColorGrabber(previewTexture, skin.Width, skin.Height))
            {
                hover.Load();
                Check(hover[sourcePixel.X, sourcePixel.Y].RGBA == new ColorPixel(255, 0, 255, 255).RGBA &&
                    ReadDocumentPixel(skin, sourcePixel) == new ColorPixel(211, 235, 234, 255).RGBA,
                    "Hover preview updates its cached texture without painting the document");
                typeof(Editor).GetField("_mousePoint", Private).SetValue(editor, new Point(-1, -1));
                Call(editor, "SetPreview"); hover.Load();
                Check(hover[sourcePixel.X, sourcePixel.Y].RGBA == new ColorPixel(211, 235, 234, 255).RGBA,
                    "Leaving the model clears cached hover pixels without a document edit");
            }
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
                Check(GlobalSettings.PreviewLightLevel == brightness && brightnessValue.Text == brightness.ToString(),
                    "Main slider immediately updates brightness and its 0-15 level label");
                using var frame = (Bitmap)Call(editor, "CopyScreenToBitmap");
                CheckHeadColors(frame, helmet.Mesh, "Brightness " + brightness, brightness);
                Check(ReferenceEquals(geometry, helmet.Mesh), "Changing brightness updates cached colors without rebuilding the skin");
                if (brightness == 15)
                {
                    SaveBrightnessBar(optionsArea, "brightness-level-15");
                    Check(brightnessValue.Text == "15" && brightnessValue.Width >= brightnessValue.GetPreferredSize(Size.Empty).Width &&
                        brightnessValue.Parent.ClientRectangle.Contains(brightnessValue.Bounds),
                        "Maximum brightness displays the current integer without clipping or a redundant maximum");
                }
            }
            lightingButton.PerformClick();
            editor.SetPreviewLightAngle(45);
            editor.SetPreviewLightSurroundings(false);
            var lightingBeforeReset = PreviewRenderOptions.Current;
            using (var settings = new MCSkin3D.Forms.SkinLayerSettingsDialog(SkinLayerOptions.Current))
            {
                Check(settings.Controls.Find("minecraftLighting", true).Length == 0 && settings.Controls.Find("gameBrightness", true).Length == 0 &&
                    settings.Controls.Find("lightAngleSlider", true).Length == 0 && settings.Controls.Find("brightnessSlider", true).Length == 0,
                    "3D Skin Layers settings contain no lighting controls");
                typeof(Button).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(
                    settings.Controls.Find("resetDefaults", true).Single(), new object[] { EventArgs.Empty });
                settings.Rendering.Save();
            }
            Call(editor, "RefreshWorkspaceControls");
            Check(PreviewRenderOptions.Current == (lightingBeforeReset with { Resolution = 100, Samples = 0 }) &&
                !lightingButton.Checked && brightnessSlider.Value == 15 && angleSlider.Value == 45 && !surroundings.Checked,
                "Resetting layer settings preserves the lighting toggle, world light, angle and surroundings preference");
            PreviewRenderOptions.Defaults.Save();
            Call(editor, "RefreshWorkspaceControls");
            SaveBrightnessBar(optionsArea, "brightness-toolbar");
            SaveBrightnessBar(lightingButton.Owner, "minecraft-lighting-toolbar");
            var beforeToggle = PreviewRenderOptions.Current;
            lightingButton.PerformClick();
            Check(!lightingButton.Checked && lightingButton.Available &&
                PreviewRenderOptions.Current == (beforeToggle with { MinecraftLighting = false }),
                "Toolbar toggle disables lighting while preserving brightness and quality preferences");
            Check(!brightnessSlider.Visible && !surroundings.Visible && !angleSlider.Visible && !lightingColumn.Visible && !bar.Visible,
                "Turning off directional shading hides all lighting controls");
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
                Check(brightnessSlider.Visible == (mode == ViewMode.Hybrid) && surroundings.Visible == (mode == ViewMode.Hybrid) &&
                    lightingColumn.Visible == (mode == ViewMode.Hybrid) &&
                    angleSlider.Visible == (mode == ViewMode.Hybrid) && !bar.Visible,
                    "Lighting controls are visible only when the view includes 3D: " + mode);
                Check(lightingButton.Available == (mode == ViewMode.Hybrid) && lightingButton.Checked,
                    "Lighting toggle follows the 3D view while retaining its state: " + mode);
                PreviewRenderOptions.Defaults.Save();
                using var before = (Bitmap)Call(editor, "CopyScreenToBitmap");
                (PreviewRenderOptions.Defaults with { Resolution = 200, Samples = 4 }).Save();
                using var after = (Bitmap)Call(editor, "CopyScreenToBitmap");
                CheckTextureFrameUnchanged(before, after, "2D texture remains pixel-identical at high quality: " + mode);
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
                    Check(brightnessSlider.Visible && surroundings.Visible && angleSlider.Visible && angleDefault.Visible && wingSlider.Visible &&
                        (tool.OptionsPanel == null || optionsArea.Controls.Contains(tool.OptionsPanel)),
                        "Tool switching preserves preview options: " + tool.Tool.GetType().Name);
                    var visible = optionsArea.Controls.Cast<Control>().Where(c => c.Visible).ToArray();
                    if (visible.Any(c => c.Right > optionsArea.ClientSize.Width || c.Bottom > optionsArea.ClientSize.Height))
                    {
                        Log("Tool layout " + optionsArea.ClientSize + ": " + string.Join("; ", visible.Select(c => c.Name + " " + c.Bounds)));
                        SaveBrightnessBar(optionsArea, "tool-layout-overflow");
                    }
                    Check(visible.All(c => c.Right <= optionsArea.ClientSize.Width && c.Bottom <= optionsArea.ClientSize.Height) &&
                        visible.SelectMany((c, i) => visible.Skip(i + 1).Select(other => c.Bounds.IntersectsWith(other.Bounds))).All(overlap => !overlap) &&
                        lightingColumn.ClientRectangle.Contains(brightnessGroup.Bounds) && lightingColumn.ClientRectangle.Contains(angleGroup.Bounds) &&
                        brightnessGroup.Left == angleGroup.Left && brightnessGroup.Bottom <= angleGroup.Top,
                        "Tool groups fit without overlap and lighting stays vertically aligned at width " + width + ": " + tool.Tool.GetType().Name);
                    if (tool.Tool is FloodFillTool) SaveBrightnessBar(optionsArea, "fill-preview-options-" + width);
                }
            }
            editor.ClientSize = originalSize;
            tools.Single(t => t.Tool is FloodFillTool).MenuItem.PerformClick();
            var expand = Field<ToolStripButton>(editor, "toolStripButton1");
            expand.PerformClick();
            Check(!brightnessSlider.Visible && !surroundings.Visible && !angleSlider.Visible && !lightingColumn.Visible && !wingSlider.Visible,
                "Collapsing tool options hides all preview controls");
            expand.PerformClick();
            Check(brightnessSlider.Visible && surroundings.Visible && angleSlider.Visible && lightingColumn.Visible && wingSlider.Visible,
                "Expanding tool options restores all preview controls");
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
            Check(brightnessSlider.Visible && wingSlider.Visible, "Brightness and elytra controls remain available with 3D skin layers disabled");
            editor.SetSkinLayers3D(true);
            editor.SetCombinedMode(false);
            Check(!wingSlider.Visible && brightnessSlider.Visible, "Leaving combined mode hides the elytra slider");
            CheckSurroundingsLighting();
            PlayerLightingToolbar();
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

    private static void PlayerLightingToolbar()
    {
        var original = Field<Skin>(editor, "_lastSkin");
        var tree = Field<TreeView>(editor, "treeView1");
        var originalSize = editor.ClientSize;
        var rendering = PreviewRenderOptions.Current;
        bool layers = GlobalSettings.SkinLayers3D;
        var toolbar = Field<ToolStrip>(editor, "toolStrip1");
        var lightingButton = Field<ToolStripButton>(editor, "_minecraftLightingButton");
        var layersButton = Field<ToolStripButton>(editor, "_skinLayersButton");
        try
        {
            PreviewRenderOptions.Defaults.Save();
            editor.SetSkinLayers3D(false);
            foreach (string name in new[] { "Steve", "Alex", "Steve (Minimal)", "Alex (Minimal)" })
            {
                var model = ModelLoader.GetModelForPath("Players/" + name);
                var skin = WorkspaceFixture(original, "lighting-toolbar-" + name, "Players/" + name,
                    (int)model.DefaultWidth, (int)model.DefaultHeight, Color.CornflowerBlue);
                tree.SelectedNode = skin;
                foreach (int width in new[] { 1100, 800 })
                {
                    editor.ClientSize = new Size(width, 950);
                    editor.PerformLayout(); toolbar.PerformLayout();
                    foreach (var button in new[] { layersButton, lightingButton })
                    {
                        string text = button == layersButton ? "3D Skin Layers" : "Minecraft Lighting";
                        Check(button.DisplayStyle == ToolStripItemDisplayStyle.Image && button.Image != null &&
                            button.Text == text && button.AccessibleName == text && !string.IsNullOrEmpty(button.ToolTipText),
                            name + " keeps the " + text + " icon labeled for accessibility and tooltips");
                        Check(button.Available && button.Visible && button.Placement == ToolStripItemPlacement.Main &&
                            !button.IsOnOverflow && toolbar.ClientRectangle.Contains(button.Bounds),
                            name + " keeps " + text + " visibly on the main toolbar at " + width + "px");
                    }
                    Check(toolbar.Items.IndexOf(lightingButton) < toolbar.Items.IndexOf(layersButton) &&
                        lightingButton.Bounds.Right <= layersButton.Bounds.Left,
                        name + " places Minecraft Lighting before 3D Skin Layers at " + width + "px");
                }
                using (var lit = (Bitmap)Call(editor, "CopyScreenToBitmap")) { }
                var item = Field<List<PreviewItem>>(editor, "_scene").Single();
                var meshes = item.Model.Meshes.Where((_, i) => item.Model.PartsEnabled[i]).ToArray();
                Check(PreviewItem.Role(skin) == EditingTarget.Skin && GlobalSettings.MinecraftLighting && lightingButton.Checked &&
                    meshes.Length > 0 && meshes.All(mesh => mesh.ShadeFaces),
                    "Selecting " + name + " retains active lighting on the regular skin meshes");
                lightingButton.PerformClick();
                using (var unlit = (Bitmap)Call(editor, "CopyScreenToBitmap")) { }
                Check(!GlobalSettings.MinecraftLighting && !lightingButton.Checked && meshes.All(mesh => !mesh.ShadeFaces),
                    name + " main-toolbar lighting icon disables mesh shading");
                lightingButton.PerformClick();
                using (var relit = (Bitmap)Call(editor, "CopyScreenToBitmap")) { }
                Check(GlobalSettings.MinecraftLighting && lightingButton.Checked && meshes.All(mesh => mesh.ShadeFaces) && !skin.Dirty,
                    name + " main-toolbar lighting icon restores shading without editing the skin");
                if (name == "Alex (Minimal)") SaveBrightnessBar(toolbar, "minimal-lighting-toolbar-800");
            }
        }
        finally
        {
            rendering.Save();
            tree.SelectedNode = original;
            editor.ClientSize = originalSize;
            editor.SetSkinLayers3D(layers);
        }
    }

    private static void RegularSkinLighting()
    {
        var item = Field<List<PreviewItem>>(editor, "_scene").Single();
        var lightingButton = Field<ToolStripButton>(editor, "_minecraftLightingButton");
        var slider = Field<TrackBar>(editor, "_brightnessSlider");
        var renderer = editor.MeshRenderer;
        foreach (bool overlay in new[] { false, true })
        {
            for (int i = 0; i < item.Model.Meshes.Count; i++)
                item.Model.PartsEnabled[i] = overlay || !item.Model.Meshes[i].IsArmor;
            var mesh = item.Model.Meshes.Single(m => m.Part == (overlay ? ModelPart.Helmet : ModelPart.Head));
            string label = overlay ? "Regular flat overlay" : "Regular base skin";
            foreach (int level in Enumerable.Range(0, 16))
            {
                slider.Value = level;
                using var frame = (Bitmap)Call(editor, "CopyScreenToBitmap");
                CheckHeadColors(frame, mesh, label + " level " + level, level);
            }
            Check(!item.UseSkinLayers && item.SkinLayers.Count == 0, label + " uses ordinary skin geometry");
            var buffers = mesh.UserData;
            lightingButton.PerformClick();
            Check(!slider.Visible && lightingButton.Available && !lightingButton.Checked && slider.Value == 15,
                label + " can disable lighting while retaining its world light level");
            using (var unlit = (Bitmap)Call(editor, "CopyScreenToBitmap"))
                CheckHeadColors(unlit, mesh, label + " unlit", lighting: false);
            var face = mesh.Faces.Single(f => f.Normal.Z < -.99f);
            var point = QualityFacePoint(mesh, face);
            Check(editor.GetPick(point.X, point.Y, out var sourcePixel), label + " remains paintable without lighting");
            lightingButton.PerformClick();
            slider.Value = 8;
            using var lit = (Bitmap)Call(editor, "CopyScreenToBitmap");
            CheckHeadColors(lit, mesh, label + " relit");
            CheckLightingAngle(lit, mesh, label);
            point = QualityFacePoint(mesh, face);
            Check(ReferenceEquals(buffers, mesh.UserData) && editor.GetPick(point.X, point.Y, out var litPixel) && litPixel == sourcePixel,
                label + " lighting toggles reuse geometry buffers and preserve source texel picking");
            typeof(Editor).GetProperty("MeshRenderer", Private).SetValue(editor, new ImmediateRenderer());
            try
            {
                using var immediate = (Bitmap)Call(editor, "CopyScreenToBitmap");
                Check(FrameDifference(lit, immediate, new Rectangle(Point.Empty, lit.Size)) == 0,
                    label + " has identical Minecraft lighting in both renderers");
            }
            finally { typeof(Editor).GetProperty("MeshRenderer", Private).SetValue(editor, renderer); }
        }
        foreach (var mode in new[] { ViewMode.Orthographic, ViewMode.Hybrid })
        {
            Call(editor, "SetViewMode", mode);
            Check(lightingButton.Available == (mode == ViewMode.Hybrid) && slider.Visible == (mode == ViewMode.Hybrid),
                "Regular skin lighting controls follow the view mode: " + mode);
            using var lit = (Bitmap)Call(editor, "CopyScreenToBitmap");
            editor.SetPreviewLightAngle(-90);
            using (var angled = (Bitmap)Call(editor, "CopyScreenToBitmap"))
                CheckTextureFrameUnchanged(lit, angled,
                    "Lighting angle leaves the 2D texture pixel-identical: " + mode);
            editor.SetPreviewLightAngle(0);
            // Keep the viewport layout fixed to compare only the rendered texture sheet.
            GlobalSettings.MinecraftLighting = false;
            using var unlit = (Bitmap)Call(editor, "CopyScreenToBitmap");
            CheckTextureFrameUnchanged(lit, unlit,
                "Regular skin lighting leaves the 2D texture pixel-identical: " + mode);
            GlobalSettings.MinecraftLighting = true;
        }
        Call(editor, "SetViewMode", ViewMode.Perspective);
    }

    private static void CheckSurroundingsLighting()
    {
        var settings = PreviewRenderOptions.Current;
        var mode = Field<ViewMode>(editor, "_currentViewMode");
        var tool = Field<ToolIndex>(editor, "_selectedTool");
        var grass = Field<ToolStripMenuItem>(editor, "grassToolStripMenuItem");
        bool hadGrass = grass.Checked, layers = GlobalSettings.SkinLayers3D;
        var toggle = Field<CheckBox>(editor, "_lightSurroundingsToggle");
        Color Dim(Color color) => Color.FromArgb((int)Math.Round(color.R * 38 / 255d),
            (int)Math.Round(color.G * 38 / 255d), (int)Math.Round(color.B * 38 / 255d));
        try
        {
            Field<List<ToolIndex>>(editor, "_tools").Single(t => t.Tool is CameraTool).MenuItem.PerformClick();
            Call(editor, "SetViewMode", ViewMode.Perspective);
            editor.SetSkinLayers3D(false);
            foreach (var quality in new[] { (Resolution: 100, Samples: 0), (Resolution: 150, Samples: 4) })
            {
                (PreviewRenderOptions.Defaults with { LightLevel = 0, LightSurroundings = false,
                    Resolution = quality.Resolution, Samples = quality.Samples }).Save();
                Call(editor, "RefreshWorkspaceControls");
                grass.Checked = false;
                using var skyOnly = (Bitmap)Call(editor, "CopyScreenToBitmap");
                grass.Checked = true;
                using var bright = (Bitmap)Call(editor, "CopyScreenToBitmap");
                Point? ground = null;
                for (int y = bright.Height - 12; y > bright.Height / 2 && ground == null; y -= 11)
                    for (int x = 12; x < bright.Width / 3; x += 11)
                        if (bright.GetPixel(x, y) != skyOnly.GetPixel(x, y) && !editor.GetPick(x, y, out _))
                        { ground = new Point(x, y); break; }
                Check(ground.HasValue, "Surroundings fixture exposes a grass pixel: " + quality);
                var mesh = Field<List<PreviewItem>>(editor, "_scene").Single().Model.Meshes.Single(m => m.Part == ModelPart.Helmet);
                var modelPoint = QualityFacePoint(mesh, mesh.Faces.Single(face => face.Normal.Z < -.99f));
                var buffers = mesh.UserData;
                toggle.Checked = true;
                Check(PreviewRenderOptions.Current.LightSurroundings, "Surroundings checkbox updates the preview preference");
                using var dark = (Bitmap)Call(editor, "CopyScreenToBitmap");
                Check(NearColor(dark.GetPixel(10, 10), Dim(bright.GetPixel(10, 10))) &&
                    NearColor(dark.GetPixel(ground.Value.X, ground.Value.Y), Dim(bright.GetPixel(ground.Value.X, ground.Value.Y))),
                    "Low world light shades both sky and grass by the vanilla level-zero lightmap: " + quality);
                Check(dark.GetPixel(modelPoint.X, modelPoint.Y) == bright.GetPixel(modelPoint.X, modelPoint.Y) &&
                    ReferenceEquals(buffers, mesh.UserData), "Surroundings checkbox preserves model shading and geometry: " + quality);
                var brightCube = bright.GetPixel(bright.Width - 60, 60);
                var darkCube = dark.GetPixel(dark.Width - 60, 60);
                // Cube faces are translucent, so their composite legitimately changes
                // over darker sky. Their own RGB must not receive the world-light factor.
                Check(!NearColor(brightCube, bright.GetPixel(10, 10)) && !NearColor(darkCube, Dim(brightCube)),
                    "Surroundings lighting preserves the orientation cube's own colors while its background darkens: " + quality);
                // Hold layout fixed while checking the master switch's rendering scope.
                GlobalSettings.MinecraftLighting = false;
                using var unlit = (Bitmap)Call(editor, "CopyScreenToBitmap");
                Check(NearColor(unlit.GetPixel(10, 10), bright.GetPixel(10, 10)) &&
                    NearColor(unlit.GetPixel(ground.Value.X, ground.Value.Y), bright.GetPixel(ground.Value.X, ground.Value.Y)),
                    "Disabling Minecraft lighting restores unlit sky and grass: " + quality);
                CheckHeadColors(unlit, mesh, "Surroundings master off " + quality, lighting: false);
                GlobalSettings.MinecraftLighting = true;
                toggle.Checked = false;
                using var restored = (Bitmap)Call(editor, "CopyScreenToBitmap");
                Check(FrameDifference(bright, restored, new Rectangle(Point.Empty, bright.Size)) == 0,
                    "Disabling only surroundings lighting restores the original scene with the model still shaded: " + quality);
            }
            foreach (var view in new[] { ViewMode.Orthographic, ViewMode.Hybrid })
            {
                Call(editor, "SetViewMode", view);
                editor.SetPreviewLightSurroundings(false);
                using var before = (Bitmap)Call(editor, "CopyScreenToBitmap");
                editor.SetPreviewLightSurroundings(true);
                using var after = (Bitmap)Call(editor, "CopyScreenToBitmap");
                CheckTextureFrameUnchanged(before, after,
                    "Surroundings lighting leaves the 2D texture view pixel-identical: " + view);
                if (view == ViewMode.Hybrid)
                {
                    var sky = new Point(10, TextureFrameArea(before).Bottom + 10);
                    Check(NearColor(after.GetPixel(sky.X, sky.Y), Dim(before.GetPixel(sky.X, sky.Y))),
                        "Hybrid surroundings lighting is confined to the 3D viewport under scaled MSAA");
                }
            }
            Call(editor, "SetViewMode", ViewMode.FirstPerson);
            editor.SetPreviewLightSurroundings(false);
            using var firstPersonBright = (Bitmap)Call(editor, "CopyScreenToBitmap");
            var firstPersonViewport = editor.Renderer.RectangleToScreen(editor.Renderer.ClientRectangle);
            editor.SetPreviewLightSurroundings(true);
            using var firstPersonDark = (Bitmap)Call(editor, "CopyScreenToBitmap");
            Check(NearColor(firstPersonDark.GetPixel(10, 10), Dim(firstPersonBright.GetPixel(10, 10))) &&
                firstPersonBright.Size == firstPersonDark.Size &&
                editor.Renderer.RectangleToScreen(editor.Renderer.ClientRectangle) == firstPersonViewport,
                "First-person surroundings lighting darkens the sky without moving or resizing the viewport");
        }
        finally
        {
            settings.Save(); grass.Checked = hadGrass;
            editor.SetSkinLayers3D(layers);
            Call(editor, "SetViewMode", mode);
            tool.MenuItem.PerformClick();
            Call(editor, "RefreshWorkspaceControls");
        }
    }

    private static void CheckLightingAngle(Bitmap baseline, Mesh mesh, string label)
    {
        var slider = Field<TrackBar>(editor, "_lightAngleSlider");
        var value = Field<NumericUpDown>(editor, "_lightAngleValue");
        var reset = Field<Button>(editor, "_lightAngleDefault");
        var settings = PreviewRenderOptions.Current;
        var buffers = mesh.UserData;
        var texture = Field<Texture>(editor, "_previewPaint");
        long textureVersion = texture.ContentVersion;
        var point = QualityFacePoint(mesh, mesh.Faces.Single(f => f.Normal.Z < -.99f));
        Check(editor.GetPick(point.X, point.Y, out var pixel), label + " angle fixture has a source texel");
        var skin = Field<Skin>(editor, "_lastSkin");
        int sourceColor = ReadDocumentPixel(skin, pixel);
        bool dirty = skin.Dirty;
        var viewport = editor.Renderer.RectangleToScreen(editor.Renderer.ClientRectangle);
        void EnterAngle(string text)
        {
            value.Text = text;
            // Exercise WinForms' Enter-key commit without depending on desktop focus.
            typeof(NumericUpDown).GetMethod("OnKeyDown", Private).Invoke(value, new object[] { new KeyEventArgs(Keys.Enter) });
        }
        EnterAngle("-37");
        Check(GlobalSettings.PreviewLightAngle == -37 && slider.Value == -37 && value.Value == -37 && reset.Enabled,
            label + " typed negative angle commits with Enter and synchronizes the slider");
        value.UpButton();
        Check(GlobalSettings.PreviewLightAngle == -36 && slider.Value == -36 && value.Value == -36,
            label + " numeric increment updates the preview and slider");
        value.DownButton();
        Check(GlobalSettings.PreviewLightAngle == -37 && slider.Value == -37 && value.Value == -37,
            label + " numeric decrement updates the preview and slider");
        foreach (var input in new[] { (Text: "999", Expected: 90), (Text: "-999", Expected: -90) })
        {
            EnterAngle(input.Text);
            Check(GlobalSettings.PreviewLightAngle == input.Expected && slider.Value == input.Expected && value.Value == input.Expected,
                label + " typed angle clamps safely at " + input.Expected + " degrees");
        }
        editor.SetPreviewLightAngle(90);
        Call(editor, "RefreshWorkspaceControls");
        Check(GlobalSettings.PreviewLightAngle == 90 && slider.Value == 90 && value.Value == 90,
            label + " positive 90-degree endpoint survives UI synchronization without changing sign");
        Check(editor.Renderer.RectangleToScreen(editor.Renderer.ClientRectangle) == viewport,
            label + " numeric edits and sign changes preserve viewport dimensions and position");
        slider.Value = -90;
        Check(GlobalSettings.PreviewLightAngle == -90 && value.Value == -90 && reset.Enabled,
            label + " angle slider updates the preference, numeric field and Default button immediately");
        using var angled = (Bitmap)Call(editor, "CopyScreenToBitmap");
        CheckHeadColors(angled, mesh, label + " angle -90", quarterTurn: true);
        Check(angled.Size == baseline.Size && FrameDifference(baseline, angled, new Rectangle(Point.Empty, baseline.Size)) > 0,
            label + " angle changes rendered shading without resizing the viewport");
        Check(ReferenceEquals(buffers, mesh.UserData) && texture.ContentVersion == textureVersion &&
            ReadDocumentPixel(skin, pixel) == sourceColor && skin.Dirty == dirty &&
            editor.GetPick(point.X, point.Y, out var rotatedPixel) && rotatedPixel == pixel,
            label + " angle preserves geometry buffers, texture pixels, document state and picking");
        var renderer = editor.MeshRenderer;
        typeof(Editor).GetProperty("MeshRenderer", Private).SetValue(editor, new ImmediateRenderer());
        try
        {
            using var immediate = (Bitmap)Call(editor, "CopyScreenToBitmap");
            Check(FrameDifference(angled, immediate, new Rectangle(Point.Empty, angled.Size)) == 0,
                label + " adjustable lighting matches in both rendering backends");
        }
        finally { typeof(Editor).GetProperty("MeshRenderer", Private).SetValue(editor, renderer); }
        var lighting = Field<ToolStripButton>(editor, "_minecraftLightingButton");
        lighting.PerformClick();
        using (var unlit = (Bitmap)Call(editor, "CopyScreenToBitmap"))
            CheckHeadColors(unlit, mesh, label + " custom angle unlit", lighting: false);
        lighting.PerformClick();
        Check(slider.Visible && slider.Value == -90, label + " lighting toggle retains the custom angle");
        reset.PerformClick();
        using var restored = (Bitmap)Call(editor, "CopyScreenToBitmap");
        Check(PreviewRenderOptions.Current == settings && slider.Value == 0 && value.Value == 0 && !reset.Enabled &&
            FrameDifference(baseline, restored, new Rectangle(Point.Empty, baseline.Size)) == 0,
            label + " Default restores the exact original frame without changing brightness or quality");
    }

    private static Point QualityFacePoint(Mesh mesh, Face face)
    {
        var p = face.Vertices.Aggregate(Vector3.Zero, (sum, v) => sum + v) / face.Vertices.Length;
        var clip = Vector4.TransformRow(new Vector4(Vector3.TransformPosition(p, mesh.Matrix), 1), Field<Matrix4>(editor, "_projectionMatrix"));
        return new Point((int)((clip.X / clip.W + 1) * editor.Renderer.Width / 2), (int)((1 - clip.Y / clip.W) * editor.Renderer.Height / 2));
    }

    private static void CheckHeadColors(Bitmap frame, Mesh mesh, string label, int brightness = 8, bool lighting = true, bool quarterTurn = false)
    {
        // Reference RGBA8 lightmap texels from Mojang's lightmap.fsh at
        // sky levels 0..15, block light 0, Minecraft Overworld noon, gamma 1, ambient #0a0a0a.
        int[] lightmap = { 38, 53, 69, 86, 104, 122, 141, 160, 179, 198, 215, 231, 244, 252, 255, 255 };
        // Independent coefficients from the fixed vanilla Overworld light directions.
        Color Lit(double shade) => !lighting ? Color.FromArgb(211, 235, 234) : Color.FromArgb((int)Math.Round(211 * shade * lightmap[brightness] / 255),
            (int)Math.Round(235 * shade * lightmap[brightness] / 255), (int)Math.Round(234 * shade * lightmap[brightness] / 255));
        var front = Lit(quarterTurn ? .49701425 : .739549875);
        var side = Lit(quarterTurn ? .739549875 : .49701425);
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

    private static Rectangle TextureFrameArea(Bitmap frame)
    {
        var area = editor.GetViewport2D();
        // Hybrid draws 2D at GL y=ceil(H/2), then readback flips Y. At odd heights
        // only floor(H/2) screenshot rows belong to 2D; the logical picking rectangle
        // has one extra row that actually belongs to the 3D viewport's background.
        if (Field<ViewMode>(editor, "_currentViewMode") == ViewMode.Hybrid)
            area = Rectangle.Intersect(area, new Rectangle(0, 0, frame.Width, frame.Height - editor.GetViewport3D().Height));
        return area;
    }

    private static void CheckTextureFrameUnchanged(Bitmap before, Bitmap after, string label)
    {
        var area = TextureFrameArea(before);
        int changed = FrameDifference(before, after, area);
        if (changed != 0 || before.Size != after.Size)
        {
            var bounds = Rectangle.Empty;
            for (int y = area.Top; y < Math.Min(area.Bottom, after.Height); y++)
                for (int x = area.Left; x < Math.Min(area.Right, after.Width); x++)
                    if (before.GetPixel(x, y) != after.GetPixel(x, y))
                        bounds = bounds.IsEmpty ? new Rectangle(x, y, 1, 1) : Rectangle.Union(bounds, new Rectangle(x, y, 1, 1));
            Log($"{label}: {changed} changed pixels in {area}, difference bounds {bounds}, frames {before.Size}/{after.Size}");
            before.Save(Path.Combine(scratch, "texture-isolation-before.png"));
            after.Save(Path.Combine(scratch, "texture-isolation-after.png"));
        }
        Check(before.Size == after.Size && changed == 0, label);
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
