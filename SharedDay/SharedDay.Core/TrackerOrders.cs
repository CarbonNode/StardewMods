using System.Globalization;
using System.Text.Json;

namespace SharedDay.Core;

/// <summary>
/// What the ModRelay turn tracker may answer to a report (0.1.29): a 200 with
/// <c>{"barred": ["<farmer id>", ...], "new_farmers": false}</c>. <see cref="Barred"/> are farmer
/// ids (strings in the JSON, because they are 64-bit; numbers are taken too) the group has removed
/// from the farm; <see cref="NewFarmers"/> false means nobody may claim a cabin as a new farmer.
/// A 204, or a body that is not this, means no orders. Parsed leniently: a field a newer server
/// adds never breaks the host.
/// </summary>
public sealed record TrackerOrders(IReadOnlyList<long> Barred, bool? NewFarmers)
{
    public static TrackerOrders? Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var barred = new List<long>();
            if (root.TryGetProperty("barred", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in list.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.Number && entry.TryGetInt64(out long number)) barred.Add(number);
                    else if (entry.ValueKind == JsonValueKind.String && long.TryParse(entry.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)) barred.Add(parsed);
                }
            }
            bool? newFarmers = root.TryGetProperty("new_farmers", out var flag) && flag.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? flag.GetBoolean() : null;
            if (barred.Count == 0 && newFarmers is null) return null;
            return new TrackerOrders(barred.Distinct().ToList(), newFarmers);
        }
        catch (JsonException) { return null; }
    }
}
