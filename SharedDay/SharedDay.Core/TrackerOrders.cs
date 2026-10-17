using System.Globalization;
using System.Text.Json;

namespace SharedDay.Core;

/// <summary>
/// What the ModRelay turn tracker may answer to a report (0.1.29): a 200 with
/// <c>{"barred": ["<farmer id>", ...], "new_farmers": false}</c>. <see cref="Barred"/> are farmer
/// ids (strings in the JSON, because they are 64-bit; numbers are taken too) the group has removed
/// from the farm; <see cref="NewFarmers"/> false means nobody may claim a cabin as a new farmer.
/// A 204, or a body that is not this, means no orders. Parsed leniently: a field a newer server
/// adds never breaks the host. From 0.1.37 the answer may carry the farm's group,
/// <c>"members": [{"id": "<account id>", "name": "Pat", "farmer_id": "<farmer id>" | null}, ...]</c>:
/// <see cref="Members"/> is null when the key is absent (an older server; the host keeps the group
/// it has) and empty when the farm has no group, which clears every placeholder.
/// </summary>
public sealed record TrackerOrders(IReadOnlyList<long> Barred, bool? NewFarmers, IReadOnlyList<GroupMember>? Members = null, IReadOnlyList<SkipOrder>? Skips = null)
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
            List<GroupMember>? members = null;
            if (root.TryGetProperty("members", out var group) && group.ValueKind == JsonValueKind.Array)
            {
                members = new List<GroupMember>();
                foreach (var entry in group.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object) continue;
                    string? account = entry.TryGetProperty("id", out var idValue) && idValue.ValueKind == JsonValueKind.String ? idValue.GetString() : null;
                    if (string.IsNullOrWhiteSpace(account)) continue;
                    string name = entry.TryGetProperty("name", out var nameValue) && nameValue.ValueKind == JsonValueKind.String ? nameValue.GetString() ?? "" : "";
                    long? farmerId = null;
                    if (entry.TryGetProperty("farmer_id", out var farmer))
                    {
                        if (farmer.ValueKind == JsonValueKind.Number && farmer.TryGetInt64(out long number)) farmerId = number;
                        else if (farmer.ValueKind == JsonValueKind.String && long.TryParse(farmer.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)) farmerId = parsed;
                    }
                    bool host = entry.TryGetProperty("host", out var hostFlag) && hostFlag.ValueKind == JsonValueKind.True;
                    members.Add(new GroupMember(account, name, farmerId, host));
                }
            }
            List<SkipOrder>? skips = null;
            if (root.TryGetProperty("skips", out var skipList) && skipList.ValueKind == JsonValueKind.Array)
            {
                skips = new List<SkipOrder>();
                foreach (var entry in skipList.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("farmer_id", out var fid) || !entry.TryGetProperty("day", out var dayValue)) continue;
                    long? farmerId = fid.ValueKind == JsonValueKind.Number && fid.TryGetInt64(out long n) ? n
                        : fid.ValueKind == JsonValueKind.String && long.TryParse(fid.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : null;
                    if (farmerId is null || dayValue.ValueKind != JsonValueKind.Number || !dayValue.TryGetInt32(out int day)) continue;
                    skips.Add(new SkipOrder(farmerId.Value, day));
                }
            }
            if (barred.Count == 0 && newFarmers is null && members is null && skips is null) return null;
            return new TrackerOrders(barred.Distinct().ToList(), newFarmers, members, skips);
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>
/// A skip the group asked for from the launcher (0.1.38): <paramref name="FarmerId"/> sits
/// <paramref name="Day"/> (the absolute farm day) out. The host applies it like its own skip,
/// only while the farm is on that day and the farmer is still to play and not on the farm;
/// the order repeats with every answer and is harmless once done.
/// </summary>
public sealed record SkipOrder(long FarmerId, int Day);
