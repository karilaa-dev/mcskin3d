using System.Drawing;

namespace MCSkin3D.Services
{
	internal static class PlayerSkinModel
	{
		internal static string PathFor(bool slim, bool legacy) =>
			(slim ? "Players/Alex" : "Players/Steve") + (legacy ? " (Minimal)" : "");

		internal static bool IsPlayer(string path) =>
			path is "Players/Alex" or "Players/Steve" or "Players/Alex (Minimal)" or "Players/Steve (Minimal)";

		internal static string Detect(Bitmap image)
		{
			int width = image.Width, height = image.Height;
			bool legacy = width == 2L * height;
			if (width < 32 || (!legacy && width != height)) return null;

			// Both arm layouts start at the same UV origin. Only classic arms use
			// the last two columns of the end faces and the long side faces.
			var arms = legacy ? new[] { new Point(40, 16) } : new[] { new Point(40, 16), new Point(32, 48) };
			int Sample(int x, int y)
			{
				var pixel = image.GetPixel(x, y);
				return pixel.A == 0 ? 0 : pixel.ToArgb();
			}
			bool AllMatch(int x, int y, int w, int h, int color)
			{
				for (int py = y * width / 64; py < (y + h) * width / 64; py++)
					for (int px = x * width / 64; px < (x + w) * width / 64; px++)
						if (Sample(px, py) != color) return false;
				return true;
			}

			bool hasArm = false;
			foreach (var arm in arms)
			{
				int x = arm.X, y = arm.Y;
				int padding = Sample((x + 10) * width / 64, y * width / 64);
				bool background = padding == 0 || ((padding == Color.Black.ToArgb() || padding == Color.White.ToArgb()) &&
					AllMatch(x + 12, y, 4, 4, padding));
				if (!background || !AllMatch(x + 10, y, 2, 4, padding) || !AllMatch(x + 14, y + 4, 2, 12, padding))
					return PathFor(false, legacy);
				hasArm |= !AllMatch(x, y + 4, 14, 12, padding) || !AllMatch(x + 4, y, 6, 4, padding);
			}
			// Empty arms do not identify either model.
			return hasArm ? PathFor(true, legacy) : null;
		}
	}
}
