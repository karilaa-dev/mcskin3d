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
			SplashForm = new Splash();
			SplashForm.Show();
			SplashForm.Update();
			try
			{
				GlobalSettings.Load();
				Updater = new UpdateSystem.UpdateChecker();
				Form = new Editor();
				Form.FormClosing += (sender, e) => GlobalSettings.Save();
				Form.FormClosed += (sender, e) => ExitThread();
				SplashForm.BeginLoaderThread();
			}
			catch
			{
				SplashForm.Dispose();
				throw;
			}
		}

		public void DoneLoadingSplash()
		{
			Form.Show();
		}
	}
}
