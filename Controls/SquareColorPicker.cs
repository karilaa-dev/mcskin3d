using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using MCSkin3D.lemon42;

namespace MCSkin3D.Controls
{
    /// <summary>A saturation/value square with a separate hue strip.</summary>
    public class SquareColorPicker : Control
    {
        private ColorManager.HSVColor _color = new ColorManager.HSVColor(0, 0, 100, 255);
        private bool _draggingHue;

        public SquareColorPicker()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.Selectable, true);
            TabStop = true;
            AccessibleName = "C_SQUAREPICKER";
            Size = new Size(147, 150);
        }

        public event EventHandler HSVChanged;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ColorManager.HSVColor CurrentHSV
        {
            get { return _color; }
            set
            {
                var next = new ColorManager.HSVColor((short)Math.Max(0, Math.Min(360, (int)value.H)),
                    (byte)Math.Min(100, (int)value.S), (byte)Math.Min(100, (int)value.V), value.A);
                if (_color.H == next.H && _color.S == next.S && _color.V == next.V && _color.A == next.A)
                    return;
                _color = next;
                Invalidate();
                if (HSVChanged != null)
                    HSVChanged(this, EventArgs.Empty);
            }
        }

        private Rectangle SquareBounds
        {
            get
            {
                int size = Math.Max(2, Math.Min(ClientSize.Width - 25, ClientSize.Height - 6));
                return new Rectangle(3, 3, size, size);
            }
        }

        private Rectangle HueBounds
        {
            get { var square = SquareBounds; return new Rectangle(square.Right + 7, square.Top, 12, square.Height); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (ClientSize.Width < 28 || ClientSize.Height < 8)
                return;
            var square = SquareBounds;
            var hue = HueBounds;
            var fullColor = ColorManager.FromHSVA(_color.H, 100, 100, 255).RGB.ToColor();
            using (var saturation = new LinearGradientBrush(square, Color.White, fullColor, 0f))
                e.Graphics.FillRectangle(saturation, square);
            using (var value = new LinearGradientBrush(square, Color.FromArgb(0, Color.Black), Color.Black, 90f))
                e.Graphics.FillRectangle(value, square);
            using (var spectrum = new LinearGradientBrush(hue, Color.Red, Color.Red, 90f))
            {
                spectrum.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta, Color.Red },
                    Positions = new[] { 0f, 1f / 6, 2f / 6, 3f / 6, 4f / 6, 5f / 6, 1f }
                };
                e.Graphics.FillRectangle(spectrum, hue);
            }
            e.Graphics.DrawRectangle(Pens.DimGray, square);
            e.Graphics.DrawRectangle(Pens.DimGray, hue);
            float x = square.Left + _color.S / 100f * (square.Width - 1);
            float y = square.Top + (100 - _color.V) / 100f * (square.Height - 1);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawEllipse(Pens.Black, x - 4, y - 4, 8, 8);
            e.Graphics.DrawEllipse(Pens.White, x - 3, y - 3, 6, 6);
            float hueY = hue.Top + _color.H / 360f * (hue.Height - 1);
            e.Graphics.DrawRectangle(Pens.Black, hue.Left - 2, hueY - 2, hue.Width + 3, 4);
            e.Graphics.DrawLine(Pens.White, hue.Left, hueY, hue.Right - 1, hueY);
            if (Focused)
                ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || (!SquareBounds.Contains(e.Location) && !HueBounds.Contains(e.Location)))
                return;
            Focus();
            _draggingHue = HueBounds.Contains(e.Location);
            Capture = true;
            UpdateFromPoint(e.Location);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Capture && (e.Button & MouseButtons.Left) != 0)
                UpdateFromPoint(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && Capture)
            {
                UpdateFromPoint(e.Location);
                Capture = false;
            }
        }

        private static int Percentage(int position, int length, int maximum)
        {
            return (int)Math.Round(Math.Max(0, Math.Min(length - 1, position)) * (double)maximum / (length - 1));
        }

        private void UpdateFromPoint(Point point)
        {
            var square = SquareBounds;
            var hue = HueBounds;
            CurrentHSV = _draggingHue
                ? new ColorManager.HSVColor((short)Percentage(point.Y - hue.Top, hue.Height, 360), _color.S, _color.V, _color.A)
                : new ColorManager.HSVColor(_color.H, (byte)Percentage(point.X - square.Left, square.Width, 100),
                    (byte)(100 - Percentage(point.Y - square.Top, square.Height, 100)), _color.A);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            return key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode != Keys.Left && e.KeyCode != Keys.Right && e.KeyCode != Keys.Up && e.KeyCode != Keys.Down)
                return;
            int step = e.Shift ? 10 : 1;
            int direction = e.KeyCode == Keys.Left || e.KeyCode == Keys.Down ? -step : step;
            int h = _color.H, s = _color.S, v = _color.V;
            if (e.Control)
                h = (h + direction + 360) % 360;
            else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
                s = Math.Max(0, Math.Min(100, s + direction));
            else
                v = Math.Max(0, Math.Min(100, v + direction));
            CurrentHSV = new ColorManager.HSVColor((short)h, (byte)s, (byte)v, _color.A);
            e.Handled = e.SuppressKeyPress = true;
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }
}
