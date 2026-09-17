using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using MCSkin3D.Languages;

namespace MCSkin3D.Forms
{
    internal sealed class OfficialCapeDialog : Form
    {
        private readonly ImageList thumbnails = new ImageList { ImageSize = new Size(72, 96), ColorDepth = ColorDepth.Depth32Bit };
        private readonly ListView list = new ListView { Dock = DockStyle.Fill, View = View.LargeIcon,
            MultiSelect = false, HideSelection = false, ShowItemToolTips = true };
        private readonly TextBox search = new TextBox { Dock = DockStyle.Fill };
        private readonly Button use = new Button { AutoSize = true, Enabled = false, DialogResult = DialogResult.OK };
        private readonly Label count = new Label { AutoSize = true, Margin = new Padding(4, 8, 16, 0) };

        public OfficialCape SelectedCape => list.SelectedItems.Count == 0 ? null : (OfficialCape)list.SelectedItems[0].Tag;
        private static string TextFor(string key) => LanguageLoader.GetStringWithFallback(key);

        public OfficialCapeDialog(OfficialCape selected = null)
        {
            Text = TextFor("W_OFFICIAL_CAPES");
            Name = "officialCapeDialog";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(720, 580);
            MinimumSize = new Size(400, 360);
            ShowInTaskbar = false;
            MinimizeBox = false;
            Padding = new Padding(12);
            AutoScaleMode = AutoScaleMode.Font;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label { Text = TextFor("W_OFFICIAL_CAPE_HELP"), AutoSize = true,
                Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) }, 0, 0);
            search.PlaceholderText = TextFor("W_SEARCH_CAPES");
            search.AccessibleName = TextFor("W_SEARCH_CAPES");
            search.Margin = new Padding(0, 0, 0, 10);
            layout.Controls.Add(search, 0, 1);
            list.Name = "officialCapeList";
            // Create the native list before adding temporary bitmaps so each image is copied immediately.
            _ = thumbnails.Handle;
            list.LargeImageList = thumbnails;
            list.Margin = new Padding(0);
            layout.Controls.Add(list, 0, 2);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
            var cancel = new Button { Text = TextFor("C_CANCEL"), AutoSize = true, DialogResult = DialogResult.Cancel };
            use.Text = TextFor("W_USE_CAPE");
            footer.Controls.Add(cancel); footer.Controls.Add(use); footer.Controls.Add(count);
            layout.Controls.Add(footer, 0, 3);
            Controls.Add(layout);
            AcceptButton = use; CancelButton = cancel;
            foreach (var cape in OfficialCapeCatalog.All)
            {
                using var texture = cape.OpenTexture();
                using var thumbnail = new Bitmap(72, 96);
                using (var graphics = Graphics.FromImage(thumbnail))
                {
                    graphics.Clear(Color.FromArgb(40, 43, 48));
                    graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                    graphics.PixelOffsetMode = PixelOffsetMode.Half;
                    float scale = texture.Width / 64f;
                    // The outward cape face, with the same orientation as the bundled cape model.
                    graphics.DrawImage(texture, new Rectangle(11, 8, 50, 80), new RectangleF(scale, scale, 10 * scale, 16 * scale), GraphicsUnit.Pixel);
                }
                thumbnails.Images.Add(cape.Id, thumbnail);
            }
            search.TextChanged += (_, _) => Filter();
            list.SelectedIndexChanged += (_, _) => use.Enabled = SelectedCape != null;
            list.ItemActivate += (_, _) => { if (SelectedCape != null) { DialogResult = DialogResult.OK; Close(); } };
            Filter();
            if (selected != null)
                foreach (ListViewItem item in list.Items)
                    if (item.Tag == selected) { item.Selected = true; item.EnsureVisible(); break; }
        }

        private void Filter()
        {
            var selected = SelectedCape;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var cape in OfficialCapeCatalog.All.Where(c => c.Name.Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                var item = new ListViewItem(cape.Name, cape.Id) { Tag = cape, ToolTipText = cape.Name + " — " + cape.ReleaseLabel + " — " + TextFor("W_READ_ONLY") };
                list.Items.Add(item);
                item.Selected = cape == selected;
            }
            list.EndUpdate();
            use.Enabled = SelectedCape != null;
            count.Text = list.Items.Count + " / " + OfficialCapeCatalog.All.Count;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { list.LargeImageList = null; thumbnails.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
