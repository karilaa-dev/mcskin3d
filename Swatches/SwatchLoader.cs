using System.Collections.Generic;
using System.IO;

namespace MCSkin3D.Swatches
{
	public class SwatchLoader
	{
		public static List<ISwatch> Swatches { get; private set; }

		private static void AddDirectory(string dir, List<ISwatch> swatches)
		{
			foreach (string swatchFile in Directory.EnumerateFiles(dir, "*"))
			{
				string ext = Path.GetExtension(swatchFile);

				ISwatch swatch = null;

				if (ext.ToLower() == ".swtch")
					swatch = new MCSwatch(swatchFile);
				else if (ext.ToLower() == ".gpl" || ext.ToLower() == ".gimp")
					swatch = new GIMPSwatch(swatchFile);
				else if (ext.ToLower() == ".act")
					swatch = new ACTSwatch(swatchFile);
				else if (ext.ToLower() == ".aco")
					swatch = new ACOSwatch(swatchFile);

				if (swatch != null)
				{
					swatches.Add(swatch);
					swatch.Load();
				}
			}
		}

		public static void LoadSwatches()
		{
			Swatches = new List<ISwatch>();
			string folder = GlobalSettings.GetDataURI("Swatches");
			if (!Directory.Exists(folder))
			{
				Directory.CreateDirectory(folder);
				using var input = typeof(SwatchLoader).Assembly.GetManifestResourceStream("FactoryPalette.swtch");
				string palette = Path.Combine(folder, "Quick Picks.swtch");
				try
				{
					using var output = new FileStream(palette, FileMode.CreateNew, FileAccess.Write, FileShare.None);
					input.CopyTo(output);
				}
				catch (IOException ex) when ((ex.HResult & 0xffff) == 80) { } // A second instance created it first.
			}
			AddDirectory(folder, Swatches);
		}

		public static void FinishedLoadingSwatches()
		{
			Editor.MainForm.ColorPanel.SwatchContainer.AddSwatches(Swatches);
			Editor.MainForm.ColorPanel.SwatchContainer.Enabled = true;
			Editor.MainForm.ColorPanel.SetLoading(false);
		}
	}
}
