// 生成应用图标 icon.ico（多尺寸：16/32/48/64/128/256）
// 设计：蓝色圆角方块 + 白色对话气泡 + 青绿点缀，简洁现代
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

class IconGen
{
    static Bitmap Render(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            float s = size;
            float pad = s * 0.04f;
            float r = s * 0.22f;              // 圆角半径
            var rect = new RectangleF(pad, pad, s - pad * 2, s - pad * 2);

            // 主体渐变：飞书蓝 -> 亮蓝
            using (var grad = new LinearGradientBrush(rect,
                Color.FromArgb(37, 99, 235),      // #2563EB
                Color.FromArgb(56, 189, 248),     // #38BDF8
                45f))
            using (var path = RoundRect(rect, r))
            {
                g.FillPath(grad, path);
            }

            // 顶部柔光高光
            using (var hl = new LinearGradientBrush(
                new RectangleF(rect.X, rect.Y, rect.Width, rect.Height * 0.45f),
                Color.FromArgb(70, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255),
                LinearGradientMode.Vertical))
            using (var hp = RoundRect(new RectangleF(rect.X, rect.Y, rect.Width, rect.Height * 0.48f), r))
            {
                g.FillPath(hl, hp);
            }

            // 对话气泡（白色圆角矩形）
            float bw = s * 0.54f;
            float bh = s * 0.42f;
            float bx = (s - bw) / 2f;
            float by = s * 0.29f;
            var bubbleRect = new RectangleF(bx, by, bw, bh);
            float br = Math.Min(bh * 0.34f, bw * 0.28f);

            using (var white = new SolidBrush(Color.White))
            using (var bpath = RoundRect(bubbleRect, br))
            {
                g.FillPath(white, bpath);
            }

            // 气泡小尾巴（左下角三角）
            float tw = s * 0.10f;
            float th = s * 0.11f;
            var tail = new PointF[]
            {
                new PointF(bx + bw * 0.22f, by + bh - s * 0.005f),
                new PointF(bx + bw * 0.22f + tw, by + bh - s * 0.005f),
                new PointF(bx + bw * 0.22f + tw * 0.25f, by + bh + th),
            };
            using (var white = new SolidBrush(Color.White))
            {
                g.FillPolygon(white, tail);
            }

            // 气泡里的三条消息线（飞书蓝）
            using (var lineBrush = new SolidBrush(Color.FromArgb(37, 99, 235)))
            {
                float lw = bw * 0.52f;
                float lh = Math.Max(1.25f, s * 0.055f);
                float lx = bx + (bw - lw) / 2f;
                float gap = bh * 0.19f;
                float ly = by + bh * 0.30f;
                using (var lp = RoundRect(new RectangleF(lx, ly, lw, lh), lh / 2f))
                    g.FillPath(lineBrush, lp);
                using (var lp2 = RoundRect(new RectangleF(lx, ly + gap, lw * 0.68f, lh), lh / 2f))
                    g.FillPath(lineBrush, lp2);
            }

            // 右下角青色小圆点（在线状态点）
            float dr = s * 0.115f;
            var dotRect = new RectangleF(s - pad - dr * 1.75f, s - pad - dr * 1.75f, dr, dr);
            using (var ring = new SolidBrush(Color.FromArgb(37, 99, 235)))
            {
                var ringRect = new RectangleF(dotRect.X - s * 0.022f, dotRect.Y - s * 0.022f,
                    dotRect.Width + s * 0.044f, dotRect.Height + s * 0.044f);
                g.FillEllipse(ring, ringRect);
            }
            using (var dot = new SolidBrush(Color.FromArgb(52, 211, 153)))  // #34D399
            {
                g.FillEllipse(dot, dotRect);
            }
        }
        return bmp;
    }

    static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static byte[] PngBytes(Bitmap bmp)
    {
        using (var ms = new MemoryStream())
        {
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
    }

    // 写成经典 DIB(BMP) 格式：兼容性最好（PNG 压缩条目部分 .NET 版本读不出来）
    static byte[] DibBytes(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        using (var ms = new MemoryStream())
        using (var bw2 = new BinaryWriter(ms))
        {
            // BITMAPINFOHEADER
            bw2.Write((uint)40);          // biSize
            bw2.Write((int)w);            // biWidth
            bw2.Write((int)(h * 2));      // biHeight = XOR + AND
            bw2.Write((ushort)1);         // biPlanes
            bw2.Write((ushort)32);        // biBitCount
            bw2.Write((uint)0);           // biCompression = BI_RGB
            bw2.Write((uint)(w * h * 4)); // biSizeImage
            bw2.Write((int)0);            // biXPelsPerMeter
            bw2.Write((int)0);            // biYPelsPerMeter
            bw2.Write((uint)0);           // biClrUsed
            bw2.Write((uint)0);           // biClrImportant

            // XOR 位图：BGRA，自下而上
            for (int y = h - 1; y >= 0; y--)
            {
                for (int x = 0; x < w; x++)
                {
                    Color c = bmp.GetPixel(x, y);
                    // 透明像素把颜色清零，避免边缘发黑
                    byte a = c.A;
                    bw2.Write(c.B);
                    bw2.Write(c.G);
                    bw2.Write(c.R);
                    bw2.Write(a);
                }
            }

            // AND 掩码：1bpp，每行按 4 字节对齐，自下而上；全 0 表示不透明部分由 alpha 决定
            int maskRowBytes = ((w + 31) / 32) * 4;
            var maskRow = new byte[maskRowBytes];
            for (int y = h - 1; y >= 0; y--)
            {
                Array.Clear(maskRow, 0, maskRowBytes);
                for (int x = 0; x < w; x++)
                {
                    if (bmp.GetPixel(x, y).A < 128)
                        maskRow[x / 8] |= (byte)(0x80 >> (x % 8));
                }
                bw2.Write(maskRow);
            }
            bw2.Flush();
            return ms.ToArray();
        }
    }

    static int Main(string[] args)
    {
        string outPath = args.Length > 0 ? args[0] : "icon.ico";
        // 全部用 DIB（.NET 的 Icon 类对 PNG 条目读不出 128/256）。
        // 尺寸只保留 16~128：Windows 资源管理器/任务栏最大的实际渲染尺寸约 128，
        // 再加 256 会让 ico 多出 264KB，性价比太低。
        int[] sizes = new int[] { 16, 24, 32, 48, 64, 128 };
        var data = new List<byte[]>();
        foreach (int sz in sizes)
        {
            using (var b = Render(sz))
                data.Add(DibBytes(b));
        }

        using (var fs = new FileStream(outPath, FileMode.Create))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write((ushort)0);      // reserved
            bw.Write((ushort)1);      // type = icon
            bw.Write((ushort)sizes.Length);

            int offset = 6 + sizes.Length * 16;
            for (int i = 0; i < sizes.Length; i++)
            {
                int sz = sizes[i];
                bw.Write((byte)(sz >= 256 ? 0 : sz));   // width  (0 = 256)
                bw.Write((byte)(sz >= 256 ? 0 : sz));   // height (0 = 256)
                bw.Write((byte)0);                       // palette
                bw.Write((byte)0);                       // reserved
                bw.Write((ushort)1);                     // planes
                bw.Write((ushort)32);                    // bpp
                bw.Write((uint)data[i].Length);          // size
                bw.Write((uint)offset);                  // offset
                offset += data[i].Length;
            }
            foreach (var p in data) bw.Write(p);
        }

        Console.WriteLine("OK " + outPath);
        return 0;
    }
}