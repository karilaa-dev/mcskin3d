using System;
using System.Collections.Generic;
using System.Globalization;
using MCSkin3D;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL;
using Paril.Drawing;

namespace Paril.OpenGL
{
	public class Mesh
	{
		public Bounds3 Bounds;
		public Vector3 Center;
		public List<Face> Faces;
		public bool HasTransparency;
		public PrimitiveType Mode;
		public string Name;

		public ModelPart Part;
		public Vector3 Pivot;
		public Vector3 Rotate;
		public float RotateFactor;
		public Texture Texture;
		public Vector3 Translate;
		public bool DrawTransparent, LastDrawTransparent;
		public IMeshUserData UserData;
		public Matrix4 Matrix;
		public bool IsSolid;
		public bool IsArmor;
		public bool ShadeFaces;
		public bool IsSkinLayer;
		public bool LightingYUp;
		private bool _lightingYUp;
		private int _gameBrightness = -1;
		private int _lightLevel = -1;
		private float _worldBrightness = 1;
		private Matrix4 _lightingMatrix;
		// Vanilla Overworld cardinal lighting, converted to the editor's Y-down axes.
		private static readonly Vector3 Light0 = new Vector3(.2f, -1, -.7f).Normalized();
		private static readonly Vector3 Light1 = new Vector3(-.2f, -1, .7f).Normalized();

		public bool SetLighting(bool enabled, int gameBrightness = 100, int lightLevel = 15)
		{
			lightLevel = Math.Clamp(lightLevel, 0, 15);
			gameBrightness = Math.Clamp(gameBrightness, 0, 100);
			if (ShadeFaces == enabled && _gameBrightness == gameBrightness && _lightLevel == lightLevel && _lightingMatrix == Matrix && _lightingYUp == LightingYUp) return false;
			ShadeFaces = enabled;
			_gameBrightness = gameBrightness;
			_lightLevel = lightLevel;
			_worldBrightness = MinecraftPreviewLighting.Daylight(lightLevel, gameBrightness);
			_lightingMatrix = Matrix;
			_lightingYUp = LightingYUp;
			return true;
		}

		public float FaceBrightness(Face face)
		{
			if (!ShadeFaces) return 1;
			// Source model normals were baked with the original pose. Recompute
			// them locally so capes/elytra also use their current preview pose.
			var normal = IsSkinLayer ? face.Normal : Vector3.Cross(face.Vertices[1] - face.Vertices[0], face.Vertices[2] - face.Vertices[0]);
			normal = Vector3.TransformVector(normal, Matrix);
			if (LightingYUp) normal.Y = -normal.Y;
			if (normal.LengthSquared < 1e-10f) return _worldBrightness;
			normal.Normalize();
			// Mojang light.glsl: two diffuse lights, power .6, ambient .4.
			// Only RGB is shaded; texture alpha and editable pixels are untouched.
			float shade = Math.Min(1, .4f + .6f * (Math.Max(0, Vector3.Dot(Light0, normal)) + Math.Max(0, Vector3.Dot(Light1, normal))));
			// Minecraft multiplies entity diffuse shading by its world lightmap.
			return shade * _worldBrightness;
		}

		public Mesh(string name)
		{
			Bounds = new Bounds3(new Vector3(float.MaxValue, float.MaxValue, float.MaxValue),
								 new Vector3(float.MinValue, float.MinValue, float.MinValue));
			Name = name;
		}

		public T GetUserData<T>() where
			T : IMeshUserData
		{
			return (T)UserData;
		}

		internal static string VertexToString(Vector3 v, string sep = " ")
		{
			return v.X.ToString(CultureInfo.InvariantCulture) + sep + v.Y.ToString(CultureInfo.InvariantCulture) + sep +
				   v.Z.ToString(CultureInfo.InvariantCulture);
		}

		internal static string VertexToString(Vector2 v, string sep = " ")
		{
			return v.X.ToString(CultureInfo.InvariantCulture) + sep + v.Y.ToString(CultureInfo.InvariantCulture);
		}

		internal static Vector3 StringToVertex3(string s)
		{
			string[] spl = s.Split(new[] { ' ', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);

			return new Vector3(float.Parse(spl[0], CultureInfo.InvariantCulture),
							   float.Parse(spl[1], CultureInfo.InvariantCulture),
							   float.Parse(spl[2], CultureInfo.InvariantCulture));
		}

		internal static Vector2 StringToVertex2(string s)
		{
			string[] spl = s.Split(new[] { ' ', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);

			return new Vector2(float.Parse(spl[0], CultureInfo.InvariantCulture),
							   float.Parse(spl[1], CultureInfo.InvariantCulture));
		}

		public void CalculateCenter()
		{
			int count = 0;
			Bounds = new Bounds3(new Vector3(9999, 9999, 9999), new Vector3(-9999, -9999, -9999));

			foreach (Face x in Faces)
			{
				foreach (Vector3 v in x.Vertices)
				{
					count++;

					Vector3 transformed = Model.TranslateVertex(Translate, Rotate, Pivot, v);
					Center += transformed;
					Bounds += transformed;
				}
			}

			Center /= count;
		}

		public void CalculateMatrix()
		{
			Matrix =
				Matrix4.CreateTranslation(Translate) *
				Matrix4.CreateTranslation(-Pivot) *
				Matrix4.CreateFromAxisAngle(new Vector3(1, 0, 0), MathHelper.DegreesToRadians(Rotate.X)) *
				Matrix4.CreateFromAxisAngle(new Vector3(0, 1, 0), MathHelper.DegreesToRadians(Rotate.Y)) *
				Matrix4.CreateFromAxisAngle(new Vector3(0, 0, 1), MathHelper.DegreesToRadians(Rotate.Z)) *
				Matrix4.CreateTranslation(Pivot);
		}
	}
}
