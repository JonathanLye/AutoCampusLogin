using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CampusLogin.Core;

namespace CampusLogin.UI
{
    /// <summary>托盘状态图标在运行时用 GDI+ 画出来，不需要额外的图片资源。</summary>
    internal static class Icons
    {
        static readonly Dictionary<Color, Icon> Cache = new Dictionary<Color, Icon>();
        static Icon _app;

        static readonly Color Green = Color.FromArgb(22, 163, 74);
        static readonly Color Amber = Color.FromArgb(217, 119, 6);
        static readonly Color Gray = Color.FromArgb(107, 114, 128);
        static readonly Color Red = Color.FromArgb(220, 38, 38);

        public static Icon App
        {
            get
            {
                if (_app == null)
                {
                    using (var s = typeof(Icons).Assembly.GetManifestResourceStream("CampusLogin.app.ico"))
                        _app = new Icon(s);
                }
                return _app;
            }
        }

        public static Icon ForState(LoopState state)
        {
            switch (state)
            {
                case LoopState.Online: return Get(Green);
                case LoopState.LoggingIn:
                case LoopState.Unknown: return Get(Amber);
                case LoopState.NoCredential:
                case LoopState.AccountProblem:
                case LoopState.LoginError: return Get(Red);
                default: return Get(Gray);
            }
        }

        static Icon Get(Color color)
        {
            if (!Cache.TryGetValue(color, out Icon icon))
            {
                icon = Draw(color, SystemInformation.SmallIconSize.Width);
                Cache[color] = icon;
            }
            return icon;
        }

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);

        static Icon Draw(Color color, int size)
        {
            using (var bmp = new Bitmap(size, size))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    float s = size;
                    using (var bg = new SolidBrush(color))
                    using (var path = RoundedRect(0, 0, s - 1, s - 1, s * 0.22f))
                        g.FillPath(bg, path);

                    // Wi-Fi 标志：两道弧 + 圆点（16px 下三道弧太挤）
                    float cx = s * 0.5f, cy = s * 0.74f;
                    using (var pen = new Pen(Color.White, Math.Max(1.5f, s * 0.11f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        foreach (float r in new[] { s * 0.25f, s * 0.45f })
                            g.DrawArc(pen, cx - r, cy - r, 2 * r, 2 * r, 225, 90);
                    }
                    float d = s * 0.17f;
                    using (var white = new SolidBrush(Color.White))
                        g.FillEllipse(white, cx - d / 2, cy - d / 2, d, d);
                }
                IntPtr h = bmp.GetHicon();
                try
                {
                    return (Icon)Icon.FromHandle(h).Clone();
                }
                finally
                {
                    DestroyIcon(h);
                }
            }
        }

        static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
        {
            var p = new GraphicsPath();
            float d = r * 2;
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
