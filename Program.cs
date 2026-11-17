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

using System;
using System.IO;
using System.Media;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using MCSkin3D.ExceptionHandler;
using MCSkin3D.Languages;
using Version = Paril.Components.Update.Version;

namespace MCSkin3D
{
	internal static class Program
	{
		public const string Name = "MCSkin3D LE";
		public static Version Version;
		public static MCSkin3DAppContext Context;
		internal static Action<Exception> ExceptionObserver = null;

		internal static void ConfigureApplication()
		{
			// Establish one DPI mode before WinForms or GLFW creates any windows.
			// Late GLFW DPI initialization otherwise makes SetParent fail for
			// controls whose HWNDs were created while the splash was loading.
			Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
		}

		public static Stream GetResourceStream(string name)
		{
			name = "MCSkin3D.Resources." + name;
			return Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
		}

		public static string GetResourceString(string name, Encoding encoding)
		{
			using (var stream = GetResourceStream(name))
			using (var br = new StreamReader(stream, encoding))
				return br.ReadToEnd();
		}

		public static string GetResourceString(string name)
		{
			return GetResourceString(name, Encoding.ASCII);
		}

		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		private static void Main(string[] args)
		{
			if (UpdateSystem.UpdateInstaller.TryRunHelper(args)) return;
			try
			{
				using var installationLock = UpdateSystem.UpdateInstaller.AcquireApplicationLock();
				MainCore();
			}
			catch (IOException ex)
			{
				MessageBox.Show("The application folder is unavailable or an update is still installing. Please try again.\n\n" + ex.Message,
					"MCSkin3D", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void MainCore()
		{
			MainThread = Thread.CurrentThread;

#if !DEBUG
			Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
			Application.ThreadException += Application_ThreadException;
			AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
#endif

			Version = new Version(Application.ProductVersion);

#if !DEBUG
			try
			{
#endif

#if CONVERT_MODELS
				Models.Convert.ConversionInterface.Convert();
#endif

				ConfigureApplication();
				Application.Run(new MCSkin3DAppContext());
				UpdateSystem.UpdateInstaller.LaunchPendingUpdate();
#if !DEBUG
			}
			catch (Exception ex)
			{
				RaiseException(ex);
			}
#endif
		}

		private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
		{
			RaiseException(e.ExceptionObject as Exception);
		}

		private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
		{
			RaiseException(e.Exception);
		}

		public static void RaiseException(Exception ex)
		{
			if (ExceptionObserver != null)
			{
				ExceptionObserver(ex);
				return;
			}
			try
			{
				var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MCSkin3D", "Logs");
				Directory.CreateDirectory(logDirectory);
				File.WriteAllText(Path.Combine(logDirectory, "last-error.log"), ex.ToString());
			}
			catch (IOException) { }
			catch (UnauthorizedAccessException) { }

			var raiseForm = Editor.MainForm?.IsHandleCreated == true ? (Form)Editor.MainForm : (Form)Program.Context?.SplashForm;
			if (raiseForm == null)
			{
				MessageBox.Show(ex.Message, "MCSkin3D", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			if (raiseForm.InvokeRequired)
			{
				raiseForm.Invoke((Action)delegate () { RaiseException(ex); });
				return;
			}

			var form = new ExceptionForm();
			form.Exception = ex;

			if (Editor.CurrentLanguage == null)
				Editor.CurrentLanguage = Language.Parse(new StreamReader(new MemoryStream(Properties.Resources.English)));

			SystemSounds.Asterisk.Play();

			if (Editor.MainForm?.Visible == true)
				form.ShowDialog(Editor.MainForm);
			else
				form.ShowDialog();
		}

		public static Thread MainThread { get; set; }
	}
}
