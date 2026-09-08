using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;
using Paril.OpenGL;

internal static class RendererChecks
{
    internal static void Run(Action<bool, string> check)
    {
        // CPU array preparation must be testable without creating an OpenGL window.
        var renderer = (ClientArrayRenderer)RuntimeHelpers.GetUninitializedObject(typeof(ClientArrayRenderer));
        var mesh = new Mesh("two quads")
        {
            Faces = new List<Face>
            {
                new Face(new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0) },
                    new[] { Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY }, new[] { 0, 1, 2, 3 }),
                new Face(new[] { new Vector3(0, 0, 2), new Vector3(1, 0, 2), new Vector3(1, 1, 2), new Vector3(0, 1, 2) },
                    new[] { Vector2.UnitY, Vector2.One, Vector2.UnitX, Vector2.Zero }, new[] { 3, 2, 1, 0 })
            }
        };
        mesh.UserData = renderer.CreateUserData(mesh);
        var data = mesh.GetUserData<ClientArrayMeshUserData>();
        check(data.VerticeArray.SequenceEqual(new float[] { 0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0, 0, 0, 2, 1, 0, 2, 1, 1, 2, 0, 1, 2 }),
            "Renderer must preserve face order and vertex positions");
        check(data.TexCoordArray.SequenceEqual(new float[] { 0, 0, 1, 0, 1, 1, 0, 1, 0, 1, 1, 1, 1, 0, 0, 0 }),
            "Renderer must preserve each face's texture orientation");
        check(data.IndiceArray.SequenceEqual(new byte[] { 0, 1, 2, 3, 7, 6, 5, 4 }),
            "Renderer must offset later face indices without changing winding");

        renderer.UpdateUserData(mesh);
        var colorBuffer = data.ColorArray;
        check(colorBuffer.Length == 32 && colorBuffer.All(x => x == 1.0f), "Visible quads must have opaque white vertex colors");
        mesh.DrawTransparent = true;
        renderer.UpdateUserData(mesh);
        check(ReferenceEquals(colorBuffer, data.ColorArray), "Ghost visibility must reuse the color buffer");
        check(ColorsMatch(data.ColorArray, 0.25f), "Ghost quads must preserve white RGB with quarter opacity");
        mesh.DrawTransparent = false;
        renderer.UpdateUserData(mesh);
        check(ColorsMatch(data.ColorArray, 1.0f), "Restoring visible parts must restore full opacity");

        for (int i = 0; i < 1000; i++) { mesh.DrawTransparent = !mesh.DrawTransparent; renderer.UpdateUserData(mesh); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { mesh.DrawTransparent = !mesh.DrawTransparent; renderer.UpdateUserData(mesh); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        check(allocated == 0, "Repeated ghost visibility changes must not allocate managed buffers");

        mesh.Faces.RemoveAt(1);
        renderer.UpdateUserData(mesh);
        check(data.ColorArray.Length == 16 && ColorsMatch(data.ColorArray, 1.0f), "Color buffer must resize when face count changes");
        mesh.Faces.Clear();
        mesh.UserData = renderer.CreateUserData(mesh);
        renderer.UpdateUserData(mesh);
        data = mesh.GetUserData<ClientArrayMeshUserData>();
        check(data.VerticeArray.Length == 0 && data.TexCoordArray.Length == 0 && data.IndiceArray.Length == 0 && data.ColorArray.Length == 0,
            "Empty meshes must produce valid empty buffers");
    }

    private static bool ColorsMatch(float[] colors, float alpha)
    {
        for (int i = 0; i < colors.Length; i += 4)
            if (colors[i] != 1.0f || colors[i + 1] != 1.0f || colors[i + 2] != 1.0f || colors[i + 3] != alpha)
                return false;
        return true;
    }
}
