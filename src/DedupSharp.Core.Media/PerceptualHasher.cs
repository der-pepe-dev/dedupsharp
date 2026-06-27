using System;
using DedupSharp.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace DedupSharp.Core.Media;

/// <summary>
/// Computes a 64-bit perceptual hash of an image. Images are decoded to 8-bit luminance
/// (grayscale) and resized to a small fixed grid, so the hash is robust to scaling and
/// re-compression. Hashes are compared by Hamming distance.
/// </summary>
public static class PerceptualHasher
{
    // A fixed resampler keeps results deterministic across runs and platforms.
    private static readonly ResizeOptions s_resize = new()
    {
        Sampler = KnownResamplers.Triangle,
        Mode = ResizeMode.Stretch
    };

    public static ulong Compute(string path, PerceptualHashKind kind) =>
        kind switch
        {
            PerceptualHashKind.AHash => AHash(path),
            PerceptualHashKind.DHash => DHash(path),
            PerceptualHashKind.PHash => PHash(path),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown PerceptualHashKind.")
        };

    /// <summary>Hamming distance between two 64-bit hashes.</summary>
    public static int Distance(ulong a, ulong b) => System.Numerics.BitOperations.PopCount(a ^ b);

    // ---------- aHash: 8x8, bit = pixel >= mean ----------

    private static ulong AHash(string path)
    {
        var px = LoadLuminance(path, 8, 8);

        long sum = 0;
        foreach (var p in px) sum += p;
        double mean = (double)sum / px.Length;

        ulong hash = 0;
        for (int i = 0; i < 64; i++)
            if (px[i] >= mean)
                hash |= 1UL << i;
        return hash;
    }

    // ---------- dHash: 9x8, bit = pixel brighter than right neighbour ----------

    private static ulong DHash(string path)
    {
        const int w = 9, h = 8;
        var px = LoadLuminance(path, w, h);

        ulong hash = 0;
        int bit = 0;
        for (int y = 0; y < h; y++)
        {
            int rowStart = y * w;
            for (int x = 0; x < w - 1; x++)
            {
                if (px[rowStart + x] > px[rowStart + x + 1])
                    hash |= 1UL << bit;
                bit++;
            }
        }
        return hash;
    }

    // ---------- pHash: 32x32 -> DCT -> top-left 8x8 -> bit = coeff >= median ----------

    private static ulong PHash(string path)
    {
        const int n = 32;
        var px = LoadLuminance(path, n, n);

        // Precompute the 1-D DCT-II cosine matrix for the 8 low-frequency rows/cols.
        var cos = new double[8, n];
        for (int u = 0; u < 8; u++)
            for (int x = 0; x < n; x++)
                cos[u, x] = Math.Cos((2 * x + 1) * u * Math.PI / (2 * n));

        // Separable 2-D DCT, keeping only the top-left 8x8 block.
        var tmp = new double[8, n]; // DCT along rows (u, x-> kept u), per column y
        for (int u = 0; u < 8; u++)
            for (int y = 0; y < n; y++)
            {
                double s = 0;
                for (int x = 0; x < n; x++)
                    s += px[y * n + x] * cos[u, x];
                tmp[u, y] = s;
            }

        var block = new double[64];
        for (int u = 0; u < 8; u++)
            for (int v = 0; v < 8; v++)
            {
                double s = 0;
                for (int y = 0; y < n; y++)
                    s += tmp[u, y] * cos[v, y];
                block[u * 8 + v] = s;
            }

        double median = Median(block);
        ulong hash = 0;
        for (int i = 0; i < 64; i++)
            if (block[i] >= median)
                hash |= 1UL << i;
        return hash;
    }

    private static double Median(double[] values)
    {
        var copy = (double[])values.Clone();
        Array.Sort(copy);
        return (copy[copy.Length / 2 - 1] + copy[copy.Length / 2]) / 2.0;
    }

    // ---------- decode + grayscale + resize ----------

    private static byte[] LoadLuminance(string path, int width, int height)
    {
        using var image = Image.Load<L8>(path);
        image.Mutate(ctx => ctx.Resize(new ResizeOptions
        {
            Sampler = s_resize.Sampler,
            Mode = s_resize.Mode,
            Size = new Size(width, height)
        }));

        var pixels = new L8[width * height];
        image.CopyPixelDataTo(pixels);

        var result = new byte[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
            result[i] = pixels[i].PackedValue;
        return result;
    }
}
