using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace Paril.OpenGL
{
	// Renderer-independent texture class
	public abstract class Texture : IDisposable
	{
		public abstract int Width { get; }
		public abstract int Height { get; }
		// Every successful pixel upload invalidates preview metadata derived from this texture.
		public long ContentVersion { get; protected set; }

		#region IDisposable Members

		public abstract void Dispose();

		#endregion

		public void Load(string fileName)
		{
			using var bitmap = new Bitmap(fileName);
			Load(bitmap);
		}

		public abstract void Load(Bitmap image);

		public abstract void Upload<T>(T[] array, int width, int height) where T : struct;
		public abstract void Upload(IntPtr array, int width, int height);
		public abstract void Get<T>(T[] array) where T : struct;
		public abstract void Get(IntPtr array);

		public abstract void SetMipmapping(bool enable);
		public abstract void SetRepeat(bool enable);
		public abstract void Bind();
	}
}
