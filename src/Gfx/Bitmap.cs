namespace MiloBot.Gfx;

public class Bitmap
{
    public readonly int Width, Height;
    public readonly uint[] Pixels;

    public Bitmap(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new uint[width * height];
    }

    public static uint Premultiply(byte r, byte g, byte b, byte a)
    {
        if (a == 0) return 0;
        if (a < 255)
        {
            r = (byte)(r * a / 255);
            g = (byte)(g * a / 255);
            b = (byte)(b * a / 255);
        }
        return ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
    }

    public void Clear() => Array.Clear(Pixels);

    public void Draw(Bitmap src, int sx, int sy, int sw, int sh, int dx, int dy, int scale, bool flipX = false, double alpha = 1.0)
    {
        int a255 = (int)Math.Round(Math.Clamp(alpha, 0, 1) * 255);
        if (a255 <= 0) return;
        for (int y = 0; y < sh; y++)
        {
            int srow = (sy + y) * src.Width;
            for (int x = 0; x < sw; x++)
            {
                int sxx = flipX ? sx + sw - 1 - x : sx + x;
                uint p = src.Pixels[srow + sxx];
                if (p == 0) continue;
                if (a255 < 255) p = Fade(p, a255);
                int bx = dx + x * scale, by = dy + y * scale;
                for (int yy = 0; yy < scale; yy++)
                {
                    int ty = by + yy;
                    if (ty < 0 || ty >= Height) continue;
                    int row = ty * Width;
                    for (int xx = 0; xx < scale; xx++)
                    {
                        int tx = bx + xx;
                        if (tx < 0 || tx >= Width) continue;
                        Pixels[row + tx] = Over(p, Pixels[row + tx]);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Draws a sprite frame turned by quarter turns (clockwise) around the point (ax, ay) of the frame,
    /// which lands on (px, py) of this bitmap. Coordinates are in unscaled sprite pixels.
    /// </summary>
    public void DrawTurned(Bitmap src, int sx, int sy, int sw, int sh, double ax, double ay, double px, double py, int quarter, int scale, bool flipX)
    {
        for (int y = 0; y < sh; y++)
        {
            int srow = (sy + y) * src.Width;
            for (int x = 0; x < sw; x++)
            {
                uint p = src.Pixels[srow + (flipX ? sx + sw - 1 - x : sx + x)];
                if (p == 0) continue;
                var (rx, ry) = Turn(x + 0.5 - ax, y + 0.5 - ay, quarter);
                int bx = (int)Math.Floor(px + rx) * scale, by = (int)Math.Floor(py + ry) * scale;
                for (int yy = 0; yy < scale; yy++)
                {
                    int ty = by + yy;
                    if (ty < 0 || ty >= Height) continue;
                    int row = ty * Width;
                    for (int xx = 0; xx < scale; xx++)
                    {
                        int tx = bx + xx;
                        if (tx < 0 || tx >= Width) continue;
                        Pixels[row + tx] = Over(p, Pixels[row + tx]);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Draws a square sprite frame rotated by any angle around its centre, which lands on (cx, cy) in bitmap pixels.
    /// Nearest-neighbour, so it stays crisp pixel art. angle is in radians, clockwise.
    /// </summary>
    public void DrawRotated(Bitmap src, int sx, int sy, int size, double cx, double cy, double angle, int scale, double alpha = 1.0)
    {
        double cos = Math.Cos(angle), sin = Math.Sin(angle);
        double half = size / 2.0;
        int r = (int)Math.Ceiling(half * 1.415 * scale) + 1;
        int a255 = (int)Math.Round(Math.Clamp(alpha, 0, 1) * 255);
        for (int ty = Math.Max(0, (int)cy - r); ty < Math.Min(Height, (int)cy + r); ty++)
        {
            double dy = (ty + 0.5 - cy) / scale;
            int row = ty * Width;
            for (int tx = Math.Max(0, (int)cx - r); tx < Math.Min(Width, (int)cx + r); tx++)
            {
                double dx = (tx + 0.5 - cx) / scale;
                // inverse rotation back into the sprite
                double u = dx * cos + dy * sin + half, v = -dx * sin + dy * cos + half;
                if (u < 0 || v < 0 || u >= size || v >= size) continue;
                uint p = src.Pixels[(sy + (int)v) * src.Width + sx + (int)u];
                if (p == 0) continue;
                if (a255 < 255) p = Fade(p, a255);
                Pixels[row + tx] = Over(p, Pixels[row + tx]);
            }
        }
    }

    public static (double x, double y) Turn(double x, double y, int quarter) => (quarter & 3) switch
    {
        1 => (-y, x),
        2 => (-x, -y),
        3 => (y, -x),
        _ => (x, y)
    };

    static uint Fade(uint p, int a255)
    {
        uint a = (p >> 24) * (uint)a255 / 255;
        uint r = ((p >> 16) & 0xFF) * (uint)a255 / 255;
        uint g = ((p >> 8) & 0xFF) * (uint)a255 / 255;
        uint b = (p & 0xFF) * (uint)a255 / 255;
        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    static uint Over(uint src, uint dst)
    {
        uint sa = src >> 24;
        if (sa == 255 || dst == 0) return src;
        uint inv = 255 - sa;
        uint a = sa + ((dst >> 24) * inv / 255);
        uint r = ((src >> 16) & 0xFF) + (((dst >> 16) & 0xFF) * inv / 255);
        uint g = ((src >> 8) & 0xFF) + (((dst >> 8) & 0xFF) * inv / 255);
        uint b = (src & 0xFF) + ((dst & 0xFF) * inv / 255);
        return (a << 24) | (r << 16) | (g << 8) | b;
    }
}
