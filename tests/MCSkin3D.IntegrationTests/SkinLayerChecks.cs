using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MCSkin3D;
using MCSkin3D.lemon42;
using OpenTK.Mathematics;
using Paril.OpenGL;

internal static partial class Program
{
    private static void SkinLayers3D()
    {
        var tree = Field<TreeView>(editor, "treeView1");
        var original = Field<Skin>(editor, "_lastSkin");
        var player = WorkspaceFixture(original, "3D Layers", "Players/Steve", 64, 64, Color.CornflowerBlue);
        using (var image = new Bitmap(64, 64))
        {
            using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.CornflowerBlue);
            foreach (var mesh in player.Model.Meshes.Where(m => m.IsArmor))
                foreach (var face in mesh.Faces)
                {
                    var rect = face.TexCoordsToInteger(64, 64);
                    for (int y = rect.Top; y < rect.Bottom; ++y)
                        for (int x = rect.Left; x < rect.Right; ++x)
                            image.SetPixel(x, y, (x + y) % 3 == 0 ? Color.Coral : (x + y) % 7 == 0 ? Color.FromArgb(128, Color.White) : Color.Transparent);
                }
            image.Save(player.File.FullName);
        }
        player.SetImages(false);
        tree.SelectedNode = player;
        var tools = Field<List<ToolIndex>>(editor, "_tools");
        tools.Single(t => t.Tool is CameraTool).MenuItem.PerformClick();
        Call(editor, "SetViewMode", ViewMode.Perspective);
        editor.PerformResetCamera();
        var menu = Field<ToolStripMenuItem>(editor, "_skinLayersMenu");
        Check(!GlobalSettings.SkinLayers3D && !menu.Checked && menu.Text == "3D Skin Layers", "Localized 3D layer option defaults off");
        using var flat = (Bitmap)Call(editor, "CopyScreenToBitmap");
        menu.PerformClick();
        Check(GlobalSettings.SkinLayers3D && menu.Checked && !player.Dirty, "View menu enables the mode without modifying the document");
        using var raised = (Bitmap)Call(editor, "CopyScreenToBitmap");
        flat.Save(Path.Combine(scratch, "skin-layers-flat.png"));
        raised.Save(Path.Combine(scratch, "skin-layers-raised.png"));
        int changed = 0;
        // Enabling the mode also reveals the main brightness control, reducing
        // the viewport height. Compare the shared framebuffer area.
        for (int y = 0; y < Math.Min(flat.Height, raised.Height); y += 2)
            for (int x = 0; x < Math.Min(flat.Width, raised.Width); x += 2)
                if (flat.GetPixel(x, y) != raised.GetPixel(x, y)) changed++;
        Check(changed > 100, "Enabling raised layers changes actual GPU pixels");
        var scene = Field<List<PreviewItem>>(editor, "_scene");
        var item = scene.Single();
        Check(item.SkinLayers.Count == 6 && item.Model == player.Model, "All six overlays use separate preview geometry");
        var helmet = item.SkinLayers.Single(p => p.Key.Part == ModelPart.Helmet).Value;
        Check(helmet.Mesh.HasTransparency, "Mixed opaque and glass layers use alpha blending");
        var layerBefore = helmet.Mesh;
        var (location, pixel) = LayerPaintLocation(helmet, player);
        int before = ReadDocumentPixel(player, pixel);
        MCSkin3D.Brushes.BrushBox.SelectedItem = MCSkin3D.Brushes.BrushList.First(b => b.Width == 1 && b.Height == 1);
        editor.ColorPanel.SelectedColor = ColorManager.FromRGBA(255, 0, 255, 255);
        tools.Single(t => t.Tool is PencilTool).MenuItem.PerformClick();
        ClickLayer(location);
        Check(ReadDocumentPixel(player, pixel) == new ColorPixel(255, 0, 255, 255).RGBA, "Painting raised geometry edits its exact source pixel");
        tools.Single(t => t.Tool is CameraTool).MenuItem.PerformClick();
        CaptureWorkspace("skin-layers-painted");
        Check(ReferenceEquals(layerBefore, helmet.Mesh), "RGB-only painting reuses the generated geometry");
        tools.Single(t => t.Tool is EraserTool).MenuItem.PerformClick();
        ClickLayer(location);
        Check((uint)ReadDocumentPixel(player, pixel) >> 24 == 0, "Erasing a raised pixel clears its alpha");
        tools.Single(t => t.Tool is CameraTool).MenuItem.PerformClick();
        CaptureWorkspace("skin-layers-erased");
        Check(!ReferenceEquals(layerBefore, helmet.Mesh), "Erasing rebuilds the visible layer");
        Call(editor, "PerformUndo");
        Check(ReadDocumentPixel(player, pixel) == new ColorPixel(255, 0, 255, 255).RGBA, "Undo restores an erased raised pixel");
        CaptureWorkspace("skin-layers-undo");
        Call(editor, "PerformUndo");
        Check(ReadDocumentPixel(player, pixel) == before, "Undo restores the original overlay color");
        Call(editor, "PerformRedo"); Call(editor, "PerformRedo");
        Check((uint)ReadDocumentPixel(player, pixel) >> 24 == 0, "Redo repeats painting and erasing");
        Call(editor, "PerformUndo"); Call(editor, "PerformUndo");
        editor.PreviewOfficialCape(OfficialCapeCatalog.All.First());
        Call(editor, "SetViewMode", ViewMode.Hybrid);
        CaptureWorkspace("skin-layers-combined");
        Check(scene.Single(i => i.Document == player).SkinLayers.Count == 6 &&
            scene.Where(i => i.Document != player).All(i => i.SkinLayers.Count == 0), "Combined workspace extrudes only player overlays");
        editor.SetCombinedMode(false);
        Call(editor, "SetViewMode", ViewMode.Perspective);
        var clientRenderer = editor.MeshRenderer;
        typeof(Editor).GetProperty("MeshRenderer", Private).SetValue(editor, new ImmediateRenderer());
        try { CaptureWorkspace("skin-layers-immediate"); }
        finally { typeof(Editor).GetProperty("MeshRenderer", Private).SetValue(editor, clientRenderer); }
        GlobalSettings.Save();
        Check(File.ReadAllText(Path.Combine(scratch, "settings.ini")).Contains("SkinLayers3D=True"), "3D layer preference is saved");
        GlobalSettings.SkinLayers3D = false;
        GlobalSettings.Load();
        Check(GlobalSettings.SkinLayers3D, "3D layer preference survives settings reload");
        editor.SetSkinLayers3D(false);
        Check(!menu.Checked && scene.All(i => i.SkinLayers.Count == 0), "Disabling restores flat meshes immediately");
        tree.SelectedNode = original;
    }

    private static (Point Location, Point Pixel) LayerPaintLocation(SkinLayerGeometry layer, Skin skin)
    {
        var viewport = editor.GetViewport3D();
        var projection = Field<Matrix4>(editor, "_projectionMatrix");
        foreach (var face in layer.PixelFaces.Where(f => f.Normal.Z < -.9f))
        {
            var pixel = layer.PixelFor(face);
            if ((uint)ReadDocumentPixel(skin, pixel) >> 24 != 255) continue;
            var point = face.Vertices.Aggregate(Vector3.Zero, (sum, v) => sum + v) / 4;
            point = Vector3.TransformPosition(point, layer.Mesh.Matrix);
            var clip = Vector4.TransformRow(new Vector4(point, 1), projection);
            var location = new Point(viewport.Left + (int)((clip.X / clip.W + 1) * viewport.Width / 2),
                viewport.Top + (int)((1 - clip.Y / clip.W) * viewport.Height / 2));
            if (editor.GetPick(location.X, location.Y, out var hit) && hit == pixel) return (location, pixel);
        }
        throw new InvalidOperationException("No visible raised pixel to paint.");
    }

    private static void ClickLayer(Point location)
    {
        Call(editor, "rendererControl_MouseMove", editor.Renderer, new MouseEventArgs(MouseButtons.None, 0, location.X, location.Y, 0));
        var click = new MouseEventArgs(MouseButtons.Left, 1, location.X, location.Y, 0);
        Call(editor, "rendererControl_MouseDown", editor.Renderer, click);
        Call(editor, "rendererControl_MouseUp", editor.Renderer, click);
    }
}
