using OpenTK.Mathematics;
using Paril.Drawing;
using Paril.OpenGL;

namespace MCSkin3D
{
    internal static class FirstPersonPreview
    {
        // Minecraft renders the hand in a separate pass with a fixed projection,
        // independent of the world FOV selected in Options.
        public const int HandFov = 70;

        public static bool IsArm(ModelPart part, bool leftHand) => leftHand
            ? part == ModelPart.LeftArm || part == ModelPart.LeftArmArmor
            : part == ModelPart.RightArm || part == ModelPart.RightArmArmor;

        public static void Pose(Model model)
        {
            foreach (var mesh in model.Meshes)
            {
                bool left = IsArm(mesh.Part, true);
                if (!left && !IsArm(mesh.Part, false)) continue;
                float side = left ? -1 : 1;
                // Minecraft's resting empty-hand pose, in row-vector order.
                // Keep the editor's pixel units (16 per block). Minecraft 26.3
                // uses Y=2 for both wide and slim arms, unlike the bundled Alex
                // model's legacy Y=2.5. Sleeves share the arm pose, which also
                // removes the historical right-sleeve export's extra Z offset.
                // See docs/first-person-preview.md for the reference transforms.
                mesh.LightingYUp = true;
                mesh.Matrix = Matrix4.CreateRotationZ(side * .1f) *
                    Matrix4.CreateTranslation(side * -5, 2, 0) *
                    Matrix4.CreateTranslation(side * 5.6f * 16, 0, 0) *
                    Matrix4.CreateRotationY(MathHelper.DegreesToRadians(side * -135)) *
                    Matrix4.CreateRotationX(MathHelper.DegreesToRadians(200)) *
                    Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(side * 120)) *
                    Matrix4.CreateTranslation(side * -16, 3.6f * 16, 3.5f * 16) *
                    Matrix4.CreateRotationY(MathHelper.DegreesToRadians(side * 45)) *
                    Matrix4.CreateTranslation(side * .64000005f * 16, -.6f * 16, -.71999997f * 16);
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
                if (count > 0) mesh.Center /= count;
            }
        }
    }
}
