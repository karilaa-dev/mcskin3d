using System;
using System.Drawing;
using System.Drawing.Imaging;
using OpenTK.Graphics.OpenGL4;
using PixelFormat = OpenTK.Graphics.OpenGL4.PixelFormat;

namespace Paril.OpenGL
{
	public class TextureGL : Texture
	{
		private static int _curTex;
		private int _height;
		private int _width;

		public TextureGL()
		{
			Generate();
		}

		public TextureGL(Bitmap b)
		{
			Load(b);
		}

		public TextureGL(string s)
		{
			Load(s);
		}

		public int TextureID { get; private set; }

		public override int Width
		{
			get
			{
				if (TextureID == 0)
					throw new InvalidOperationException();

				return _width;
			}
		}

		public override int Height
		{
			get
			{
				if (TextureID == 0)
					throw new InvalidOperationException();

				return _height;
			}
		}

		public void Generate()
		{
			if (TextureID == 0)
				TextureID = GL.GenTexture();
		}

		public override unsafe void Load(Bitmap image)
		{
			ArgumentNullException.ThrowIfNull(image);
			// GDI+ supplies straight-alpha BGRA, including conversion of indexed,
			// 24-bit and premultiplied sources. OpenGL accepts this layout directly.
			var data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height),
				ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
			try
			{
				Generate();
				SetMipmapping(false);
				SetRepeat(false);
				int rowBytes = checked(image.Width * 4);
				if (data.Stride == rowBytes)
				{
					Upload(data.Scan0, image.Width, image.Height, PixelFormat.Bgra);
				}
				else
				{
					// Bottom-up or padded bitmap buffers need rows packed in logical order.
					var packed = GC.AllocateUninitializedArray<byte>(checked(rowBytes * image.Height));
					fixed (byte* destination = packed)
					{
						for (int y = 0; y < image.Height; ++y)
							System.Buffer.MemoryCopy((byte*)data.Scan0 + (long)y * data.Stride,
								destination + (long)y * rowBytes, rowBytes, rowBytes);
						Upload((IntPtr)destination, image.Width, image.Height, PixelFormat.Bgra);
					}
				}
			}
			finally
			{
				image.UnlockBits(data);
			}
		}

		public override void Upload<T>(T[] array, int width, int height)
		{
			Bind();

			GL.TexImage2D(TextureTarget.Texture2D,
						  0,
						  PixelInternalFormat.Rgba,
						  width,
						  height,
						  0,
						  PixelFormat.Rgba,
						  PixelType.UnsignedByte,
						  array);

			ErrorCode err = GL.GetError();

			if (err != ErrorCode.NoError)
			{
				Dispose();
				throw new Exception(err.ToString());
			}
			else
			{
				_width = width;
				_height = height;
				ContentVersion++;
			}
		}

		public override void Upload(IntPtr ptr, int width, int height)
		{
			Upload(ptr, width, height, PixelFormat.Rgba);
		}

		private void Upload(IntPtr ptr, int width, int height, PixelFormat format)
		{
			Bind();

			GL.TexImage2D(TextureTarget.Texture2D,
						  0,
						  PixelInternalFormat.Rgba,
						  width,
						  height,
						  0,
						  format,
						  PixelType.UnsignedByte,
						  ptr);

			ErrorCode err = GL.GetError();

			if (err != ErrorCode.NoError)
			{
				Dispose();
				throw new Exception(err.ToString());
			}
			else
			{
				_width = width;
				_height = height;
				ContentVersion++;
			}
		}

		public override void Get<T>(T[] array)
		{
			Bind();
			GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rgba, PixelType.UnsignedByte, array);
		}

		public override void Get(IntPtr array)
		{
			Bind();
			GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rgba, PixelType.UnsignedByte, array);
		}

		public override void SetMipmapping(bool enable)
		{
			Bind();

			if (enable)
			{
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
			}
			else
			{
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
			}
		}

		public override void SetRepeat(bool enable)
		{
			Bind();

			if (enable)
			{
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
			}
			else
			{
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
				GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
			}
		}

		public override void Bind()
		{
			BindTextureInternal(TextureID);
		}

		public override void Dispose()
		{
			if (TextureID == 0)
				return;

			DeleteTextureInternal(TextureID);
			TextureID = 0;
			_width = _height = 0;
		}

		private static void BindTextureInternal(int texID)
		{
			if (_curTex == texID)
				return;

			GL.BindTexture(TextureTarget.Texture2D, texID);
			_curTex = texID;
		}

		private static void DeleteTextureInternal(int GLImage)
		{
			GL.DeleteTexture(GLImage);

			if (_curTex == GLImage)
				BindTextureInternal(0);
		}

		public static void Unbind()
		{
			BindTextureInternal(0);
		}
	}
}
