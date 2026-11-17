using System;

namespace MCSkin3D
{
    internal readonly record struct PreviewRenderOptions(int Resolution, int Samples, bool MinecraftLighting, int LightLevel = 8, int LightAngle = 0, bool LightSurroundings = true)
    {
        public static PreviewRenderOptions Defaults => new(100, 0, true);
        public static PreviewRenderOptions Current => new PreviewRenderOptions(GlobalSettings.PreviewResolution,
            GlobalSettings.Multisamples, GlobalSettings.MinecraftLighting, GlobalSettings.PreviewLightLevel, GlobalSettings.PreviewLightAngle, GlobalSettings.PreviewLightSurroundings).Validated();

        public PreviewRenderOptions Validated() => new(Math.Clamp(Resolution, 50, 200),
            Samples >= 8 ? 8 : Samples >= 4 ? 4 : Samples >= 2 ? 2 : 0, MinecraftLighting, Math.Clamp(LightLevel, 0, 15), NormalizeLightAngle(LightAngle), LightSurroundings);

        public static int NormalizeLightAngle(int angle)
        {
            // Wrap older angles into one half turn, preserving either selectable endpoint.
            int wrapped = angle % 180;
            return wrapped > 90 ? wrapped - 180 : wrapped < -90 ? wrapped + 180 : wrapped;
        }

        public void Save()
        {
            var valid = Validated();
            GlobalSettings.PreviewResolution = valid.Resolution;
            GlobalSettings.Multisamples = valid.Samples;
            GlobalSettings.MinecraftLighting = valid.MinecraftLighting;
            GlobalSettings.PreviewLightLevel = valid.LightLevel;
            GlobalSettings.PreviewLightAngle = valid.LightAngle;
            GlobalSettings.PreviewLightSurroundings = valid.LightSurroundings;
        }
    }
}
