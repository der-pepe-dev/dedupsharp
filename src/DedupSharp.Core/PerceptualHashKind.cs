using System.Text.Json.Serialization;

namespace DedupSharp.Core;

/// <summary>
/// Perceptual hash algorithm used by the media (image) core. All produce a 64-bit hash
/// compared by Hamming distance; they trade simplicity for robustness.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PerceptualHashKind
{
    /// <summary>Average hash: 8x8 grayscale, bit = pixel &gt;= mean. Simplest, weakest.</summary>
    AHash = 0,

    /// <summary>Difference hash: 9x8 grayscale, bit = pixel brighter than right neighbour.</summary>
    DHash = 1,

    /// <summary>Perceptual hash: 32x32 grayscale, 2-D DCT, low-frequency 8x8, bit = coeff &gt;= median.</summary>
    PHash = 2
}
