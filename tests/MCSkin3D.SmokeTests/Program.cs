using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MCSkin3D;
using MCSkin3D.Controls;
using MCSkin3D.lemon42;
using MCSkin3D.Models;
using MCSkin3D.Swatches;
using Paril.Imaging;
using OpenTK.GLControl;
using OpenTK.Windowing.Common;
using OpenTK.Graphics.OpenGL;

internal static class Program
{
    private static int failures;
    private static int assertions;
    private static int unavailable;
    private static readonly string Scratch = Path.Combine(Path.GetTempPath(), "MCSkin3D-Smoke-" + Guid.NewGuid().ToString("N"));

    [STAThread]
    private static int Main(string[] args)
    {
        if (Array.IndexOf(args, "--verify-release") >= 0)
            return LiveReleaseChecks.Run(args);
        if (Array.IndexOf(args, "--update-parent") >= 0)
        {
            string job = args[Array.IndexOf(args, "--update-parent") + 1];
            using var installationLock = new FileStream(Path.Combine(Path.GetDirectoryName(job), ".mcskin3d-update.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
            MCSkin3D.UpdateSystem.UpdateInstaller.PendingUpdate = new MCSkin3D.UpdateSystem.PreparedUpdate(job);
            MCSkin3D.UpdateSystem.UpdateInstaller.LaunchPendingUpdate();
            return 0;
        }
        Directory.CreateDirectory(Scratch);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.EnableVisualStyles();
        if (Array.IndexOf(args, "--move-dialog") >= 0)
        {
            SkinMoveChecks.ShowInteractive(Scratch);
            return 0;
        }
        using var watchdog = new System.Threading.Timer(_ => { Console.Error.WriteLine("Smoke checks stalled for 60 seconds."); Environment.Exit(124); }, null, 60000, Timeout.Infinite);
        Run("Square picker mouse positions, hue, alpha and capture", PickerMouse);
        Run("Square picker keyboard, bounds and change events", PickerKeyboard);
        Run("Square picker paints at normal and small sizes", PickerPaint);
        Run("Color panel primary/secondary, numeric, hex and picker synchronization", PanelSynchronization);
        Run("Swatch selection and editing synchronize with picker", SwatchSynchronization);
        Run("Square/wheel preference, hue/alpha and both color tabs stay synchronized", PickerToggle);
        Run("Model XML export/import and compressed TCN import", ModelRoundTrip);
        Run("Renderer geometry and reusable ghost color buffers", () => RendererChecks.Run(Check));
        Run("3D skin layer geometry, transparency, picking and cache", () => SkinLayerChecks.Run(Check, Scratch));
        Run("PNG pixels and skin metadata survive save/reload", PngRoundTrip);
        Run("Skin moves preserve files, selection and library boundaries", () => SkinMoveChecks.Run(Check, Scratch));
        Run("Minecraft skin download resolves current endpoints and validates responses offline", () => SkinDownloadChecks.Run(Check));
        Run("GitHub release discovery, editions and skipped versions", () => UpdateCheckerChecks.Run(Check));
        Run("Update dialog, skip persistence and cancellation", () => UpdateDialogChecks.Run(Check));
        Run("Update installation, file preservation, rollback and package validation", () => UpdateInstallerChecks.Run(Check, Scratch));
        Run("Editor-wide chronological history", () => WorkspaceChecks.History(Check));
        Run("Combined preview geometry, picking and native layout", () => WorkspaceChecks.GeometryAndPicking(Check));
        Run("First-person hand geometry, controls and preferences", () => FirstPersonChecks.Run(Check, Scratch));
        Run("Official cape assets, chronology, read-only protection and gallery", () => OfficialCapesChecks.Run(Check));
        if (Array.IndexOf(args, "--startup") >= 0)
            Run("OpenTK context and legacy rendering before editor becomes visible", HiddenOpenGlStartup);
        try { Directory.Delete(Scratch, true); }
        catch (IOException ex) { Console.Error.WriteLine("Temporary files retained: " + ex.Message); failures++; }
        Console.WriteLine($"{assertions} assertions; {failures} failed checks; {unavailable} unavailable checks.");
        return failures != 0 ? 1 : unavailable != 0 ? 2 : 0;
    }

    private static void Run(string name, Action action)
    {
        try { action(); Console.WriteLine("PASS " + name); }
        catch (CheckUnavailableException ex) { unavailable++; Console.Error.WriteLine("UNAVAILABLE " + name + ": " + ex.Message); }
        catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + ex); }
    }

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Hsv(SquareColorPicker picker, int h, int s, int v, int a)
    {
        var actual = picker.CurrentHSV;
        Check(actual.H == h && actual.S == s && actual.V == v && actual.A == a,
            $"Expected HSVA {h},{s},{v},{a}; got {actual.H},{actual.S},{actual.V},{actual.A}");
    }

    private static void PickerMouse()
    {
        using var picker = new TestPicker { Size = new Size(126, 107), CurrentHSV = new ColorManager.HSVColor(120, 50, 50, 77) };
        picker.CreateControl();
        picker.ClickAt(3, 3); Hsv(picker, 120, 0, 100, 77);
        picker.ClickAt(103, 3); Hsv(picker, 120, 100, 100, 77);
        picker.ClickAt(3, 103); Hsv(picker, 120, 0, 0, 77);
        picker.ClickAt(103, 103); Hsv(picker, 120, 100, 0, 77);
        picker.ClickAt(53, 53); Hsv(picker, 120, 50, 50, 77);
        picker.ClickAt(115, 3); Hsv(picker, 0, 50, 50, 77);
        picker.ClickAt(115, 53); Hsv(picker, 180, 50, 50, 77);
        picker.ClickAt(115, 103); Hsv(picker, 360, 50, 50, 77);
        picker.Down(53, 53);
        Check(picker.Capture, "Mouse down must capture the pointer");
        picker.MoveTo(-100, 999); Hsv(picker, 360, 0, 0, 77);
        picker.MoveTo(999, -100); Hsv(picker, 360, 100, 100, 77);
        picker.Up(999, -100);
        Check(!picker.Capture, "Mouse up must release capture");
        picker.Down(115, 53); picker.MoveTo(-100, 999); Hsv(picker, 360, 100, 100, 77);
        picker.Up(115, -99); Hsv(picker, 0, 100, 100, 77);
        picker.MoveTo(53, 53); Hsv(picker, 0, 100, 100, 77);
        picker.ClickAt(0, 0); Hsv(picker, 0, 100, 100, 77);
    }

    private static void PickerKeyboard()
    {
        using var picker = new TestPicker { CurrentHSV = new ColorManager.HSVColor(0, 50, 50, 39) };
        int events = 0;
        picker.HSVChanged += (_, _) => events++;
        picker.CurrentHSV = picker.CurrentHSV;
        Check(events == 0, "Unchanged assignment must not fire HSVChanged");
        picker.Key(Keys.Right); Hsv(picker, 0, 51, 50, 39);
        Check(events == 1, "One edit must raise one HSVChanged event");
        picker.Key(Keys.Left | Keys.Shift); Hsv(picker, 0, 41, 50, 39);
        picker.Key(Keys.Up); Hsv(picker, 0, 41, 51, 39);
        picker.Key(Keys.Down | Keys.Shift); Hsv(picker, 0, 41, 41, 39);
        picker.Key(Keys.Left | Keys.Control); Hsv(picker, 359, 41, 41, 39);
        picker.Key(Keys.Right | Keys.Control | Keys.Shift); Hsv(picker, 9, 41, 41, 39);
        picker.CurrentHSV = new ColorManager.HSVColor(-12, 255, 255, 39); Hsv(picker, 0, 100, 100, 39);
        picker.Key(Keys.Up); picker.Key(Keys.Right); Hsv(picker, 0, 100, 100, 39);
        picker.CurrentHSV = new ColorManager.HSVColor(999, 0, 0, 39); Hsv(picker, 360, 0, 0, 39);
        picker.Key(Keys.Down); picker.Key(Keys.Left); Hsv(picker, 360, 0, 0, 39);
        Check(picker.ArrowIsInput(Keys.Right | Keys.Control), "Arrow keys must remain input keys");
    }

    private static void PickerPaint()
    {
        using var picker = new TestPicker { Size = new Size(147, 150), CurrentHSV = new ColorManager.HSVColor(120, 80, 70, 255) };
        using var bitmap = new Bitmap(147, 150);
        picker.DrawToBitmap(bitmap, new Rectangle(Point.Empty, picker.Size));
        var green = bitmap.GetPixel(115, 10);
        Check(green.G > green.R + 100 && green.G > green.B + 100, "Upper right must show selected green hue");
        var black = bitmap.GetPixel(60, 120);
        Check(black.R < 30 && black.G < 30 && black.B < 30, "Bottom of square must approach black");
        picker.Size = new Size(1, 1);
        picker.DrawToBitmap(bitmap, new Rectangle(0, 0, 1, 1));
    }

    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
    private static void Invoke(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, args);

    private static void PanelMatches(ColorPanel panel)
    {
        var color = panel.SelectedColor;
        Hsv(Field<SquareColorPicker>(panel, "colorPick1"), color.HSV.H, color.HSV.S, color.HSV.V, color.HSV.A);
        Check(Field<NumericUpDown>(panel, "redNumericUpDown").Value == color.RGB.R, "Red numeric mismatch");
        Check(Field<NumericUpDown>(panel, "alphaNumericUpDown").Value == color.RGB.A, "Alpha numeric mismatch");
    }

    private static void PanelSynchronization()
    {
        using var panel = new ColorPanel();
        panel.CreateControl();
        panel.SelectedColor = ColorManager.FromRGBA(22, 44, 66, 88); PanelMatches(panel);
        Check(panel.HexTextBox.Text == "162C4258", "Hex must use RRGGBBAA ordering");
        panel.UnselectedColor = ColorManager.FromRGBA(100, 200, 50, 19);
        panel.SwitchColors(); PanelMatches(panel);
        Check(panel.SelectedColor.RGB.A == 19 && panel.UnselectedColor.RGB.A == 88, "Switch must select secondary color");
        panel.HexTextBox.Text = "00FF007F"; PanelMatches(panel);
        Check(panel.SelectedColor.RGB.G == 255 && panel.SelectedColor.RGB.A == 127, "Hex edits must update selected color");
        Field<NumericUpDown>(panel, "alphaNumericUpDown").Value = 42; PanelMatches(panel);
        Field<SquareColorPicker>(panel, "colorPick1").CurrentHSV = new ColorManager.HSVColor(240, 100, 100, 42);
        PanelMatches(panel);
        Check(panel.HexTextBox.Text == "0000FF2A", "Picker edit must update hex and retain alpha");
        panel.SwitchColors(); PanelMatches(panel);
        Check(panel.HexTextBox.Text == "162C4258", "Editing secondary must preserve primary");
        var tabs = Field<TabControl>(panel, "colorTabControl");
        tabs.CreateControl();
        tabs.SelectedIndex = 1;
        Check(Field<SquareColorPicker>(panel, "colorPick1").Parent == tabs.SelectedTab.Controls[0], "Picker must move into HSV tab");
        tabs.SelectedIndex = 0; PanelMatches(panel);
    }

    private static void SwatchSynchronization()
    {
        using var panel = new ColorPanel();
        Invoke(panel, "swatchContainer_SwatchChanged", panel.SwatchContainer, new SwatchChangedEventArgs(Color.FromArgb(81, 20, 40, 60), MouseButtons.Left));
        PanelMatches(panel);
        Check(panel.SelectedColor.RGB.A == 81, "Left swatch selection must update selected color");
        Invoke(panel, "swatchContainer_SwatchChanged", panel.SwatchContainer, new SwatchChangedEventArgs(Color.FromArgb(99, 90, 70, 50), MouseButtons.Right));
        Check(panel.UnselectedColor.RGB.A == 99, "Right swatch selection must update unselected color");
        var swatch = new MCSwatch(Path.Combine(Scratch, "test.swtch"));
        swatch.Add(new NamedColor("Primary", Color.Red));
        swatch.Add(new NamedColor("Secondary", Color.Blue));
        var displayer = panel.SwatchContainer.SwatchDisplayer;
        displayer.Swatch = swatch; displayer.PrimaryColorIndex = 0; displayer.SecondaryColorIndex = 1;
        Field<ToolStripButton>(panel.SwatchContainer, "editModeToolStripButton").Checked = true;
        panel.SelectedColor = ColorManager.FromRGBA(10, 20, 30, 40);
        Check(displayer.PrimaryColor.ToArgb() == Color.FromArgb(40, 10, 20, 30).ToArgb(), "Edit mode must update primary swatch");
        panel.SwitchColors();
        Field<SquareColorPicker>(panel, "colorPick1").CurrentHSV = new ColorManager.HSVColor(120, 100, 100, 99);
        Check(displayer.SecondaryColor.G == 255 && displayer.SecondaryColor.A == 99, "Picker must update secondary swatch in edit mode");
    }

    private static void ModelRoundTrip()
    {
        var model = new ModelLoader.ModelBase { textureWidth = 64, textureHeight = 64 };
        var box = new ModelLoader.ModelRenderer(model, 8, 16);
        box.addBox(-1.5f, -2, -3, 4, 5, 6, "Test cube");
        box.setRotationPoint(1.5f, 2.5f, 3.5f);
        box.rotateAngleY = 0.5f;
        string xml = Path.Combine(Scratch, "model.xml");
        var culture = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU"); model.Save("Smoke cube", 1, xml, null); }
        finally { CultureInfo.CurrentCulture = culture; }
        var parser = new ModelFormatTCN();
        var imported = parser.Load(xml);
        Check(imported.Name == "Smoke cube" && imported.DefaultWidth == 64 && imported.DefaultHeight == 64, "Model identity and texture size must survive XML round trip");
        Check(imported.Meshes.Count == 1 && imported.Meshes[0].Faces.Count == 6, "Cube geometry must contain six faces");
        Check(Math.Abs(imported.Meshes[0].Translate.X - 1.5f) < 0.001, "Decimal coordinates must survive Russian culture export");
        string tcn = Path.Combine(Scratch, "model.tcn");
        using (var archive = ZipFile.Open(tcn, ZipArchiveMode.Create)) archive.CreateEntryFromFile(xml, "model.xml");
        var compressed = parser.Load(tcn);
        Check(compressed.Meshes.Count == imported.Meshes.Count && compressed.Name == imported.Name, "SharpZipLib TCN import must match plain XML");
        using var reopened = new FileStream(tcn, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Check(reopened.Length > 0, "TCN import must release its file handle");
    }

    private static void PickerToggle()
    {
        bool previousPreference = GlobalSettings.UseSquareColorPicker;
        try
        {
            GlobalSettings.UseSquareColorPicker = false;
            using var panel = new ColorPanel();
            panel.CreateControl();
            var toggle = Field<CheckBox>(panel, "_squarePickerToggle");
            var wheel = Field<ColorPick>(panel, "_wheelPicker");
            var square = Field<SquareColorPicker>(panel, "colorPick1");
            var tabs = Field<TabControl>(panel, "colorTabControl");
            tabs.CreateControl();
            Check(!toggle.Checked && wheel.Visible && !square.Visible, "Existing wheel preference must initialize the panel");
            panel.SelectedColor = ColorManager.FromHSVA(30, 70, 90, 113);
            foreach (int tab in new[] { 0, 1, 0 })
            {
                tabs.SelectedIndex = tab;
                toggle.Checked = false;
                Check(!GlobalSettings.UseSquareColorPicker && wheel.Visible && !square.Visible, "Wheel selection must persist and hide square on each tab");
                Check(wheel.Parent == tabs.SelectedTab.Controls[0] && toggle.Parent == wheel.Parent, "Wheel and preference control must follow selected color tab");
                wheel.CurrentHSV = new ColorManager.HSVColor((short)(120 + tab * 60), 100, 80, panel.SelectedColor.HSV.A);
                Check(panel.SelectedColor.HSV.H == 120 + tab * 60 && panel.SelectedColor.HSV.A == 113, "Wheel edit must update hue while preserving selected alpha");
                PanelMatches(panel);
                Field<NumericUpDown>(panel, "alphaNumericUpDown").Value = 57;
                Check(wheel.CurrentHSV.A == 57 && square.CurrentHSV.A == 57, "Alpha numeric edit must synchronize both pickers");
                int expectedColor = panel.SelectedColor.RGB.ToColor().ToArgb();
                toggle.Checked = true;
                Check(GlobalSettings.UseSquareColorPicker && square.Visible && !wheel.Visible, "Square selection must persist and hide wheel on each tab");
                Check(square.Parent == tabs.SelectedTab.Controls[0], "Square must follow selected color tab");
                Check(panel.SelectedColor.RGB.ToColor().ToArgb() == expectedColor, "Changing picker mode must preserve selected RGBA color");
                square.CurrentHSV = new ColorManager.HSVColor(240, 90, 75, 57);
                Check(wheel.CurrentHSV.H == 240 && wheel.CurrentHSV.S == 90 && wheel.CurrentHSV.V == 75 && wheel.CurrentHSV.A == 57,
                    "Square edit must update hidden wheel including hue and alpha");
                Field<NumericUpDown>(panel, "alphaNumericUpDown").Value = 113;
            }
            using var reopened = new ColorPanel();
            Check(Field<CheckBox>(reopened, "_squarePickerToggle").Checked, "A new panel must honor the updated square preference");
        }
        finally { GlobalSettings.UseSquareColorPicker = previousPreference; }
    }

    private static void PngRoundTrip()
    {
        string png = Path.Combine(Scratch, "skin.png");
        using (var bitmap = new Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            bitmap.SetPixel(5, 9, Color.FromArgb(71, 20, 90, 180));
            bitmap.Save(png, ImageFormat.Png);
        }
        PNGMetadata.WriteMetadata(png, new Dictionary<string, string> { { "Model", "Players/Steve" }, { "Test", "round-trip" } });
        var metadata = PNGMetadata.ReadMetadata(png);
        Check(metadata["Model"] == "Players/Steve" && metadata["Test"] == "round-trip", "PNG text metadata must survive reload");
        using var loaded = new Bitmap(png);
        Check(loaded.Width == 64 && loaded.Height == 64, "PNG size must survive reload");
        Check(loaded.GetPixel(5, 9).ToArgb() == Color.FromArgb(71, 20, 90, 180).ToArgb(), "PNG must preserve RGBA pixels");
    }

    private static unsafe void HiddenOpenGlStartup()
    {
        using var watchdog = new System.Threading.Timer(_ =>
        {
            Console.Error.WriteLine("FAIL OpenGL startup exceeded 20 seconds");
            Environment.Exit(2);
        }, null, TimeSpan.FromSeconds(20), Timeout.InfiniteTimeSpan);
        OpenTK.Windowing.Desktop.GLFWProvider.EnsureInitialized();
        if (OpenTK.Windowing.GraphicsLibraryFramework.GLFW.GetPrimaryMonitor() == null)
            throw new CheckUnavailableException("The Windows session has no active primary monitor; OpenGL startup requires an interactive desktop.");
        using var form = new Form { ShowInTaskbar = false, ClientSize = new Size(64, 64) };
        using var renderer = new GLControl(new GLControlSettings
        {
            API = ContextAPI.OpenGL,
            APIVersion = new Version(3, 3),
            Profile = ContextProfile.Compatability,
            Flags = ContextFlags.Default,
            NumberOfSamples = 0
        }) { Dock = DockStyle.Fill };
        form.Controls.Add(renderer);
        Check(!form.Visible && !renderer.IsHandleCreated, "Fixture must begin with a hidden form and no native renderer handle");
        // The editor performs these steps during splash initialization, before Show().
        _ = renderer.Handle;
        renderer.MakeCurrent();
        Check(renderer.IsHandleCreated && !form.Visible, "Context must initialize while editor remains hidden");
        string version = GL.GetString(StringName.Version);
        Check(!string.IsNullOrEmpty(version), "OpenGL version must be available before the form is shown");
        Console.WriteLine("OpenGL startup: " + version + " / " + GL.GetString(StringName.Renderer));

        // Use a texture framebuffer so this check does not depend on window visibility.
        int texture = GL.GenTexture();
        int framebuffer = GL.GenFramebuffer();
        try
        {
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 8, 8, 0,
                OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);
            Check(GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) == FramebufferErrorCode.FramebufferComplete, "Offscreen framebuffer must be complete");
            GL.Viewport(0, 0, 8, 8);
            GL.ClearColor(1, 0, 0, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            GL.MatrixMode(MatrixMode.Projection); GL.LoadIdentity();
            GL.MatrixMode(MatrixMode.Modelview); GL.LoadIdentity();
            GL.Color4(0f, 1f, 0f, 1f);
            GL.Begin(PrimitiveType.Quads);
            GL.Vertex2(-1f, -1f); GL.Vertex2(1f, -1f); GL.Vertex2(1f, 1f); GL.Vertex2(-1f, 1f);
            GL.End();
            var pixel = new byte[4];
            GL.ReadPixels(4, 4, 1, 1, OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
            Check(GL.GetError() == ErrorCode.NoError, "Legacy fixed-function commands must remain valid in compatibility context");
            Check(pixel[0] == 0 && pixel[1] == 255 && pixel[2] == 0 && pixel[3] == 255,
                "GPU readback must contain rendered green quad, not red clear color");
        }
        finally
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.DeleteFramebuffer(framebuffer);
            GL.DeleteTexture(texture);
        }
    }

    private sealed class TestPicker : SquareColorPicker
    {
        public void Down(int x, int y) => OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
        public void MoveTo(int x, int y) => OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, x, y, 0));
        public void Up(int x, int y) => OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
        public void ClickAt(int x, int y) { Down(x, y); Up(x, y); }
        public void Key(Keys keys) { var e = new KeyEventArgs(keys); OnKeyDown(e); Check(e.Handled && e.SuppressKeyPress, "Arrow edit must suppress standard key handling"); }
        public bool ArrowIsInput(Keys keys) => IsInputKey(keys);
    }

    private sealed class CheckUnavailableException : Exception
    {
        public CheckUnavailableException(string message) : base(message) { }
    }
}
