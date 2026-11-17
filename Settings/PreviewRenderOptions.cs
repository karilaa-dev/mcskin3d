using System;

namespace MCSkin3D
{
    internal readonly record struct PreviewRenderOptions(int Resolution, int Samples, bool MinecraftLighting, int GameBrightness = 100, int LightLevel = 8)
    {
        public static PreviewRenderOptions Defaults => new(100, 0, true, 100, 8);
        public static PreviewRenderOptions Current => new PreviewRenderOptions(GlobalSettings.PreviewResolution,
            GlobalSettings.Multisamples, GlobalSettings.MinecraftLighting, GlobalSettings.PreviewGameBrightness, GlobalSettings.PreviewLightLevel).Validated();

        public PreviewRenderOptions Validated() => new(Math.Clamp(Resolution, 50, 200),
            Samples >= 8 ? 8 : Samples >= 4 ? 4 : Samples >= 2 ? 2 : 0, MinecraftLighting, Math.Clamp(GameBrightness, 0, 100), Math.Clamp(LightLevel, 0, 15));

        public void Save()
        {
            var valid = Validated();
            GlobalSettings.PreviewResolution = valid.Resolution;
            GlobalSettings.Multisamples = valid.Samples;
            GlobalSettings.MinecraftLighting = valid.MinecraftLighting;
            GlobalSettings.PreviewGameBrightness = valid.GameBrightness;
            GlobalSettings.PreviewLightLevel = valid.LightLevel;
        }
    }
}
