using System;
using System.Drawing;
using System.Windows.Forms;
using MCSkin3D.Languages;

namespace MCSkin3D.Forms
{
    internal sealed class SkinLayerSettingsDialog : Form
    {
        private readonly NumericUpDown pixels = Number("pixelSize", 1.001m, 1.4m, 3);
        private readonly NumericUpDown head = Number("headSize", 1.001m, 1.25m, 3);
        private readonly NumericUpDown body = Number("bodyWidth", 1.001m, 1.4m, 3);
        private readonly NumericUpDown distance = Number("renderDistance", 5, 40, 0);
        private bool fastRender;
        private readonly TrackBar resolution = Slider("renderResolution", 50, 200, 25);
        private readonly ComboBox antialiasing = new() { Name = "antialiasing", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly CheckBox lighting = new() { Name = "minecraftLighting", AutoSize = true };
        private readonly TrackBar gameBrightness = Slider("gameBrightness", 0, 100, 25);
        private int lightLevel;
        private readonly CheckBox[] parts = new CheckBox[6];
        public SkinLayerOptions Options => new((float)pixels.Value, (float)head.Value, (float)body.Value,
            fastRender, (int)distance.Value, PartMask());
        public PreviewRenderOptions Rendering => new(resolution.Value, new[] { 0, 2, 4, 8 }[antialiasing.SelectedIndex], lighting.Checked, gameBrightness.Value, lightLevel);
        private static string T(string key) => LanguageLoader.GetStringWithFallback(key);

        public SkinLayerSettingsDialog(SkinLayerOptions options, PreviewRenderOptions? rendering = null)
        {
            Text = T("SL_SETTINGS"); Name = "skinLayerSettingsDialog";
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            MinimizeBox = MaximizeBox = false; FormBorderStyle = FormBorderStyle.FixedDialog;
            AutoScaleMode = AutoScaleMode.Font; ClientSize = new Size(550, 630); Padding = new Padding(16);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 14 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            var help = new Label { Text = T("SL_HELP"), AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12) };
            layout.Controls.Add(help, 0, 0); layout.SetColumnSpan(help, 2);
            string[] labels = { "SL_PIXEL_SIZE", "SL_HEAD_SIZE", "SL_BODY_WIDTH", "SL_DISTANCE" };
            var controls = new[] { pixels, head, body, distance };
            for (int i = 0; i < controls.Length; i++)
            {
                controls[i].AccessibleName = T(labels[i]);
                layout.Controls.Add(new Label { Text = T(labels[i]), AutoSize = true, Anchor = AnchorStyles.Left }, 0, i + 1);
                layout.Controls.Add(controls[i], 1, i + 1);
            }
            var qualityHelp = new Label { Text = T("SL_RENDER_HELP"), AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 8) };
            layout.Controls.Add(qualityHelp, 0, 5); layout.SetColumnSpan(qualityHelp, 2);
            var resolutionRow = SliderRow(resolution, "SL_RESOLUTION", "%");
            layout.Controls.Add(resolutionRow, 0, 6); layout.SetColumnSpan(resolutionRow, 2);
            layout.Controls.Add(new Label { Text = T("SL_ANTIALIASING"), AutoSize = true, Anchor = AnchorStyles.Left }, 0, 7);
            antialiasing.AccessibleName = T("SL_ANTIALIASING");
            antialiasing.Items.AddRange(new object[] { T("SL_AA_OFF"), "2x MSAA", "4x MSAA", "8x MSAA" });
            layout.Controls.Add(antialiasing, 1, 7);
            lighting.Text = T("SL_LIGHTING"); lighting.Margin = new Padding(0, 14, 0, 2);
            layout.Controls.Add(lighting, 0, 8); layout.SetColumnSpan(lighting, 2);
            var gameBrightnessRow = SliderRow(gameBrightness, "SL_GAME_BRIGHTNESS", "%");
            layout.Controls.Add(gameBrightnessRow, 0, 9); layout.SetColumnSpan(gameBrightnessRow, 2);
            var brightnessHelp = new Label { Text = T("SL_BRIGHTNESS_LOCATION"), AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
            layout.Controls.Add(brightnessHelp, 0, 10); layout.SetColumnSpan(brightnessHelp, 2);
            lighting.CheckedChanged += (_, _) => gameBrightnessRow.Visible = brightnessHelp.Visible = lighting.Checked;
            var group = new GroupBox { Text = T("SL_PARTS"), Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10, 20, 10, 8) };
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            string[] names = { "SL_HAT", "SL_JACKET", "SL_LEFT_SLEEVE", "SL_RIGHT_SLEEVE", "SL_LEFT_PANTS", "SL_RIGHT_PANTS" };
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = new CheckBox { Name = "layerPart" + i, Text = T(names[i]), AutoSize = true };
                grid.Controls.Add(parts[i], i % 2, i / 2);
            }
            group.Controls.Add(grid); layout.Controls.Add(group, 0, 11); layout.SetColumnSpan(group, 2);
            for (int i = 0; i < 12; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var cancel = new Button { Text = T("C_CANCEL"), AutoSize = true, DialogResult = DialogResult.Cancel };
            var apply = new Button { Name = "applySettings", Text = T("SL_APPLY"), AutoSize = true, DialogResult = DialogResult.OK };
            var reset = new Button { Name = "resetDefaults", Text = T("SL_RESET"), AutoSize = true };
            reset.Click += (_, _) => { LoadOptions(SkinLayerOptions.Defaults); LoadRendering(PreviewRenderOptions.Defaults); };
            footer.Controls.Add(cancel); footer.Controls.Add(apply); footer.Controls.Add(reset);
            layout.Controls.Add(footer, 0, 13); layout.SetColumnSpan(footer, 2);
            Controls.Add(layout); AcceptButton = apply; CancelButton = cancel;
            LoadOptions(options);
            LoadRendering(rendering ?? PreviewRenderOptions.Current);
            brightnessHelp.Visible = lighting.Checked;
        }

        private void LoadOptions(SkinLayerOptions options)
        {
            options = options.Validated();
            pixels.Value = (decimal)options.PixelSize; head.Value = (decimal)options.HeadSize; body.Value = (decimal)options.BodyWidth;
            distance.Value = options.Distance; fastRender = options.FastRender;
            for (int i = 0; i < parts.Length; i++) parts[i].Checked = (options.Parts & (1 << i)) != 0;
        }
        private void LoadRendering(PreviewRenderOptions options)
        {
            options = options.Validated();
            resolution.Value = options.Resolution;
            antialiasing.SelectedIndex = Array.IndexOf(new[] { 0, 2, 4, 8 }, options.Samples);
            lighting.Checked = options.MinecraftLighting;
            gameBrightness.Value = options.GameBrightness;
            lightLevel = options.LightLevel;
            gameBrightness.Parent.Visible = lighting.Checked;
        }
        private static TrackBar Slider(string name, int min, int max, int ticks) =>
            new() { Name = name, Minimum = min, Maximum = max, TickFrequency = ticks, LargeChange = ticks,
                SmallChange = 1, AutoSize = false, Height = 35, Dock = DockStyle.Fill, Margin = new Padding(0) };
        private static Control SliderRow(TrackBar slider, string key, string suffix)
        {
            var row = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 5) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));
            var value = new Label { AutoSize = true, Anchor = AnchorStyles.Right, Text = slider.Value + suffix };
            slider.ValueChanged += (_, _) => value.Text = slider.Value + suffix;
            slider.AccessibleName = T(key);
            row.Controls.Add(new Label { Text = T(key), AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            row.Controls.Add(slider, 1, 0); row.Controls.Add(value, 2, 0);
            return row;
        }
        private int PartMask() { int mask = 0; for (int i = 0; i < parts.Length; i++) if (parts[i].Checked) mask |= 1 << i; return mask; }
        private static NumericUpDown Number(string name, decimal min, decimal max, int decimals) =>
            new() { Name = name, Minimum = min, Maximum = max, DecimalPlaces = decimals,
                Increment = decimals == 0 ? 1 : .001m, Dock = DockStyle.Fill, Margin = new Padding(3, 3, 0, 5) };
    }
}
