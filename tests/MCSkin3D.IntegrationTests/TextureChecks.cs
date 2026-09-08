using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
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
