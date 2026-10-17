using System.Security.Cryptography;
using System.Text;

namespace SharedDay.Core;

/// <summary>
/// Who a joining player is, as the ModRelay launcher says (0.1.37). The launcher puts the
/// signed-in account's id and name in its link file, with a join ticket it asked the server
/// for at Play (`GET /turns/:id/ticket`); the player's Shared Day sends all three in its hello.
/// The host checks the ticket by itself, with no network call: the server signed it with the
/// SHA-256 of this tracker's token (the server keeps only that hash; the host keeps the token),
/// over the tracker id, the account id, the name and an expiry, so a ticket is for one farm,
/// one account, one name and a day. A hello without a ticket, or with one that does not
/// check out, is a player with no account: on a group's farm they are turned away, and no
/// farmer is ever recorded as theirs.
/// </summary>
public static class JoinTicket
{
    /// <summary>The ticket on the wire: "&lt;expiry unix seconds&gt;.&lt;base64url HMAC-SHA256&gt;".</summary>
    public static string Make(string trackerId, string trackerToken, string accountId, string name, DateTimeOffset expires)
    {
        long exp = expires.ToUnixTimeSeconds();
        return exp.ToString() + "." + Base64Url(Sign(trackerId, trackerToken, accountId, name, exp));
    }

    /// <summary>True when <paramref name="ticket"/> was signed for this tracker, account and name and has not expired.</summary>
    public static bool Verify(string? trackerId, string? trackerToken, string? accountId, string? name, string? ticket, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(trackerId) || string.IsNullOrEmpty(trackerToken) || string.IsNullOrEmpty(accountId) || string.IsNullOrEmpty(ticket)) return false;
        int dot = ticket.IndexOf('.');
        if (dot <= 0 || dot == ticket.Length - 1) return false;
        if (!long.TryParse(ticket.AsSpan(0, dot), out long exp) || exp <= now.ToUnixTimeSeconds()) return false;
        byte[] given;
        try { given = FromBase64Url(ticket[(dot + 1)..]); }
        catch (FormatException) { return false; }
        var expected = Sign(trackerId, trackerToken, accountId, name ?? "", exp);
        return given.Length == expected.Length && CryptographicOperations.FixedTimeEquals(given, expected);
    }

    /// <summary>The tracker's id from its URL ("https://modrelay.app/turns/&lt;id&gt;"), or null.</summary>
    public static string? TrackerIdOf(string? trackerUrl)
    {
        if (!Uri.TryCreate(trackerUrl, UriKind.Absolute, out var uri)) return null;
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int at = Array.IndexOf(parts, "turns");
        return at >= 0 && at + 1 < parts.Length ? parts[at + 1] : null;
    }

    private static byte[] Sign(string trackerId, string trackerToken, string accountId, string name, long exp)
    {
        // The key is the token's SHA-256: the server stores the hash, never the token.
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(trackerToken));
        return HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"modrelay.join-ticket/1\n{trackerId}\n{accountId}\n{name}\n{exp}"));
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        string s = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}

/// <summary>A joining player's ModRelay account, once the host has checked their ticket.</summary>
public sealed record JoinAccount(string Id, string Name);

/// <summary>
/// Whether a player may play the farmer they picked (0.1.37). The owner, 2026-10-15, on being
/// offered Casey's farmer in the join list: "wtf why can i join as someone else????". The game
/// marks whose farmer is whose only on a Steam or GOG join; on a farm joined by its address it
/// marks nobody's, so any made farmer is anyone's to pick. The host now keeps an owner on each
/// farmer (the account that made it, a claim in the launcher, or the host's console) and turns
/// away a player who picks another account's farmer; a group's farm also turns away a player
/// with no account in the group at all.
/// </summary>
public static class FarmerOwnership
{
    public const string PrivateFarm = "this farm is private to its group. Join it from the ModRelay launcher";

    /// <param name="farmerName">The farmer picked, for the words.</param>
    /// <param name="ownerId">The account the farmer belongs to, or null for a farmer nobody owns yet.</param>
    /// <param name="ownerName">That account's name, when known.</param>
    /// <param name="account">The joining player's checked account, or null for none.</param>
    /// <param name="requireGroupAccount">The farm is a group's: a player needs an account in the group.</param>
    /// <param name="groupAccountIds">The group's members' account ids, when known.</param>
    /// <param name="ticketsProven">
    /// A join ticket has been checked and passed on this farm, so the chain that says who a player
    /// is (launcher, link file, hello, this host) is known to work (0.1.41). Until it has, a player
    /// who proves nothing is let in and the log says why: a mistake in that chain, or a server
    /// that cannot be reached, must never lock a group out of its own farm. The owner, 2026-10-15:
    /// "just be sure this isnt going to be an issue again." A player who DOES prove who they are
    /// is held to it either way, so nobody can take a farmer that belongs to somebody else by
    /// signing in as themselves.
    /// </param>
    /// <returns>Null to let them play, or why they are turned away (a sentence fragment after "disconnected Name: ").</returns>
    public static string? Refusal(string farmerName, string? ownerId, string? ownerName, JoinAccount? account, bool requireGroupAccount, IEnumerable<string>? groupAccountIds, bool ticketsProven = true)
    {
        if (ownerId is not null && account?.Id != ownerId)
        {
            // Somebody who says who they are, and is not the owner, is turned away whatever the
            // chain's standing. Somebody who says nothing is only turned away once it is proven.
            if (account is not null || ticketsProven) return $"{farmerName} belongs to {ownerName ?? "another player"}";
        }
        if (requireGroupAccount)
        {
            if (account is null) return ticketsProven ? PrivateFarm : null;
            if (groupAccountIds is not null && !groupAccountIds.Contains(account.Id)) return $"{account.Name} is not in this farm's group";
        }
        return null;
    }

    /// <summary>What the log says when a player was let in only because the ticket chain has never been seen to work.</summary>
    public static string NotProven(string who) =>
        $"{who} joined without a ModRelay account the farm could check, and no join ticket has ever been checked on this farm, so the farm let them in rather than lock its own group out. " +
        "Every player needs the ModRelay launcher (0.0.543 or newer) and this farm's turn tracker must be reachable. Set RequireGroupAccount: true in the host's config.json to turn people away from the start instead.";
}

/// <summary>
/// Saying who is who by hand (0.1.43). The owner, 2026-10-15, on the farm waiting for one person
/// twice: "fix or allow combining in the UI in game?" — both. The farm matches people to farmers
/// by itself (a launcher pick, then a name, then a plain count), and from 0.1.42 a player claims
/// the farmer they join as, so the guessing ends by itself as everyone plays. Until it does, or
/// when a guess was wrong, somebody at the farm can simply say it: this farmer is that member's.
/// The farm then stops waiting for them twice.
/// </summary>
public static class WhoIsWho
{
    /// <param name="MemberId">The ModRelay account whose place on the roster this is.</param>
    /// <param name="Name">The member's own name, as the group has it.</param>
    public sealed record WaitingMember(string MemberId, string Name);

    /// <param name="Id">The farmer's id in the game.</param>
    /// <param name="Name">The farmer's name.</param>
    /// <param name="OwnerName">Who it belongs to already, or null when nobody has said.</param>
    public sealed record Candidate(long Id, string Name, string? OwnerName);

    /// <summary>
    /// The members the farm is waiting on who have no farmer: each one a row to attach a farmer to.
    /// </summary>
    public static IReadOnlyList<WaitingMember> Waiting(IEnumerable<FarmerSnapshot> roster) =>
        roster.Where(f => f.IsPlaceholder && f.MemberId is not null)
            .Select(f => new WaitingMember(f.MemberId!, f.MemberName ?? f.Name))
            .ToList();

    /// <summary>
    /// The farmers that could be a given member's: every real farmer on the roster, nobody else's
    /// first and then the ones already spoken for, so a wrong answer can be put right without
    /// hunting. The host's own farmer is left out when it takes no turns.
    /// </summary>
    public static IReadOnlyList<Candidate> CandidatesFor(IEnumerable<FarmerSnapshot> roster, Func<long, string?> ownerName, long? hostFarmerToSkip = null) =>
        roster.Where(f => !f.IsPlaceholder && f.Id != hostFarmerToSkip)
            .Select(f => new Candidate(f.Id, f.Name, ownerName(f.Id)))
            .OrderBy(c => c.OwnerName is null ? 0 : 1)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The line on a farmer's row: "MOSS" on its own, or "MOSS (said to be Casey's)".</summary>
    public static string Label(Candidate candidate) =>
        candidate.OwnerName is null ? candidate.Name : $"{candidate.Name} (said to be {candidate.OwnerName}'s)";

    /// <summary>What the farm asks before it writes it down.</summary>
    public static string Confirm(string memberName, string farmerName) =>
        $"Is {farmerName} the farmer {memberName} plays? The farm then waits for {memberName} once, as {farmerName}, instead of waiting for both. " +
        $"Nothing anyone owns or did changes, and it can be said again differently at any time.";

    /// <summary>What the farm says once it has.</summary>
    public static string Said(string memberName, string farmerName) =>
        $"{farmerName} is {memberName}'s farmer. The farm waits for them once from now on.";
}
