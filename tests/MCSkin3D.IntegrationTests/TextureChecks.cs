using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using MCSkin3D;
using MCSkin3D.Models;
using OpenTK.Graphics.OpenGL4;
using Paril.OpenGL;
using BitmapPixelFormat = System.Drawing.Imaging.PixelFormat;

internal static class TextureChecks
{
    // The caller must make the editor's real OpenGL context current.
    public static void Run(Action<bool, string> check)
    {
        foreach (var format in new[] { BitmapPixelFormat.Format32bppArgb, BitmapPixelFormat.Format24bppRgb,
            BitmapPixelFormat.Format32bppPArgb, BitmapPixelFormat.Format32bppRgb })
        {
            using var bitmap = new Bitmap(3, 5, format);
            Fill(bitmap);
            Verify(bitmap, format.ToString(), check);
        }

        foreach (var format in new[] { BitmapPixelFormat.Format8bppIndexed, BitmapPixelFormat.Format1bppIndexed })
        {
            using var bitmap = new Bitmap(3, 5, format);
            var palette = bitmap.Palette;
            palette.Entries[0] = Color.FromArgb(128, 32, 96, 224);
            palette.Entries[1] = Color.FromArgb(255, 240, 48, 16);
            bitmap.Palette = palette;
            var bits = bitmap.LockBits(new Rectangle(0, 0, 3, 5), ImageLockMode.WriteOnly, format);
            try
            {
                for (int y = 0; y < 5; y++)
                {
                    if (format == BitmapPixelFormat.Format8bppIndexed)
                        for (int x = 0; x < 3; x++) Marshal.WriteByte(bits.Scan0, y * bits.Stride + x, (byte)((x + y) % 2));
                    else Marshal.WriteByte(bits.Scan0, y * bits.Stride, (byte)(y % 2 == 0 ? 0xA0 : 0x40));
                }
            }
            finally { bitmap.UnlockBits(bits); }
            Verify(bitmap, format.ToString(), check);
        }

        // Negative stride is valid for caller-owned bottom-up images.
        IntPtr memory = Marshal.AllocHGlobal(3 * 5 * 4);
        try
        {
            using var bitmap = new Bitmap(3, 5, -12, BitmapPixelFormat.Format32bppArgb, memory + 4 * 12);
            Fill(bitmap);
            Verify(bitmap, "negative stride", check);
        }
        finally { Marshal.FreeHGlobal(memory); }

        using var source = new Bitmap(64, 64, BitmapPixelFormat.Format32bppArgb);
        using var texture = new TextureGL(source);
        int originalId = texture.TextureID;
        for (int i = 0; i < 10; i++) texture.Load(source);
        check(texture.TextureID == originalId && GL.IsTexture(originalId), "Reload reuses the live texture instead of leaking GPU IDs");
        using (var resized = new Bitmap(7, 9))
        {
            texture.Load(resized);
            check(texture.Width == 7 && texture.Height == 9 && texture.TextureID == originalId, "Reload updates dimensions without replacing texture ID");
        }
        texture.Dispose();
        check(texture.TextureID == 0 && !GL.IsTexture(originalId), "Disposal deletes and clears texture handle");
        using var other = new TextureGL(source);
        texture.Dispose();
        check(GL.IsTexture(other.TextureID), "Repeated disposal preserves subsequently created textures");
        texture.Load(source);
        check(texture.TextureID != 0 && texture.Width == 64, "Loading after disposal creates a usable texture again");

        // Bound the former width*height*4 managed staging-array allocation.
        using var large = new Bitmap(512, 512, BitmapPixelFormat.Format32bppArgb);
        texture.Load(large);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 8; i++) texture.Load(large);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        check(allocated < 8 * 1024, "512x512 uploads avoid megabyte managed staging buffers");
        check(GL.GetError() == ErrorCode.NoError, "Texture format and lifecycle checks leave no OpenGL error");
        PreviewMetadata(check);
    }

    private sealed class CountingTexture : TextureGL
    {
        public int Reads;
        public override void Get<T>(T[] array) { Reads++; base.Get(array); }
        public override void Get(IntPtr array) { Reads++; base.Get(array); }
    }

    private static void PreviewMetadata(Action<bool, string> check)
    {
        bool enabled = GlobalSettings.SkinLayers3D;
        var options = SkinLayerOptions.Current;
        try
        {
            GlobalSettings.SkinLayers3D = true;
            SkinLayerOptions.Defaults.Save();
            string path = Path.Combine(GlobalSettings.GetDataURI("Models"), "Players", "Alex.xml");
            var model = new ModelFormatTCN().Load(path); model.File = new FileInfo(path);
            var skin = new Skin("cache-fixture.png") { Model = model, Size = new Size(64, 64) };
            var item = new PreviewItem { Document = skin, Model = model };
            using var texture = new CountingTexture();
            var pixels = Enumerable.Repeat(new ColorPixel(211, 235, 234, 255).RGBA, 64 * 64).ToArray();
            texture.Upload(pixels, 64, 64);
            item.CaptureTexture(texture); item.UpdateSkinLayers();
            var source = model.Meshes.Single(m => m.Part == ModelPart.Helmet);
            var mesh = item.SkinLayers[source].Mesh;
            check(!item.HasTransparency(source), "Opaque metadata initializes from the texture");
            long version = texture.ContentVersion;
            texture.SetMipmapping(false); texture.SetRepeat(false);
            for (int i = 0; i < 10; i++) { item.CaptureTexture(texture); item.UpdateSkinLayers(); }
            check(texture.Reads == 1 && texture.ContentVersion == version && ReferenceEquals(mesh, item.SkinLayers[source].Mesh),
                "Unchanged preview metadata reuses geometry without texture readbacks");
            var pixel = item.SkinLayers[source].PixelFor(item.SkinLayers[source].PixelFaces[0]);
            int index = pixel.Y * 64 + pixel.X;
            pixels[index] = new ColorPixel(255, 0, 255, 255).RGBA;
            texture.Upload(pixels, 64, 64);
            item.CaptureTexture(texture); item.UpdateSkinLayers();
            check(texture.Reads == 2 && texture.ContentVersion > version && ReferenceEquals(mesh, item.SkinLayers[source].Mesh),
                "RGB uploads refresh texture metadata while retaining layer geometry");
            using (var grabber = new ColorGrabber(texture, 64, 64))
            {
                grabber.Load(); grabber[pixel.X, pixel.Y] = new ColorPixel(0, 0, 0, 0); grabber.Save();
                item.CaptureAlpha(grabber);
            }
            int reads = texture.Reads;
            item.CaptureTexture(texture); item.UpdateSkinLayers();
            check(texture.Reads == reads && !ReferenceEquals(mesh, item.SkinLayers[source].Mesh) && item.HasTransparency(source),
                "Prepared preview pixels rebuild erased geometry and transparency without another readback");
            using var replacement = new CountingTexture();
            replacement.Upload(pixels, 64, 64);
            item.CaptureTexture(replacement); item.UpdateSkinLayers();
            check(replacement.Reads == 1 && !item.HasTransparency(source),
                "Replacing the texture invalidates cached alpha and transparency");
            using (var blank = new Bitmap(64, 64)) replacement.Load(blank);
            item.CaptureTexture(replacement); item.UpdateSkinLayers();
            check(item.SkinLayers[source].Mesh.Faces.Count == 0 && item.HasTransparency(source),
                "Bitmap reload invalidates metadata even when dimensions stay the same");
            replacement.Upload(pixels, 64, 64);
            item.CaptureTexture(replacement); item.UpdateSkinLayers();
            mesh = item.SkinLayers[source].Mesh; reads = replacement.Reads;
            (SkinLayerOptions.Defaults with { HeadSize = 1.2f }).Save();
            item.CaptureTexture(replacement); item.UpdateSkinLayers();
            check(!ReferenceEquals(mesh, item.SkinLayers[source].Mesh) && replacement.Reads == reads,
                "Geometry settings invalidate cached layers without reading unchanged pixels");
            GlobalSettings.SkinLayers3D = false; item.UpdateSkinLayers();
            GlobalSettings.SkinLayers3D = true; item.UpdateSkinLayers();
            check(item.SkinLayers.Count == 6, "Re-enabling layers rebuilds cleared geometry from cached alpha");
            skin.Size = new Size(128, 128);
            replacement.Upload(new int[128 * 128], 128, 128);
            item.CaptureTexture(replacement); item.UpdateSkinLayers();
            check(item.Alpha.Length == 128 * 128 && item.SkinLayers[source].Mesh.Faces.Count == 0,
                "Texture resize refreshes alpha dimensions and layer geometry");
        }
        finally { GlobalSettings.SkinLayers3D = enabled; options.Save(); }
    }

    private static void Fill(Bitmap bitmap)
    {
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
                bitmap.SetPixel(x, y, Color.FromArgb((x + y) % 3 == 0 ? 0 : (x + y) % 3 == 1 ? 128 : 255,
                    20 + x * 40, 30 + y * 35, 220 - x * 30));
    }

    private static void Verify(Bitmap bitmap, string name, Action<bool, string> check)
    {
        using var texture = new TextureGL(bitmap);
        var pixels = new byte[bitmap.Width * bitmap.Height * 4];
        texture.Get(pixels);
        bool equal = true;
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                var expected = bitmap.GetPixel(x, y);
                int index = (y * bitmap.Width + x) * 4;
                equal &= pixels[index] == expected.R && pixels[index + 1] == expected.G &&
                    pixels[index + 2] == expected.B && pixels[index + 3] == expected.A;
            }
        check(equal, name + " upload preserves every pixel, alpha and row orientation");
        // Upload must release the read lock and leave its source usable.
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, bitmap.PixelFormat);
        bitmap.UnlockBits(data);
        check(texture.Width == bitmap.Width && texture.Height == bitmap.Height, name + " NPOT dimensions preserved");
    }
}
