using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MCSkin3D;
using MCSkin3D.lemon42;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using Paril.Imaging;
using Paril.OpenGL;

internal static partial class Program
{
    private static void FirstPerson()
    {
        var tree = Field<TreeView>(editor, "treeView1");
        var original = Field<Skin>(editor, "_lastSkin");
        var rendering = PreviewRenderOptions.Current;
        bool layers = GlobalSettings.SkinLayers3D, ghost = GlobalSettings.Ghost;
        bool leftHand = GlobalSettings.FirstPersonLeftHand;
        var mode = Field<ViewMode>(editor, "_currentViewMode");
        var grass = Field<ToolStripMenuItem>(editor, "grassToolStripMenuItem");
        bool grassChecked = grass.Checked;
        var tools = Field<List<ToolIndex>>(editor, "_tools");
        try
        {
            (PreviewRenderOptions.Defaults with { MinecraftLighting = false, Samples = 0 }).Save();
            editor.SetSkinLayers3D(false);
            GlobalSettings.Ghost = false;
            grass.Checked = true;
            tools.Single(t => t.Tool is CameraTool).MenuItem.PerformClick();
            Check(!editor.CombinedEnabled, "First-person checks start outside the combined workspace");
            var steve = FirstPersonFixture(original, "first-person-Steve", "Players/Steve", 64);
            var alex = FirstPersonFixture(original, "first-person-Alex-HD", "Players/Alex", 128);
            foreach (var skin in new[] { steve, alex })
            {
                tree.SelectedNode = skin;
                var matrices = skin.Model.Meshes.Select(m => m.Matrix).ToArray();
                var uvs = skin.Model.Meshes.SelectMany(m => m.Faces).SelectMany(face => face.TexCoords).ToArray();
                byte[] fileBytes = File.ReadAllBytes(skin.File.FullName);
                Call(editor, "SetViewMode", ViewMode.Perspective);
                SetField(editor, "_3DRotationX", 193f);
                SetField(editor, "_3DRotationY", 27f);
                SetField(editor, "_3DZoom", -106f);
                Call(editor, "CalculateMatrices");
                var thirdPersonProjection = Field<Matrix4>(editor, "_projectionMatrix");
                editor.SetFirstPersonLeftHand(false);
                Field<ToolStripButton>(editor, "_firstPersonButton").PerformClick();
                Check(Field<ViewMode>(editor, "_currentViewMode") == ViewMode.FirstPerson &&
                    Field<ToolStripMenuItem>(editor, "_firstPersonMenu").Checked &&
                    Field<GroupBox>(editor, "_firstPersonGroup").Visible,
                    "Toolbar enters first person and synchronizes the View menu and controls");
                var firstPersonButton = Field<ToolStripButton>(editor, "_firstPersonButton");
                Check(firstPersonButton.DisplayStyle == ToolStripItemDisplayStyle.Image && firstPersonButton.Image != null &&
                    Field<ToolStripMenuItem>(editor, "_firstPersonMenu").Image != null &&
                    !string.IsNullOrWhiteSpace(firstPersonButton.ToolTipText) && !string.IsNullOrWhiteSpace(firstPersonButton.AccessibleName),
                    "First person has an icon-only toolbar button, a menu icon and an accessible tooltip/name");
                Check(editor.GetViewport3D() == new Rectangle(Point.Empty, editor.Renderer.ClientSize),
                    "First person renders and picks across the full viewport");
                Check(!Controls(Field<GroupBox>(editor, "_firstPersonGroup")).Any(control =>
                    control is TrackBar || control.Name == "firstPersonDefault" || control.Name.Contains("Fov", StringComparison.OrdinalIgnoreCase)),
                    "First-person options no longer contain the unused FOV slider, value or reset button");
                var scene = Field<List<PreviewItem>>(editor, "_scene");
                Check(scene.Single().Model != skin.Model, "First-person preview owns separate mesh transforms");

                foreach (bool left in new[] { false, true })
                {
                    Field<ComboBox>(editor, "_firstPersonHand").SelectedIndex = left ? 1 : 0;
                    Check(Field<ComboBox>(editor, "_firstPersonHand").Text == (left ? "Left hand" : "Right hand"),
                        "The hand selector displays the active hand name");
                    using var frame = FirstPersonFrame(skin.Name + (left ? "-left" : "-right"));
                    var pixels = FirstPersonPixels(frame, left);
                    Check(pixels.Count > 200 && (left ? pixels.AverageX < frame.Width / 2d : pixels.AverageX > frame.Width / 2d),
                        skin.Name + " places the chosen arm on its matching screen side");
                    Check(FirstPersonPixels(frame, !left).Count == 0 && CountFirstPersonBodyPixels(frame) == 0,
                        "Only the selected arm and sleeve appear; the body and other hand stay hidden");
                    FirstPersonToggleInvariance(skin, left);
                    MCSkin3D.Brushes.BrushBox.SelectedItem = MCSkin3D.Brushes.BrushList.First(b => b.Width == 1 && b.Height == 1);
                    editor.ColorPanel.SelectedColor = ColorManager.FromRGBA(25, 80, 230, 255);
                    tools.Single(t => t.Tool is PencilTool).MenuItem.PerformClick();
                    // The compact Camera options leave more viewport space than
                    // brush options, so locate the target after selecting the tool.
                    using var paintFrame = FirstPersonFrame(skin.Name + (left ? "-left-paint" : "-right-paint"));
                    var (location, pixel) = FirstPersonPaintLocation(paintFrame, skin, left);
                    int before = ReadDocumentPixel(skin, pixel);
                    ClickLayer(location);
                    Check(ReadDocumentPixel(skin, pixel) == new ColorPixel(25, 80, 230, 255).RGBA,
                        skin.Name + " first-person picking paints the visible " + (left ? "left" : "right") + " atlas pixel");
                    Call(editor, "PerformUndo");
                    Check(ReadDocumentPixel(skin, pixel) == before, "Undo restores the first-person painted pixel");
                    tools.Single(t => t.Tool is CameraTool).MenuItem.PerformClick();
                }

                editor.SetFirstPersonLeftHand(false);
                using var normal = FirstPersonFrame(skin.Name + "-resting-hand");
                var savedVisibility = skin.Model.PartsEnabled.ToArray();
                try
                {
                    for (int i = 0; i < skin.Model.Meshes.Count; ++i)
                        if (!FirstPersonPreview.IsArm(skin.Model.Meshes[i].Part, false)) skin.Model.PartsEnabled[i] = false;
                    GlobalSettings.Ghost = true;
                    using var ghostFrame = FirstPersonFrame(skin.Name + "-ghost");
                    Check(EqualFirstPersonFrames(normal, ghostFrame), "Ghost mode cannot reveal excluded body parts or the other hand");
                }
                finally
                {
                    Array.Copy(savedVisibility, skin.Model.PartsEnabled, savedVisibility.Length);
                    GlobalSettings.Ghost = false;
                }
                editor.RotateView(new Point(30, 20), 1);
                editor.ScaleView(new Point(0, 20), 1);
                editor.PerformResetCamera();
                Call(editor, "SetViewMode", ViewMode.Perspective);
                Check(Field<Matrix4>(editor, "_projectionMatrix") == thirdPersonProjection,
                    "First-person controls and Reset Camera preserve the previous third-person camera");
                Check(skin.Model.Meshes.Select(m => m.Matrix).SequenceEqual(matrices) &&
                    skin.Model.Meshes.SelectMany(m => m.Faces).SelectMany(face => face.TexCoords).SequenceEqual(uvs) &&
                    File.ReadAllBytes(skin.File.FullName).SequenceEqual(fileBytes),
                    "First-person preview and hand switches preserve source geometry, UVs, saved pixels and model metadata");
            }

            tree.SelectedNode = steve;
            Call(editor, "SetViewMode", ViewMode.FirstPerson);
            editor.SetSkinLayers3D(true);
            FirstPersonFlatLayers(steve);
            var sleeve = Field<List<PreviewItem>>(editor, "_scene").Single().Model.Meshes.Single(m => m.Part == ModelPart.RightArmArmor);
            FirstPersonLayout();
            FirstPersonRenderQuality(steve, sleeve);
            var clientRenderer = editor.MeshRenderer;
            typeof(Editor).GetProperty("MeshRenderer", Private).SetValue(editor, new ImmediateRenderer());
            try { using (FirstPersonFrame("first-person-immediate")) { } }
            finally { typeof(Editor).GetProperty("MeshRenderer", Private).SetValue(editor, clientRenderer); }
            editor.SetSkinLayers3D(false);
            var cape = WorkspaceFixture(original, "first-person-cape", "Players/Cape", 64, 32, Color.Gold);
            editor.SetCombinedMode(true);
            editor.SetWorkspaceDocument(EditingTarget.Cape, cape);
            using (FirstPersonFrame("first-person-combined")) { }
            editor.SetEditingTarget(EditingTarget.Cape);
            Check(!editor.GetPick(editor.Renderer.Width * 3 / 4, editor.Renderer.Height * 3 / 4, out _),
                "The displayed player hand cannot paint a selected cape document");
            editor.SetCombinedMode(false);
            Check(Field<ViewMode>(editor, "_currentViewMode") == ViewMode.Perspective &&
                !Field<ToolStripButton>(editor, "_firstPersonButton").Enabled,
                "Selecting a standalone cape exits first person and disables its entry control");
            tree.SelectedNode = steve;
            Check(Field<ToolStripButton>(editor, "_firstPersonButton").Enabled,
                "Returning to a player skin enables first person again");
        }
        finally
        {
            if (editor.CombinedEnabled) editor.SetCombinedMode(false);
            GlobalSettings.Ghost = ghost;
            grass.Checked = grassChecked;
            rendering.Save();
            editor.SetSkinLayers3D(layers);
            editor.SetFirstPersonLeftHand(leftHand);
            tree.SelectedNode = original;
            Call(editor, "SetViewMode", mode);
        }
    }

    private static void FirstPersonToggleInvariance(Skin skin, bool left)
    {
        var originalSize = editor.ClientSize;
        var originalTool = Field<ToolIndex>(editor, "_selectedTool");
        var originalMousePoint = Field<Point>(editor, "_mousePoint");
        var rendering = PreviewRenderOptions.Current;
        bool layers = GlobalSettings.SkinLayers3D;
        var options = Field<FlowLayoutPanel>(editor, "_toolOptionsArea");
        var tools = Field<List<ToolIndex>>(editor, "_tools");
        try
        {
            (rendering with { Resolution = 100, Samples = 0, LightLevel = 15, MinecraftLighting = false, LightAngle = 0 }).Save();
            SetField(editor, "_mousePoint", new Point(-1, -1));
            foreach (var tool in tools.Where(t => t.Tool is CameraTool || t.Tool is FloodFillTool))
                foreach (int width in new[] { 1100, 620 })
                {
                    tool.MenuItem.PerformClick();
                    editor.ClientSize = new Size(width, 950);
                    editor.SetSkinLayers3D(false);
                    Call(editor, "SetMinecraftLighting", false);
                    editor.PerformLayout();
                    options.PerformLayout();
                    // Retain a landscape viewport even at narrow window widths;
                    // a portrait frustum can legitimately crop the entire hand.
                    editor.ClientSize = new Size(width, editor.ClientSize.Height - editor.Renderer.ClientSize.Height +
                        Math.Max(160, editor.Renderer.ClientSize.Width * 9 / 16));
                    editor.PerformLayout();
                    options.PerformLayout();
                    var appSize = editor.ClientSize;
                    string name = skin.Name + (left ? "-left-" : "-right-") + tool.Tool.GetType().Name + "-" + width;
                    using var reference = FirstPersonFrame(name + "-unlit");
                    var viewport = editor.Renderer.RectangleToScreen(editor.Renderer.ClientRectangle);
                    var projection = Field<Matrix4>(editor, "_projectionMatrix");
                    var meshes = Field<List<PreviewItem>>(editor, "_scene").Single().Model.Meshes
                        .Where(mesh => FirstPersonPreview.IsArm(mesh.Part, left)).ToArray();
                    var poses = meshes.Select(mesh => mesh.Matrix).ToArray();
                    var vertices = meshes.SelectMany(mesh => mesh.Faces).SelectMany(face => face.Vertices).ToArray();
                    var sourceUvs = meshes.SelectMany(mesh => mesh.Faces).SelectMany(face => face.TexCoords).ToArray();
                    var foreground = FirstPersonForeground(reference);
                    Check(!foreground.Bounds.IsEmpty, name + " has a visible hand mask before toggling");
                    var (location, sourcePixel) = FirstPersonPaintLocation(reference, skin, left);
                    // Change one option per step, covering all four combinations
                    // and returning to the starting state without resizing the app.
                    foreach (var state in new[] { (Layers: true, Lighting: false, Angle: 0), (Layers: true, Lighting: true, Angle: 0),
                        (Layers: true, Lighting: true, Angle: -90), (Layers: false, Lighting: true, Angle: -90),
                        (Layers: false, Lighting: true, Angle: 0), (Layers: false, Lighting: false, Angle: 0) })
                    {
                        if (GlobalSettings.SkinLayers3D != state.Layers) editor.SetSkinLayers3D(state.Layers);
                        if (GlobalSettings.MinecraftLighting != state.Lighting) Call(editor, "SetMinecraftLighting", state.Lighting);
                        if (GlobalSettings.PreviewLightAngle != state.Angle) editor.SetPreviewLightAngle(state.Angle);
                        editor.PerformLayout();
                        options.PerformLayout();
                        string label = name + "-layers-" + state.Layers + "-lighting-" + state.Lighting + "-angle-" + state.Angle;
                        using var frame = FirstPersonFrame(label);
                        var currentViewport = editor.Renderer.RectangleToScreen(editor.Renderer.ClientRectangle);
                        Check(editor.ClientSize == appSize && currentViewport == viewport && frame.Size == reference.Size,
                            label + " preserves viewport dimensions and screen position at a fixed app size");
                        Check(Field<Matrix4>(editor, "_projectionMatrix") == projection,
                            label + " preserves the first-person projection");
                        var item = Field<List<PreviewItem>>(editor, "_scene").Single();
                        var currentMeshes = item.Model.Meshes.Where(mesh => FirstPersonPreview.IsArm(mesh.Part, left)).ToArray();
                        Check(currentMeshes.Select(mesh => mesh.Matrix).SequenceEqual(poses) &&
                            currentMeshes.SelectMany(mesh => mesh.Faces).SelectMany(face => face.Vertices).SequenceEqual(vertices) &&
                            currentMeshes.SelectMany(mesh => mesh.Faces).SelectMany(face => face.TexCoords).SequenceEqual(sourceUvs) &&
                            item.IsFirstPerson && !item.UseSkinLayers && item.SkinLayers.Count == 0,
                            label + " preserves the flat hand pose, geometry and atlas coordinates");
                        var currentForeground = FirstPersonForeground(frame);
                        Check(currentForeground.Bounds == foreground.Bounds && currentForeground.Pixels.SequenceEqual(foreground.Pixels),
                            label + " preserves every hand silhouette pixel and its screen-relative bounds while allowing lighting colors to change");
                        Check(editor.GetPick(location.X, location.Y, out var pixel) && pixel == sourcePixel,
                            label + " keeps the same visible hand point mapped to the same source texel");
                        Check(GlobalSettings.SkinLayers3D == state.Layers && GlobalSettings.MinecraftLighting == state.Lighting && GlobalSettings.PreviewLightAngle == state.Angle,
                            label + " preserves the requested layer and lighting settings");
                    }
                }
        }
        finally
        {
            rendering.Save();
            editor.SetSkinLayers3D(layers);
            editor.ClientSize = originalSize;
            originalTool.MenuItem.PerformClick();
            SetField(editor, "_mousePoint", originalMousePoint);
            Call(editor, "RefreshWorkspaceControls");
        }
    }

    private static (bool[] Pixels, Rectangle Bounds) FirstPersonForeground(Bitmap frame)
    {
        // The sky is unaffected by entity lighting. Comparing membership in the
        // foreground permits legitimate shading changes without hiding movement.
        int background = frame.GetPixel(0, 0).ToArgb();
        var pixels = new bool[frame.Width * frame.Height];
        int minX = frame.Width, minY = frame.Height, maxX = -1, maxY = -1;
        var data = frame.LockBits(new Rectangle(Point.Empty, frame.Size), System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var row = new int[frame.Width];
            for (int y = 0; y < frame.Height; ++y)
            {
                System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                for (int x = 0; x < frame.Width; ++x)
                {
                    if (row[x] == background) continue;
                    pixels[y * frame.Width + x] = true;
                    minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                }
            }
        }
        finally { frame.UnlockBits(data); }
        return (pixels, maxX < 0 ? Rectangle.Empty : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1));
    }

    private static void FirstPersonFlatLayers(Skin skin)
    {
        foreach (bool left in new[] { false, true })
        {
            editor.SetFirstPersonLeftHand(left);
            using var frame = FirstPersonFrame("first-person-flat-sleeve-3d-enabled-" + (left ? "left" : "right"));
            var item = Field<List<PreviewItem>>(editor, "_scene").Single();
            var arm = item.Model.Meshes.Single(m => m.Part == (left ? ModelPart.LeftArm : ModelPart.RightArm));
            var sleeve = item.Model.Meshes.Single(m => m.Part == (left ? ModelPart.LeftArmArmor : ModelPart.RightArmArmor));
            Check(GlobalSettings.SkinLayers3D && item.IsFirstPerson && !item.UseSkinLayers && item.SkinLayers.Count == 0,
                "First person keeps the global 3D layer setting while allocating no raised sleeve geometry");
            Check(!arm.IsSkinLayer && !sleeve.IsSkinLayer && sleeve.Faces.Count == 6 && sleeve.Matrix == arm.Matrix,
                "The first-person sleeve is the vanilla six-face box sharing its arm pose");
            var opaque = (List<Mesh>)typeof(Paril.OpenGL.Renderer).GetField("_opaqueMeshes", Private).GetValue(editor.MeshRenderer);
            var transparent = (List<Mesh>)typeof(Paril.OpenGL.Renderer).GetField("_transparentMeshes", Private).GetValue(editor.MeshRenderer);
            try
            {
                Call(editor, "QueueSceneMeshes", Field<Texture>(editor, "_previewPaint"));
                var queued = opaque.Concat(transparent).ToArray();
                Check(queued.Length == 2 && queued.Contains(arm) && queued.Contains(sleeve),
                    "With 3D layers enabled, the first-person render queue contains only the flat arm and sleeve");
            }
            finally { opaque.Clear(); transparent.Clear(); }
            var (_, sleevePixel) = FirstPersonPaintLocation(frame, skin, left);
            Check(sleeve.Faces.Any(face => face.TexCoordsToInteger(skin.Width, skin.Height).Contains(sleevePixel)),
                "The visible flat sleeve picks its own original atlas texel with 3D layers enabled");
            int sleeveIndex = item.Model.Meshes.IndexOf(sleeve);
            bool visible = item.Model.PartsEnabled[sleeveIndex];
            try
            {
                item.Model.PartsEnabled[sleeveIndex] = false;
                using var bare = FirstPersonFrame("first-person-flat-bare-" + (left ? "left" : "right"));
                var (_, armPixel) = FirstPersonPaintLocation(bare, skin, left);
                Check(arm.Faces.Any(face => face.TexCoordsToInteger(skin.Width, skin.Height).Contains(armPixel)),
                    "Hiding the flat sleeve exposes paintable base-arm texels");
            }
            finally { item.Model.PartsEnabled[sleeveIndex] = visible; }
        }
        editor.SetFirstPersonLeftHand(false);
        Call(editor, "SetViewMode", ViewMode.Perspective);
        editor.PerformResetCamera();
        using (FirstPersonFrame("first-person-return-to-raised-perspective")) { }
        var thirdPerson = Field<List<PreviewItem>>(editor, "_scene").Single();
        Check(GlobalSettings.SkinLayers3D && !thirdPerson.IsFirstPerson && thirdPerson.UseSkinLayers &&
            thirdPerson.SkinLayers.Any(pair => pair.Key.Part == ModelPart.RightArmArmor && pair.Value.Mesh.IsSkinLayer),
            "Returning to Perspective restores raised sleeves without changing the 3D layer preference");
        Call(editor, "SetViewMode", ViewMode.FirstPerson);
        using (FirstPersonFrame("first-person-return-to-flat-sleeve")) { }
        Check(GlobalSettings.SkinLayers3D && Field<List<PreviewItem>>(editor, "_scene").Single().SkinLayers.Count == 0,
            "Returning to first person clears raised layer geometry again");
    }

    private static void FirstPersonLayout()
    {
        var originalSize = editor.ClientSize;
        var originalTool = Field<ToolIndex>(editor, "_selectedTool");
        bool lighting = GlobalSettings.MinecraftLighting;
        int lightAngle = GlobalSettings.PreviewLightAngle;
        var options = Field<FlowLayoutPanel>(editor, "_toolOptionsArea");
        var group = Field<GroupBox>(editor, "_firstPersonGroup");
        var hand = Field<ComboBox>(editor, "_firstPersonHand");
        var angleGroup = Field<Control>(editor, "_lightAngleGroup");
        var brightnessGroup = Field<Control>(editor, "_brightnessGroup");
        var lightingColumn = Field<FlowLayoutPanel>(editor, "_lightingOptionsColumn");
        var issues = new List<string>();
        try
        {
            Call(editor, "SetMinecraftLighting", true);
            editor.SetPreviewLightAngle(-90);
            Field<List<ToolIndex>>(editor, "_tools").Single(t => t.Tool is FloodFillTool).MenuItem.PerformClick();
            foreach (int width in new[] { 1100, 800, 620 })
            {
                editor.ClientSize = new Size(width, 950);
                editor.PerformLayout();
                options.PerformLayout();
                SaveBrightnessBar(options, "first-person-fill-options-" + width);
                SaveBrightnessBar(Field<ToolStrip>(editor, "toolStrip1"), "first-person-toolbar-" + width);
                if (width == 1100)
                    SaveBrightnessBar(editor, "first-person-editor-layout");
                var visible = options.Controls.Cast<Control>().Where(c => c.Visible).ToArray();
                if (!visible.All(c => options.ClientRectangle.Contains(c.Bounds)) ||
                    visible.SelectMany((c, i) => visible.Skip(i + 1).Select(other => c.Bounds.IntersectsWith(other.Bounds))).Any(overlap => overlap))
                    issues.Add(width + "px tool groups in " + options.ClientSize + ": " + string.Join("; ", visible.Select(c => c.Name + " " + c.Bounds)));
                if (brightnessGroup.Left != angleGroup.Left || brightnessGroup.Bottom > angleGroup.Top)
                    issues.Add(width + "px lighting groups do not remain stacked and left-aligned");
                foreach (var container in new[] { (Control)group, lightingColumn }
                    .SelectMany(root => new[] { root }.Concat(Controls(root))).Where(c => c.Controls.Count > 0))
                {
                    var children = container.Controls.Cast<Control>().Where(c => c.Visible).ToArray();
                    if (!children.All(c => container.ClientRectangle.Contains(c.Bounds)) ||
                        children.SelectMany((c, i) => children.Skip(i + 1).Select(other => c.Bounds.IntersectsWith(other.Bounds))).Any(overlap => overlap))
                        issues.Add(width + "px first-person controls overflow " + container.Name + " " + container.ClientSize);
                }
                if (!Field<TrackBar>(editor, "_brightnessSlider").Visible || !group.Visible || !hand.Visible ||
                    !Field<TrackBar>(editor, "_lightAngleSlider").Visible ||
                    !Field<NumericUpDown>(editor, "_lightAngleValue").Visible || !Field<CheckBox>(editor, "_lightSurroundingsToggle").Visible ||
                    !Field<Button>(editor, "_lightAngleDefault").Visible)
                    issues.Add(width + "px is missing a required first-person/brightness control");
                hand.SelectedIndex = 1;
                if (!GlobalSettings.FirstPersonLeftHand || hand.Text != "Left hand")
                    issues.Add(width + "px hand selector is not usable");
                hand.SelectedIndex = 0;
            }
        }
        finally
        {
            editor.ClientSize = originalSize;
            originalTool.MenuItem.PerformClick();
            Call(editor, "SetMinecraftLighting", lighting);
            editor.SetPreviewLightAngle(lightAngle);
            editor.SetFirstPersonLeftHand(false);
        }
        foreach (string issue in issues) Log("First-person layout: " + issue);
        Check(issues.Count == 0, "First-person, brightness and Fill controls fit and remain usable at 1100, 800 and 620px: " + string.Join("; ", issues));
    }

    private static void FirstPersonRenderQuality(Skin skin, Mesh sleeve)
    {
        var rendering = PreviewRenderOptions.Current;
        try
        {
            (PreviewRenderOptions.Defaults with { Resolution = 150, Samples = 4, LightLevel = 8 }).Save();
            Call(editor, "RefreshWorkspaceControls");
            using var frame = FirstPersonFrame("first-person-lit-flat-150-4x");
            var target = Field<PreviewRenderTarget>(editor, "_previewRenderTarget");
            Check(target.Width == (int)Math.Round(editor.Renderer.Width * 1.5) &&
                target.Height == (int)Math.Round(editor.Renderer.Height * 1.5) &&
                target.Samples == Math.Min(4, GL.GetInteger(GetPName.MaxSamples)),
                "First-person hand uses the requested 150% render resolution and 4x multisampling");
            bool sampled = false;
            foreach (var face in sleeve.Faces)
            {
                var localNormal = Vector3.Cross(face.Vertices[1] - face.Vertices[0], face.Vertices[2] - face.Vertices[0]);
                var normal = Vector3.TransformVector(localNormal, sleeve.Matrix).Normalized();
                // Independent light.glsl reference in camera-space Y-up axes,
                // using the level-8 RGBA8 lightmap texel (179/255).
                double Light(double y) => Math.Min(1, .4 + .6 *
                    (Math.Max(0, (.2 * normal.X + y - .7 * normal.Z) / Math.Sqrt(1.53)) +
                     Math.Max(0, (-.2 * normal.X + y + .7 * normal.Z) / Math.Sqrt(1.53))));
                double shade = Light(normal.Y);
                if (Math.Abs(shade - Light(-normal.Y)) < .08) continue;
                // A whole sleeve face can have its center below the viewport.
                // Project its source texel centers instead, away from UV seams.
                var rect = face.TexCoordsToInteger(skin.Width, skin.Height);
                var uv1 = face.TexCoords[1] - face.TexCoords[0];
                var uv3 = face.TexCoords[3] - face.TexCoords[0];
                float determinant = uv1.X * uv3.Y - uv1.Y * uv3.X;
                for (int y = rect.Top; y < rect.Bottom && !sampled; ++y)
                    for (int x = rect.Left; x < rect.Right && !sampled; ++x)
                    {
                        var uv = new Vector2((x + .5f) / skin.Width, (y + .5f) / skin.Height) - face.TexCoords[0];
                        float u = (uv.X * uv3.Y - uv.Y * uv3.X) / determinant;
                        float v = (uv1.X * uv.Y - uv1.Y * uv.X) / determinant;
                        var local = face.Vertices[0] + u * (face.Vertices[1] - face.Vertices[0]) + v * (face.Vertices[3] - face.Vertices[0]);
                        var clip = Vector4.TransformRow(new Vector4(Vector3.TransformPosition(local, sleeve.Matrix), 1),
                            Field<Matrix4>(editor, "_projectionMatrix"));
                        var location = new Point((int)((clip.X / clip.W + 1) * frame.Width / 2),
                            (int)((1 - clip.Y / clip.W) * frame.Height / 2));
                        if (location.X < 3 || location.Y < 3 || location.X >= frame.Width - 3 || location.Y >= frame.Height - 3 ||
                            !editor.GetPick(location.X, location.Y, out var pixel) || pixel != new Point(x, y)) continue;
                        var expected = Color.FromArgb((int)Math.Round(220 * shade * 179 / 255),
                            (int)Math.Round(60 * shade * 179 / 255), (int)Math.Round(45 * shade * 179 / 255));
                        var actual = frame.GetPixel(location.X, location.Y);
                        Check(Math.Abs(actual.R - expected.R) <= 2 && Math.Abs(actual.G - expected.G) <= 2 && Math.Abs(actual.B - expected.B) <= 2,
                            $"Lit first-person flat sleeve uses Y-up normals at the picked source texel: {actual}, expected {expected}");
                        sampled = true;
                    }
                if (sampled) break;
            }
            Check(sampled, "Scaled and multisampled first-person frame has a visible, correctly picked flat sleeve lighting sample");
        }
        finally
        {
            rendering.Save();
            Call(editor, "RefreshWorkspaceControls");
        }
    }

    private static Skin FirstPersonFixture(Skin parent, string name, string modelPath, int size)
    {
        var skin = WorkspaceFixture(parent, name, modelPath, size, size, Color.Magenta);
        using (var image = new Bitmap(size, size))
        {
            using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.Magenta);
            foreach (var mesh in skin.Model.Meshes)
            {
                bool left = FirstPersonPreview.IsArm(mesh.Part, true);
                if (!left && !FirstPersonPreview.IsArm(mesh.Part, false)) continue;
                var color = left ? Color.FromArgb(40, 190, 100) : Color.FromArgb(220, 60, 45);
                foreach (var face in mesh.Faces)
                {
                    var rect = face.TexCoordsToInteger(size, size);
                    for (int y = rect.Top; y < rect.Bottom; ++y)
                        for (int x = rect.Left; x < rect.Right; ++x) image.SetPixel(x, y, color);
                }
            }
            image.Save(skin.File.FullName);
        }
        PNGMetadata.WriteMetadata(skin.File.FullName, Skin.ModelMetadata(modelPath));
        Check(skin.SetImages(false), "First-person fixture loads its asymmetric hand textures");
        return skin;
    }

    private static Bitmap FirstPersonFrame(string name)
    {
        var bitmap = (Bitmap)Call(editor, "CopyScreenToBitmap");
        Check(GL.GetError() == ErrorCode.NoError, name + " renders without OpenGL errors");
        bitmap.Save(Path.Combine(scratch, name + ".png"));
        return bitmap;
    }

    private static bool IsFirstPersonHandColor(Color color, bool left) => left
        ? color.G > 130 && color.R < 90 && color.B < 140
        : color.R > 150 && color.G < 100 && color.B < 100;

    private static (int Count, double AverageX) FirstPersonPixels(Bitmap bitmap, bool left)
    {
        int count = 0;
        long xSum = 0;
        for (int y = 0; y < bitmap.Height; y += 2)
            for (int x = 0; x < bitmap.Width; x += 2)
                if (IsFirstPersonHandColor(bitmap.GetPixel(x, y), left)) { count++; xSum += x; }
        return (count, count == 0 ? 0 : xSum / (double)count);
    }

    private static int CountFirstPersonBodyPixels(Bitmap bitmap)
    {
        int count = 0;
        for (int y = 0; y < bitmap.Height; y += 2)
            for (int x = 0; x < bitmap.Width; x += 2)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.R > 150 && color.B > 150 && color.G < 100) count++;
            }
        return count;
    }

    private static bool EqualFirstPersonFrames(Bitmap first, Bitmap second)
    {
        if (first.Size != second.Size) return false;
        for (int y = 0; y < first.Height; ++y)
            for (int x = 0; x < first.Width; ++x)
                if (first.GetPixel(x, y) != second.GetPixel(x, y)) return false;
        return true;
    }

    private static (Point Location, Point Pixel) FirstPersonPaintLocation(Bitmap frame, Skin skin, bool left)
    {
        for (int y = 4; y < frame.Height - 4; y += 3)
            for (int x = 4; x < frame.Width - 4; x += 3)
                if (IsFirstPersonHandColor(frame.GetPixel(x, y), left) && editor.GetPick(x, y, out var pixel))
                {
                    bool correctAtlas = skin.Model.Meshes.Where(m => FirstPersonPreview.IsArm(m.Part, left))
                        .SelectMany(m => m.Faces).Any(face => face.TexCoordsToInteger(skin.Width, skin.Height).Contains(pixel));
                    Check(correctAtlas, "Rendered hand picks only its own atlas coordinates");
                    return (new Point(x, y), pixel);
                }
        throw new InvalidOperationException("No rendered first-person hand pixel could be picked.");
    }
}
