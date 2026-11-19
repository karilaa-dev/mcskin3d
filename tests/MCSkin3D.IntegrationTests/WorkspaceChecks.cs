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
    private static void OfficialCapePreview()
    {
        var original = Field<Skin>(editor, "_lastSkin");
        var tree = Field<TreeView>(editor, "treeView1");
        var player = WorkspaceFixture(original, "Official Cape Preview Player", "Players/Steve", 64, 64, Color.CornflowerBlue);
        tree.SelectedNode = player;
        editor.PreviewOfficialCape(OfficialCapeCatalog.All.Single(c => c.Id == "migrator"));
        var reference = Field<Skin>(editor, "_workspaceCape");
        Check(reference.IsReadOnly && Field<Skin>(editor, "_lastSkin") == player, "Official cape leaves the skin as the editable target");
        var originalPixels = new int[reference.Width * reference.Height];
        using (var pixels = new ColorGrabber(reference.GLImage, reference.Width, reference.Height))
        {
            pixels.Load();
            for (int y = 0; y < reference.Height; ++y)
                for (int x = 0; x < reference.Width; ++x) originalPixels[y * reference.Width + x] = pixels[x, y].RGBA;
        }
        editor.SetEditingTarget(EditingTarget.Cape);
        Check(Field<Skin>(editor, "_lastSkin") == player, "Official cape cannot be activated as an editing target");
        foreach (var mode in new[] { ViewMode.Orthographic, ViewMode.Perspective, ViewMode.Hybrid })
        {
            Call(editor, "SetViewMode", mode);
            foreach (var view in new[] { AccessoryView.Cape, AccessoryView.Elytra })
            {
                editor.SetAccessoryView(view);
                foreach (var pose in new[] { 0f, .5f, 1f })
                {
                    editor.SetWingSpread(pose);
                    CaptureWorkspace("official-" + mode + "-" + view + "-" + pose);
                }
            }
        }
        Call(editor, "SetViewMode", ViewMode.Orthographic);
        PaintWorkspacePixel(player, new Point(10, 10), Color.Magenta, ViewMode.Orthographic);
        Check(ReadDocumentPixel(player, new Point(10, 10)) == new ColorPixel(255, 0, 255, 255).RGBA, "Skin can be edited with an official reference equipped");
        editor.SetWorkspaceDocument(EditingTarget.Skin, null);
        Check(Field<Skin>(editor, "_lastSkin") == reference, "Official cape can be viewed without an editable skin");
        int before = editor.History.UndoList.Count();
        var click = new MouseEventArgs(MouseButtons.Left, 1, editor.Renderer.Width / 2, editor.Renderer.Height / 2, 0);
        foreach (var tool in Field<List<ToolIndex>>(editor, "_tools").Where(t => t.Tool is PencilTool || t.Tool is FloodFillTool || t.Tool is StampTool))
        {
            tool.MenuItem.PerformClick();
            Call(editor, "rendererControl_MouseDown", editor.Renderer, click);
            Call(editor, "rendererControl_MouseUp", editor.Renderer, click);
        }
        Call(editor, "PerformSave"); Call(editor, "PerformSaveAll");
        Check(editor.History.UndoList.Count() == before && !reference.Dirty, "Brush, fill, stamp and saving cannot edit an official cape or add history");
        using (var pixels = new ColorGrabber(reference.GLImage, reference.Width, reference.Height))
        {
            pixels.Load();
            bool unchanged = true;
            for (int y = 0; y < reference.Height; ++y)
                for (int x = 0; x < reference.Width; ++x) unchanged &= originalPixels[y * reference.Width + x] == pixels[x, y].RGBA;
            Check(unchanged, "Every official cape pixel survives preview, pose changes, tool clicks and saving unchanged");
        }
        editor.SetWorkspaceDocument(EditingTarget.Cape, null);
        Check(Field<Skin>(editor, "_workspaceCape") == null, "Clearing the official cape through the empty custom slot is safe");
        editor.SetCombinedMode(false); tree.SelectedNode = original;
    }

    private static void CombinedWorkspace()
    {
        var tree = Field<TreeView>(editor, "treeView1");
        var original = Field<Skin>(editor, "_lastSkin");
        var player = WorkspaceFixture(original, "Combined Player", "Players/Steve", 64, 64, Color.CornflowerBlue);
        var alex = WorkspaceFixture(original, "Combined Alex HD", "Players/Alex", 128, 128, Color.LightSeaGreen);
        var cape = WorkspaceFixture(original, "Combined Cape", "Players/Cape", 64, 32, Color.Goldenrod);
        var cape2 = WorkspaceFixture(original, "Combined Cape HD", "Items/Elytra", 128, 64, Color.OrangeRed);
        tree.SelectedNode = player;
        editor.SetCombinedMode(true);
        editor.SetWorkspaceDocument(EditingTarget.Cape, cape);
        Check(editor.CombinedEnabled && Field<Skin>(editor, "_lastSkin") == player, "Assigning companion keeps the current edit target");
        var pixel = new Point(10, 10);
        Call(editor, "SetViewMode", ViewMode.Orthographic);
        editor.PerformResetCamera();
        int playerBefore = ReadDocumentPixel(player, pixel);
        int capeBefore = ReadDocumentPixel(cape, pixel);
        PaintWorkspacePixel(player, pixel, Color.Magenta);
        int playerAfter = ReadDocumentPixel(player, pixel);
        Check(playerAfter != playerBefore && ReadDocumentPixel(cape, pixel) == capeBefore, "Painting the skin preserves the cape texture");
        editor.SetEditingTarget(EditingTarget.Cape);
        PaintWorkspacePixel(cape, pixel, Color.Lime);
        int capeAfter = ReadDocumentPixel(cape, pixel);
        Check(capeAfter != capeBefore && ReadDocumentPixel(player, pixel) == playerAfter, "Painting the cape preserves unsaved skin edits");
        editor.SetEditingTarget(EditingTarget.Cape);
        Check(ReadDocumentPixel(cape, pixel) == capeAfter, "Selecting the already-active target preserves unsaved edits");
        Call(editor, "PerformSaveAll");
        Check(!player.Dirty && !cape.Dirty, "Save All writes both documents separately");
        Call(editor, "PerformUndo");
        Check(Field<Skin>(editor, "_lastSkin") == cape && ReadDocumentPixel(cape, pixel) == capeBefore && cape.Dirty, "First undo restores and focuses the saved cape edit");
        Call(editor, "PerformUndo");
        Check(Field<Skin>(editor, "_lastSkin") == player && ReadDocumentPixel(player, pixel) == playerBefore && player.Dirty, "Second undo restores and focuses the skin edit");
        Call(editor, "PerformRedo"); Call(editor, "PerformRedo");
        Check(ReadDocumentPixel(player, pixel) == playerAfter && ReadDocumentPixel(cape, pixel) == capeAfter && !editor.History.CanRedo, "Redo replays both documents and reaches the end of history");
        Check(!Field<ToolStripMenuItem>(editor, "redoToolStripMenuItem").Enabled, "Redo UI follows the global cursor");

        var position = Field<Vector3>(editor, "_3DOffset");
        float zoom = Field<float>(editor, "_3DZoom");
        editor.SetWorkspaceDocument(EditingTarget.Skin, alex);
        editor.SetWorkspaceDocument(EditingTarget.Cape, cape2);
        Check(Field<Vector3>(editor, "_3DOffset") == position && Field<float>(editor, "_3DZoom") == zoom, "Replacing either slot preserves camera settings");
        Call(editor, "PerformUndo");
        Check(Field<Skin>(editor, "_workspaceCape") == cape && Field<Skin>(editor, "_workspaceSkin") == alex, "Undo restores a swapped-out document while retaining the companion");
        Call(editor, "PerformRedo");
        editor.SetWorkspaceDocument(EditingTarget.Cape, cape2);
        PaintWorkspacePixel(cape2, new Point(50, 10), Color.DeepPink);
        Check(ReadDocumentPixel(cape2, new Point(50, 10)) == new ColorPixel(255, 20, 147, 255).RGBA, "Scaled cape painting reaches the exact requested pixel");
        Call(editor, "PerformUndo");
        editor.SetEditingTarget(EditingTarget.Skin);
        PaintWorkspacePixel(alex, new Point(30, 30), Color.White);
        Check(!editor.History.CanRedo, "An edit in another texture clears the global redo branch");

        var canonical = cape2.Model;
        Call(editor, "PerformSaveAll");
        editor.SetAccessoryView(AccessoryView.Elytra); editor.SetWingSpread(1f);
        Check(!cape2.Dirty && !alex.Dirty, "Presentation and pose switches leave clean documents clean");
        int historyBefore = editor.History.UndoList.Count();
        foreach (var view in new[] { AccessoryView.Cape, AccessoryView.Elytra })
            foreach (var pose in new[] { 0f, .5f, 1f })
            {
                editor.SetAccessoryView(view); editor.SetWingSpread(pose);
                // First Person intentionally excludes accessories; its protection is tested separately.
                foreach (var mode in new[] { ViewMode.Perspective, ViewMode.Orthographic, ViewMode.Hybrid })
                {
                    Call(editor, "SetViewMode", mode); editor.PerformResetCamera();
                    SetField(editor, "_3DRotationY", 180f);
                    Call(editor, "CalculateMatrices");
                    editor.SetEditingTarget(EditingTarget.Cape);
                    CaptureWorkspace($"combined-{view}-{pose}-{mode}");
                    Point paint = FindWorkspaceHit(cape2, mode);
                    int before = ReadDocumentPixel(cape2, paint);
                    PaintWorkspacePixel(cape2, paint, Color.MediumPurple, mode);
                    Check(ReadDocumentPixel(cape2, paint) != before, "Real brush strokes work on " + view + "/" + pose + "/" + mode);
                    Call(editor, "PerformUndo");
                    Check(ReadDocumentPixel(cape2, paint) == before, "Undo restores exact accessory pixels in every preview mode");
                }
            }
        Check(cape2.Model == canonical && cape2.Width == 128 && cape2.Height == 64, "Preview geometry changes preserve canonical model and PNG dimensions");
        Check(editor.History.UndoList.Count() == historyBefore, "Preview switching adds no undo records");
        Call(editor, "PerformSaveAll");
        Check(PNGMetadata.ReadMetadata(cape2.File.FullName)["Model"] == "Items/Elytra", "Saving after cape preview preserves the file's model metadata");
        using (var disk = new Bitmap(player.File.FullName)) Check(disk.GetPixel(10, 10).ToArgb() == Color.Magenta.ToArgb(), "Skin PNG retains independent edited pixels");

        Call(editor, "SetViewMode", ViewMode.Orthographic);
        editor.SetAccessoryView(AccessoryView.Cape);
        editor.SetEditingTarget(EditingTarget.Cape);
        Point location = WorkspaceLocation(cape2, new Point(6, 6), ViewMode.Orthographic);
        var pencil = Field<List<ToolIndex>>(editor, "_tools").Single(t => t.Tool is PencilTool);
        pencil.MenuItem.PerformClick();
        Call(editor, "rendererControl_MouseMove", editor.Renderer, new MouseEventArgs(MouseButtons.None, 0, location.X, location.Y, 0));
        int countBefore = editor.History.UndoList.Count();
        Call(editor, "rendererControl_MouseDown", editor.Renderer, new MouseEventArgs(MouseButtons.Left, 1, location.X, location.Y, 0));
        editor.SetEditingTarget(EditingTarget.Skin);
        Check(editor.History.UndoList.Count() == countBefore + 1 && !Field<bool>(editor, "_mouseIsDown"), "Changing targets finishes the current stroke as one history entry");

        editor.SetCombinedMode(false);
        Call(editor, "PerformUndo");
        Check(!editor.CombinedEnabled && Field<Skin>(editor, "_lastSkin") == cape2, "Global Undo works after leaving the combined workspace");
        int otherEntries = editor.History.UndoList.Count(e => e.Document != cape2);
        bool previousResizePrompt = GlobalSettings.ResChangeDontShowAgain;
        try
        {
            GlobalSettings.ResChangeDontShowAgain = true;
            editor.PerformIncreaseResolution();
            Check(cape2.Width == 256 && cape2.Height == 128 && editor.History.UndoList.All(e => e.Document != cape2) &&
                editor.History.UndoList.Count() == otherEntries, "Irreversible resize prunes only its document's history");
        }
        finally { GlobalSettings.ResChangeDontShowAgain = previousResizePrompt; }
        editor.SetCombinedMode(true);
        editor.SetWorkspaceDocument(EditingTarget.Cape, null);
        editor.SetWorkspaceDocument(EditingTarget.Skin, null);
        foreach (ViewMode mode in Enum.GetValues<ViewMode>())
        {
            Call(editor, "SetViewMode", mode);
            Call(editor, "rendererControl_Paint", editor.Renderer, null);
            Check(!editor.GetPick(100, 100, out _), "Empty slots are not paintable in " + mode);
        }
        editor.SetWorkspaceDocument(EditingTarget.Skin, player);
        editor.SetWorkspaceDocument(EditingTarget.Cape, cape);
        editor.SetEditingTarget(EditingTarget.Cape);
        var scene = Field<List<PreviewItem>>(editor, "_scene");
        var capeMesh = scene.Single(item => item.Document == cape).Model;
        for (int i = 0; i < 15; ++i)
        {
            editor.SetCombinedMode(false); editor.SetCombinedMode(true);
            editor.SetAccessoryView(AccessoryView.Elytra); editor.SetAccessoryView(AccessoryView.Cape);
        }
        Check(ReferenceEquals(capeMesh, scene.Single(item => item.Document == cape).Model), "Repeated toggles reuse accessory preview geometry");
        Call(editor, "RemoveWorkspaceDocument", cape);
        Check(Field<Skin>(editor, "_workspaceCape") == null && editor.History.UndoList.All(e => e.Document != cape) && editor.History.RedoList.All(e => e.Document != cape), "Removing a document clears its slot and history only");
        editor.SetCombinedMode(false); tree.SelectedNode = original;
        Call(editor, "PerformSaveAll");
    }

    private static Skin WorkspaceFixture(Skin parent, string name, string modelPath, int width, int height, Color color)
    {
        string path = Path.Combine(parent.Directory.FullName, name + ".png");
        using (var bitmap = new Bitmap(width, height))
        {
            for (int y = 0; y < height; ++y)
                for (int x = 0; x < width; ++x)
                    bitmap.SetPixel(x, y, (x + y) % 8 == 0 ? Color.FromArgb(160, color) : color);
            bitmap.Save(path);
        }
        PNGMetadata.WriteMetadata(path, Skin.ModelMetadata(modelPath));
        var skin = new Skin(path);
        (parent.Parent?.Nodes ?? Field<TreeView>(editor, "treeView1").Nodes).Add(skin);
        Check(skin.SetImages(), "Combined fixture loads");
        return skin;
    }

    private static int ReadDocumentPixel(Skin skin, Point pixel)
    {
        if (skin.GLImage == null) skin.Create();
        using var grabber = new ColorGrabber(Field<Skin>(editor, "_lastSkin") == skin ? GlobalDirtiness.CurrentSkin : skin.GLImage, skin.Width, skin.Height);
        grabber.Load(); return grabber[pixel.X, pixel.Y].RGBA;
    }

    private static Point WorkspaceLocation(Skin skin, Point pixel, ViewMode mode)
    {
        var viewport = mode == ViewMode.Perspective ? editor.GetViewport3D() : editor.GetViewport2D();
        Call(editor, "CalculateMatrices");
        for (int y = viewport.Top; y < viewport.Bottom; ++y)
            for (int x = viewport.Left; x < viewport.Right; ++x)
                if (editor.GetPick(x, y, out var hit) && hit == pixel) return new Point(x, y);
        throw new InvalidOperationException($"No visible pixel {pixel} on {skin.Name} in {mode}.");
    }

    private static Point FindWorkspaceHit(Skin skin, ViewMode mode)
    {
        var viewport = mode == ViewMode.Orthographic ? editor.GetViewport2D() : editor.GetViewport3D();
        for (int y = viewport.Top + 3; y < viewport.Bottom; y += 3)
            for (int x = viewport.Left + 3; x < viewport.Right; x += 3)
                if (editor.GetPick(x, y, out var hit)) return hit;
        throw new InvalidOperationException("No accessory pixels in " + mode);
    }

    private static void PaintWorkspacePixel(Skin skin, Point pixel, Color color, ViewMode mode = ViewMode.Orthographic)
    {
        // In hybrid mode exercise the 3D half; 2D is covered separately.
        var location = WorkspaceLocation(skin, pixel, mode == ViewMode.Hybrid ? ViewMode.Perspective : mode);
        Field<List<ToolIndex>>(editor, "_tools").Single(t => t.Tool is PencilTool).MenuItem.PerformClick();
        MCSkin3D.Brushes.BrushBox.SelectedItem = MCSkin3D.Brushes.BrushList.First(b => b.Width == 1 && b.Height == 1);
        editor.ColorPanel.SelectedColor = ColorManager.FromRGBA(color.R, color.G, color.B, color.A);
        Call(editor, "rendererControl_MouseMove", editor.Renderer, new MouseEventArgs(MouseButtons.None, 0, location.X, location.Y, 0));
        var click = new MouseEventArgs(MouseButtons.Left, 1, location.X, location.Y, 0);
        Call(editor, "rendererControl_MouseDown", editor.Renderer, click);
        Call(editor, "rendererControl_MouseUp", editor.Renderer, click);
    }

    private static void CaptureWorkspace(string name)
    {
        using var screenshot = (Bitmap)Call(editor, "CopyScreenToBitmap");
        Check(GL.GetError() == ErrorCode.NoError, name + " renders without OpenGL errors");
        screenshot.Save(Path.Combine(scratch, name + ".png"));
        int detailed = 0;
        for (int y = 0; y < screenshot.Height; y += 13)
            for (int x = 0; x < screenshot.Width; x += 13)
                if (screenshot.GetPixel(x, y).ToArgb() != GlobalSettings.BackgroundColor.ToArgb()) ++detailed;
        Check(detailed > 20, name + " contains visible scene pixels");
    }
}
