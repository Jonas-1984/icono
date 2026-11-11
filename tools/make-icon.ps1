<#
.SYNOPSIS
  Makes the program icon from a logo picture (PNG with a transparent background):
    src\Icono.App\Assets\icono.png  the wide logo shown in the About and start windows (1200 px wide)
    src\Icono.App\Assets\icono.ico  the Windows icon: the brush with the splash on a light plate at 16-64 px, the whole logo at 128 and 256 px.
.NOTES
  The brush is cut out of the logo with fixed pixel limits that fit the current logo (2172 x 724 px, brush on the left). A logo of another
  layout needs those limits (in Gen.Run, "the brush with the orange splash") adjusted.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1 -Source "C:\Users\me\Downloads\logo.png"
#>
param([Parameter(Mandatory = $true)][string]$Source)

Add-Type -ReferencedAssemblies System.Drawing @"
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;

public static class Gen
{
    static Bitmap ToArgb(Image src)
    {
        Bitmap b = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(b)) { g.DrawImage(src, 0, 0, src.Width, src.Height); }
        return b;
    }

    // the box round every pixel that is not (nearly) transparent
    static Rectangle BBox(Bitmap b, int threshold)
    {
        int minX = b.Width, minY = b.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
                if (b.GetPixel(x, y).A > threshold)
                {
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
        return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    static Bitmap Crop(Bitmap b, Rectangle r, int margin)
    {
        Bitmap o = new Bitmap(r.Width + 2 * margin, r.Height + 2 * margin, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(o))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(b, new Rectangle(margin, margin, r.Width, r.Height), r, GraphicsUnit.Pixel);
        }
        return o;
    }

    static Bitmap Scale(Bitmap b, int w, int h)
    {
        Bitmap o = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(o))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.DrawImage(b, new Rectangle(0, 0, w, h));
        }
        return o;
    }

    // a square picture of size x size with the picture centred and fitted into the given share of the square
    static Bitmap Square(Bitmap b, int size, double share, bool fitWidth)
    {
        double scale = fitWidth ? size * share / b.Width : size * share / b.Height;
        int w = Math.Max(1, (int)Math.Round(b.Width * scale));
        int h = Math.Max(1, (int)Math.Round(b.Height * scale));
        if (w > size) { h = (int)Math.Round(h * (double)size / w); w = size; }
        if (h > size) { w = (int)Math.Round(w * (double)size / h); h = size; }
        Bitmap o = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(o))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.DrawImage(b, new Rectangle((size - w) / 2, (size - h) / 2, w, h));
        }
        return o;
    }

    // removes every connected piece that is neither near the left edge (the brush) nor entirely in the top part (the splash)
    static void DropStrays(Bitmap b, int leftLimit, int topLimit)
    {
        int w = b.Width, h = b.Height;
        bool[] seen = new bool[w * h];
        for (int sy = 0; sy < h; sy++)
            for (int sx = 0; sx < w; sx++)
            {
                if (seen[sy * w + sx] || b.GetPixel(sx, sy).A <= 12) continue;
                List<int> pixels = new List<int>();
                Stack<int> stack = new Stack<int>();
                stack.Push(sy * w + sx); seen[sy * w + sx] = true;
                int minX = w, maxY = 0;
                while (stack.Count > 0)
                {
                    int p = stack.Pop(); pixels.Add(p);
                    int x = p % w, y = p / w;
                    if (x < minX) minX = x; if (y > maxY) maxY = y;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int q = ny * w + nx;
                            if (seen[q] || b.GetPixel(nx, ny).A <= 12) continue;
                            seen[q] = true; stack.Push(q);
                        }
                }
                if (minX >= leftLimit && maxY >= topLimit)
                    foreach (int p in pixels) b.SetPixel(p % w, p / w, Color.Transparent);
            }
    }

    // the picture on a light rounded plate (like a blueprint on paper): it reads on dark and light surfaces alike
    static Bitmap Plate(Bitmap art, int size)
    {
        Bitmap o = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(o))
        {
            g.SmoothingMode = SmoothingMode.HighQuality;
            float r = size * 0.20f;
            RectangleF box = new RectangleF(0.5f, 0.5f, size - 1f, size - 1f);
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(box.X, box.Y, 2 * r, 2 * r, 180, 90);
                path.AddArc(box.Right - 2 * r, box.Y, 2 * r, 2 * r, 270, 90);
                path.AddArc(box.Right - 2 * r, box.Bottom - 2 * r, 2 * r, 2 * r, 0, 90);
                path.AddArc(box.X, box.Bottom - 2 * r, 2 * r, 2 * r, 90, 90);
                path.CloseFigure();
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(247, 249, 253))) g.FillPath(fill, path);
                if (size >= 32) using (Pen edge = new Pen(Color.FromArgb(190, 200, 220), 1f)) g.DrawPath(edge, path);
            }
            g.DrawImage(art, 0, 0, size, size);
        }
        return o;
    }

    static byte[] Png(Bitmap b)
    {
        using (MemoryStream m = new MemoryStream()) { b.Save(m, ImageFormat.Png); return m.ToArray(); }
    }

    public static void Run(string src, string assets, string preview)
    {
        Bitmap all = ToArgb(Image.FromFile(src));
        Rectangle box = BBox(all, 12);
        int margin = (int)(all.Width * 0.01);
        Bitmap word = Crop(all, box, margin);

        // the brush with the orange splash: everything left of the "c"
        Bitmap mark = new Bitmap(all);
        for (int y = 0; y < mark.Height; y++)
            for (int x = 0; x < mark.Width; x++)
            {
                bool keep = (y < 272 && x < 560) || (y >= 272 && y < 335 && x < 450) || (y >= 335 && x < 412);
                if (!keep) mark.SetPixel(x, y, Color.Transparent);
            }
        Bitmap brush = Crop(mark, BBox(mark, 12), 0);
        DropStrays(brush, 110, 215);
        // faint ghost lines of the drawing next to the handle
        for (int y = 340; y < brush.Height; y++)
            for (int x = 130; x < brush.Width; x++)
                if (brush.GetPixel(x, y).A < 90) brush.SetPixel(x, y, Color.Transparent);
        brush = Crop(brush, BBox(brush, 12), 2);
        brush.Save(Path.Combine(preview, "brush.png"), ImageFormat.Png);

        // wide logo for the windows (about, splash): 1200 px wide is twice the size it is shown at
        Bitmap wide = Scale(word, 1200, (int)Math.Round(word.Height * 1200.0 / word.Width));
        wide.Save(Path.Combine(assets, "icono.png"), ImageFormat.Png);
        Console.WriteLine("logo " + wide.Width + "x" + wide.Height + ", brush " + brush.Width + "x" + brush.Height);

        // the icon file: the brush at the small sizes, the whole logo at the large ones
        int[] sizes = new int[] { 16, 24, 32, 48, 64, 128, 256 };
        List<byte[]> pngs = new List<byte[]>();
        foreach (int s in sizes)
        {
            Bitmap art = s <= 64 ? Square(brush, s, 0.84, false) : Square(word, s, 0.86, true);
            Bitmap pic = Plate(art, s);
            pic.Save(Path.Combine(preview, "ico-" + s + ".png"), ImageFormat.Png);
            pngs.Add(Png(pic));
        }

        using (FileStream f = new FileStream(Path.Combine(assets, "icono.ico"), FileMode.Create))
        using (BinaryWriter w = new BinaryWriter(f))
        {
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0); w.Write((ushort)1); w.Write((ushort)32);
                w.Write((uint)pngs[i].Length); w.Write((uint)offset);
                offset += pngs[i].Length;
            }
            foreach (byte[] p in pngs) w.Write(p);
        }
    }
}
"@
$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'src\Icono.App\Assets'
$preview = Join-Path $env:TEMP 'icono-icon-preview'
New-Item -ItemType Directory -Force $preview | Out-Null
[Gen]::Run($Source, $assets, $preview)
Write-Host "Written: $assets\icono.ico and icono.png (previews in $preview)"
