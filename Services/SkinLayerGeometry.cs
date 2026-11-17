using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK.Mathematics;
using Paril.Drawing;
using Paril.OpenGL;

namespace MCSkin3D
{
    // A preview-only mesh. UVs always sample the original pixel, including on its sides.
    internal sealed class SkinLayerGeometry
    {
        private readonly Mesh _source;
        private byte[] _alpha;
        private int _width, _height;
        private SkinLayerOptions _options;
        private List<Face> _pixelFaces = new();
        private readonly HashSet<int> _frontFaces = new();
        private readonly List<long> _drawOrder = new();
        public IReadOnlyList<Face> PixelFaces => _pixelFaces;
        public Point PixelFor(Face face) => new((int)((face.TexCoords[0].X + face.TexCoords[2].X) * .5f * _width),
            (int)((face.TexCoords[0].Y + face.TexCoords[2].Y) * .5f * _height));
        private sealed class Cell
        {
            public Vector3 Min, Max;
            public readonly List<(Vector3 Normal, Point Pixel, byte Alpha)> Pixels = new();
            public int FirstFace, EndFace;
        }
        private readonly List<Cell> _cells = new();
        private static readonly Vector3[] Directions =
            { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ };
        public Mesh Mesh { get; private set; }

        public SkinLayerGeometry(Mesh source) => _source = source;

        public void Update(byte[] alpha, int width, int height)
        {
            var options = SkinLayerOptions.Current;
            bool changed = _alpha == null || width != _width || height != _height || options != _options;
            if (!changed)
                foreach (var face in _source.Faces)
                {
                    var rect = Rectangle.Intersect(face.TexCoordsToInteger(width, height), new Rectangle(0, 0, width, height));
                    for (int y = rect.Top; y < rect.Bottom && !changed; ++y)
                        for (int x = rect.Left; x < rect.Right; ++x)
                            if (_alpha[y * width + x] != alpha[y * width + x]) { changed = true; break; }
                    if (changed) break;
                }
            if (!changed) return;
            _width = width;
            _height = height;
            _options = options;
            if (_alpha == null || _alpha.Length != alpha.Length) _alpha = new byte[alpha.Length];
            alpha.CopyTo(_alpha, 0);
            Build();
        }

        private void Build()
        {
            Mesh = new Mesh(_source.Name)
            {
                Faces = new List<Face>(), Mode = OpenTK.Graphics.OpenGL.PrimitiveType.Triangles, Part = _source.Part,
                IsArmor = true, ShadeFaces = true, IsSkinLayer = true,
                Matrix = _source.Matrix, Center = _source.Center, Bounds = _source.Bounds,
                LightingYUp = _source.LightingYUp
            };
            _cells.Clear();
            _frontFaces.Clear();
            _drawOrder.Clear();
            var bounds = Bounds3.EmptyBounds;
            foreach (var face in _source.Faces)
                foreach (var vertex in face.Vertices) bounds += vertex;
            var center = bounds.Center;
            // Start with the uninflated Minecraft cuboid. The mod places full pixel
            // cubes inside it, then scales the whole part (not each pixel outward).
            bool head = _source.Part == ModelPart.Helmet;
            bool arm = _source.Part == ModelPart.LeftArmArmor || _source.Part == ModelPart.RightArmArmor;
            float padding = head ? .5f : .25f;
            float step = 64f / _width;
            var min = bounds.Mins + new Vector3(padding);
            var scale = head ? new Vector3(_options.HeadSize) : new Vector3(_source.Part == ModelPart.ChestArmor ? _options.BodyWidth : _options.PixelSize, 1.035f, _options.PixelSize);
            float headTranslation = (-.25f + .25f * scale.Y - .04f * scale.Y) * 16;
            Vector3 Transform(Vector3 p)
            {
                if (head)
                {
                    // Preserve the mod's arithmetic order at the shifted head
                    // pivot; reassociation can move a silhouette edge by a pixel.
                    p.Y = (min.Y + .6f) + (p.Y - min.Y);
                    return p * scale + new Vector3(0, headTranslation, 0);
                }
                // Minecraft's arm mesh offset is .499/.998 rather than .5/1.
                if (arm) p.X = p.X - center.X + center.X * .998f;
                p.Y += arm ? -.1f : -.2f;
                return p * scale;
            }
            Vector3 Uninflate(Vector3 p) => p - new Vector3(Math.Sign(p.X - center.X), Math.Sign(p.Y - center.Y), Math.Sign(p.Z - center.Z)) * padding;
            var grid = new Dictionary<(int X, int Y, int Z), Cell>();
            foreach (var face in _source.Faces)
            {
                if (face.Vertices.Length != 4) continue;
                var rect = Rectangle.Intersect(face.TexCoordsToInteger(_width, _height), new Rectangle(0, 0, _width, _height));
                if (rect.IsEmpty) continue;
                // Build an affine UV-to-position mapping, retaining mirrored and bottom UVs.
                var uvU = face.TexCoords[1] - face.TexCoords[0];
                var uvV = face.TexCoords[3] - face.TexCoords[0];
                float determinant = uvU.X * uvV.Y - uvU.Y * uvV.X;
                if (Math.Abs(determinant) < 1e-8f) continue;
                var origin = Uninflate(face.Vertices[0]);
                var edgeU = Uninflate(face.Vertices[1]) - origin;
                var edgeV = Uninflate(face.Vertices[3]) - origin;
                Vector3 Position(float x, float y)
                {
                    var uv = new Vector2(x / _width, y / _height) - face.TexCoords[0];
                    return origin + edgeU * ((uv.X * uvV.Y - uv.Y * uvV.X) / determinant)
                        + edgeV * ((uvU.X * uv.Y - uvU.Y * uv.X) / determinant);
                }
                var normal = Vector3.Normalize(Vector3.Cross(edgeU, edgeV));
                normal = new Vector3(Math.Sign(normal.X), Math.Sign(normal.Y), Math.Sign(normal.Z));
                if (Vector3.Dot(normal, origin - center) < 0) normal = -normal;
                for (int y = rect.Top; y < rect.Bottom; ++y)
                    for (int x = rect.Left; x < rect.Right; ++x)
                    {
                        byte alpha = _alpha[y * _width + x];
                        if (alpha == 0) continue;
                        if (alpha != 255) Mesh.HasTransparency = true;
                        var cellCenter = Position(x + .5f, y + .5f) - normal * (step / 2);
                        var index = (cellCenter - min) / step - new Vector3(.5f);
                        var key = ((int)Math.Round(index.X), (int)Math.Round(index.Y), (int)Math.Round(index.Z));
                        if (!grid.TryGetValue(key, out var cell))
                        {
                            var cellMin = min + new Vector3(key.Item1, key.Item2, key.Item3) * step;
                            grid.Add(key, cell = new Cell { Min = cellMin, Max = cellMin + new Vector3(step) });
                        }
                        cell.Pixels.Add((normal, new Point(x, y), alpha));
                    }
            }

            var dimensions = (bounds.Maxs - bounds.Mins - new Vector3(padding * 2)) / step;
            bool Outside((int X, int Y, int Z) key) => key.X < 0 || key.Y < 0 || key.Z < 0 ||
                key.X >= dimensions.X || key.Y >= dimensions.Y || key.Z >= dimensions.Z;
            (int X, int Y, int Z) Adjacent((int X, int Y, int Z) key, Vector3 n) =>
                (key.X + (int)n.X, key.Y + (int)n.Y, key.Z + (int)n.Z);
            byte Alpha((int X, int Y, int Z) key, Vector3 n)
            {
                if (grid.TryGetValue(key, out var cell))
                    foreach (var pixel in cell.Pixels) if (pixel.Normal == n) return pixel.Alpha;
                return 0;
            }
            // Surface pixels at a cuboid edge occupy the same cell, but retain
            // independent visibility. A union alone incorrectly removes some of
            // the mod's corner triangles (notably where a sleeve cap meets a side).
            foreach (var pair in grid)
            {
                var cell = pair.Value;
                var cellCenter = (cell.Min + cell.Max) / 2;
                cell.FirstFace = Mesh.Faces.Count;
                foreach (var normal in Directions)
                {
                    // Match the mod's starting corner as well as its winding.
                    // A trimmed corner reassigns the full pixel UV rectangle to
                    // its remaining three vertices, so cyclic order matters.
                    var u = normal.X != 0 ? Vector3.UnitZ * -normal.X : normal.Y != 0 ? -Vector3.UnitX : Vector3.UnitX * normal.Z;
                    var v = Vector3.Cross(normal, u);
                    var p = cellCenter + normal * (step / 2);
                    var quad = new[] { p - (u + v) * (step / 2), p + (u - v) * (step / 2),
                        p + (u + v) * (step / 2), p + (v - u) * (step / 2) };
                    foreach (var pixel in cell.Pixels)
                    {
                        float alignment = Vector3.Dot(normal, pixel.Normal);
                        if (alignment < -.5f)
                        {
                            // Inner faces are useful only at an open cuboid edge.
                            bool border = false, covered = false;
                            foreach (var edge in Directions)
                                if (Vector3.Dot(edge, pixel.Normal) == 0 && Outside(Adjacent(pair.Key, edge)))
                                {
                                    border = true;
                                    covered |= Alpha(pair.Key, edge) != 0 || Alpha(Adjacent(pair.Key, -pixel.Normal), edge) != 0;
                                }
                            if (!border || covered) continue;
                        }
                        else if (alignment == 0)
                        {
                            var next = Adjacent(pair.Key, normal);
                            if (Outside(next))
                            {
                                if (Alpha(pair.Key, normal) != 0) continue;
                            }
                            else
                            {
                                byte neighbor = Alpha(next, pixel.Normal);
                                // The last cell on the adjoining face can cover
                                // a gap in this face's final row of pixels.
                                if (neighbor == 0 && Outside(Adjacent(next, normal))) neighbor = Alpha(next, normal);
                                if (neighbor != 0 && !(pixel.Alpha == 255 && neighbor != 255)) continue;
                            }
                        }
                        var polygon = new List<Vector3>(quad);
                        foreach (var other in cell.Pixels)
                        {
                            if (alignment != 0 || other.Normal == pixel.Normal || Vector3.Dot(normal, other.Normal) != 0) continue;
                            polygon.RemoveAll(p => Vector3.Dot(p - cellCenter, pixel.Normal - other.Normal) < -1e-6f);
                        }
                        if (polygon.Count < 3) continue;
                        var uv = new Vector2((pixel.Pixel.X + .5f) / _width, (pixel.Pixel.Y + .5f) / _height);
                        long order = (((long)DirectionOrder(pixel.Normal) * _width + pixel.Pixel.X) * _height + pixel.Pixel.Y) * 6 + DirectionOrder(normal);
                        if (alignment > .5f) _frontFaces.Add(Mesh.Faces.Count);
                        if (polygon.Count == 4)
                            AddFace(Transform(polygon[0]), Transform(polygon[1]), Transform(polygon[2]), Transform(polygon[3]), normal, uv, order);
                        else
                            for (int i = 1; i + 1 < polygon.Count; i++)
                                AddFace(Transform(polygon[0]), Transform(polygon[i]), Transform(polygon[i + 1]), Transform(polygon[i + 1]), normal, uv, order);
                    }
                }
                cell.EndFace = Mesh.Faces.Count;
                cell.Min = Transform(cell.Min);
                cell.Max = Transform(cell.Max);
                _cells.Add(cell);
            }
            _pixelFaces = Mesh.Faces;
            // Preserve cell-contiguous faces for picking, but submit in upstream
            // Direction / texture-u / texture-v / cube-face order. Overlapping
            // translucent edge samples depend on draw order, even with identical UVs.
            var drawIndices = new List<int>(_pixelFaces.Count);
            for (int i = 0; i < _pixelFaces.Count; i++) drawIndices.Add(i);
            drawIndices.Sort((a, b) => _drawOrder[a].CompareTo(_drawOrder[b]));
            Mesh.Faces = new List<Face>(_pixelFaces.Count);
            foreach (int i in drawIndices)
                if (!_options.FastRender || !_frontFaces.Contains(i)) Mesh.Faces.Add(_pixelFaces[i]);
            if (_options.FastRender && _pixelFaces.Count != 0)
            {
                // The mod batches front surfaces into the six faces of a cuboid.
                // Keep the expanded pixel faces for exact picking and parity checks.
                foreach (var face in _source.Faces)
                {
                    var vertices = new Vector3[face.Vertices.Length];
                    for (int i = 0; i < vertices.Length; i++) vertices[i] = Transform(Uninflate(face.Vertices[i]));
                    var normal = Vector3.Normalize(Vector3.Cross(vertices[1] - vertices[0], vertices[3] - vertices[0]));
                    normal = new Vector3(Math.Sign(normal.X), Math.Sign(normal.Y), Math.Sign(normal.Z));
                    if (Vector3.Dot(normal, vertices[0] - Transform(center)) < 0) normal = -normal;
                    Mesh.Faces.Add(new Face(vertices, (Vector2[])face.TexCoords.Clone(), TriangleIndices(vertices[0], vertices[1], vertices[2], normal)) { Normal = normal });
                }
            }
            if (Mesh.Faces.Count != 0)
            {
                Mesh.Center = Vector3.Zero;
                Mesh.CalculateCenter();
                Mesh.Center = Vector3.TransformPosition(Mesh.Center, _source.Matrix);
            }
        }

        private static int DirectionOrder(Vector3 n) => n.Y < -.5f ? 0 : n.Y > .5f ? 1 : n.Z < -.5f ? 2 : n.Z > .5f ? 3 : n.X < -.5f ? 4 : 5;

        private void AddFace(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Vector2 uv, long drawOrder)
        {
            _drawOrder.Add(drawOrder);
            var half = new Vector2(.5f / _width, .5f / _height);
            Mesh.Faces.Add(new Face(new[] { a, b, c, d }, new[] { uv + new Vector2(half.X, -half.Y), uv - half,
                uv + new Vector2(-half.X, half.Y), uv + half },
                TriangleIndices(a, b, c, normal)) { Normal = normal });
        }

        private static int[] TriangleIndices(Vector3 a, Vector3 b, Vector3 c, Vector3 normal) =>
            // Minecraft's quad index buffer is 0,1,2,2,3,0. Keep its 0–2
            // diagonal and reverse only winding for the editor's front culling.
            // GL_QUADS can choose a different diagonal/UV at collapsed corners,
            // which becomes visible with multisampling and translucent texels.
            Vector3.Dot(Vector3.Cross(b - a, c - a), normal) > 0
                ? new[] { 0, 2, 1, 2, 0, 3 } : new[] { 0, 1, 2, 2, 3, 0 };

        public bool TryPick(Vector3 start, Vector3 end, out float distance, out Point pixel)
        {
            distance = float.MaxValue;
            pixel = new Point(-1, -1);
            var inverse = Matrix4.Invert(_source.Matrix);
            var origin = Vector3.TransformPosition(start, inverse);
            var direction = Vector3.TransformPosition(end, inverse) - origin;
            float nearest = float.MaxValue;
            foreach (var cell in _cells)
            {
                float enter = 0, exit = 1;
                for (int axis = 0; axis < 3; ++axis)
                {
                    if (Math.Abs(direction[axis]) < 1e-7f)
                    {
                        if (origin[axis] < cell.Min[axis] || origin[axis] > cell.Max[axis]) { exit = -1; break; }
                    }
                    else
                    {
                        float a = (cell.Min[axis] - origin[axis]) / direction[axis];
                        float b = (cell.Max[axis] - origin[axis]) / direction[axis];
                        enter = Math.Max(enter, Math.Min(a, b));
                        exit = Math.Min(exit, Math.Max(a, b));
                    }
                }
                if (enter > exit || enter >= nearest) continue;
                // Test the visible polygon after the cheap box test so a split
                // corner selects the pixel actually supplying its color.
                for (int i = cell.FirstFace; i < cell.EndFace; i++)
                {
                    var face = _pixelFaces[i];
                    float denominator = Vector3.Dot(direction, face.Normal);
                    if (denominator >= -1e-7f) continue;
                    float t = Vector3.Dot(face.Vertices[0] - origin, face.Normal) / denominator;
                    if (t < 0 || t > 1 || t >= nearest) continue;
                    var hit = origin + direction * t;
                    bool inside = true;
                    for (int edge = 0; edge < 4; edge++)
                    {
                        var a = face.Vertices[edge];
                        var b = face.Vertices[(edge + 1) % 4];
                        if (Vector3.Dot(Vector3.Cross(b - a, hit - a), face.Normal) < -1e-5f) { inside = false; break; }
                    }
                    if (!inside) continue;
                    nearest = t;
                    pixel = PixelFor(face);
                }
            }
            if (nearest == float.MaxValue) return false;
            distance = nearest * (end - start).Length;
            return true;
        }
    }
}
