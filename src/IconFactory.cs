// 矢量图标工厂：GDI+ 代码绘制，替代 emoji，保证任意 DPI 下风格一致、可维护。
// （说明：WinForms/.NET4.0 无内置 SVG 渲染器，引入第三方库会破坏"单文件零依赖"，故采用等效的矢量绘制。）
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace AutoDateShift
{
    internal static class IconFactory
    {
        public static readonly Color Green = Color.FromArgb(0x2E, 0x8B, 0x57);
        public static readonly Color Red = Color.FromArgb(0xC0, 0x39, 0x2B);
        public static readonly Color Blue = Color.FromArgb(0x2F, 0x6F, 0xB2);
        public static readonly Color Gray = Color.FromArgb(0x77, 0x77, 0x77);
        public static readonly Color Dark = Color.FromArgb(0x33, 0x33, 0x33);
        public static readonly Color Orange = Color.FromArgb(0xD8, 0x86, 0x00);

        private static Bitmap New(int size)
        {
            return new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        }

        private static Graphics G(Bitmap b)
        {
            var g = Graphics.FromImage(b);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            return g;
        }

        public static Bitmap Play(int size, Color c)
        {
            var bmp = New(size);
            using (var g = G(bmp))
            using (var brush = new SolidBrush(c))
            {
                float m = size * 0.2f;
                g.FillPolygon(brush, new[]
                {
                    new PointF(m, m * 0.7f),
                    new PointF(m, size - m * 0.7f),
                    new PointF(size - m * 0.6f, size / 2f)
                });
            }
            return bmp;
        }

        public static Bitmap Stop(int size, Color c)
        {
            var bmp = New(size);
            using (var g = G(bmp))
            using (var brush = new SolidBrush(c))
            {
                float m = size * 0.2f;
                g.FillRectangle(brush, m, m, size - 2 * m, size - 2 * m);
            }
            return bmp;
        }

        public static Bitmap Plus(int size, Color c)
        {
            var bmp = New(size);
            using (var g = G(bmp))
            using (var pen = new Pen(c, size * 0.16f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(pen, size / 2f, size * 0.18f, size / 2f, size * 0.82f);
                g.DrawLine(pen, size * 0.18f, size / 2f, size * 0.82f, size / 2f);
            }
            return bmp;
        }

        public static Bitmap Trash(int size, Color c)
        {
            var bmp = New(size);
            using (var g = G(bmp))
            using (var pen = new Pen(c, Math.Max(1f, size * 0.09f)))
            {
                g.DrawLine(pen, size * 0.15f, size * 0.25f, size * 0.85f, size * 0.25f); // 盖
                g.DrawLine(pen, size * 0.4f, size * 0.15f, size * 0.6f, size * 0.15f);  // 把
                g.DrawLine(pen, size * 0.25f, size * 0.32f, size * 0.3f, size * 0.85f); // 左壁
                g.DrawLine(pen, size * 0.75f, size * 0.32f, size * 0.7f, size * 0.85f); // 右壁
                g.DrawLine(pen, size * 0.3f, size * 0.85f, size * 0.7f, size * 0.85f);  // 底
                g.DrawLine(pen, size * 0.44f, size * 0.42f, size * 0.44f, size * 0.72f);
                g.DrawLine(pen, size * 0.56f, size * 0.42f, size * 0.56f, size * 0.72f);
            }
            return bmp;
        }

        public static Bitmap Save(int size, Color c)
        {
            var bmp = New(size);
            using (var g = G(bmp))
            using (var brush = new SolidBrush(c))
            using (var white = new SolidBrush(Color.White))
            {
                float m = size * 0.14f;
                g.FillRectangle(brush, m, m, size - 2 * m, size - 2 * m);
                g.FillRectangle(white, size * 0.3f, m, size * 0.4f, size * 0.28f);
                g.FillRectangle(white, size * 0.28f, size * 0.55f, size * 0.44f, size * 0.31f);
            }
            return bmp;
        }

        public static Bitmap Folder(int size, Color c)
        {
            var bmp = New(size);
            using (var g = G(bmp))
            using (var brush = new SolidBrush(c))
            {
                g.FillRectangle(brush, size * 0.1f, size * 0.22f, size * 0.35f, size * 0.14f);
                g.FillRectangle(brush, size * 0.1f, size * 0.34f, size * 0.8f, size * 0.46f);
            }
            return bmp;
        }

        public static Bitmap Refresh(int size, Color c)
        {
            var bmp = New(size);
            using (var g = G(bmp))
            using (var pen = new Pen(c, size * 0.11f))
            using (var brush = new SolidBrush(c))
            {
                g.DrawArc(pen, size * 0.15f, size * 0.15f, size * 0.7f, size * 0.7f, 40, 300);
                g.FillPolygon(brush, new[]
                {
                    new PointF(size * 0.85f, size * 0.08f),
                    new PointF(size * 0.92f, size * 0.42f),
                    new PointF(size * 0.58f, size * 0.3f)
                });
            }
            return bmp;
        }

        /// <summary>向下箭头（导入）</summary>
        public static Bitmap ArrowIn(int size, Color c)
        {
            var bmp = New(size);
            using (var g = G(bmp))
            using (var pen = new Pen(c, size * 0.11f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var brush = new SolidBrush(c))
            {
                g.DrawLine(pen, size / 2f, size * 0.12f, size / 2f, size * 0.6f);
                g.FillPolygon(brush, new[]
                {
                    new PointF(size * 0.26f, size * 0.48f),
                    new PointF(size * 0.74f, size * 0.48f),
                    new PointF(size / 2f, size * 0.8f)
                });
                g.DrawLine(pen, size * 0.16f, size * 0.9f, size * 0.84f, size * 0.9f);
            }
            return bmp;
        }

        /// <summary>向上箭头（导出）</summary>
        public static Bitmap ArrowOut(int size, Color c)
        {
            var bmp = New(size);
            using (var g = G(bmp))
            using (var pen = new Pen(c, size * 0.11f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var brush = new SolidBrush(c))
            {
                g.DrawLine(pen, size / 2f, size * 0.68f, size / 2f, size * 0.18f);
                g.FillPolygon(brush, new[]
                {
                    new PointF(size * 0.26f, size * 0.3f),
                    new PointF(size * 0.74f, size * 0.3f),
                    new PointF(size / 2f, size * 0.08f)
                });
                g.DrawLine(pen, size * 0.16f, size * 0.9f, size * 0.84f, size * 0.9f);
            }
            return bmp;
        }

        public static Bitmap Shield(int size, Color c)
        {
            var bmp = New(size);
            using (var g = G(bmp))
            using (var brush = new SolidBrush(c))
            {
                g.FillPolygon(brush, new[]
                {
                    new PointF(size * 0.5f, size * 0.08f),
                    new PointF(size * 0.88f, size * 0.22f),
                    new PointF(size * 0.88f, size * 0.52f),
                    new PointF(size * 0.5f, size * 0.92f),
                    new PointF(size * 0.12f, size * 0.52f),
                    new PointF(size * 0.12f, size * 0.22f)
                });
            }
            return bmp;
        }

        /// <summary>时钟图标（窗体与托盘用）</summary>
        public static Bitmap Clock(int size)
        {
            var bmp = New(size);
            using (var g = G(bmp))
            using (var ring = new Pen(Blue, size * 0.09f))
            using (var hand = new Pen(Dark, Math.Max(1f, size * 0.07f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float m = size * 0.1f;
                g.DrawEllipse(ring, m, m, size - 2 * m, size - 2 * m);
                g.DrawLine(hand, size / 2f, size / 2f, size / 2f, size * 0.28f);
                g.DrawLine(hand, size / 2f, size / 2f, size * 0.68f, size * 0.6f);
            }
            return bmp;
        }
    }
}
