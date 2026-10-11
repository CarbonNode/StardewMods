namespace SharedDay.Core;

/// <summary>
/// A farmer's face on its way to the turn tracker (docs/turn-tracker-contract.md, "Portraits"):
/// a small PNG as a <c>data:image/png;base64,</c> address, one per farmer, with a ceiling for one
/// face and for all the faces of a report. The tracker enforces the same limits and drops what
/// is over them, so the host stays inside them rather than send what will not be kept.
/// </summary>
public static class PortraitWire
{
    public const string Prefix = "data:image/png;base64,";

    /// <summary>The longest address the tracker keeps for one farmer (about 4.4 KB of PNG).</summary>
    public const int OneMaxChars = 6000;

    /// <summary>All the portraits of one report together.</summary>
    public const int TotalMaxChars = 40_000;

    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>A PNG's bytes as the address the tracker accepts; null when they are not a PNG or too large.</summary>
    public static string? ToAddress(byte[]? png)
    {
        if (png is null || png.Length <= PngSignature.Length || !png.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature)) return null;
        string address = Prefix + Convert.ToBase64String(png);
        return address.Length <= OneMaxChars ? address : null;
    }

    /// <summary>
    /// The faces that fit one report, in the farmers' own order: each is kept while the report's
    /// total has room for it and left out (null) once it has not. A farmer never loses their row.
    /// </summary>
    public static IReadOnlyList<string?> Fit(IReadOnlyList<string?> addresses)
    {
        var kept = new string?[addresses.Count];
        int used = 0;
        for (int i = 0; i < addresses.Count; i++)
        {
            string? address = addresses[i];
            if (address is null || address.Length > OneMaxChars || used + address.Length > TotalMaxChars) continue;
            used += address.Length;
            kept[i] = address;
        }
        return kept;
    }
}
