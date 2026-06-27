using System.Text.Json.Serialization;

namespace DedupSharp.Core;

/// <summary>
/// What kind of duplication a <see cref="DuplicateGroup"/> represents.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DuplicateKind
{
    /// <summary>Byte-for-byte identical files.</summary>
    Exact = 0,

    /// <summary>Visually similar images (perceptual hash within a distance threshold).</summary>
    MediaImage = 1
}
