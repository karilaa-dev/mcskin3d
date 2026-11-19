//
//    MCSkin3D, a 3d skin management studio for Minecraft
//    Copyright (C) 2013 Altered Softworks & MCSkin3D Team
//
//    This program is free software: you can redistribute it and/or modify
//    it under the terms of the GNU General Public License as published by
//    the Free Software Foundation, either version 3 of the License, or
//    (at your option) any later version.
//
//    This program is distributed in the hope that it will be useful,
//    but WITHOUT ANY WARRANTY; without even the implied warranty of
//    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//    GNU General Public License for more details.
//
//    You should have received a copy of the GNU General Public License
//    along with this program.  If not, see <http://www.gnu.org/licenses/>.
//

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using MCSkin3D.Forms;
using MCSkin3D.Services;
using Microsoft.VisualBasic.FileIO;
using OpenTK.Mathematics;
using Paril.Components;
using Paril.Drawing;
using Paril.Imaging;
using Paril.OpenGL;

namespace MCSkin3D
{
	[Serializable]
	public class Skin : TreeNode, IDisposable
	{
		private readonly Dictionary<int, bool> _transparentParts = new Dictionary<int, bool>();
		public bool Dirty;
		public Texture GLImage;
		public Bitmap Head;
		public Size Size;
		public bool IsLastSkin { get; set; }
		public OfficialCape ReferenceCape { get; }
		public bool IsReadOnly => ReferenceCape != null;

		internal Skin(OfficialCape cape) : this(cape.Id + ".png")
		{
			ReferenceCape = cape;
			Name = cape.Name;
			Model = ModelLoader.GetModelForPath("Players/Cape");
			using var image = cape.OpenTexture();
			Size = image.Size;
		}

		internal void EnsureEditable()
		{
			if (IsReadOnly) throw new InvalidOperationException("Official capes are read-only preview references.");
		}

		public Skin(string fileName)
		{
			Name = Path.GetFileNameWithoutExtension(fileName);
		}

		public Skin(FileInfo file) :
			this(file.FullName)
		{
		}

		public Model Model { get; set; }
		internal bool ModelChecked { get; private set; }
		internal static string ModelChoicesDirectory { get; set; } = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MCSkin3D", "ModelChoices");

		internal static Dictionary<string, string> ModelMetadata(string modelPath, bool modelChecked = true)
		{
			var data = new Dictionary<string, string> { ["Model"] = modelPath };
			if (modelChecked) data["ModelChecked"] = "1";
			return data;
		}

		internal bool CheckPlayerModel()
		{
			if (ModelChecked || IsReadOnly || Model == null || !PlayerSkinModel.IsPlayer(Model.Path)) return false;
			string detected;
			using (var image = new Bitmap(File.FullName))
				detected = PlayerSkinModel.Detect(image);
			var previous = Model;
			if (detected != null) Model = ModelLoader.GetModelForPath(detected) ?? previous;
			RememberModel();
			if (previous != Model && GLImage != null) SetTransparentParts();
			return previous != Model;
		}

		// A library can be on read-only media. Scope its local preference to this file revision.
		private static string CachedModelPath(FileInfo file)
		{
			string identity = $"{file.FullName.ToUpperInvariant()}\n{file.Length}\n{file.LastWriteTimeUtc.Ticks}";
			byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity));
			return Path.Combine(ModelChoicesDirectory, Convert.ToHexString(hash));
		}

		internal void RememberModel()
		{
			ModelChecked = true;
			var file = File;
			string cached = System.IO.Directory.Exists(ModelChoicesDirectory) ? CachedModelPath(file) : null;
			try
			{
				PNGMetadata.WriteMetadata(file.FullName, ModelMetadata(Model.Path));
				if (cached != null && System.IO.File.Exists(cached)) System.IO.File.Delete(cached);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
			{
				cached ??= CachedModelPath(file);
				System.IO.Directory.CreateDirectory(ModelChoicesDirectory);
				string temporary = cached + "." + Guid.NewGuid().ToString("N");
				try
				{
					System.IO.File.WriteAllText(temporary, Model.Path);
					System.IO.File.Move(temporary, cached, true);
				}
				finally
				{
					if (System.IO.File.Exists(temporary)) System.IO.File.Delete(temporary);
				}
			}
		}

		public int Width
		{
			get { return Size.Width; }
		}

		public int Height
		{
			get { return Size.Height; }
		}

		public Dictionary<int, bool> TransparentParts
		{
			get { return _transparentParts; }
		}

		public new string Name
		{
			get { return base.Name; }
			set
			{
				base.Name = value;
				base.Text = base.Name = value;
			}
		}

		public DirectoryInfo Directory
		{
			get
			{
				if (Level == 0)
					return new DirectoryInfo(Editor.RootFolderString);

				return new DirectoryInfo(((Parent != null) ? Editor.GetFolderForNode(Parent) : ""));
			}
		}

		public FileInfo File
		{
			get { EnsureEditable(); return new FileInfo(Directory.FullName + '\\' + Name + ".png"); }
		}

		#region IDisposable Members

		public void Dispose()
		{
			if (GLImage != null)
			{
				GLImage.Dispose();
				GLImage = null;
			}

			if (Head != null)
			{
				Head.Dispose();
				Head = null;
			}
		}

		#endregion

		static void SetImage(Skin skin)
		{
			Editor.MainForm.Renderer.MakeCurrent();
			if (skin.IsReadOnly)
			{
				using var image = skin.ReferenceCape.OpenTexture();
				skin.GLImage = new TextureGL(image);
			}
			else skin.GLImage = new TextureGL(skin.File.FullName);
			skin.GLImage.SetMipmapping(false);
			skin.GLImage.SetRepeat(false);
		}

		public bool SetImages(bool updateGL = true)
		{
			if (IsReadOnly) return true;
			Bitmap image = null;

			try
			{
				using (FileStream file = File.Open(FileMode.Open, FileAccess.Read, FileShare.Read))
					image = new Bitmap(file);

				Size = image.Size;

				if (Head != null)
				{
					Head.Dispose();

					if (updateGL)
					{
						Unload();
						Create();
					}
				}

				float scale = Size.Width / 64.0f;
				var headSize = (int)(8.0f * scale);
				var helmetLoc = (int)(40.0f * scale);

				Head = new Bitmap(headSize, headSize);
				using (Graphics g = Graphics.FromImage(Head))
				{
					g.DrawImage(image, new Rectangle(0, 0, headSize, headSize), new Rectangle(headSize, headSize, headSize, headSize),
								GraphicsUnit.Pixel);
					g.DrawImage(image, new Rectangle(0, 0, headSize, headSize), new Rectangle(helmetLoc, headSize, headSize, headSize),
								GraphicsUnit.Pixel);
				}

				if (Model == null)
				{
					var file = File;
					var metadata = PNGMetadata.ReadMetadata(file.FullName);
					if (metadata.TryGetValue("Model", out var modelPath))
						Model = ModelLoader.GetModelForPath(modelPath);
					ModelChecked = Model != null && metadata.GetValueOrDefault("ModelChecked") == "1";
					if (System.IO.Directory.Exists(ModelChoicesDirectory))
					{
						string cached = CachedModelPath(file);
						if (System.IO.File.Exists(cached) && ModelLoader.GetModelForPath(System.IO.File.ReadAllText(cached)) is Model saved)
						{
							Model = saved;
							ModelChecked = true;
						}
					}
					Model ??= ModelLoader.GetModelForPath(PlayerSkinModel.PathFor(false, image.Width != image.Height));
				}
			}
			catch (Exception)
			{
				MessageBox.Show(String.Format(Editor.GetLanguageString("E_SKINERROR"), File.FullName));
				return false;
			}
			finally
			{
				if (image != null)
					image.Dispose();
			}

			return true;
		}

		public void Create()
		{
			Form f = (Editor.MainForm.IsHandleCreated) ? (Form)Editor.MainForm : (Form)Program.Context.SplashForm;

			if (f.InvokeRequired)
				f.Invoke((Action<Skin>)SetImage, this);
			else
				SetImage(this);

			if (f.InvokeRequired)
				f.Invoke((Action)SetTransparentParts);
			else
				SetTransparentParts();
		}

		public void Unload()
		{
			GLImage.Dispose();
			GLImage = null;
		}

		public override string ToString()
		{
			if (Dirty)
				return Name + " *";
			return Name;
		}

		public void CommitChanges(Texture currentSkin, bool save)
		{
			// Switching away from a reference must never copy a working texture into it.
			if (IsReadOnly) return;
			using (var grabber = new ColorGrabber(currentSkin, Width, Height))
			{
				grabber.Load();

				if (currentSkin != GLImage)
				{
					grabber.Texture = GLImage;
					grabber.Save();
				}

				if (save)
				{
					var newBitmap = new Bitmap(Width, Height);

					using (var fp = new FastPixel(newBitmap, true))
					{
						for (int y = 0; y < Height; ++y)
						{
							for (int x = 0; x < Width; ++x)
							{
								ColorPixel c = grabber[x, y];
								fp.SetPixel(x, y, Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue));
							}
						}
					}

					newBitmap.Save(File.FullName);
					newBitmap.Dispose();

					PNGMetadata.WriteMetadata(File.FullName, ModelMetadata(Model.Path, ModelChecked));

					SetImages(true);

					Dirty = false;
				}
			}
		}

		public bool ChangeName(string newName, bool force = false)
		{
			EnsureEditable();
			if (!newName.EndsWith(".png"))
				newName += ".png";

			if (force)
			{
				Name = Path.GetFileNameWithoutExtension(newName);
				return true;
			}

			if (System.IO.File.Exists(Directory.FullName + "\\" + newName))
				return false;

			FileSystem.RenameFile(File.FullName, newName);
			//File.MoveToParent(newName);
			Name = Path.GetFileNameWithoutExtension(newName);

			return true;
		}

		public void Resize(int width, int height, ResizeType type = ResizeType.Scale)
		{
			EnsureEditable();
			Editor.MainForm?.PrepareDocumentResize(this);
			using var source = new Bitmap(Width, Height);
			if (GLImage != null)
			{
				using var pixels = new ColorGrabber(GLImage, Width, Height);
				pixels.Load();
				using var output = new FastPixel(source, true);
				for (int y = 0; y < Height; ++y)
					for (int x = 0; x < Width; ++x)
					{
						var pixel = pixels[x, y];
						output.SetPixel(x, y, Color.FromArgb(pixel.Alpha, pixel.Red, pixel.Green, pixel.Blue));
					}
			}
			else
			{
				using var image = Image.FromFile(File.FullName);
				using var graphics = Graphics.FromImage(source);
				graphics.DrawImageUnscaled(image, 0, 0);
			}
			using (var newBitmap = new Bitmap(width, height))
			{
				using (Graphics g = Graphics.FromImage(newBitmap))
				{
					g.SmoothingMode = SmoothingMode.None;
					g.InterpolationMode = InterpolationMode.NearestNeighbor;
					g.PixelOffsetMode = PixelOffsetMode.HighQuality;
					g.Clear(Color.FromArgb(0, 0, 0, 0));

					if (type == ResizeType.Scale)
						g.DrawImage(source, 0, 0, newBitmap.Width, newBitmap.Height);
					else
						g.DrawImageUnscaled(source, 0, 0);
				}

				newBitmap.Save(File.FullName);

				PNGMetadata.WriteMetadata(File.FullName, ModelMetadata(Model.Path, ModelChecked));
			}

			SetImages();

			Editor.MainForm?.ForgetDocumentHistory(this);
		}

		public void Delete()
		{
			EnsureEditable();
			FileSystem.DeleteFile(File.FullName, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
			//File.Delete();
		}

		public void MoveTo(string newPath)
		{
			MoveTo(newPath, true);
		}

		internal void MoveTo(string newPath, bool autoRename)
		{
			EnsureEditable();
			if (string.Equals(Path.GetFullPath(newPath), File.FullName, StringComparison.OrdinalIgnoreCase))
				return;
			while (autoRename && (System.IO.File.Exists(newPath) || System.IO.Directory.Exists(newPath)))
				newPath = newPath.Insert(newPath.Length - 4, " - " + Languages.LanguageLoader.GetStringWithFallback("C_MOVED"));

			// Do not overwrite, create a missing destination, or change the node on failure.
			System.IO.File.Move(File.FullName, newPath);
			Name = Path.GetFileNameWithoutExtension(newPath);
		}

		public void CheckTransparentPart(ColorGrabber grabber, int index)
		{
			foreach (Face f in Model.Meshes[index].Faces)
			{
				var bounds = new Bounds(new Point(9999, 9999), new Point(-9999, -9999));

				foreach (Vector2 c in f.TexCoords)
				{
					var coord = new Vector2(c.X * Width, c.Y * Height);
					bounds.AddPoint(new Point((int)coord.X, (int)coord.Y));
				}

				Rectangle rect = bounds.ToRectangle();
				bool gotOne = false;

				for (int y = rect.Y; !gotOne && y < rect.Y + rect.Height; ++y)
				{
					for (int x = rect.X; x < rect.X + rect.Width; ++x)
					{
						ColorPixel pixel = grabber[x, y];

						if (pixel.Alpha != 255)
						{
							gotOne = true;
							break;
						}
					}
				}

				if (gotOne)
				{
					TransparentParts[index] = gotOne;
					return;
				}
			}

			TransparentParts[index] = false;
		}

		public void SetTransparentParts()
		{
			using (var grabber = new ColorGrabber(GLImage, Width, Height))
			{
				grabber.Load();

				int mesh = 0;

				TransparentParts.Clear();

				foreach (Mesh m in Model.Meshes)
				{
					TransparentParts.Add(mesh, false);

					CheckTransparentPart(grabber, mesh);
					mesh++;
				}
			}
		}
	}
}
