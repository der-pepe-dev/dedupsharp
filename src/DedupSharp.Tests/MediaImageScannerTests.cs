using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DedupSharp.Core;
using DedupSharp.Core.Media;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace DedupSharp.Tests;

public class MediaImageScannerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly MediaImageScanner _scanner = new();

    public MediaImageScannerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DedupSharpMedia_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private ScanOptions Options(PerceptualHashKind kind, int threshold = 10) => new()
    {
        Paths = [_tempDir],
        Recursive = true,
        PerceptualHash = kind,
        HammingThreshold = threshold
    };

    // pHash sits near its DCT median on smooth images, so near-duplicates land a bit
    // further apart under resample/JPEG than aHash/dHash; give it more slack.
    private static int NearDupThreshold(PerceptualHashKind kind) =>
        kind == PerceptualHashKind.PHash ? 20 : 10;

    // ---------- image generators ----------

    private string SaveGradient(string name, int w, int h, bool horizontal, bool jpeg = false)
    {
        using var img = new Image<Rgba32>(w, h);
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                {
                    int t = horizontal ? x * 255 / Math.Max(1, w - 1)
                                       : y * 255 / Math.Max(1, h - 1);
                    byte v = (byte)t;
                    row[x] = new Rgba32(v, v, v);
                }
            }
        });

        var path = Path.Combine(_tempDir, name);
        if (jpeg) img.SaveAsJpeg(path);
        else img.SaveAsPng(path);
        return path;
    }

    // A smooth pattern with several distinct low-frequency components, so its perceptual
    // hash (including the DCT-based pHash) is stable across resize/recompression — unlike
    // a pure gradient whose DCT is almost all zeros and flips noisily.
    private string SaveSmooth(string name, int w, int h, bool jpeg = false)
    {
        using var img = new Image<Rgba32>(w, h);
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                double fy = (double)y / h;
                for (int x = 0; x < w; x++)
                {
                    double fx = (double)x / w;
                    double f = 0.5
                               + 0.25 * Math.Cos(2 * Math.PI * fx)
                               + 0.15 * Math.Cos(4 * Math.PI * fy)
                               + 0.10 * Math.Cos(2 * Math.PI * (fx + fy));
                    f = Math.Clamp(f, 0, 1);
                    byte v = (byte)(f * 255);
                    row[x] = new Rgba32(v, v, v);
                }
            }
        });

        var path = Path.Combine(_tempDir, name);
        if (jpeg) img.SaveAsJpeg(path);
        else img.SaveAsPng(path);
        return path;
    }

    private string SaveCheckerboard(string name, int w, int h, int blocks)
    {
        int bw = Math.Max(1, w / blocks);
        using var img = new Image<Rgba32>(w, h);
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                {
                    bool on = ((x / bw) + (y / bw)) % 2 == 0;
                    byte v = on ? (byte)255 : (byte)0;
                    row[x] = new Rgba32(v, v, v);
                }
            }
        });

        var path = Path.Combine(_tempDir, name);
        img.SaveAsPng(path);
        return path;
    }

    // ---------- near-duplicates cluster ----------

    [Test]
    [Arguments(PerceptualHashKind.AHash)]
    [Arguments(PerceptualHashKind.DHash)]
    [Arguments(PerceptualHashKind.PHash)]
    public async Task Scanner_ClustersResizedAndRecompressedCopies(PerceptualHashKind kind)
    {
        // Same smooth image at different size and as JPEG => one cluster.
        SaveSmooth("base.png", 256, 256);
        SaveSmooth("resized.png", 200, 150);
        SaveSmooth("recompressed.jpg", 256, 256, jpeg: true);

        var groups = _scanner.Scan(Options(kind, NearDupThreshold(kind))).ToList();

        await Assert.That(groups).HasSingleItem();
        await Assert.That(groups[0].Files.Count).IsEqualTo(3);
        await Assert.That(groups[0].Kind).IsEqualTo(DuplicateKind.MediaImage);
    }

    // ---------- clearly different images do not cluster ----------

    [Test]
    [Arguments(PerceptualHashKind.AHash)]
    [Arguments(PerceptualHashKind.DHash)]
    [Arguments(PerceptualHashKind.PHash)]
    public async Task Scanner_DoesNotClusterDifferentImages(PerceptualHashKind kind)
    {
        SaveGradient("gradient.png", 256, 256, horizontal: true);
        SaveCheckerboard("checker.png", 256, 256, blocks: 8);

        var groups = _scanner.Scan(Options(kind)).ToList();

        await Assert.That(groups).IsEmpty();
    }

    // ---------- non-image files ignored ----------

    [Test]
    public async Task Scanner_IgnoresNonImageFiles()
    {
        SaveGradient("a.png", 64, 64, horizontal: true);
        File.WriteAllText(Path.Combine(_tempDir, "notes.txt"), "not an image");
        File.WriteAllText(Path.Combine(_tempDir, "data.bin"), "binary");

        var groups = _scanner.Scan(Options(PerceptualHashKind.DHash)).ToList();

        // Only one image, nothing to group with.
        await Assert.That(groups).IsEmpty();
    }

    // ---------- validation ----------

    [Test]
    public async Task Scanner_NullOptions_Throws()
    {
        await Assert.That(() => _scanner.Scan(null!).ToList()).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Scanner_BadThreshold_Throws()
    {
        var options = Options(PerceptualHashKind.DHash);
        options.HammingThreshold = 99;

        await Assert.That(() => _scanner.Scan(options).ToList()).Throws<ArgumentException>();
    }
}
