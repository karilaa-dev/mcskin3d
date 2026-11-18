using System;
using System.Drawing;
using System.Windows.Forms;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using Paril.OpenGL;

namespace MCSkin3D
{
    public partial class Editor
    {
        // Face coordinates run from -1 to 1 in both texture directions.
        private readonly record struct CubeFace(Vector3 Normal, Vector3 Across, Vector3 Up, int Column, int Row)
        {
            public Vector3 At(float u, float v) => Normal + u * Across + v * Up;
        }

        private static readonly CubeFace[] CubeFaces =
        {
            new(-Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, 0, 0),
            new( Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY, 1, 0),
            new( Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, 2, 0),
            new(-Vector3.UnitY, Vector3.UnitX, -Vector3.UnitZ, 3, 0),
            new(-Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY, 0, 1),
            new( Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY, 1, 1)
        };
        private const float FaceCenterExtent = .58f;
        private Point? _cubePointer;
        private Vector3 _pressedCubeTarget;
        private Rectangle _cubeClickBounds;
        private bool _draggingCube;

        private Rectangle CubeBounds()
        {
            if (_currentViewMode != ViewMode.Perspective && _currentViewMode != ViewMode.Hybrid)
                return Rectangle.Empty;
            var view = GetViewport3D();
            int size = Math.Min(LogicalToDeviceUnits(120), Math.Min(view.Width, view.Height));
            return new Rectangle(view.Right - size, Renderer.Height - view.Height, size, size);
        }

        private static Matrix4 CubeRotation() =>
            Matrix4.CreateRotationY(-MathHelper.DegreesToRadians(_3DRotationY)) *
            Matrix4.CreateRotationX(-MathHelper.DegreesToRadians(_3DRotationX));

        private Vector3 PickCube(int x, int y)
        {
            var bounds = CubeBounds();
            if (!bounds.Contains(x, y)) return Vector3.Zero;

            // The orthographic pointer ray in cube space uses the same rotation
            // and scale as the textured faces. Positive view Z faces the viewer.
            var inverse = Matrix4.Transpose(CubeRotation());
            float radius = bounds.Width / 4f;
            var origin = Vector3.TransformVector(new Vector3((x + .5f - bounds.Left) / radius - 2,
                (y + .5f - bounds.Top) / radius - 2, 0), inverse);
            var direction = Vector3.TransformVector(Vector3.UnitZ, inverse);
            foreach (var face in CubeFaces)
            {
                float facing = Vector3.Dot(face.Normal, direction);
                if (facing <= .00001f) continue;
                var hit = origin + direction * ((1 - Vector3.Dot(face.Normal, origin)) / facing);
                float u = Vector3.Dot(hit, face.Across), v = Vector3.Dot(hit, face.Up);
                if (Math.Abs(u) > 1.00001f || Math.Abs(v) > 1.00001f) continue;
                return face.At(TargetAxis(u), TargetAxis(v));
            }
            return Vector3.Zero;
        }

        private static int TargetAxis(float value) => Math.Abs(value) < FaceCenterExtent ? 0 : Math.Sign(value);

        private static (float Min, float Max) TargetSpan(float axis) => axis switch
        {
            < 0 => (-1, -FaceCenterExtent),
            > 0 => (FaceCenterExtent, 1),
            _ => (-FaceCenterExtent, FaceCenterExtent)
        };

        private void BeginCubeGesture(Vector3 target, MouseEventArgs e)
        {
            _pressedCubeTarget = target;
            var tolerance = SystemInformation.DragSize;
            _cubeClickBounds = new Rectangle(e.X - tolerance.Width / 2, e.Y - tolerance.Height / 2,
                tolerance.Width, tolerance.Height);
            _mouseIn3D = true;
            _isValidPick = false;
            _pickPosition = new Point(-1, -1);
            _cameraTool.BeginClick(_lastSkin, Point.Empty, e);
            Renderer.Capture = true;
            Renderer.Invalidate();
        }

        private void MoveCubeGesture(Point location)
        {
            if (!_draggingCube && _cubeClickBounds.Contains(location)) return;
            _draggingCube = true;
            RotateView(_cameraTool.DragDelta(location), 1);
        }

        private void ReleaseCubeGesture(Point location)
        {
            var target = _pressedCubeTarget;
            bool click = !_draggingCube && _cubeClickBounds.Contains(location) && PickCube(location.X, location.Y) == target;
            CancelCubeGesture();
            if (click) SnapToCube(target);
        }

        private void CancelCubeGesture()
        {
            if (_pressedCubeTarget == Vector3.Zero) return;
            _pressedCubeTarget = Vector3.Zero;
            _draggingCube = false;
            Renderer.Capture = false;
            Renderer.Invalidate();
        }

        private void SnapToCube(Vector3 target)
        {
            // Pitch 180, yaw 0 is the editor's upright, front-facing orbit.
            float horizontal = MathF.Sqrt(target.X * target.X + target.Z * target.Z);
            _3DRotationX = 180 + MathHelper.RadiansToDegrees(MathF.Atan2(target.Y, horizontal));
            _3DRotationY = horizontal == 0 ? 0 : MathHelper.RadiansToDegrees(MathF.Atan2(-target.X, -target.Z));
            _isValidPick = false;
            _pickPosition = new Point(-1, -1);
            CalculateMatrices();
            Renderer.Invalidate();
        }

        private void DrawOrientationCube()
        {
            var bounds = CubeBounds();
            if (bounds.IsEmpty || bounds.Width == 0) return;
            var hover = !_mouseIsDown && !_draggingCube && _cubePointer is Point p ? PickCube(p.X, p.Y) : Vector3.Zero;
            Setup2D(new Rectangle(bounds.X, Renderer.Height - bounds.Bottom, bounds.Width, bounds.Height));
            GL.Translate(bounds.Width / 2f, bounds.Height / 2f, 0);
            GL.Scale(bounds.Width / 4f, bounds.Height / 4f, 1);
            var rotation = CubeRotation();
            GL.MultMatrix(ref rotation);
            GL.Enable(EnableCap.Blend);
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(TriangleFace.Back);
            _cubeSides.Bind();
            GL.Color4((byte)255, (byte)255, (byte)255, (byte)190);
            GL.Begin(PrimitiveType.Quads);
            bool flipPoles = MathF.Cos(MathHelper.DegreesToRadians(_3DRotationY)) < 0;
            foreach (var face in CubeFaces)
            {
                int flip = face.Normal.Y != 0 && flipPoles ? -1 : 1;
                TexturedVertex(face, -1, -1, flip);
                TexturedVertex(face,  1, -1, flip);
                TexturedVertex(face,  1,  1, flip);
                TexturedVertex(face, -1,  1, flip);
            }
            GL.End();

            TextureGL.Unbind();
            GL.Disable(EnableCap.Texture2D);
            GL.Color4((byte)60, (byte)156, (byte)245, (byte)150);
            GL.Begin(PrimitiveType.Quads);
            foreach (var face in CubeFaces)
            {
                if (Vector3.Dot(face.Normal, hover) != 1) continue;
                var (left, right) = TargetSpan(Vector3.Dot(hover, face.Across));
                var (bottom, top) = TargetSpan(Vector3.Dot(hover, face.Up));
                GL.Vertex3(face.At(left, bottom));
                GL.Vertex3(face.At(right, bottom));
                GL.Vertex3(face.At(right, top));
                GL.Vertex3(face.At(left, top));
            }
            GL.End();

            GL.Color4((byte)72, (byte)72, (byte)72, (byte)190);
            GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);
            GL.Begin(PrimitiveType.Quads);
            foreach (var face in CubeFaces)
            {
                GL.Vertex3(face.At(-1, -1));
                GL.Vertex3(face.At( 1, -1));
                GL.Vertex3(face.At( 1,  1));
                GL.Vertex3(face.At(-1,  1));
            }
            GL.End();
            GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
            GL.Enable(EnableCap.Texture2D);
            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.CullFace);
            GL.CullFace(TriangleFace.Front);
        }

        private static void TexturedVertex(CubeFace face, int u, int v, int flip)
        {
            GL.TexCoord2((face.Column + (1 + flip * u) * .5f) / 4, (face.Row + (1 - flip * v) * .5f) / 2);
            GL.Vertex3(face.At(u, v));
        }
    }
}
