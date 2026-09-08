using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using MCSkin3D.Forms;

namespace MCSkin3D
{
	class MCSkin3DAppContext : ApplicationContext
	{
		public Form Form;
		public Splash SplashForm;
		public UpdateSystem.UpdateChecker Updater;

		public MCSkin3DAppContext(string dataDirectory = null)
		{
			Program.Context = this;
			Environment.CurrentDirectory = Path.GetFullPath(dataDirectory ?? AppContext.BaseDirectory);
			GlobalSettings.Load();

			try
			{
				if (Directory.Exists(GlobalSettings.GetDataURI("__updateFiles")))
					Directory.Delete(GlobalSettings.GetDataURI("__updateFiles"), true);
			}
			catch
			{
			}

			SplashForm = new Splash();
			Updater = new UpdateSystem.UpdateChecker();

			Form = new Editor();
			Form.FormClosing += (sender, e) => GlobalSettings.Save();
			Form.FormClosed += (sender, e) => ExitThread();

			SplashForm.Show();
		}

		public void DoneLoadingSplash()
		{
			Form.Show();
		}
	}
}
