using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace MutfakDizilim
{
    public class CircularProgressBar : Control
    {
        private int _value;
        [Browsable(true)]
        [DefaultValue(0)]
        public int Value
        {
            get => _value;
            set
            {
                int v = Math.Max(0, Math.Min(100, value));
                if (v != _value)
                {
                    _value = v;
                    Invalidate();
                }
            }
        }

        [Browsable(true)]
        [DefaultValue(12)]
        public int Thickness { get; set; } = 12;

        [Browsable(true)]
        public Color TrackColor { get; set; } = Color.Gainsboro;

        [Browsable(true)]
        public Color ProgressColor { get; set; } = Color.DodgerBlue;

        [Browsable(true)]
        public Color TextColor { get; set; } = Color.Black;

        [Browsable(true)]
        [DefaultValue(true)]
        public bool ShowPercentage { get; set; } = true;

        public CircularProgressBar()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            Size = new Size(90, 90);
            Font = new Font("Segoe UI", 11, FontStyle.Bold);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            int pad = Thickness / 2 + 2;
            var rect = new Rectangle(pad, pad, Width - pad * 2, Height - pad * 2);

            using (var backPen = new Pen(TrackColor, Thickness))
                g.DrawArc(backPen, rect, -90, 360);

            using (var progPen = new Pen(ProgressColor, Thickness))
                g.DrawArc(progPen, rect, -90, (float)(360.0 * Value / 100.0));

            if (ShowPercentage)
            {
                string txt = Value + "%";
                var sz = g.MeasureString(txt, Font);
                using (var br = new SolidBrush(TextColor))
                {
                    g.DrawString(txt, Font, br,
                        (Width - sz.Width) / 2f,
                        (Height - sz.Height) / 2f);
                }
            }
        }
    }
}
