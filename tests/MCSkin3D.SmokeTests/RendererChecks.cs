using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using MCSkin3D;
using OpenTK.Mathematics;
using Paril.OpenGL;

internal static class RendererChecks
{
    internal static void Run(Action<bool, string> check)
    {
        int[] daylight = { 38, 53, 69, 86, 104, 122, 141, 160, 179, 198, 215, 231, 244, 252, 255, 255 };
        for (int level = 0; level < daylight.Length; level++)
            check(Math.Abs(MinecraftPreviewLighting.Daylight(level) * 255 - daylight[level]) < .001f,
                "Minecraft daylight lightmap texel matches at level " + level);
        check(MinecraftPreviewLighting.Daylight(-1) == MinecraftPreviewLighting.Daylight(0) &&
            MinecraftPreviewLighting.Daylight(16) == MinecraftPreviewLighting.Daylight(15), "World light clamps to 0-15");
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
        check(data.IndiceArray.SequenceEqual(new uint[] { 0, 1, 2, 3, 7, 6, 5, 4 }),
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
        LightingAngle(renderer, check);
    }

    private static void LightingAngle(ClientArrayRenderer renderer, Action<bool, string> check)
    {
        foreach (var sample in new[] { (0, 0), (89, 89), (90, 90), (-90, -90), (180, 0), (-180, 0),
            (207, 27), (-153, 27), (int.MaxValue, -53), (int.MinValue, 52) })
            check(PreviewRenderOptions.NormalizeLightAngle(sample.Item1) == sample.Item2,
                "Lighting angle normalization preserves equivalent direction for " + sample.Item1);
        check(Enumerable.Range(-90, 181).All(angle => PreviewRenderOptions.NormalizeLightAngle(angle) == angle),
            "The angle control preserves every selectable value including both endpoints");
        Face Quad(Vector3 a, Vector3 b) => new Face(new[] { Vector3.Zero, a, a + b, b },
            new[] { Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY }, new[] { 0, 1, 2, 3 })
            { Normal = Vector3.Cross(a, b).Normalized() };
        var mesh = new Mesh("lighting directions") { Matrix = Matrix4.Identity,
            Faces = new List<Face> { Quad(Vector3.UnitX, Vector3.UnitY), Quad(Vector3.UnitY, Vector3.UnitZ),
                Quad(Vector3.UnitX, Vector3.UnitZ), Quad(Vector3.UnitZ, Vector3.UnitX) } };
        mesh.UserData = renderer.CreateUserData(mesh);
        var data = mesh.GetUserData<ClientArrayMeshUserData>();
        var vertices = data.VerticeArray;
        var uvs = data.TexCoordArray;
        bool Near(float actual, double expected) => Math.Abs(actual - expected) < .000001;
        foreach (bool raised in new[] { false, true })
        {
            mesh.IsSkinLayer = raised;
            mesh.SetLighting(true);
            renderer.UpdateUserData(mesh);
            var colors = data.ColorArray;
            var original = colors.ToArray();
            check(Near(mesh.FaceBrightness(mesh.Faces[0]), .739549875) && Near(mesh.FaceBrightness(mesh.Faces[1]), .49701425),
                "Default front and side shading matches vanilla for " + (raised ? "raised" : "regular") + " meshes");
            check(!mesh.SetLighting(true, lightAngle: 180) && !mesh.SetLighting(true, lightAngle: -180),
                "Equivalent half-turns retain the exact default colors without invalidating the cache");
            check(mesh.SetLighting(true, lightAngle: 90), "Changing only the light angle invalidates cached colors");
            renderer.UpdateUserData(mesh);
            check(Near(mesh.FaceBrightness(mesh.Faces[0]), .49701425) && Near(mesh.FaceBrightness(mesh.Faces[1]), .739549875),
                "A quarter-turn exchanges front and side lighting");
            check(mesh.FaceBrightness(mesh.Faces[2]) == 1 && mesh.FaceBrightness(mesh.Faces[3]) == .4f,
                "Horizontal light rotation preserves top and bottom lighting");
            check(!mesh.SetLighting(true, lightAngle: 90) && ReferenceEquals(colors, data.ColorArray) &&
                ReferenceEquals(vertices, data.VerticeArray) && ReferenceEquals(uvs, data.TexCoordArray),
                "Unchanged angles reuse lighting, geometry and texture-coordinate buffers");
            check(!mesh.SetLighting(true, lightAngle: -90), "The two quarter-turn endpoints share one cached lighting direction");
            mesh.SetLighting(true, lightLevel: 8, lightAngle: 90);
            check(Near(mesh.FaceBrightness(mesh.Faces[0]), .49701425 * 179 / 255),
                "Angle shading still multiplies Minecraft world-light brightness");
            mesh.SetLighting(true);
            renderer.UpdateUserData(mesh);
            check(original.SequenceEqual(data.ColorArray), "Restoring the default angle exactly restores original vertex colors");
        }
        mesh.SetLighting(true, lightAngle: 90);
        mesh.DrawTransparent = true;
        renderer.UpdateUserData(mesh);
        check(data.ColorArray.Where((_, i) => i % 4 == 3).All(alpha => alpha == .25f), "Angle changes preserve ghost opacity");
        mesh.SetLighting(false, lightAngle: -47);
        renderer.UpdateUserData(mesh);
        check(ColorsMatch(data.ColorArray, .25f), "Disabled lighting preserves texture colors at any angle");
        mesh.SetLighting(true, lightAngle: 999);
        check(!mesh.SetLighting(true, lightAngle: -81), "Rendering normalizes saved angles before comparing cached state");
    }

    private static bool ColorsMatch(float[] colors, float alpha)
    {
        for (int i = 0; i < colors.Length; i += 4)
            if (colors[i] != 1.0f || colors[i + 1] != 1.0f || colors[i + 2] != 1.0f || colors[i + 3] != alpha)
                return false;
        return true;
    }
}
