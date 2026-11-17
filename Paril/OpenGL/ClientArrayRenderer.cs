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

using OpenTK.Graphics.OpenGL;

namespace Paril.OpenGL
{
	/// <summary>
	/// OpenGL renderer that renders using client-side array mode.
	/// 
	/// </summary>
	public class ClientArrayRenderer : Renderer
	{
		public ClientArrayRenderer()
		{
			GL.EnableClientState(ArrayCap.VertexArray);
			GL.EnableClientState(ArrayCap.ColorArray);
			GL.EnableClientState(ArrayCap.TextureCoordArray);
		}

		public override void RenderMesh(Mesh mesh)
		{
			mesh.Texture.Bind();

			GL.PushMatrix();

			GL.MultMatrix(ref mesh.Matrix);

			var data = mesh.GetUserData<ClientArrayMeshUserData>();

			GL.VertexPointer(3, VertexPointerType.Float, 0, data.VerticeArray);
			GL.TexCoordPointer(2, TexCoordPointerType.Float, 0, data.TexCoordArray);
			GL.ColorPointer(4, ColorPointerType.Float, 0, data.ColorArray);

			GL.DrawElements(mesh.Mode, data.IndiceArray.Length, DrawElementsType.UnsignedInt, data.IndiceArray);

			GL.PopMatrix();
		}

		protected override void PreRender()
		{
		}

		protected override void PostRender()
		{
		}

		public override IMeshUserData CreateUserData(Mesh mesh)
		{
			ClientArrayMeshUserData data = new ClientArrayMeshUserData();

			int vertexCount = 0;
			int texCoordCount = 0;
			int indexCount = 0;
			foreach (var f in mesh.Faces)
			{
				vertexCount += f.Vertices.Length;
				texCoordCount += f.TexCoords.Length;
				indexCount += f.Indices.Length;
			}

			data.VerticeArray = new float[vertexCount * 3];
			data.TexCoordArray = new float[texCoordCount * 2];
			data.IndiceArray = new uint[indexCount];

			int vi = 0;
			int ti = 0;
			int ii = 0;
			int vertexOffset = 0;
			foreach (var f in mesh.Faces)
			{
				foreach (var vertex in f.Vertices)
				{
					data.VerticeArray[vi++] = vertex.X;
					data.VerticeArray[vi++] = vertex.Y;
					data.VerticeArray[vi++] = vertex.Z;
				}
				foreach (var texCoord in f.TexCoords)
				{
					data.TexCoordArray[ti++] = texCoord.X;
					data.TexCoordArray[ti++] = texCoord.Y;
				}
				foreach (var index in f.Indices)
					data.IndiceArray[ii++] = (uint)(index + vertexOffset);
				vertexOffset += f.Vertices.Length;
			}

			return data;
		}

		public override void UpdateUserData(Mesh mesh)
		{
			ClientArrayMeshUserData data = mesh.GetUserData<ClientArrayMeshUserData>();
			int colorCount = mesh.Faces.Count * 4 * 4;
			if (data.ColorArray == null || data.ColorArray.Length != colorCount)
				data.ColorArray = new float[colorCount];

			// Ghost visibility changes only the colors; keep the client-side buffer reusable.
			float alpha = mesh.DrawTransparent ? 0.25f : 1.0f;
			int i = 0;
			foreach (var face in mesh.Faces)
			{
				float brightness = mesh.FaceBrightness(face);
				for (int vertex = 0; vertex < face.Vertices.Length; ++vertex, i += 4)
				{
					data.ColorArray[i] = data.ColorArray[i + 1] = data.ColorArray[i + 2] = brightness;
					data.ColorArray[i + 3] = alpha;
				}
			}
		}
	}
}
