using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using MCSkin3D;
using MCSkin3D.Models;
using OpenTK.Mathematics;
using Paril.Components;
using Paril.Drawing;
using Paril.OpenGL;

internal static class WorkspaceChecks
{
    private const BindingFlags Private = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    public static void History(Action<bool, string> check)
    {
        var skin = new Skin("Player.png");
        var cape = new Skin("Cape.png");
        var history = new EditorHistory();
        var calls = new List<string>();
        void Activate(Skin document) => calls.Add("focus:" + document.Name);
        history.Add(skin, new Change("skin1", calls));
        history.Add(cape, new Change("cape1", calls));
        history.Undo(Activate); history.Undo(Activate);
        check(calls.SequenceEqual(new[] { "focus:Cape", "undo:cape1:Cape", "focus:Player", "undo:skin1:Player" }), "Undo alternates documents in reverse chronological order and focuses before applying");
        check(skin.Dirty && cape.Dirty && !history.CanUndo && history.CanRedo, "Each undone document becomes dirty, including intermediate multi-step entries");
        calls.Clear(); history.Redo(Activate); history.Redo(Activate);
        check(calls.SequenceEqual(new[] { "focus:Player", "redo:skin1:Player", "focus:Cape", "redo:cape1:Cape" }), "Redo follows chronological order across documents");
        check(!history.CanRedo && history.CanUndo, "Redo cursor reaches the end");
        cape.Name = "Renamed";
        check(history.UndoList.Last().Action == "Renamed: cape1", "History labels track document renames");
        history.Undo(Activate);
        history.Add(skin, new Change("skin2", calls));
        check(!history.CanRedo && history.UndoList.Count() == 2, "A new edit discards redo entries from every document");
        history.Add(cape, new Change("cape2", calls));
        history.Undo(Activate); history.Undo(Activate);
        history.Remove(skin);
        check(!history.CanUndo && history.CanRedo && history.RedoList.Single().Document == cape, "Deleting or resizing a document prunes both sides of the cursor without losing other documents");
        history.Redo(Activate);
        check(history.CanUndo && !history.CanRedo, "Pruned history continues correctly");
        history.Remove(cape);
        check(!history.CanUndo && !history.CanRedo, "Removing final document leaves an empty timeline");
        history.Add(skin, new Change("failure", calls));
        try { history.Undo(_ => throw new IOException("Cannot activate")); } catch (IOException) { }
        check(history.CanUndo && !history.CanRedo, "Failed activation leaves the undo cursor unchanged");
    }

    public static void GeometryAndPicking(Action<bool, string> check)
    {
        string previousDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            GeometryAndPickingCore(check);
        }
        finally { Directory.SetCurrentDirectory(previousDirectory); }
    }

    private static void GeometryAndPickingCore(Action<bool, string> check)
    {
        string models = Path.Combine(AppContext.BaseDirectory, "Models");
        Model Load(string path)
        {
            string file = Path.Combine(models, path + ".xml");
            var model = new ModelFormatTCN().Load(file); model.File = new FileInfo(file); return model;
        }
        // Role resolution uses the configured model root, as it does in the actual editor.
        var sourcePlayer = Load("Players/Steve");
        var sourceCape = Load("Players/Cape");
        var sourceElytra = Load("Items/Elytra");
        foreach (var path in new[] { "Players/Steve", "Players/Alex", "Players/Steve (Minimal)", "Players/Alex (Minimal)" })
        {
            var model = Load(path);
            check(PreviewItem.Role(new Skin("test.png") { Model = model, Size = new Size((int)model.DefaultWidth * 2, (int)model.DefaultHeight * 2) }) == EditingTarget.Skin,
                "Library selector accepts scaled " + path);
        }
        check(PreviewItem.Role(new Skin("cape.png") { Model = sourceCape, Size = new Size(128, 64) }) == EditingTarget.Cape,
            "Library selector accepts scaled capes");
        check(PreviewItem.Role(new Skin("wings.png") { Model = sourceElytra, Size = new Size(64, 32) }) == EditingTarget.Cape,
            "Library selector accepts elytra textures in the cape slot");
        check(PreviewItem.Role(new Skin("wrong.png") { Model = sourceCape, Size = new Size(64, 64) }) == null,
            "Library selector rejects incompatible dimensions without resizing the document");
        var player = PreviewItem.Clone(sourcePlayer);
        var cape = PreviewItem.Clone(sourceCape);
        var wings = PreviewItem.Clone(sourceElytra);
        check(!ReferenceEquals(player, sourcePlayer) && !ReferenceEquals(player.Meshes[0], sourcePlayer.Meshes[0]) &&
            !ReferenceEquals(player.PartsEnabled, sourcePlayer.PartsEnabled), "Preview instances own independent geometry and part visibility");
        var originalMatrix = sourceCape.Meshes[0].Matrix;
        var originalUv = sourceElytra.Meshes.SelectMany(m => m.Faces).SelectMany(f => f.TexCoords).ToArray();
        PreviewItem.PoseAccessory(cape, AccessoryView.Cape, 0f);
        check(sourceCape.Meshes[0].Matrix == originalMatrix, "Posing does not mutate the bundled cape model");
        bool Near(Vector3 actual, Vector3 expected) => (actual - expected).Length < .0001f;
        var capeMesh = cape.Meshes[0];
        check(Near(Vector3.TransformPosition(Vector3.Zero, capeMesh.Matrix), new Vector3(0, 0, 2)),
            "Cape top attaches at the torso back, with no extra half-pixel gap");
        check(capeMesh.Faces.SelectMany(f => f.Vertices).All(v =>
            Near(Vector3.TransformPosition(v, capeMesh.Matrix), ReferenceAccessory(v, false, false, 0f))),
            "Every cape vertex follows Minecraft's turn, resting tilt and layer translation");
        var foldedWidth = 0f;
        foreach (var pose in new[] { 0f, .5f, 1f })
        {
            PreviewItem.PoseAccessory(wings, AccessoryView.Elytra, pose);
            check(originalUv.SequenceEqual(wings.Meshes.SelectMany(m => m.Faces).SelectMany(f => f.TexCoords)), "Elytra poses preserve exact UV coordinates and mirrored wing layout");
            var right = wings.Meshes.Single(m => m.Part == ModelPart.RightArm);
            var left = wings.Meshes.Single(m => m.Part == ModelPart.LeftArm);
            check(Near(Vector3.TransformPosition(new Vector3(-4, 0, 0), right.Matrix), new Vector3(5, 0, 2)) &&
                Near(Vector3.TransformPosition(Vector3.Zero, left.Matrix), new Vector3(-5, 0, 2)),
                "Both " + pose + " wing pivots attach at the shoulder line and torso back");
            check(wings.Meshes.All(m => m.Faces.SelectMany(f => f.Vertices).All(v =>
                Near(Vector3.TransformPosition(v, m.Matrix), ReferenceAccessory(v, true, m.Part == ModelPart.RightArm, pose)))),
                "Every " + pose + " wing vertex matches the Minecraft model's scalar rotation and pivot transform");
            check(Math.Abs(right.Center.X + left.Center.X) < .001f && Math.Abs(right.Center.Z - left.Center.Z) < .001f, "Both wing poses attach symmetrically to the player's spine");
            var bounds = Bounds3.EmptyBounds;
            foreach (var mesh in wings.Meshes)
            {
                bounds += mesh.Bounds;
                foreach (var face in mesh.Faces)
                    foreach (var vertex in face.Vertices)
                    {
                        var world = Vector3.TransformPosition(vertex, mesh.Matrix);
                        check(world.X >= mesh.Bounds.Mins.X - .001f && world.X <= mesh.Bounds.Maxs.X + .001f &&
                            world.Y >= mesh.Bounds.Mins.Y - .001f && world.Y <= mesh.Bounds.Maxs.Y + .001f &&
                            world.Z >= mesh.Bounds.Mins.Z - .001f && world.Z <= mesh.Bounds.Maxs.Z + .001f, "Posed bounds contain rendered and picked vertices");
                    }
            }
            if (pose == 0f) foldedWidth = bounds.Maxs.X - bounds.Mins.X;
            else if (pose == 1f) check(bounds.Maxs.X - bounds.Mins.X > foldedWidth * 1.5f, "Spread pose visibly extends the wings");
            DrawGeometry(player, wings, "elytra-" + (int)(pose * 100));
        }
        DrawGeometry(player, cape, "cape");

        MCSkin3D.Program.Version = new Paril.Components.Update.Version(1, 7, 1, 0);
        GlobalSettings.DynamicOverlayTextSize = GlobalSettings.DynamicOverlayLineSize = 1;
        GlobalSettings.TreeViewHeight = 24;
        using var editor = new Editor();
        using var viewport = new OpenTK.GLControl.GLControl { Size = new Size(800, 600) };
        typeof(Editor).GetProperty("Renderer").SetValue(editor, viewport);
        var scene = (List<PreviewItem>)typeof(Editor).GetField("_scene", Private).GetValue(editor);
        var skinDoc = new Skin("Player.png") { Model = sourcePlayer, Size = new Size(128, 128) };
        var capeDoc = new Skin("Cape.png") { Model = sourceCape, Size = new Size(128, 64) };
        scene.Add(new PreviewItem { Document = skinDoc, Model = player, Sheet = new RectangleF(-32, 0, 64, 64) });
        scene.Add(new PreviewItem { Document = capeDoc, Model = cape, Sheet = new RectangleF(-32, -48, 64, 32) });
        void Set(string field, object value) => typeof(Editor).GetField(field, Private).SetValue(editor, value);
        Set("_lastSkin", skinDoc); Set("_2DZoom", 4f); Set("_currentViewMode", ViewMode.Orthographic);
        typeof(Editor).GetProperty("CombinedEnabled").SetValue(editor, true);
        typeof(Editor).GetMethod("CalculateMatrices", Private).Invoke(editor, null);
        check(editor.GetPick(405, 305, out var pixel) && pixel == new Point(66, 2), "Combined 2D picking maps canvas position to scaled skin pixels");
        check(!editor.GetPick(405, 121, out _), "Inactive cape sheet cannot receive skin edits");
        check(!editor.GetPick(405, 261, out _), "Gap between texture sheets is not paintable");
        Set("_lastSkin", capeDoc);
        check(editor.GetPick(405, 121, out pixel) && pixel == new Point(66, 6), "Cape picking uses its own offset and actual resolution");
        check(!editor.GetPick(405, 305, out _), "Inactive skin sheet cannot receive cape edits");
        Set("_currentViewMode", ViewMode.Hybrid);
        Set("_lastSkin", skinDoc);
        typeof(Editor).GetMethod("CalculateMatrices", Private).Invoke(editor, null);
        check(editor.GetPick(405, 155, out pixel) && pixel == new Point(66, 2), "Hybrid 2D picking keeps sheet placement and pixel scaling");
        Set("_lastSkin", capeDoc);
        Set("_currentViewMode", ViewMode.Perspective);
        Set("_3DRotationY", 180f);
        typeof(Editor).GetMethod("CalculateMatrices", Private).Invoke(editor, null);
        int capeHits = CountHits(editor);
        check(capeHits > 0, "3D picking finds the attached cape");
        Set("_lastSkin", skinDoc);
        check(!editor.GetPick(400, 300, out _), "Opaque companion cape blocks painting the skin behind it");
        scene[1].Alpha = new byte[capeDoc.Width * capeDoc.Height];
        check(editor.GetPick(400, 300, out _), "Fully transparent companion pixels do not block the editable skin");
        Array.Fill(scene[1].Alpha, (byte)128);
        check(!editor.GetPick(400, 300, out _), "Visible translucent companion pixels still block painting through them");
        Set("_lastSkin", capeDoc);
        Set("_3DRotationY", 0f);
        typeof(Editor).GetMethod("CalculateMatrices", Private).Invoke(editor, null);
        check(!editor.GetPick(400, 300, out _), "Inactive player blocks painting the cape through its torso");
        Set("_lastSkin", skinDoc);
        check(editor.GetPick(400, 300, out _), "The same front-facing location picks the skin when it is active");
        Set("_3DRotationY", 180f);
        foreach (var pose in new[] { 0f, .5f, 1f })
        {
            PreviewItem.PoseAccessory(wings, AccessoryView.Elytra, pose);
            scene[1].Model = wings; Set("_lastSkin", capeDoc);
            typeof(Editor).GetMethod("CalculateMatrices", Private).Invoke(editor, null);
            check(CountHits(editor) > 0, "3D picking finds " + pose + " elytra pixels");
        }
        for (int i = 0; i < 100; ++i) editor.GetPick(400, 300, out _);
        long beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; ++i) editor.GetPick(400, 300, out _);
        check(GC.GetAllocatedBytesForCurrentThread() - beforeAllocation < 1000, "Combined picking does not allocate per query");
        Set("_workspaceSkin", skinDoc); Set("_workspaceCape", capeDoc);
        typeof(Editor).GetProperty("AccessoryPresentation").SetValue(editor, AccessoryView.Elytra);
        var slider = (TrackBar)typeof(Editor).GetField("_wingSpreadSlider", Private).GetValue(editor);
        var sceneModel = scene[1].Model;
        var alpha = scene[1].Alpha;
        var originalDocumentModel = capeDoc.Model;
        float originalZoom = (float)typeof(Editor).GetField("_3DZoom", Private).GetValue(editor);
        int historyCount = editor.History.UndoList.Count();
        editor.SetWingSpread(1);
        foreach (int percentage in new[] { 0, 37, 100, 50, 0 })
        {
            slider.Value = percentage;
            check(Math.Abs(editor.WingSpread - percentage / 100f) < .0001f,
                "Native slider immediately sets " + percentage + "% wing spread");
            check(sceneModel.Meshes.All(m => m.Faces.SelectMany(f => f.Vertices).All(v =>
                Near(Vector3.TransformPosition(v, m.Matrix), ReferenceAccessory(v, true, m.Part == ModelPart.RightArm, editor.WingSpread)))),
                "Slider updates the existing rendered and picked geometry at " + percentage + "%");
        }
        check(scene[1].Model == sceneModel && scene[1].Alpha == alpha && capeDoc.Model == originalDocumentModel &&
            editor.History.UndoList.Count() == historyCount && !capeDoc.Dirty &&
            (float)typeof(Editor).GetField("_3DZoom", Private).GetValue(editor) == originalZoom,
            "Slider preserves texture buffers, document model, clean state, history and camera");
        editor.SetWingSpread(-1); check(editor.WingSpread == 0, "Wing spread clamps to folded");
        editor.SetWingSpread(2); check(editor.WingSpread == 1, "Wing spread clamps to fully spread");
        bool invalidRejected = false;
        try { editor.SetWingSpread(float.NaN); } catch (ArgumentOutOfRangeException) { invalidRejected = true; }
        check(invalidRejected && editor.WingSpread == 1, "Invalid pose values cannot corrupt picking matrices");
        typeof(Editor).GetMethod("LayoutWorkspaceSheets", Private).Invoke(editor, null);
        var compactCape = (RectangleF)typeof(Editor).GetField("_capeSheet", Private).GetValue(editor);
        var compactSkin = (RectangleF)typeof(Editor).GetField("_skinSheet", Private).GetValue(editor);
        check(compactSkin.Top - compactCape.Bottom == 2 && compactSkin.Bottom == -compactCape.Top,
            "Combined sheets have a small two-pixel gap and remain centered");
        scene[0].Sheet = compactSkin; scene[1].Sheet = compactCape;
        Set("_currentViewMode", ViewMode.Orthographic); Set("_lastSkin", skinDoc);
        typeof(Editor).GetMethod("CalculateMatrices", Private).Invoke(editor, null);
        check(!editor.GetPick(400, (int)(300 + (compactCape.Bottom + 1) * 4), out _),
            "The narrow gap between the sheets cannot receive paint");
        check(editor.GetPick((int)(400 + (compactSkin.Left + 5.25f) * 4), (int)(300 + (compactSkin.Top + 5.25f) * 4), out pixel) && pixel == new Point(10, 10),
            "The compact layout still maps exact scaled skin pixels");
        VerifyPixelRouting(editor, scene, skinDoc, capeDoc, cape, check);
        // Exercise native layout without creating or rendering an OpenGL context.
        var bar = (FlowLayoutPanel)typeof(Editor).GetField("_workspaceBar", Private).GetValue(editor);
        Set("_currentViewMode", ViewMode.Perspective);
        typeof(Editor).GetProperty("AccessoryPresentation").SetValue(editor, AccessoryView.Elytra);
        typeof(Editor).GetMethod("RefreshWorkspaceControls", Private).Invoke(editor, null);
        bar.Visible = true;
        foreach (int width in new[] { 900, 420, 630 })
        {
            bar.Parent?.Controls.Remove(bar);
            using var host = new Form { ClientSize = new Size(width, 250), StartPosition = FormStartPosition.Manual,
                Location = new Point(-30000, -30000), ShowInTaskbar = false };
            if (width == 630) host.Font = new Font(host.Font.FontFamily, host.Font.Size * 1.5f);
            host.Controls.Add(bar); host.Show(); host.PerformLayout(); bar.PerformLayout();
            check(bar.Controls.Cast<Control>().Where(c => c.Visible).All(c => c.Bottom <= bar.Height && c.Right <= bar.Width), "Workspace toolbar wraps within " + width + "px layout");
            using var image = new Bitmap(width, Math.Max(1, bar.Height));
            bar.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            image.Save(Path.Combine(AppContext.BaseDirectory, "workspace-" + width + ".png"));
            host.Controls.Remove(bar);
            host.Close();
        }
        bar.Dispose();
    }

    private static void VerifyPixelRouting(Editor editor, List<PreviewItem> scene, Skin skin, Skin cape, Model capeModel, Action<bool, string> check)
    {
        // Exercise the real mesh queue and pixel undo commands with CPU textures. Rendering
        // and actual document activation still belong to the desktop integration suite.
        MCSkin3D.Program.MainThread = Thread.CurrentThread;
        using var skinTexture = new MemoryTexture(skin.Width, skin.Height, Color.Blue);
        using var capeTexture = new MemoryTexture(cape.Width, cape.Height, Color.Orange);
        skin.GLImage = skinTexture; cape.GLImage = capeTexture;
        scene[1].Model = capeModel;
        typeof(Editor).GetField("_lastSkin", Private).SetValue(editor, skin);
        typeof(Editor).GetProperty("MeshRenderer").SetValue(editor, new CpuRenderer());
        capeTexture.Pixels[10 * cape.Width + 10] = new ColorPixel(255, 128, 0, 128).RGBA;
        typeof(Editor).GetMethod("QueueSceneMeshes", Private).Invoke(editor, new object[] { skinTexture });
        check(scene[0].Model.Meshes.All(m => ReferenceEquals(m.Texture, skinTexture)) && capeModel.Meshes.All(m => ReferenceEquals(m.Texture, capeTexture)),
            "The real scene queue routes each model to its own live texture");
        check(capeModel.Meshes[0].HasTransparency, "The scene queue detects transparency using the accessory's effective UVs");
        check(scene.SelectMany(s => s.Model.Meshes).All(m => m.GetUserData<ClientArrayMeshUserData>().ColorArray.All(c => c == 1f)),
            "New preview meshes initialize visible vertex colors before their first draw");
        capeModel.PartsEnabled[0] = false;
        GlobalSettings.Ghost = true;
        typeof(Editor).GetMethod("QueueSceneMeshes", Private).Invoke(editor, new object[] { skinTexture });
        check(capeModel.Meshes[0].GetUserData<ClientArrayMeshUserData>().ColorArray[3] == .25f, "Preview part ghosting updates the reusable color buffer");
        GlobalSettings.Ghost = false; capeModel.PartsEnabled[0] = true;

        var point = new Point(10, 10);
        int skinIndex = point.Y * skin.Width + point.X, capeIndex = point.Y * cape.Width + point.X;
        int skinBefore = skinTexture.Pixels[skinIndex], capeBefore = capeTexture.Pixels[capeIndex];
        Color Unpack(int packed) { var p = new ColorPixel(packed); return Color.FromArgb(p.Alpha, p.Red, p.Green, p.Blue); }
        var skinEdit = new PixelsChangedUndoable("skin pixel");
        skinEdit.Points.Add(point, Tuple.Create(Unpack(skinBefore), new ColorAlpha(Color.Lime, 1)));
        var capeEdit = new PixelsChangedUndoable("cape pixel");
        capeEdit.Points.Add(point, Tuple.Create(Unpack(capeBefore), new ColorAlpha(Color.Magenta, 1)));
        skinTexture.Pixels[skinIndex] = new ColorPixel(0, 255, 0, 255).RGBA;
        capeTexture.Pixels[capeIndex] = new ColorPixel(255, 0, 255, 255).RGBA;
        var history = new EditorHistory(); history.Add(skin, skinEdit); history.Add(cape, capeEdit);
        void Activate(Skin document)
        {
            typeof(Editor).GetField("_lastSkin", Private).SetValue(editor, document);
            GlobalDirtiness.CurrentSkin = document.GLImage;
        }
        var previousTexture = GlobalDirtiness.CurrentSkin;
        try
        {
            history.Undo(Activate);
            check(capeTexture.Pixels[capeIndex] == capeBefore && skinTexture.Pixels[skinIndex] != skinBefore, "Real pixel undo restores only the latest document's RGBA value");
            history.Undo(Activate);
            check(skinTexture.Pixels[skinIndex] == skinBefore, "Next pixel undo restores the other document");
            history.Redo(Activate); history.Redo(Activate);
            check(skinTexture.Pixels[skinIndex] == new ColorPixel(0, 255, 0, 255).RGBA && capeTexture.Pixels[capeIndex] == new ColorPixel(255, 0, 255, 255).RGBA,
                "Real pixel redo replays both documents without cross-writing their buffers");
        }
        finally { GlobalDirtiness.CurrentSkin = previousTexture; skin.GLImage = cape.GLImage = null; }
    }

    private sealed class CpuRenderer : Renderer
    {
        private readonly ClientArrayRenderer arrays = (ClientArrayRenderer)RuntimeHelpers.GetUninitializedObject(typeof(ClientArrayRenderer));
        public override IMeshUserData CreateUserData(Mesh mesh) => arrays.CreateUserData(mesh);
        public override void UpdateUserData(Mesh mesh) => arrays.UpdateUserData(mesh);
        public override void RenderMesh(Mesh mesh) => throw new InvalidOperationException("This test does not render OpenGL frames.");
    }

    private sealed class MemoryTexture : Texture
    {
        public int[] Pixels;
        private int width, height;
        public override int Width => width;
        public override int Height => height;
        public MemoryTexture(int width, int height, Color color)
        {
            this.width = width; this.height = height;
            Pixels = Enumerable.Repeat(new ColorPixel(color.R, color.G, color.B, color.A).RGBA, width * height).ToArray();
        }
        public override void Get(IntPtr array) => Marshal.Copy(Pixels, 0, array, Pixels.Length);
        public override void Upload(IntPtr array, int width, int height)
        {
            this.width = width; this.height = height; Pixels = new int[width * height]; Marshal.Copy(array, Pixels, 0, Pixels.Length);
        }
        public override void Get<T>(T[] array) => throw new NotSupportedException();
        public override void Upload<T>(T[] array, int width, int height) => throw new NotSupportedException();
        public override void Load(Bitmap image) => throw new NotSupportedException();
        public override void Bind() => throw new InvalidOperationException("CPU texture cannot be bound to OpenGL.");
        public override void SetMipmapping(bool enable) { }
        public override void SetRepeat(bool enable) { }
        public override void Dispose() { }
    }

    private static int CountHits(Editor editor)
    {
        int count = 0;
        for (int y = 40; y < 560; y += 8)
            for (int x = 40; x < 760; x += 8) if (editor.GetPick(x, y, out _)) ++count;
        return count;
    }

    // Independent scalar reference for Minecraft's layer/model transforms; deliberately
    // does not compose matrices or call the preview pose implementation.
    private static Vector3 ReferenceAccessory(Vector3 point, bool elytra, bool exportedRight, float pose)
    {
        double x = point.X, y = point.Y, z = point.Z;
        double pitch = (elytra ? (15 + 5 * pose) : 6) * Math.PI / 180;
        if (!elytra) { x = -x; z = -z; }
        else if (exportedRight) x += 4;
        double tiltedY = y * Math.Cos(pitch) - z * Math.Sin(pitch);
        z = y * Math.Sin(pitch) + z * Math.Cos(pitch);
        y = tiltedY;
        if (elytra)
        {
            double roll = (15 + 75 * pose) * (exportedRight ? -1 : 1) * Math.PI / 180;
            double rolledX = x * Math.Cos(roll) - y * Math.Sin(roll);
            y = x * Math.Sin(roll) + y * Math.Cos(roll);
            x = rolledX + (exportedRight ? 5 : -5);
        }
        return new Vector3((float)x, (float)y, (float)z + 2);
    }

    private static void DrawGeometry(Model player, Model accessory, string name)
    {
        using var bitmap = new Bitmap(1200, 360);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(31, 35, 42));
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var views = new[] { 0f, 90f, 180f };
        var labels = new[] { "Back", "Side", "Front" };
        for (int view = 0; view < views.Length; ++view)
        {
            var rotation = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(views[view]));
            var polygons = new List<(float depth, PointF[] points, bool accessory)>();
            foreach (var model in new[] { player, accessory })
                foreach (var mesh in model.Meshes)
                    foreach (var face in mesh.Faces)
                    {
                        var transformed = face.Vertices.Select(v => Vector3.TransformPosition(v, mesh.Matrix * rotation)).ToArray();
                        polygons.Add((transformed.Average(v => v.Z), transformed.Select(v => new PointF(view * 400 + 200 + v.X * 7, 100 + v.Y * 7)).ToArray(), model == accessory));
                    }
            foreach (var polygon in polygons.OrderBy(p => p.depth))
            {
                using var brush = new SolidBrush(polygon.accessory ? Color.FromArgb(210, 236, 157, 54) : Color.FromArgb(230, 79, 145, 187));
                graphics.FillPolygon(brush, polygon.points);
                graphics.DrawPolygon(Pens.Black, polygon.points);
            }
            graphics.DrawString(labels[view], SystemFonts.DefaultFont, System.Drawing.Brushes.White, view * 400 + 175, 310);
        }
        bitmap.Save(Path.Combine(AppContext.BaseDirectory, "geometry-" + name + ".png"), ImageFormat.Png);
    }

    private sealed class Change : IUndoable
    {
        private readonly List<string> calls;
        public string Action { get; }
        public Change(string action, List<string> calls) { Action = action; this.calls = calls; }
        public void Undo(object obj) => calls.Add("undo:" + Action + ":" + ((Skin)obj).Name);
        public void Redo(object obj) => calls.Add("redo:" + Action + ":" + ((Skin)obj).Name);
    }
}
