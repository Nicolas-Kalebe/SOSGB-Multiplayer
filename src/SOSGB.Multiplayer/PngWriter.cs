// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/CONTEXTO.md (screenshots da DevBridge, D20).
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace SOSGB.Multiplayer;

/// <summary>
/// Codificador PNG mínimo (RGB 8 bits, sem filtro). Existe porque o ImageConversion.EncodeToPNG
/// da Unity não funciona pelo Il2CppInterop neste jogo. Os pixels da Unity vêm de baixo para cima.
/// </summary>
internal static class PngWriter
{
    public static void Write(string path, int width, int height, Color32[] pixels)
    {
        var raw = new byte[(width * 3 + 1) * height];
        var i = 0;
        for (var y = height - 1; y >= 0; y--)
        {
            raw[i++] = 0; // filtro "None"
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var c = pixels[row + x];
                raw[i++] = c.r;
                raw[i++] = c.g;
                raw[i++] = c.b;
            }
        }

        using var fs = File.Create(path);
        fs.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        var ihdr = new byte[13];
        WriteBE(ihdr, 0, width);
        WriteBE(ihdr, 4, height);
        ihdr[8] = 8; // bits por canal
        ihdr[9] = 2; // RGB
        WriteChunk(fs, "IHDR", ihdr);

        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, System.IO.Compression.CompressionLevel.Fastest, true))
                z.Write(raw, 0, raw.Length);
            WriteChunk(fs, "IDAT", ms.ToArray());
        }

        WriteChunk(fs, "IEND", Array.Empty<byte>());
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBE(len, 0, data.Length);
        s.Write(len);
        var t = Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        var crc = Crc32(t, data);
        var c = new byte[4];
        WriteBE(c, 0, (int)crc);
        s.Write(c);
    }

    private static void WriteBE(byte[] b, int o, int v)
    {
        b[o] = (byte)(v >> 24);
        b[o + 1] = (byte)(v >> 16);
        b[o + 2] = (byte)(v >> 8);
        b[o + 3] = (byte)v;
    }

    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in type) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
