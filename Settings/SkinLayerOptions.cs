using System;

namespace MCSkin3D
{
    // Player-preview controls from 3D Skin Layers' Config/ConfigScreenProvider.
    internal readonly record struct SkinLayerOptions(float PixelSize, float HeadSize, float BodyWidth,
        bool FastRender, int Distance, int Parts)
    {
        public static SkinLayerOptions Defaults => new(1.15f, 1.18f, 1.05f, true, 14, 63);
        public static SkinLayerOptions Current => new SkinLayerOptions(GlobalSettings.SkinLayerPixelSize,
            GlobalSettings.SkinLayerHeadSize, GlobalSettings.SkinLayerBodyWidth, GlobalSettings.SkinLayerFastRender,
            GlobalSettings.SkinLayerDistance, GlobalSettings.SkinLayerParts).Validated();

        public SkinLayerOptions Validated() => new(Clamp(PixelSize, 1.4f, 1.15f), Clamp(HeadSize, 1.25f, 1.18f),
            Clamp(BodyWidth, 1.4f, 1.05f), FastRender, Math.Clamp(Distance, 5, 40), Parts & 63);
        private static float Clamp(float value, float max, float fallback) =>
            float.IsFinite(value) ? Math.Clamp(value, 1.001f, max) : fallback;

        public bool Enabled(ModelPart part)
        {
            int bit = part switch { ModelPart.Helmet => 0, ModelPart.ChestArmor => 1,
                ModelPart.LeftArmArmor => 2, ModelPart.RightArmArmor => 3,
                ModelPart.LeftLegArmor => 4, ModelPart.RightLegArmor => 5, _ => -1 };
            return bit >= 0 && (Parts & (1 << bit)) != 0;
        }

        public void Save()
        {
            var valid = Validated();
            GlobalSettings.SkinLayerPixelSize = valid.PixelSize;
            GlobalSettings.SkinLayerHeadSize = valid.HeadSize;
            GlobalSettings.SkinLayerBodyWidth = valid.BodyWidth;
            GlobalSettings.SkinLayerFastRender = valid.FastRender;
            GlobalSettings.SkinLayerDistance = valid.Distance;
            GlobalSettings.SkinLayerParts = valid.Parts;
        }
    }
}
