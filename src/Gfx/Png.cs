using System.IO.Compression;
using System.Text;

namespace MiloBot.Gfx;

public static class Png
{
    public static Bitmap Load(string path) => Decode(File.ReadAllBytes(path));

    public static Bitmap Decode(byte[] data)
    {
        int pos = 8;
        int width = 0, height = 0, colorType = 0, bitDepth = 0;
        var idat = new MemoryStream();
        while (pos < data.Length)
        {
            int len = ReadInt(data, pos);
            string type = Encoding.ASCII.GetString(data, pos + 4, 4);
            int body = pos + 8;
            if (type == "IHDR")
            {
                width = ReadInt(data, body);
                height = ReadInt(data, body + 4);
                bitDepth = data[body + 8];
                colorType = data[body + 9];
                if (data[body + 12] != 0) throw new NotSupportedException("interlaced png");
            }
            else if (type == "IDAT") idat.Write(data, body, len);
            else if (type == "IEND") break;
            pos = body + len + 4;
        }
        if (bitDepth != 8 || (colorType != 6 && colorType != 2)) throw new NotSupportedException("png must be 8-bit RGB or RGBA");
        int bpp = colorType == 6 ? 4 : 3;
        int stride = width * bpp;
        idat.Position = 0;
        var raw = new byte[(stride + 1) * height];
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
        {
            int read = 0;
            while (read < raw.Length)
            {
                int n = z.Read(raw, read, raw.Length - read);
                if (n <= 0) break;
                read += n;
            }
        }
        var cur = new byte[stride];
        var prev = new byte[stride];
        var bmp = new Bitmap(width, height);
        for (int y = 0; y < height; y++)
        {
            int row = y * (stride + 1);
            byte filter = raw[row];
            for (int i = 0; i < stride; i++)
            {
                int x = raw[row + 1 + i];
                int a = i >= bpp ? cur[i - bpp] : 0;
                int b = prev[i];
                int c = i >= bpp ? prev[i - bpp] : 0;
                cur[i] = (byte)(filter switch
                {
                    1 => x + a,
                    2 => x + b,
                    3 => x + ((a + b) >> 1),
                    4 => x + Paeth(a, b, c),
                    _ => x
                });
            }
            for (int px = 0; px < width; px++)
            {
                int o = px * bpp;
                byte r = cur[o], g = cur[o + 1], bl = cur[o + 2];
                byte al = bpp == 4 ? cur[o + 3] : (byte)255;
                bmp.Pixels[y * width + px] = Bitmap.Premultiply(r, g, bl, al);
            }
            (prev, cur) = (cur, prev);
        }
        return bmp;
    }

    public static byte[] EncodeRgb(byte[] bgra, int width, int height)
    {
        var raw = new byte[(width * 3 + 1) * height];
        int o = 0;
        for (int y = 0; y < height; y++)
        {
            raw[o++] = 0;
            int s = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                raw[o++] = bgra[s + 2];
                raw[o++] = bgra[s + 1];
                raw[o++] = bgra[s];
                s += 4;
            }
        }
        var comp = new MemoryStream();
        using (var z = new ZLibStream(comp, CompressionLevel.Fastest, true)) z.Write(raw, 0, raw.Length);
        var ms = new MemoryStream();
        ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        WriteInt(ihdr, 0, width);
        WriteInt(ihdr, 4, height);
        ihdr[8] = 8;
        ihdr[9] = 2;
        Chunk(ms, "IHDR", ihdr);
        Chunk(ms, "IDAT", comp.ToArray());
        Chunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        return pb <= pc ? b : c;
    }

    static int ReadInt(byte[] d, int p) => (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3];

    static void WriteInt(byte[] d, int p, int v)
    {
        d[p] = (byte)(v >> 24);
        d[p + 1] = (byte)(v >> 16);
        d[p + 2] = (byte)(v >> 8);
        d[p + 3] = (byte)v;
    }

    static void Chunk(Stream s, string type, byte[] body)
    {
        var len = new byte[4];
        WriteInt(len, 0, body.Length);
        s.Write(len);
        var t = Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(body);
        uint crc = Crc(t, 0xFFFFFFFF);
        crc = Crc(body, crc) ^ 0xFFFFFFFF;
        var c = new byte[4];
        WriteInt(c, 0, (int)crc);
        s.Write(c);
    }

    static uint[] crcTable;

    static uint Crc(byte[] data, uint crc)
    {
        if (crcTable == null)
        {
            crcTable = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                crcTable[n] = c;
            }
        }
        foreach (byte b in data) crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
