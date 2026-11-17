using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using MCSkin3D;
using MCSkin3D.Models;
using OpenTK.Mathematics;
using Paril.OpenGL;

internal static class FirstPersonChecks
{
    private const BindingFlags Private = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    internal static void Run(Action<bool, string> check, string scratch)
    {
        string directory = Directory.GetCurrentDirectory();
        var saved = typeof(GlobalSettings).GetProperties().Where(p => p.CanWrite).ToDictionary(p => p, p => p.GetValue(null));
        try
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            var settings = new Paril.Settings.Settings();
            settings.Structures.Add(typeof(GlobalSettings));
            settings.LoadDefaults();
            check(!GlobalSettings.FirstPersonLeftHand, "New and older settings start with the right hand");

            foreach (var name in new[] { "Steve", "Alex", "Steve (Minimal)", "Alex (Minimal)" })
            {
                var source = Load(name);
                var originalMatrices = source.Meshes.Select(m => m.Matrix).ToArray();
                var preview = PreviewItem.Clone(source);
                var uv = preview.Meshes.SelectMany(m => m.Faces).SelectMany(f => f.TexCoords).ToArray();
                var vertices = preview.Meshes.SelectMany(m => m.Faces).SelectMany(f => f.Vertices).ToArray();
                FirstPersonPreview.Pose(preview);
                check(source.Meshes.Select(m => m.Matrix).SequenceEqual(originalMatrices) &&
                    preview.Meshes.SelectMany(m => m.Faces).SelectMany(f => f.TexCoords).SequenceEqual(uv) &&
                    preview.Meshes.SelectMany(m => m.Faces).SelectMany(f => f.Vertices).SequenceEqual(vertices),
                    name + " first-person posing preserves document transforms, atlas orientation and local geometry");
                var right = preview.Meshes.Single(m => m.Part == ModelPart.RightArm);
                var left = preview.Meshes.Single(m => m.Part == ModelPart.LeftArm);
                // Independently evaluated Minecraft 26.3 idle-hand positions,
                // rounded to a thousandth of a block (the renderer uses pixels).
                var sample = name.StartsWith("Alex") ? new Vector3(-2, 10, -2) : new Vector3(-3, 10, -2);
                var expected = name.StartsWith("Alex") ? new Vector3(.667f, -.334f, -1.064f) : new Vector3(.688f, -.291f, -1.023f);
                check((Vector3.TransformPosition(sample, right.Matrix) / 16 - expected).Length < .002f,
                    name + " fingertip corner matches Minecraft's resting first-person pose");
                foreach (var sleeve in preview.Meshes.Where(m => m.Part == ModelPart.RightArmArmor || m.Part == ModelPart.LeftArmArmor))
                    check(sleeve.Matrix == (sleeve.Part == ModelPart.RightArmArmor ? right.Matrix : left.Matrix),
                        name + " sleeves attach to their arms despite historical exported pivot offsets");
                var rightPoints = right.Faces.SelectMany(f => f.Vertices).Select(v => Vector3.TransformPosition(v, right.Matrix)).ToArray();
                var leftPoints = left.Faces.SelectMany(f => f.Vertices).Select(v => Vector3.TransformPosition(v, left.Matrix)).ToArray();
                check(rightPoints.All(v => leftPoints.Any(p => (p - new Vector3(-v.X, v.Y, v.Z)).Length < .001f)),
                    name + " left and right hands have mirrored positions without mirroring their textures");
                foreach (var mesh in preview.Meshes.Where(m => FirstPersonPreview.IsArm(m.Part, false) || FirstPersonPreview.IsArm(m.Part, true)))
                {
                    var points = mesh.Faces.SelectMany(f => f.Vertices).Select(v => Vector3.TransformPosition(v, mesh.Matrix)).ToArray();
                    check(points.All(v => float.IsFinite(v.LengthSquared) &&
                        v.X >= mesh.Bounds.Mins.X - .001f && v.X <= mesh.Bounds.Maxs.X + .001f &&
                        v.Y >= mesh.Bounds.Mins.Y - .001f && v.Y <= mesh.Bounds.Maxs.Y + .001f &&
                        v.Z >= mesh.Bounds.Mins.Z - .001f && v.Z <= mesh.Bounds.Maxs.Z + .001f) &&
                        (points.Aggregate(Vector3.Zero, (sum, v) => sum + v) / points.Length - mesh.Center).Length < .001f,
                        name + " arm bounds and sorting centers follow the rendered pose");
                }
                foreach (bool leftHand in new[] { false, true })
                {
                    var parts = preview.Meshes.Where(m => FirstPersonPreview.IsArm(m.Part, leftHand)).ToArray();
                    check(parts.Length == (name.EndsWith("(Minimal)") ? 1 : 2) &&
                        parts.All(m => m.Part == (leftHand ? ModelPart.LeftArm : ModelPart.RightArm) ||
                            m.Part == (leftHand ? ModelPart.LeftArmArmor : ModelPart.RightArmArmor)),
                        name + " selects exactly the chosen hand and available sleeve");
                }
            }

            MCSkin3D.Program.Version = new Paril.Components.Update.Version(1, 7, 2, 0);
            using var editor = new Editor();
            using var viewport = new OpenTK.GLControl.GLControl { Size = new Size(800, 600) };
            typeof(Editor).GetProperty("Renderer").SetValue(editor, viewport);
            var model = Load("Steve");
            var document = new Skin("first-person.png") { Model = model, Size = new Size(64, 64) };
            var posed = PreviewItem.Clone(model);
            FirstPersonPreview.Pose(posed);
            var scene = Field<List<PreviewItem>>(editor, "_scene");
            var previewItem = new PreviewItem { Document = document, Model = posed, IsFirstPerson = true };
            scene.Add(previewItem);
            GlobalSettings.SkinLayers3D = true;
            previewItem.UpdateSkinLayers();
            check(!previewItem.UseSkinLayers && previewItem.SkinLayers.Count == 0 && GlobalSettings.SkinLayers3D,
                "First-person previews keep sleeves flat without changing the saved 3D Skin Layers setting");
            Set(editor, "_lastSkin", document);
            Set(editor, "_currentViewMode", ViewMode.FirstPerson);
            Call(editor, "RefreshWorkspaceControls");
            Call(editor, "CalculateMatrices");
            var hand = Field<ComboBox>(editor, "_firstPersonHand");
            var options = Field<GroupBox>(editor, "_firstPersonGroup");
            check(hand.SelectedIndex == 0 && new[] { "firstPersonFovSlider", "firstPersonFovValue", "firstPersonDefault" }
                .All(name => options.Controls.Find(name, true).Length == 0),
                "First-person controls offer handedness without an ineffective FOV control or reset button");
            var defaultProjection = Field<Matrix4>(editor, "_projectionMatrix");
            check(Math.Abs(defaultProjection.M22 - 1 / Math.Tan(35 * Math.PI / 180)) < .000001,
                "The first-person projection uses Minecraft's fixed 70-degree vertical FOV");
            hand.SelectedIndex = 1;
            check(GlobalSettings.FirstPersonLeftHand, "Selecting Left hand updates the preview preference");
            editor.PerformResetCamera();
            typeof(Editor).GetMethod("rendererControl_MouseWheel", Private).Invoke(editor,
                new object[] { viewport, new MouseEventArgs(MouseButtons.None, 0, 100, 100, 120) });
            check(Field<Matrix4>(editor, "_projectionMatrix") == defaultProjection && hand.SelectedIndex == 1,
                "Reset Camera and the mouse wheel leave the fixed hand projection and handedness unchanged");
            string file = Path.Combine(scratch, "first-person-settings.ini");
            settings.Save(file);
            File.AppendAllText(file, "FirstPersonFov=110" + Environment.NewLine);
            GlobalSettings.FirstPersonLeftHand = false;
            settings.Load(file);
            Call(editor, "CalculateMatrices");
            settings.Save(file);
            check(GlobalSettings.FirstPersonLeftHand && Field<Matrix4>(editor, "_projectionMatrix") == defaultProjection &&
                !File.ReadAllText(file).Contains("FirstPersonFov="),
                "Hand preference persists and obsolete FOV settings are safely ignored and removed on save");
        }
        finally
        {
            Directory.SetCurrentDirectory(directory);
            foreach (var pair in saved) pair.Key.SetValue(null, pair.Value);
        }
    }

    private static Model Load(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Models", "Players", name + ".xml");
        var model = new ModelFormatTCN().Load(path);
        model.File = new FileInfo(path);
        return model;
    }

    private static T Field<T>(Editor editor, string name) => (T)typeof(Editor).GetField(name, Private).GetValue(editor);
    private static void Set(Editor editor, string name, object value) => typeof(Editor).GetField(name, Private).SetValue(editor, value);
    private static void Call(Editor editor, string name) => typeof(Editor).GetMethod(name, Private).Invoke(editor, null);
}
