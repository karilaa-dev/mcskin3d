using System;

namespace MCSkin3D
{
    internal static class MinecraftPreviewLighting
    {
        // Minecraft lightmap.fsh, specialized to Overworld noon:
        // sky level 0..15, block level 0, sky factor/color 1, no effects.
        // overworld.json supplies ambient_light_color #0a0a0a, even at level 0.
        // The requested in-game Brightness setting is 100% (gamma 1).
        // All RGB channels are equal under these conditions.
        public static float Daylight(int level, int gameBrightness = 100)
        {
            float normalized = Math.Clamp(level, 0, 15) / 15f;
            float curved = normalized / (4 - 3 * normalized);
            float color = Math.Clamp(curved + 10 / 255f, 0, 1);
            float inverse = 1 - color;
            float gammaCorrected = 1 - inverse * inverse * inverse * inverse;
            color += (gammaCorrected - color) * (Math.Clamp(gameBrightness, 0, 100) / 100f);
            // Minecraft stores its 16x16 lightmap in an RGBA8 render target.
            return MathF.Round(Math.Clamp(color, 0, 1) * 255) / 255;
        }
    }
}
