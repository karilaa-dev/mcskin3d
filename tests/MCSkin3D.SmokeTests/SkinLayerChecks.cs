using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using MCSkin3D;
using MCSkin3D.Models;
using OpenTK.Mathematics;
using Paril.OpenGL;

internal static class SkinLayerChecks
{
    internal static void Run(Action<bool, string> check, string scratch)
    {
        bool fastRender = GlobalSettings.SkinLayerFastRender;
        GlobalSettings.SkinLayerFastRender = false;
        try { RunGeometry(check, scratch); }
        finally { GlobalSettings.SkinLayerFastRender = fastRender; }
    }

    private static void RunGeometry(Action<bool, string> check, string scratch)
    {
        var renderer = (ClientArrayRenderer)RuntimeHelpers.GetUninitializedObject(typeof(ClientArrayRenderer));
        foreach (var name in new[] { "Steve", "Alex", "Steve (Minimal)", "Alex (Minimal)" })
        {
            var model = new ModelFormatTCN().Load(Path.Combine(AppContext.BaseDirectory, "Models", "Players", name + ".xml"));
            foreach (int scale in new[] { 1, 2 })
            {
                int width = (int)model.DefaultWidth * scale, height = (int)model.DefaultHeight * scale;
                var alpha = new byte[width * height];
                foreach (var source in model.Meshes.Where(m => m.IsArmor || m.Part == ModelPart.Helmet))
                {
                    var original = source.Faces.SelectMany(f => f.Vertices).ToArray();
                    var layer = new SkinLayerGeometry(source);
                    layer.Update(alpha, width, height);
                    check(layer.Mesh.Faces.Count == 0 && float.IsFinite(layer.Mesh.Center.LengthSquared), "Empty outer layer generates no geometry and retains a finite center");
                    // Each orientation, including mirrored arms and upside-down bottom UVs.
                    foreach (var face in source.Faces)
                    {
                        var rect = face.TexCoordsToInteger(width, height);
                        int x = rect.Left + rect.Width / 2, y = rect.Top + rect.Height / 2;
                        alpha[y * width + x] = 255;
                        layer.Update(alpha, width, height);
                        check(layer.Mesh.Faces.Count == 5, "An interior pixel omits its buried back face, matching the mod");
                        check(layer.Mesh.Mode == OpenTK.Graphics.OpenGL.PrimitiveType.Triangles && layer.Mesh.Faces.All(f => f.Indices.Length == 6),
                            "Voxel faces use Minecraft's explicit quad triangles for stable multisample interpolation");
                        foreach (var generated in layer.Mesh.Faces)
                        {
                            var uv = new Vector2((x + .5f) / width, (y + .5f) / height);
                            check(generated.TexCoords.Aggregate(Vector2.Zero, (sum, v) => sum + v) / 4 == uv &&
                                generated.TexCoordsToInteger(width, height) == new Rectangle(x, y, 1, 1), "Every voxel face spans exactly its source pixel");
                            var v = generated.Vertices; var i = generated.Indices;
                            check(Vector3.Dot(Vector3.Cross(v[i[1]] - v[i[0]], v[i[2]] - v[i[0]]), generated.Normal) < 0,
                                "All voxel faces wind clockwise toward their exterior");
                        }
                        var top = layer.Mesh.Faces[0];
                        var point = top.Vertices.Aggregate(Vector3.Zero, (sum, v) => sum + v) / 4;
                        var start = Vector3.TransformPosition(point + top.Normal * 4, source.Matrix);
                        var end = Vector3.TransformPosition(point - top.Normal * 2, source.Matrix);
                        check(layer.TryPick(start, end, out float distance, out var pixel) && pixel == new Point(x, y) && Math.Abs(distance - 4) < .001f,
                            "Raised surface picking returns the exact pixel and world distance");
                        var side = layer.Mesh.Faces[2];
                        point = side.Vertices.Aggregate(Vector3.Zero, (sum, v) => sum + v) / 4;
                        check(layer.TryPick(Vector3.TransformPosition(point + side.Normal * 2, source.Matrix),
                            Vector3.TransformPosition(point - side.Normal, source.Matrix), out _, out pixel) && pixel == new Point(x, y),
                            "Voxel side walls pick their source pixel");
                        var cached = layer.Mesh;
                        layer.Update(alpha, width, height);
                        check(ReferenceEquals(layer.Mesh, cached), "Unchanged alpha reuses the mesh and renderer buffers");
                        alpha[y * width + x + 1] = 255;
                        layer.Update(alpha, width, height);
                        check(layer.Mesh.Faces.Count == 8 + (x + 2 == rect.Right ? 1 : 0), "Adjacent pixels omit shared walls; a boundary pixel keeps its exposed back");
                        alpha[y * width + x + 1] = 0;
                        alpha[y * width + x] = 128;
                        layer.Update(alpha, width, height);
                        check(layer.Mesh.Faces.Count == 5 && layer.Mesh.HasTransparency, "Translucent pixels retain voxel depth and alpha blending");
                        alpha[y * width + x] = 0;
                        layer.Update(alpha, width, height);
                        check(layer.Mesh.Faces.Count == 0 && !layer.TryPick(start, end, out _, out _), "Erasing removes geometry and hit boxes");
                    }
                    check(source.Faces.SelectMany(f => f.Vertices).SequenceEqual(original), "Preview generation preserves source model geometry");
                }
                Array.Fill(alpha, (byte)255);
                var dense = new SkinLayerGeometry(model.Meshes.First(m => m.Part == ModelPart.Helmet));
                dense.Update(alpha, width, height);
                dense.Mesh.UserData = renderer.CreateUserData(dense.Mesh);
                renderer.UpdateUserData(dense.Mesh);
                var data = dense.Mesh.GetUserData<ClientArrayMeshUserData>();
                check(data.IndiceArray.Max() > 255 && data.IndiceArray.Max() < data.VerticeArray.Length / 3,
                    "Dense layers use indices wider than a byte, without wrapping or out-of-range vertices");
                check(data.ColorArray.Min() < .8f && data.ColorArray.Max() == 1, "Voxel shading makes exposed sides visible");
                var buffer = data.ColorArray;
                dense.Mesh.DrawTransparent = true;
                renderer.UpdateUserData(dense.Mesh);
                check(ReferenceEquals(buffer, data.ColorArray) && data.ColorArray.Where((_, i) => i % 4 == 3).All(a => a == .25f),
                    "Ghosted layers retain cached buffers and quarter opacity");
            }
        }
        ModProportionsAndCorners(check);
        AdvancedSettings(check, scratch);
        OptionsAndScene(check, scratch);
    }

    private static void AdvancedSettings(Action<bool, string> check, string scratch)
    {
        var snapshot = SkinLayerOptions.Current;
        var allSettings = typeof(GlobalSettings).GetProperties().Where(p => p.CanWrite).ToDictionary(p => p, p => p.GetValue(null));
        try
        {
            var custom = new SkinLayerOptions(1.3f, 1.22f, 1.2f, false, 28, 21);
            var customRendering = new PreviewRenderOptions(175, 4, false, 6, -45, false);
            var renderingSnapshot = PreviewRenderOptions.Current;
            using (var dialog = new MCSkin3D.Forms.SkinLayerSettingsDialog(custom, customRendering))
            {
                check(dialog.Options == custom, "Advanced settings initialize every control from saved values");
                check(dialog.Rendering == customRendering && dialog.Controls.Find("fastRender", true).Length == 0,
                    "Rendering quality controls replace the Fast rendering checkbox");
                check(new[] { "brightness", "minecraftLighting", "gameBrightness", "lightDirection", "lightAngleSlider" }
                    .All(name => dialog.Controls.Find(name, true).Length == 0),
                    "3D Skin Layers settings contain no Minecraft lighting controls");
                dialog.Show(); dialog.PerformLayout();
                ((TrackBar)dialog.Controls.Find("renderResolution", true).Single()).Value = 125;
                ((ComboBox)dialog.Controls.Find("antialiasing", true).Single()).SelectedIndex = 1;
                check(dialog.Rendering == (customRendering with { Resolution = 125, Samples = 2 }),
                    "Changing layer render quality preserves every Minecraft lighting setting");
                typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(
                    dialog.Controls.Find("resetDefaults", true).Single(), new object[] { EventArgs.Empty });
                check(dialog.Options == SkinLayerOptions.Defaults, "Reset restores all mod defaults");
                check(dialog.Rendering == (customRendering with { Resolution = PreviewRenderOptions.Defaults.Resolution,
                    Samples = PreviewRenderOptions.Defaults.Samples }),
                    "Layer Reset restores resolution and AA while preserving lighting enabled, world light, angle and surroundings");
                check(SkinLayerOptions.Current == snapshot, "Dialog reset and cancel do not mutate application settings");
                check(PreviewRenderOptions.Current == renderingSnapshot, "Quality controls remain local until Apply");
                dialog.Show(); dialog.PerformLayout();
                using var image = new Bitmap(dialog.Width, dialog.Height);
                var applyButton = dialog.Controls.Find("applySettings", true).Single();
                var applyBounds = dialog.RectangleToClient(applyButton.RectangleToScreen(applyButton.ClientRectangle));
                check(applyButton.Visible && dialog.ClientRectangle.Contains(applyBounds),
                    "Settings footer remains visible after Reset: " + applyBounds + " in " + dialog.ClientRectangle);
                dialog.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                image.Save(Path.Combine(AppContext.BaseDirectory, "skin-layer-settings.png"));
                ((Button)dialog.CancelButton).PerformClick();
                check(dialog.DialogResult == DialogResult.Cancel && PreviewRenderOptions.Current == renderingSnapshot,
                    "Cancel leaves all saved rendering and lighting settings unchanged");
            }
            using (var dialog = new MCSkin3D.Forms.SkinLayerSettingsDialog(custom, customRendering))
            {
                ((TrackBar)dialog.Controls.Find("renderResolution", true).Single()).Value = 150;
                ((ComboBox)dialog.Controls.Find("antialiasing", true).Single()).SelectedIndex = 3;
                dialog.Rendering.Save();
                check(PreviewRenderOptions.Current == (customRendering with { Resolution = 150, Samples = 8 }),
                    "Applying layer quality settings preserves the input lighting snapshot");
            }
            var settings = new Paril.Settings.Settings(); settings.Structures.Add(typeof(GlobalSettings));
            settings.LoadDefaults();
            check(PreviewRenderOptions.Current.LightLevel == 15 && PreviewRenderOptions.Defaults.LightLevel == 15,
                "World light defaults to full daylight, level 15, in settings and render options");
            check(PreviewRenderOptions.Current.LightAngle == 0, "Light angle defaults to the original Minecraft directions");
            check(PreviewRenderOptions.Current.LightSurroundings, "Surroundings share Minecraft lighting by default");
            custom.Save(); customRendering.Save();
            string path = Path.Combine(scratch, "advanced-layer-settings.ini"); settings.Save(path);
            SkinLayerOptions.Defaults.Save(); PreviewRenderOptions.Defaults.Save(); settings.Load(path);
            check(SkinLayerOptions.Current == custom, "All advanced rendering settings survive save and reload");
            check(PreviewRenderOptions.Current == customRendering, "Resolution, AA and lighting survive save and reload");
            check(new PreviewRenderOptions(-1, 16, true, 999).Validated() == new PreviewRenderOptions(50, 8, true, 15),
                "Out-of-range rendering settings are bounded");
            check((customRendering with { LightLevel = -10 }).Validated().LightLevel == 0 &&
                (customRendering with { LightLevel = 500 }).Validated().LightLevel == 15, "World light is bounded to Minecraft's 16 levels");
            check((customRendering with { LightAngle = -999 }).Validated().LightAngle == 81 &&
                (customRendering with { LightAngle = 999 }).Validated().LightAngle == -81,
                "Saved angles wrap to the equivalent direction within the selectable half-turn range");
            File.WriteAllText(path, "[GlobalSettings]\r\nPreviewBrightness=47\r\nPreviewLightDirection=-90\r\nPreviewGameBrightness=0\r\n");
            settings.Load(path);
            check(PreviewRenderOptions.Current == PreviewRenderOptions.Defaults,
                "Obsolete fill percentages, light direction and game brightness settings leave the lighting defaults intact");
            foreach (int oldAngle in new[] { -180, 180, -90, 90 })
            {
                File.WriteAllText(path, "[GlobalSettings]\r\nPreviewLightAngle=" + oldAngle + "\r\n");
                settings.Load(path);
                check(PreviewRenderOptions.Current.LightAngle == (Math.Abs(oldAngle) == 180 ? 0 : oldAngle),
                    "Existing saved angle " + oldAngle + " retains its equivalent lighting direction");
            }
            custom.Save(); customRendering.Save();
            var model = new ModelFormatTCN().Load(Path.Combine(AppContext.BaseDirectory, "Models", "Players", "Alex.xml"));
            var source = model.Meshes.Single(m => m.Part == ModelPart.Helmet);
            var alpha = new byte[4096]; Array.Fill(alpha, (byte)255);
            var layer = new SkinLayerGeometry(source); layer.Update(alpha, 64, 64);
            var old = layer.Mesh;
            check(Math.Abs(layer.Mesh.Bounds.Maxs.X - 4 * custom.HeadSize) < .0001f, "Head size changes actual geometry");
            SkinLayerOptions.Defaults.Save(); layer.Update(alpha, 64, 64);
            check(!ReferenceEquals(old, layer.Mesh) && layer.Mesh.Faces.Count == 6 && layer.PixelFaces.Count == 384,
                "Changing settings rebuilds the cache and fast rendering batches solid front faces");
            var invalid = new SkinLayerOptions(float.NaN, float.PositiveInfinity, -5, true, 500, -1).Validated();
            check(invalid.PixelSize == 1.15f && invalid.HeadSize == 1.18f && invalid.BodyWidth == 1.001f && invalid.Distance == 40 && invalid.Parts == 63,
                "Invalid saved numeric settings cannot corrupt rendering");
        }
        finally { foreach (var pair in allSettings) pair.Key.SetValue(null, pair.Value); }
    }

    private static void ModProportionsAndCorners(Action<bool, string> check)
    {
        var model = new ModelFormatTCN().Load(Path.Combine(AppContext.BaseDirectory, "Models", "Players", "Alex.xml"));
        var alpha = new byte[64 * 64];
        Array.Fill(alpha, (byte)255);
        foreach (var source in model.Meshes.Where(m => m.IsArmor))
        {
            var layer = new SkinLayerGeometry(source);
            layer.Update(alpha, 64, 64);
            var size = layer.Mesh.Bounds.Maxs - layer.Mesh.Bounds.Mins;
            bool head = source.Part == ModelPart.Helmet;
            bool arm = source.Part == ModelPart.LeftArmArmor || source.Part == ModelPart.RightArmArmor;
            float width = head ? 9.44f : arm ? 3.45f : source.Part == ModelPart.ChestArmor ? 8.4f : 4.6f;
            check((size - new Vector3(width, head ? 9.44f : 12.42f, head ? 9.44f : 4.6f)).Length < .0001f,
                "Default mod dimensions for " + source.Part);
            if (head) check(Math.Abs(layer.Mesh.Bounds.Mins.Y + 8.7672f) < .0001f, "Head uses the mod's vertical pivot correction");
        }
        // These two atlas pixels meet at the right/front hat corner. They must
        // occupy one 1.18-unit cube, with two different colors on each end cap.
        Array.Clear(alpha);
        alpha[12 * 64 + 40] = alpha[12 * 64 + 39] = 255;
        var helmet = new SkinLayerGeometry(model.Meshes.Single(m => m.Part == ModelPart.Helmet));
        helmet.Update(alpha, 64, 64);
        check(((helmet.Mesh.Bounds.Maxs - helmet.Mesh.Bounds.Mins) - new Vector3(1.18f)).Length < .0001f,
            "Pixels across the atlas seam share one corner voxel");
        float area = 0;
        foreach (var face in helmet.Mesh.Faces)
        {
            var v = face.Vertices;
            area += (Vector3.Cross(v[1] - v[0], v[2] - v[0]).Length + Vector3.Cross(v[2] - v[0], v[3] - v[0]).Length) / 2;
            var point = (v[0] + v[1] + v[2]) / 3;
            check(helmet.TryPick(point + face.Normal * 2, point - face.Normal, out _, out var pixel) &&
                pixel == helmet.PixelFor(face),
                "Each corner triangle picks its visible color");
        }
        check(Math.Abs(area - 6 * 1.18f * 1.18f) < .0001f, "Joined corner has no overlapping surfaces or holes");
    }

    private static void OptionsAndScene(Action<bool, string> check, string scratch)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        string directory = Directory.GetCurrentDirectory();
        var settingsSnapshot = typeof(GlobalSettings).GetProperties().Where(p => p.CanWrite).ToDictionary(p => p, p => p.GetValue(null));
        try
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            var settings = new Paril.Settings.Settings();
            settings.Structures.Add(typeof(GlobalSettings));
            settings.LoadDefaults();
            check(!GlobalSettings.SkinLayers3D, "3D layers default off for existing settings");
            MCSkin3D.Program.Version = new Paril.Components.Update.Version(1, 7, 2, 0);
            using var editor = new Editor();
            using var viewport = new OpenTK.GLControl.GLControl { Size = new Size(800, 600) };
            typeof(Editor).GetProperty("Renderer").SetValue(editor, viewport);
            void Set(string field, object value) => typeof(Editor).GetField(field, flags).SetValue(editor, value);
            var menu = (ToolStripMenuItem)typeof(Editor).GetField("_skinLayersMenu", flags).GetValue(editor);
            check(menu.Text == "3D Skin Layers" && !menu.Checked, "View option has an English fallback and reflects the saved setting");
            menu.PerformClick();
            check(menu.Checked && GlobalSettings.SkinLayers3D, "Clicking the View option immediately enables 3D layers");
            string file = Path.Combine(scratch, "layer-settings.ini");
            settings.Save(file);
            GlobalSettings.SkinLayers3D = false;
            settings.Load(file);
            check(GlobalSettings.SkinLayers3D, "3D layer preference survives save and reload");
            var scene = (List<PreviewItem>)typeof(Editor).GetField("_scene", flags).GetValue(editor);
            foreach (var name in new[] { "Steve", "Alex", "Steve (Minimal)", "Alex (Minimal)", "Cape" })
            {
                string modelPath = Path.Combine(AppContext.BaseDirectory, "Models", "Players", name + ".xml");
                var model = new ModelFormatTCN().Load(modelPath); model.File = new FileInfo(modelPath);
                var document = new Skin("fixture.png") { Model = model, Size = new Size((int)model.DefaultWidth, (int)model.DefaultHeight) };
                var item = new PreviewItem { Document = document, Model = model, Alpha = new byte[document.Width * document.Height] };
                Array.Fill(item.Alpha, (byte)255);
                item.UpdateSkinLayers();
                int expected = name == "Cape" ? 0 : name.EndsWith("(Minimal)") ? 1 : 6;
                check(item.SkinLayers.Count == expected, "Preview selects only the intended overlays for " + name);
                if (name == "Cape") continue;
                scene.Clear(); scene.Add(item);
                Set("_lastSkin", document);
                Set("_currentViewMode", ViewMode.Perspective);
                editor.PerformResetCamera();
                var projection = (Matrix4)typeof(Editor).GetField("_projectionMatrix", flags).GetValue(editor);
                var helmet = item.SkinLayers.Single(p => p.Key.Part == ModelPart.Helmet).Value;
                var top = helmet.PixelFaces.Where(f => f.Normal.Z < -.9f)
                    .OrderBy(f => (f.Vertices.Aggregate(Vector3.Zero, (sum, v) => sum + v) / 4 - helmet.Mesh.Center).LengthSquared).First();
                var point = top.Vertices.Aggregate(Vector3.Zero, (sum, v) => sum + v) / 4;
                var clip = Vector4.TransformRow(new Vector4(Vector3.TransformPosition(point, helmet.Mesh.Matrix), 1), projection);
                var location = new Point((int)((clip.X / clip.W + 1) * 400), (int)((1 - clip.Y / clip.W) * 300));
                var expectedPixel = helmet.PixelFor(top);
                bool hit = editor.GetPick(location.X, location.Y, out var pixel);
                check(hit && pixel == expectedPixel, $"Editor hits the rendered raised pixel on {name}: expected {expectedPixel}, got {pixel} at {location}");
                for (int i = 0; i < 100; ++i) editor.GetPick(location.X, location.Y, out _);
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 100; ++i) editor.GetPick(location.X, location.Y, out _);
                check(GC.GetAllocatedBytesForCurrentThread() - allocated == 0, "Raised pixel picking allocates no per-query buffers");
                item.Alpha[expectedPixel.Y * document.Width + expectedPixel.X] = 0;
                item.UpdateSkinLayers();
                check(editor.GetPick(location.X, location.Y, out pixel) && pixel == expectedPixel,
                    "An empty overlay pixel remains paintable over the base skin");
                if (name == "Alex")
                {
                    GlobalSettings.SkinLayerParts = 1; item.UpdateSkinLayers();
                    check(item.SkinLayers.Count == 1 && item.SkinLayers.Keys.Single().Part == ModelPart.Helmet,
                        "Per-layer settings leave disabled clothing as flat source meshes");
                    GlobalSettings.SkinLayerParts = 0; item.UpdateSkinLayers();
                    check(item.SkinLayers.Count == 0, "Every 3D layer can be disabled independently of model visibility");
                    GlobalSettings.SkinLayerParts = 63; item.UpdateSkinLayers();
                    Set("_3DZoom", -650f);
                    typeof(Editor).GetMethod("CalculateMatrices", flags).Invoke(editor, null);
                    check(!item.UseSkinLayers, "Distant previews switch to flat layers at the configured distance");
                    editor.PerformResetCamera();
                    check(item.UseSkinLayers, "Zooming back in restores 3D layers");
                }
                GlobalSettings.SkinLayers3D = false;
                item.UpdateSkinLayers();
                check(item.SkinLayers.Count == 0 && !document.Dirty, "Disabling releases preview geometry without editing the skin");
                GlobalSettings.SkinLayers3D = true;
            }
            Set("_lastSkin", null);
            menu.PerformClick();
            check(!GlobalSettings.SkinLayers3D && !menu.Checked && scene.Count == 0, "View option switches back to flat rendering");
        }
        finally
        {
            Directory.SetCurrentDirectory(directory);
            foreach (var pair in settingsSnapshot) pair.Key.SetValue(null, pair.Value);
        }
    }
}
