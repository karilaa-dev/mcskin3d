using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MCSkin3D;
using MCSkin3D.Controls;
using MCSkin3D.Languages;
using MCSkin3D.lemon42;
using MCSkin3D.Swatches;
using OpenTK.Graphics.OpenGL;
using Paril.OpenGL;

internal static partial class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static int failures, checks;
    private static string scratch;
    private static Editor editor;
    private static bool ran;
    private static Point strokeLocation, strokePixel;
    private static readonly object LogLock = new object();

    [STAThread]
    private static int Main(string[] args)
    {
        scratch = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? Path.Combine(Path.GetTempPath(), "MCSkin3D-Integration-" + Guid.NewGuid().ToString("N")));
        if (Directory.Exists(scratch)) throw new IOException("Integration output directory must be new: " + scratch);
        Directory.CreateDirectory(scratch);
        Skin.ModelChoicesDirectory = Path.Combine(scratch, "ModelChoices");
        Log("Artifacts: " + scratch);
        using var watchdog = new System.Threading.Timer(_ => { Log("FAIL: 90-second application watchdog expired (possible modal dialog or deadlock)."); Environment.Exit(124); }, null, 90000, Timeout.Infinite);
        try
        {
            foreach (var name in new[] { "Languages", "Models", "Swatches", "Overlays", "Skins" })
                CopyDirectory(Path.Combine(AppContext.BaseDirectory, name), Path.Combine(scratch, name));
            if (Directory.Exists(Path.Combine(AppContext.BaseDirectory, "Brushes"))) CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Brushes"), Path.Combine(scratch, "Brushes"));
            foreach (string file in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.png")) File.Copy(file, Path.Combine(scratch, Path.GetFileName(file)));
            File.WriteAllText(Path.Combine(scratch, "settings.ini"), "[GlobalSettings]\r\nAutoUpdate=False\r\nLanguageFile=en\r\nUseSquareColorPicker=True\r\n");
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => Error(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Error((Exception)e.ExceptionObject);
            var assembly = typeof(Editor).Assembly;
            var program = assembly.GetType("MCSkin3D.Program", true);
            if (args.Contains("--legacy-dpi"))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Log("Regression reproduction: legacy DPI initialization");
            }
            else program.GetMethod("ConfigureApplication", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            program.GetProperty("MainThread").SetValue(null, Thread.CurrentThread);
            program.GetField("Version").SetValue(null, new Paril.Components.Update.Version(assembly.GetName().Version.ToString()));
            program.GetField("ExceptionObserver", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).SetValue(null, (Action<Exception>)Error);
            var contextType = assembly.GetType("MCSkin3D.MCSkin3DAppContext", true);
            using var context = (ApplicationContext)Activator.CreateInstance(contextType, new object[] { scratch });
            editor = (Editor)contextType.GetField("Form").GetValue(context);
            Run("Startup splash artwork and credits", () =>
            {
                var splash = Field<Form>(context, "SplashForm");
                Check(splash.Visible && !editor.Visible, "Splash is visible before the editor opens");
                CheckSplashLayout(splash, "splash");
                splash.Scale(new SizeF(1.5f, 1.5f));
                CheckSplashLayout(splash, "splash-scaled");
            });
            using var timer = new System.Windows.Forms.Timer { Interval = 200 };
            timer.Tick += (_, _) =>
            {
                if (ran || !editor.Visible || editor.IsDisposed) return;
                ran = true;
                timer.Stop();
                using var keyboardState = new IsolatedKeyboardState();
                Run("Real splash/application startup", Startup);
                if (args.Contains("--benchmark-render")) Run("3D layer rendering performance", BenchmarkLayerRendering);
                else if (args.Contains("--render-quality")) Run("Minecraft colors, resolution and antialiasing", RenderQuality);
                else if (args.Contains("--first-person")) Run("First-person rendering, stable lighting controls, hands and painting", FirstPerson);
                else if (args.Contains("--viewport-controls")) ViewportControls();
                else
                {
                    if (args.Contains("--benchmark")) Run("Picking performance and deterministic results", BenchmarkPicking);
                    Run("Perspective, texture and hybrid OpenGL frames", Rendering);
                    Run("Texture pixel formats, reload and resource lifetime", () => { editor.Renderer.MakeCurrent(); TextureChecks.Run(Check); });
                    Run("All tabs, square/wheel and English localization", TabsAndLanguage);
                    ViewportControls();
                    Run("Pencil through viewport, undo, redo, save and reload", EditSaveReload);
                    Run("Eraser, eyedropper and fill through viewport", MoreEditingTools);
                    Run("Move edited skin preserves undo, texture and save destination", MoveEditedSkin);
                    Run("Tool selection, camera and model visibility", ToolsAndModel);
                    Run("Loaded swatches, selection and persistence", Swatches);
                    Run("Clone and import PNG into skin tree", ImportClone);
                    Run("One-time player model detection and manual overrides", PlayerModelSelection);
                    Run("Combined workspace painting, swaps, poses and global undo", CombinedWorkspace);
                    Run("Official cape rendering, pixel protection and visibility", OfficialCapePreview);
                    Run("3D skin layer rendering, editing, undo and preference", SkinLayers3D);
                    Run("Minecraft colors, resolution and antialiasing", RenderQuality);
                    Run("First-person rendering, stable lighting controls, hands and painting", FirstPerson);
                    // This check pumps native messages and can import the desktop's live
                    // modifier-key state; keep it after deterministic painting checks.
                    Run("Resized explorer arrow clicks", ExplorerArrows);
                    Run("Automatic updates do not block the editor", () => StartupUpdateChecks.Run(editor, Check));
                    Run("Update restart saves edits and honors cancellation", UpdateShutdown);
                }
                Run("Clean shutdown saves isolated preferences", () =>
                {
                    Call(editor, "PerformSaveAll");
                    editor.Close();
                    Check(editor.IsDisposed, "Main editor must dispose on close");
                    Check(File.ReadAllText(Path.Combine(scratch, "settings.ini")).Contains("UseSquareColorPicker=True"), "Settings saved to isolated directory");
                });
                context.ExitThread();
            };
            timer.Start();
            Application.Run(context);
            if (!ran) { failures++; Log("FAIL: application exited before main editor became visible."); }
        }
        catch (Exception ex) { Error(ex); }
        Log($"{checks} assertions, {failures} failures. Artifacts: {scratch}");
        return failures == 0 ? 0 : 1;
    }

    private static void BenchmarkPicking()
    {
        const int iterations = 20000;
        foreach (ViewMode mode in Enum.GetValues<ViewMode>())
        {
            Call(editor, "SetViewMode", mode);
            editor.PerformResetCamera();
            for (int pose = 0; pose < 2; ++pose)
            {
                if (pose == 1) editor.RotateView(new Point(29, 17), 1);
                long checksum = 17;
                int hits = 0;
                for (int i = 0; i < 1000; ++i)
                    editor.GetPick(i * 37 % editor.Renderer.Width, i * 19 % editor.Renderer.Height, out _);
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                for (int i = 0; i < iterations; ++i)
                {
                    bool hit = editor.GetPick(i * 37 % editor.Renderer.Width, i * 19 % editor.Renderer.Height, out var pixel);
                    if (hit) hits++;
                    checksum = unchecked(checksum * 31 + pixel.X * 397 + pixel.Y + (hit ? 1 : 0));
                }
                double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                Log($"BENCH {mode} pose={pose} picks={iterations} elapsedMs={elapsed:F2} bytes={allocated} hits={hits} checksum={checksum}");
                Check(hits > 0 && hits < iterations, "Picking benchmark covers hits and misses");
                Check(allocated <= iterations * 8, "Picking avoids per-face heap allocations");
            }
        }
        editor.PerformResetCamera();
    }

    private static void CheckSplashLayout(Form splash, string name)
    {
        using var frame = new Bitmap(splash.ClientSize.Width, splash.ClientSize.Height);
        splash.DrawToBitmap(frame, splash.ClientRectangle);
        frame.Save(Path.Combine(scratch, name + ".png"));
        var artwork = (Bitmap)splash.BackgroundImage;
        Color expected = artwork.GetPixel(24, 80);
        Color actual = frame.GetPixel(24 * frame.Width / artwork.Width, 80 * frame.Height / artwork.Height);
        Check(Math.Abs(actual.B - expected.B) < 10 && Math.Abs(actual.R - expected.R) < 10, "Splash artwork fills the scaled window");
        var labels = splash.Controls.OfType<Label>().ToArray();
        Check(labels.Any(l => l.Text.Contains("karilaa-dev")), "Splash credits karilaa-dev");
        foreach (var label in labels)
        {
            var text = label.GetPreferredSize(Size.Empty);
            Check(label.Visible && splash.ClientRectangle.Contains(label.Bounds) && label.Width >= text.Width && label.Height >= text.Height,
                "Splash label fits at " + frame.Size + ": " + label.Text);
            Check(!labels.Any(other => other != label && other.Bounds.IntersectsWith(label.Bounds)), "Splash labels have separate space");
        }
    }

    private static void Startup()
    {
        Check(editor.Visible && editor.IsHandleCreated, "Real editor visible with HWND");
        Check(GlobalSettings.Loaded && !GlobalSettings.AutoUpdate, "Isolated settings loaded and updater disabled");
        Check(Path.GetFullPath(Environment.CurrentDirectory) == scratch, "All data paths isolated");
        Check(ModelLoader.Models.Count > 0, "Models loaded by splash worker");
        var tree = Field<TreeView>(editor, "treeView1");
        var skin = Nodes(tree.Nodes).OfType<Skin>().FirstOrDefault();
        Check(skin != null, "Sample skins loaded by splash worker");
        tree.SelectedNode = skin;
        Check(Editor.CurrentModel != null && skin.GLImage != null, "Skin selected and GPU texture created");
        editor.Renderer.MakeCurrent();
        Log("OpenGL: " + GL.GetString(StringName.Vendor) + " | " + GL.GetString(StringName.Renderer) + " | " + GL.GetString(StringName.Version));
        Log("Editor DPI: " + editor.DeviceDpi + "; DPI mode: " + Application.HighDpiMode);
        Check(editor.Renderer.IsHandleCreated && editor.Renderer.Visible, "Editor GL child visible with HWND");
    }

    private static void Rendering()
    {
        // First-person frames use a dedicated asymmetric-hand fixture below;
        // an arbitrary startup skin need not have eight colors on its one arm.
        foreach (ViewMode mode in new[] { ViewMode.Perspective, ViewMode.Orthographic, ViewMode.Hybrid })
        {
            Call(editor, "SetViewMode", mode);
            editor.Renderer.MakeCurrent();
            while (GL.GetError() != ErrorCode.NoError) { }
            SetField(editor, "_screenshotMode", true);
            try
            {
                Call(editor, "rendererControl_Paint", editor.Renderer, null);
                GL.Finish();
                int width = editor.Renderer.Width, height = editor.Renderer.Height;
                byte[] pixels = new byte[width * height * 4];
                GL.ReadBuffer(ReadBufferMode.Back);
                GL.ReadPixels(0, 0, width, height, OpenTK.Graphics.OpenGL.PixelFormat.Bgra, PixelType.UnsignedByte, pixels);
                Check(GL.GetError() == ErrorCode.NoError, mode + " rendering has no OpenGL errors");
                var colors = new HashSet<int>();
                for (int i = 0; i < pixels.Length; i += 4 * 31) colors.Add(BitConverter.ToInt32(pixels, i));
                Check(colors.Count > 8, mode + " framebuffer has visible scene detail");
                using var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, bitmap.PixelFormat);
                System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
                bitmap.UnlockBits(data);
                bitmap.RotateFlip(RotateFlipType.RotateNoneFlipY);
                bitmap.Save(Path.Combine(scratch, mode + ".png"));
            }
            finally { SetField(editor, "_screenshotMode", false); }
        }
    }

    private static void TabsAndLanguage()
    {
        foreach (var tabs in Controls(editor).OfType<TabControl>())
        {
            for (int i = 0; i < tabs.TabCount; ++i) { tabs.SelectedIndex = i; tabs.Refresh(); Check(tabs.SelectedTab.IsHandleCreated, tabs.Name + " tab " + i + " created"); }
            tabs.SelectedIndex = 0;
        }
        var panel = editor.ColorPanel;
        var toggle = Controls(panel).OfType<CheckBox>().Single(c => c.Name == "squarePickerToggle");
        var colorTabs = Controls(panel).OfType<TabControl>().Single();
        for (int i = 0; i < 2; ++i)
        {
            colorTabs.SelectedIndex = i;
            panel.Refresh();
            Check(toggle.Visible, "Square checkbox visible on color tab " + i);
            Check(toggle.Parent.ClientRectangle.Contains(toggle.Bounds), "Square checkbox fits color tab " + i);
            foreach (Control sibling in toggle.Parent.Controls)
                if (sibling != toggle && sibling.Visible) Check(!sibling.Bounds.IntersectsWith(toggle.Bounds), "Square checkbox must not overlap " + sibling.Name + " on color tab " + i);
        }
        colorTabs.SelectedIndex = 0;
        panel.SelectedColor = ColorManager.FromRGBA(31, 93, 177, 127);
        int initial = ((Color)panel.SelectedColor.RGB).ToArgb();
        foreach (bool square in new[] { false, true, false, true })
        {
            toggle.Checked = square;
            panel.Refresh();
            Check(GlobalSettings.UseSquareColorPicker == square, "Picker preference follows toggle");
            Check(((Color)panel.SelectedColor.RGB).ToArgb() == initial, "Picker switch preserves RGBA");
        }
        var english = LanguageLoader.FindLanguage("en");
        Check(english != null, "English language loaded from packaged file");
        english.Item.PerformClick();
        Check(Editor.CurrentLanguage == english, "English menu selection applied");
        Check(toggle.Text == Editor.GetLanguageString("C_SQUARE") && toggle.Text != "C_SQUARE", "Dynamic square label translated");
    }

    private static void EditSaveReload()
    {
        var skin = Field<Skin>(editor, "_lastSkin");
        Call(editor, "SetViewMode", ViewMode.Orthographic);
        Call(editor, "rendererControl_Paint", editor.Renderer, null);
        var pencil = Field<List<ToolIndex>>(editor, "_tools").Single(t => t.Tool is PencilTool);
        pencil.MenuItem.PerformClick();
        MCSkin3D.Brushes.BrushBox.SelectedItem = MCSkin3D.Brushes.BrushList.First(b => b.Width == 1 && b.Height == 1);
        editor.ColorPanel.SelectedColor = ColorManager.FromRGBA(19, 231, 87, 255);
        Point location = Point.Empty, pixel = Point.Empty;
        bool found = false;
        for (int y = 0; y < editor.Renderer.Height && !found; y += 2)
            for (int x = 0; x < editor.Renderer.Width && !found; x += 2)
                if (editor.GetPick(x, y, out var picked) && picked.X >= 8 && picked.X < 16 && picked.Y >= 8 && picked.Y < 16)
                { location = new Point(x, y); pixel = picked; found = true; }
        Check(found, "2D viewport hit testing finds head pixel");
        strokeLocation = location; strokePixel = pixel;
        int original = ReadPixel(skin, pixel);
        Call(editor, "rendererControl_MouseMove", editor.Renderer, new MouseEventArgs(MouseButtons.None, 0, location.X, location.Y, 0));
        var click = new MouseEventArgs(MouseButtons.Left, 1, location.X, location.Y, 0);
        Call(editor, "rendererControl_MouseDown", editor.Renderer, click);
        Call(editor, "rendererControl_MouseUp", editor.Renderer, click);
        int edited = ReadPixel(skin, pixel);
        Check(edited != original && skin.Dirty && editor.History.CanUndo, $"Real pencil stroke writes pixel and creates undo record (before={original:X8}, after={edited:X8}, dirty={skin.Dirty}, undo={editor.History.CanUndo}, modifiers={Control.ModifierKeys})");
        Field<ToolStripMenuItem>(editor, "undoToolStripMenuItem").PerformClick();
        Check(ReadPixel(skin, pixel) == original && editor.History.CanRedo, "Undo restores original GPU pixel");
        Field<ToolStripMenuItem>(editor, "redoToolStripMenuItem").PerformClick();
        Check(ReadPixel(skin, pixel) == edited, "Redo restores painted GPU pixel");
        Field<ToolStripMenuItem>(editor, "saveToolStripMenuItem").PerformClick();
        Check(!skin.Dirty, "Save clears dirty state");
        using (var disk = new Bitmap(skin.File.FullName)) Check(disk.GetPixel(pixel.X, pixel.Y).ToArgb() == Color.FromArgb(255, 19, 231, 87).ToArgb(), "Saved PNG contains exact painted pixel");
        var tree = Field<TreeView>(editor, "treeView1");
        tree.SelectedNode = Nodes(tree.Nodes).OfType<Skin>().First(s => s != skin);
        tree.SelectedNode = skin;
        Check(ReadPixel(skin, pixel) == edited, "Selecting another skin and returning reloads saved texture");
    }

    private static void MoreEditingTools()
    {
        var skin = Field<Skin>(editor, "_lastSkin");
        int original = ReadPixel(skin, strokePixel);
        StrokeWith<DropperTool>();
        Check(((Color)editor.ColorPanel.SelectedColor.RGB).ToArgb() == Color.FromArgb(255, 19, 231, 87).ToArgb(), "Eyedropper samples actual GPU pixel into picker");
        Check(ReadPixel(skin, strokePixel) == original, "Eyedropper preserves skin pixel");
        StrokeWith<EraserTool>();
        Check(ReadPixel(skin, strokePixel) == 0, "Eraser clears pixel alpha and RGB");
        Call(editor, "PerformUndo");
        Check(ReadPixel(skin, strokePixel) == original, "Undo restores erased pixel");
        editor.ColorPanel.SelectedColor = ColorManager.FromRGBA(235, 43, 127, 255);
        StrokeWith<FloodFillTool>();
        Check(ReadPixel(skin, strokePixel) != original, "Flood fill recolors target region");
        Call(editor, "PerformUndo");
        Check(ReadPixel(skin, strokePixel) == original, "Undo restores filled pixel");
        Call(editor, "PerformSave");
    }

    private static void StrokeWith<T>()
    {
        Field<List<ToolIndex>>(editor, "_tools").Single(t => t.Tool is T).MenuItem.PerformClick();
        Call(editor, "rendererControl_MouseMove", editor.Renderer, new MouseEventArgs(MouseButtons.None, 0, strokeLocation.X, strokeLocation.Y, 0));
        var click = new MouseEventArgs(MouseButtons.Left, 1, strokeLocation.X, strokeLocation.Y, 0);
        Call(editor, "rendererControl_MouseDown", editor.Renderer, click);
        Call(editor, "rendererControl_MouseUp", editor.Renderer, click);
    }

    private static void MoveEditedSkin()
    {
        var skin = Field<Skin>(editor, "_lastSkin");
        var tree = Field<SkinTreeView>(editor, "treeView1");
        var oldRightClicked = Field<TreeNode>(editor, "_rightClickedNode");
        var menu = Field<ToolStripMenuItem>(editor, "moveToToolStripMenuItem");
        SetField(editor, "_rightClickedNode", null);
        Call(editor, "contextMenuStrip1_Opening", null, new System.ComponentModel.CancelEventArgs());
        Check(!menu.Enabled, "Move is disabled on empty space");
        SetField(editor, "_rightClickedNode", skin.Parent ?? new FolderNode("folder"));
        Call(editor, "contextMenuStrip1_Opening", null, new System.ComponentModel.CancelEventArgs());
        Check(!menu.Enabled, "Move is disabled on folders");
        SetField(editor, "_rightClickedNode", skin);
        Call(editor, "contextMenuStrip1_Opening", null, new System.ComponentModel.CancelEventArgs());
        Check(menu.Enabled && menu.Text == "Move to...", "Skin context menu exposes localized move command");

        bool opened = false;
        using (var closeDialog = new System.Windows.Forms.Timer { Interval = 100 })
        {
            closeDialog.Tick += (_, _) =>
            {
                var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Name == "moveSkinDialog");
                if (dialog == null) return;
                opened = true;
                closeDialog.Stop();
                ((Button)dialog.Controls.Find("cancelButton", true).Single()).PerformClick();
            };
            closeDialog.Start();
            menu.PerformClick();
        }
        Check(opened && tree.SelectedNode == skin, "Context command opens cancellable browser without changing skin");

        int original = ReadPixel(skin, strokePixel);
        editor.ColorPanel.SelectedColor = ColorManager.FromRGBA(82, 141, 239, 255);
        StrokeWith<PencilTool>();
        int edited = ReadPixel(skin, strokePixel);
        Check(edited != original && skin.Dirty && editor.History.CanUndo, "Skin has unsaved paint before move");
        var undo = editor.History;
        var texture = skin.GLImage;
        string source = skin.File.FullName;
        byte[] diskBefore = File.ReadAllBytes(source);
        var session = new MCSkin3D.Services.SkinMoveSession(tree, skin);
        string destination = session.CreateFolder(session.RootPath, "Moved by integration");
        session.Move(destination, "Renamed edited skin");
        Check(!File.Exists(source) && skin.File.Exists && File.ReadAllBytes(skin.File.FullName).SequenceEqual(diskBefore), "Move preserves saved bytes until user saves");
        Check(tree.SelectedNode == skin && Field<Skin>(editor, "_lastSkin") == skin && skin.Dirty && ReferenceEquals(editor.History, undo) && ReferenceEquals(skin.GLImage, texture), "Move retains selection, dirty state, undo and loaded texture");
        Check(ReadPixel(skin, strokePixel) == edited, "Unsaved GPU pixels survive move");
        Call(editor, "PerformUndo");
        Check(ReadPixel(skin, strokePixel) == original && editor.History.CanRedo, "Undo still restores pixel after move");
        Call(editor, "PerformRedo");
        Check(ReadPixel(skin, strokePixel) == edited, "Redo still restores edited pixel after move");
        Call(editor, "PerformSave");
        Check(!skin.Dirty && !File.Exists(source), "Save writes only the new location");
        using (var png = new Bitmap(skin.File.FullName))
            Check(png.GetPixel(strokePixel.X, strokePixel.Y).ToArgb() == Color.FromArgb(255, 82, 141, 239).ToArgb(), "Moved PNG contains exact edited pixel after save");
        tree.SelectedNode = Nodes(tree.Nodes).OfType<Skin>().First(s => s != skin);
        tree.SelectedNode = skin;
        Check(ReadPixel(skin, strokePixel) == edited, "Moved skin reloads from new filename");
        SetField(editor, "_rightClickedNode", oldRightClicked);
    }

    private static void ToolsAndModel()
    {
        foreach (var tool in Field<List<ToolIndex>>(editor, "_tools"))
        { tool.MenuItem.PerformClick(); Check(editor.SelectedTool == tool, "Select tool " + tool.Name); }
        var head = Field<ToolStripMenuItem>(editor, "headToolStripMenuItem");
        bool original = head.Checked;
        head.PerformClick(); Check(head.Checked != original, "Head visibility toggles");
        head.PerformClick(); Check(head.Checked == original, "Head visibility restores");
        editor.RotateView(new Point(15, 7), 1);
        editor.ScaleView(new Point(0, 2), 1);
        editor.PerformResetCamera();
        Call(editor, "rendererControl_Paint", editor.Renderer, null);
        Check(!Editor.IsRendering, "Camera operations complete another frame");
    }

    private static void Swatches()
    {
        Check(SwatchLoader.Swatches.Count > 0, "Packaged swatches loaded");
        var container = editor.ColorPanel.SwatchContainer;
        var displayer = container.SwatchDisplayer;
        var swatch = SwatchLoader.Swatches.First(s => s.Count > 0);
        displayer.Swatch = swatch;
        displayer.PrimaryColorIndex = 0;
        Check(displayer.PrimaryColor == swatch[0].Color, "Swatch color selection");
        container.ToggleEditMode();
        editor.ColorPanel.SelectedColor = ColorManager.FromRGBA(45, 123, 210, 255);
        Check(displayer.PrimaryColor == Color.FromArgb(45, 123, 210), "Picker edits selected swatch color");
        container.ToggleEditMode();
        container.ZoomIn(); container.ZoomOut();
        container.SaveSwatches();
        Check(Directory.EnumerateFiles(Path.Combine(scratch, "Swatches")).Any(), "Swatches persist in isolated fixture");
        var reloaded = (ISwatch)Activator.CreateInstance(swatch.GetType(), new object[] { swatch.FilePath });
        reloaded.Load();
        Check(reloaded[0].Color.ToArgb() == swatch[0].Color.ToArgb(), "Saved swatch color reloads exactly");
    }

    private static void ImportClone()
    {
        var tree = Field<TreeView>(editor, "treeView1");
        var skin = Field<Skin>(editor, "_lastSkin");
        var beforeSkins = Nodes(tree.Nodes).OfType<Skin>().ToArray();
        editor.PerformCloneSkin();
        Check(Nodes(tree.Nodes).OfType<Skin>().Count() == beforeSkins.Length + 1, "Clone adds a skin tree item");
        var clone = Nodes(tree.Nodes).OfType<Skin>().Single(s => !beforeSkins.Contains(s));
        Check(clone != skin && clone.File.Exists, "Clone PNG created");
        tree.SelectedNode = clone;
        Check(Field<Skin>(editor, "_lastSkin") == clone && clone.GLImage != null, "Cloned skin loads into editor");
        using var original = new Bitmap(skin.File.FullName);
        using var copied = new Bitmap(clone.File.FullName);
        Check(original.Size == copied.Size && original.GetPixel(8, 8) == copied.GetPixel(8, 8), "Cloned PNG preserves dimensions and pixels");
        string importPath = Path.Combine(scratch, "integration-import.png");
        File.Copy(skin.File.FullName, importPath);
        int count = Nodes(tree.Nodes).OfType<Skin>().Count();
        Call(editor, "ImportSkins", new[] { importPath }, skin);
        Check(Nodes(tree.Nodes).OfType<Skin>().Count() == count + 1, "PNG import adds tree item");
        var imported = Nodes(tree.Nodes).OfType<Skin>().Single(s => s.Name == "integration-import");
        Check(imported.File.Exists && imported.Model != null, "PNG import creates file and resolves model");
        tree.SelectedNode = imported;
        Check(imported.GLImage != null && Editor.CurrentModel == imported.Model, "Imported skin displays with GPU texture");
    }

    private static int ReadPixel(Skin skin, Point point)
    {
        editor.Renderer.MakeCurrent();
        using var grabber = new ColorGrabber(GlobalDirtiness.CurrentSkin, skin.Width, skin.Height);
        grabber.Load();
        return grabber[point.X, point.Y].RGBA;
    }
    private static void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("Missing packaged fixture: " + source);
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (string dir in Directory.EnumerateDirectories(source)) CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }
    private static IEnumerable<Control> Controls(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Controls(c)));
    private static IEnumerable<TreeNode> Nodes(TreeNodeCollection nodes) => nodes.Cast<TreeNode>().SelectMany(n => new[] { n }.Concat(Nodes(n.Nodes)));
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
    private static void Run(string name, Action action) { try { action(); Log("PASS " + name); } catch (Exception ex) { Error(new Exception(name, ex)); } }
    private static void Error(Exception ex) { Interlocked.Increment(ref failures); Log("FAIL " + ex); }
    private static void Log(string message) { lock (LogLock) { Console.WriteLine(message); File.AppendAllText(Path.Combine(scratch, "integration.log"), message + Environment.NewLine); } }
}
