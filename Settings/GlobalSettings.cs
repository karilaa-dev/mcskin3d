//
//    MCSkin3D, a 3d skin management studio for Minecraft
//    Copyright (C) 2013 Altered Softworks & MCSkin3D Team
//
//    This program is free software: you can redistribute it and/or modify
//    it under the terms of the GNU General Public License as published by
//    the Free Software Foundation, either version 3 of the License, or
//    (at your option) any later version.
//
//    This program is distributed in the hope that it will be useful,
//    but WITHOUT ANY WARRANTY; without even the implied warranty of
//    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//    GNU General Public License for more details.
//
//    You should have received a copy of the GNU General Public License
//    along with this program.  If not, see <http://www.gnu.org/licenses/>.
//

using System.ComponentModel;
using System.Drawing;
using System.IO;
using MCSkin3D.Macros;
using Paril.Settings;
using Paril.Settings.Serializers;

namespace MCSkin3D
{
	public static class GlobalSettings
	{
		private static Settings Settings;
		public static bool Loaded;

		[Savable]
		[DefaultValue(true)]
		public static bool UseSquareColorPicker { get; set; } = true;

		[Savable]
		[DefaultValue(true)]
		public static bool Grass { get; set; }

		[Savable]
		[DefaultValue(false)]
		public static bool Ghost { get; set; }

		[Savable]
		[DefaultValue(false)]
		public static bool SkinLayers3D { get; set; }

		[Savable, DefaultValue(1.15f)]
		public static float SkinLayerPixelSize { get; set; } = 1.15f;
		[Savable, DefaultValue(1.18f)]
		public static float SkinLayerHeadSize { get; set; } = 1.18f;
		[Savable, DefaultValue(1.05f)]
		public static float SkinLayerBodyWidth { get; set; } = 1.05f;
		[Savable, DefaultValue(true)]
		public static bool SkinLayerFastRender { get; set; } = true;
		[Savable, DefaultValue(14)]
		public static int SkinLayerDistance { get; set; } = 14;
		[Savable, DefaultValue(63)]
		public static int SkinLayerParts { get; set; } = 63;

		[Savable, DefaultValue(100)]
		public static int PreviewResolution { get; set; } = 100;
		[Savable, DefaultValue(true)]
		public static bool MinecraftLighting { get; set; } = true;
		[Savable, DefaultValue(100)]
		public static int PreviewGameBrightness { get; set; } = 100;
		// A distinct key keeps old percentage fill values from becoming world-light levels.
		[Savable, DefaultValue(8)]
		public static int PreviewLightLevel { get; set; } = 8;

		[Savable, DefaultValue(false)]
		public static bool FirstPersonLeftHand { get; set; }

		[Savable]
		[DefaultValue("")]
		public static string LastSkin { get; set; }

		[Savable]
		public static bool RememberMe { get; set; }

		[Savable]
		public static bool AutoLogin { get; set; }

		[Savable]
		[DefaultValue("")]
		public static string LastUsername { get; set; }

		[Savable]
		[DefaultValue("")]
		[TypeSerializer(typeof(PasswordSerializer), false)]
		public static string LastPassword { get; set; }

		[Savable]
		[DefaultValue(TransparencyMode.Helmet)]
		[TypeSerializer(typeof(EnumSerializer<TransparencyMode>), true)]
		public static TransparencyMode Transparency { get; set; }

		[Savable]
		[DefaultValue(true)]
		public static bool AlphaCheckerboard { get; set; }

		[Savable]
		[DefaultValue(true)]
		public static bool TextureOverlay { get; set; }

		[Savable]
		[DefaultValue("Dynamic")]
		public static string LastBackground { get; set; }

		[Savable]
		[DefaultValue("")]
		public static string ShortcutKeys { get; set; }

		[Savable]
		[DefaultValue("135 206 235 255")]
		[TypeSerializer(typeof(ColorSerializer), true)]
		public static Color BackgroundColor { get; set; }

		[Savable]
		[DefaultValue(true)]
		public static bool AutoUpdate { get; set; }

		[Savable]
		[DefaultValue(0)]
		public static int Multisamples { get; set; }

		[Savable]
		public static bool PencilIncremental { get; set; }

		[Savable]
		public static bool DodgeBurnIncremental { get; set; }

		[Savable]
		[DefaultValue(0.70f)]
		public static float DodgeBurnExposure { get; set; }

		[Savable]
		public static bool DarkenLightenIncremental { get; set; }

		[Savable]
		[DefaultValue(0.25f)]
		public static float DarkenLightenExposure { get; set; }

		[Savable]
		[DefaultValue(0.20f)]
		public static float NoiseSaturation { get; set; }

		[Savable]
		public static string LanguageFile { get; set; }

		[Savable]
		[DefaultValue(25)]
		public static int TreeViewHeight { get; set; }

		[Savable]
		[DefaultValue(0)]
		public static float FloodFillThreshold { get; set; }

		[Savable]
		public static bool ResChangeDontShowAgain { get; set; }

		[Savable]
		[DefaultValue(".\\Skins\\")]
		[TypeSerializer(typeof(StringArraySerializer), true)]
		public static string[] SkinDirectories { get; set; }

		[Savable]
		[DefaultValue("255 255 255 255")]
		[TypeSerializer(typeof(ColorSerializer), true)]
		public static Color DynamicOverlayLineColor { get; set; }

		[Savable]
		[DefaultValue("255 255 255 255")]
		[TypeSerializer(typeof(ColorSerializer), true)]
		public static Color DynamicOverlayTextColor { get; set; }

		[Savable]
		[DefaultValue(1)]
		public static int DynamicOverlayLineSize { get; set; }

		[Savable]
		[DefaultValue(1)]
		public static int DynamicOverlayTextSize { get; set; }

		[Savable]
		[DefaultValue("255 255 255 127")]
		[TypeSerializer(typeof(ColorSerializer), true)]
		public static Color DynamicOverlayGridColor { get; set; }

		[Savable]
		[DefaultValue(true)]
		public static bool InfiniteMouse { get; set; }

		[Savable]
		[DefaultValue(false)]
		public static bool RenderBenchmark { get; set; }

		[Savable]
		[DefaultValue(false)]
		public static bool GridEnabled { get; set; }

		[Savable]
		[DefaultValue(true)]
		public static bool UseTextureBases { get; set; }

		public static string GetDataURI(string fileOrFolder)
		{
			return Directory.GetCurrentDirectory() + '\\' + fileOrFolder;
		}
	
		public static void Load()
		{
			MacroHandler.RegisterMacro("DataLocation", ".\\");

			try
			{
				Settings = new Settings();
				Settings.Structures.Add(typeof(GlobalSettings));

				Settings.Load(GetDataURI("settings.ini"));

				for (var i = 0; i < SkinDirectories.Length; ++i)
				{
					var dir = SkinDirectories[i];

					if (!dir.EndsWith("\\") && !dir.EndsWith("/"))
						dir += "\\";

					SkinDirectories[i] = dir;
				}

				Loaded = true;
			}
			catch
			{
				Settings.LoadDefaults();
				Loaded = false;
			}

			MacroHandler.RegisterMacro("DefaultSkinFolder", MacroHandler.ReplaceMacros(SkinDirectories[0]));
		}

		public static void Save()
		{
			Settings.Save(GetDataURI("settings.ini"));
		}
	}
}
