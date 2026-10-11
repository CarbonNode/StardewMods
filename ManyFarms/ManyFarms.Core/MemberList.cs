using System.Globalization;

namespace ManyFarms.Core;

/// <summary>
/// Whose farm a farm is, as it is kept on the farm itself: farmer IDs separated by commas, in the
/// order they joined. IDs are 64-bit and stay whole. Anything that is not an ID is skipped, so a
/// damaged entry costs one member and not the list.
/// </summary>
public static class MemberList
{
    public static IReadOnlyList<long> Parse(string? raw)
    {
        var ids = new List<long>();
        if (string.IsNullOrWhiteSpace(raw)) return ids;
        foreach (string part in raw.Split(','))
        {
            if (long.TryParse(part.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long id) && id != 0 && !ids.Contains(id))
                ids.Add(id);
        }
        return ids;
    }

    public static string Format(IEnumerable<long> ids) =>
        string.Join(",", ids.Where(id => id != 0).Distinct().Select(id => id.ToString(CultureInfo.InvariantCulture)));

    public static string With(string? raw, long id) => Format(Parse(raw).Append(id));

    public static string Without(string? raw, long id) => Format(Parse(raw).Where(other => other != id));

    public static bool Has(string? raw, long id) => Parse(raw).Contains(id);
}
