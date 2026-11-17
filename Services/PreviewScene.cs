using System;
using System.Collections.Generic;
using System.Drawing;
using MCSkin3D.Models;
using OpenTK.Mathematics;
using Paril.Drawing;
using Paril.OpenGL;

namespace MCSkin3D
{
    public enum EditingTarget { Skin, Cape }
    public enum AccessoryView { Cape, Elytra }

    internal sealed class PreviewItem
    {
        public Skin Document;
        public Model Model;
        public RectangleF Sheet;
        public byte[] Alpha;
        public readonly Dictionary<Mesh, SkinLayerGeometry> SkinLayers = new();
        public bool UseSkinLayers => GlobalSettings.SkinLayers3D &&
            (Editor.CameraPosition - new Vector3(0, 24, 0)).LengthSquared <= MathF.Pow(SkinLayerOptions.Current.Distance * 16, 2);

        public void UpdateSkinLayers()
        {
            if (!GlobalSettings.SkinLayers3D || Role(Document) != EditingTarget.Skin)
            {
                SkinLayers.Clear();
                return;
            }
            foreach (var mesh in Model.Meshes)
            {
                if ((!mesh.IsArmor && mesh.Part != ModelPart.Helmet) || mesh.IsSolid) continue;
                if (!SkinLayerOptions.Current.Enabled(mesh.Part)) { SkinLayers.Remove(mesh); continue; }
                if (!SkinLayers.TryGetValue(mesh, out var layer))
                    SkinLayers.Add(mesh, layer = new SkinLayerGeometry(mesh));
                layer.Update(Alpha, Document.Width, Document.Height);
            }
        }

        public void CaptureAlpha(ColorGrabber pixels)
        {
            int count = Document.Width * Document.Height;
            if (Alpha == null || Alpha.Length != count) Alpha = new byte[count];
            for (int y = 0; y < Document.Height; ++y)
                for (int x = 0; x < Document.Width; ++x)
                    Alpha[y * Document.Width + x] = pixels[x, y].Alpha;
        }

        public bool TransparentAt(Vector2 uv)
        {
            if (Alpha == null || Alpha.Length != Document.Width * Document.Height) return false;
            int x = Math.Clamp((int)Math.Floor(uv.X * Document.Width), 0, Document.Width - 1);
            int y = Math.Clamp((int)Math.Floor(uv.Y * Document.Height), 0, Document.Height - 1);
            return Alpha[y * Document.Width + x] == 0;
        }

        public static EditingTarget? Role(Skin document)
        {
            if (document?.Model?.File == null || document.Width <= 0 || document.Height <= 0) return null;
            var model = document.Model;
            if (Math.Abs(document.Width / (float)document.Height - model.DefaultWidth / model.DefaultHeight) > .001f) return null;
            switch (model.Path)
            {
                case "Players/Steve": case "Players/Alex":
                case "Players/Steve (Minimal)": case "Players/Alex (Minimal)": return EditingTarget.Skin;
                case "Players/Cape": case "Items/Elytra": return EditingTarget.Cape;
                default: return null;
            }
        }

        public static Model Clone(Model source)
        {
            var model = new ModelFormatTCN().Load(source.File.FullName);
            model.File = source.File;
            Array.Copy(source.PartsEnabled, model.PartsEnabled, model.PartsEnabled.Length);
            return model;
        }

        public static void PoseAccessory(Model model, AccessoryView view, float wingSpread)
        {
            if (!float.IsFinite(wingSpread)) throw new ArgumentOutOfRangeException(nameof(wingSpread));
            wingSpread = Math.Clamp(wingSpread, 0, 1);
            // Minecraft's Cape/Elytra layers translate 0.125 blocks along +Z.
            // These meshes use pixels (16 per block), with Y down and the torso
            // spanning Z=-2..2. Attach at its back, without an extra air gap.
            // Reference transforms and the historical export correction: docs/accessory-rendering.md.
            const float backOffset = 2;
            foreach (var mesh in model.Meshes)
            {
                if (view == AccessoryView.Cape)
                {
                    // LayerCape applies the 180-degree turn, then its resting 6-degree tilt.
                    mesh.Matrix = Matrix4.CreateRotationY(MathHelper.Pi) *
                        Matrix4.CreateRotationX(MathHelper.DegreesToRadians(6)) * Matrix4.CreateTranslation(0, 0, backOffset);
                }
                else
                {
                    bool right = mesh.Part == ModelPart.RightArm;
                    float pitch = 15 + 5 * wingSpread;
                    float spread = 15 + 75 * wingSpread;
                    // The export calls Minecraft's left wing RightArm and starts it at
                    // -14 rather than -10. Correct only the independent preview instance.
                    // Row-vector order matches ModelRenderer: pitch, roll, then pivot.
                    mesh.Matrix = Matrix4.CreateTranslation(right ? 4 : 0, 0, 0) *
                        Matrix4.CreateRotationX(MathHelper.DegreesToRadians(pitch)) *
                        Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(right ? -spread : spread)) *
                        Matrix4.CreateTranslation(right ? 5 : -5, 0, backOffset);
                }
                // Rendering, depth sorting, camera fitting and picking use exactly the same transform.
                mesh.Bounds = Bounds3.EmptyBounds;
                mesh.Center = Vector3.Zero;
                int count = 0;
                foreach (var face in mesh.Faces)
                    foreach (var vertex in face.Vertices)
                    {
                        var point = Vector3.TransformPosition(vertex, mesh.Matrix);
                        mesh.Bounds += point;
                        mesh.Center += point;
                        ++count;
                    }
                if (count != 0) mesh.Center /= count;
            }
        }
    }
}
