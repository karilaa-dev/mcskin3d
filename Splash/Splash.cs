using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MCSkin3D.Languages;
using MCSkin3D.Swatches;

namespace MCSkin3D.Forms
{
	public partial class Splash : Form
	{
		private Thread _loaderThread;
		private System.Windows.Forms.Timer _loadingTimer;
		private bool _finishedLoading;
		private volatile bool _closing;

		public Splash()
		{
			InitializeComponent();
			Disposed += (sender, e) => _closing = true;
		}

		public static string LoadingValue = "(nothing?)";

		public void SetLoadingString(string s)
		{
			if (_closing)
				throw new OperationCanceledException();

			LoadingValue = s;

			if (InvokeRequired)
			{
				Invoke((Action<string>)SetLoadingString, s);
				return;
			}

			label1.Text = s;
		}

		private void label1_Click(object sender, EventArgs e)
		{
		}

		Language LoadLanguages()
		{
			LanguageLoader.LoadLanguages(GlobalSettings.GetDataURI("Languages"));

			Language useLanguage = null;
			try
			{
				// stage 1 (prelim): if no language, see if our languages contain it
				if (string.IsNullOrEmpty(GlobalSettings.LanguageFile))
				{
					useLanguage =
						LanguageLoader.FindLanguage((CultureInfo.CurrentUICulture.IsNeutralCulture == false)
														? CultureInfo.CurrentUICulture.Parent.Name
														: CultureInfo.CurrentUICulture.Name);
				}

				// stage 2: load from last used language
				if (useLanguage == null)
					useLanguage = LanguageLoader.FindLanguage(GlobalSettings.LanguageFile);

				// stage 3: use English file, if it exists
				if (useLanguage == null)
					useLanguage = LanguageLoader.FindLanguage("English");
			}
			catch
			{
			}
			finally
			{
				// stage 4: fallback to built-in English file
				if (useLanguage == null)
				{
					RunOnUiThread(() => MessageBox.Show(this, "For some reason, the default language files were missing or failed to load (did you extract?) - we'll supply you with a base language of English just so you know what you're doing!"));
					useLanguage = LanguageLoader.LoadDefault();
				}
			}

			return useLanguage;
		}

		private void RunOnUiThread(Action action)
		{
			if (_closing)
				throw new OperationCanceledException();

			// Let failures propagate to the loader's single error boundary. Swallowing
			// them here would let later stages run against a partially initialized UI.
			Invoke(action);
		}

		void PerformLoading()
		{
			try
			{
				SetLoadingString("Loading Languages...");

				var language = LoadLanguages();

				SetLoadingString("Initializing base forms...");

				RunOnUiThread(() =>
					{
						Editor.MainForm.FinishedLoadingLanguages();
						Editor.MainForm.Initialize(language);
					});

				SetLoadingString("Loading swatches...");

				SwatchLoader.LoadSwatches();
				RunOnUiThread(SwatchLoader.FinishedLoadingSwatches);

				SetLoadingString("Loading models...");

				ModelLoader.LoadModels();
				RunOnUiThread(Editor.MainForm.FinishedLoadingModels);

				SetLoadingString("Loading skins...");

				SkinLoader.LoadSkins();

				SetLoadingString("Opening editor...");
				RunOnUiThread(() =>
					{
						Program.Context.DoneLoadingSplash();
						_finishedLoading = true;
						Close();
						if (GlobalSettings.AutoUpdate)
							_ = CheckForUpdatesAfterLoadingAsync(Program.Context.Form,
								Program.Context.Updater.CheckForUpdatesAsync, Editor.ShowUpdateDialog);
					});
			}
			catch (Exception) when (_closing)
			{
				// Closing the application can race a pending UI invocation. The
				// background loader must stop without reopening an error dialog.
			}
			catch (Exception ex)
			{
				try
				{
					RunOnUiThread(() =>
					{
						try
						{
							Program.RaiseException(new Exception("Failed to initialize program during \"" + LoadingValue + "\"", ex));
						}
						finally
						{
							Close();
							Application.Exit();
						}
					});
				}
				catch (Exception) when (_closing)
				{
					// The UI may have closed before the failure could be reported.
				}
			}
		}

		internal static async Task CheckForUpdatesAfterLoadingAsync(Form editor,
			Func<Task<bool>> checkForUpdates, Action<IWin32Window> showUpdateDialog)
		{
			if (editor.IsDisposed || editor.Disposing || !editor.Visible)
				return;

			// Called on the editor's UI thread after startup. Keep the UI context
			// while the network request is pending, so notification and close cannot race.
			try
			{
				if (await checkForUpdates() && !editor.IsDisposed && !editor.Disposing && editor.Visible)
					showUpdateDialog(editor);
			}
			catch (Exception ex)
			{
				if (!editor.IsDisposed && !editor.Disposing && editor.Visible)
					Program.RaiseException(ex);
			}
		}

		public static void BeginLoaderThread()
		{
			var splash = Program.Context.SplashForm;
			if (splash._closing || splash._loaderThread != null)
				return;

			splash._loaderThread = new Thread(splash.PerformLoading)
			{
				IsBackground = true,
				Name = "MCSkin3D startup loader"
			};
			splash._loaderThread.Start();
		}

		private void Splash_Load(object sender, EventArgs e)
		{
			label4.Text = Program.Name + " v" + Program.Version.ToString();
			SetLoadingString("Doing nothing yet...");

			components ??= new System.ComponentModel.Container();
			_loadingTimer = new System.Windows.Forms.Timer(components) { Interval = 100 };
			_loadingTimer.Tick += (s, ev) =>
			{
				_loadingTimer.Stop();
				_loadingTimer.Dispose();
				_loadingTimer = null;
				BeginLoaderThread();
			};
			_loadingTimer.Start();
		}

		protected override void OnFormClosed(FormClosedEventArgs e)
		{
			_closing = true;
			_loadingTimer?.Stop();
			base.OnFormClosed(e);
			if (!_finishedLoading)
				Application.ExitThread();
		}
	}
}
