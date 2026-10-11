using System.Security.Cryptography;
using System.Text;

namespace SharedDay.Core;

/// <summary>
/// The farm's bypass password, kept as a salted hash so the word itself is in no file the game
/// writes and no message the host sends. A player who knows it can switch Shared Day's hold off
/// for themselves for the rest of a day when something has gone wrong.
/// </summary>
public static class BypassCode
{
    /// <summary>The hash to keep for a password. Empty for an empty password: no bypass at all.</summary>
    public static string Hash(string? salt, string? password)
    {
        if (string.IsNullOrEmpty(password)) return "";
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes((salt ?? "") + "\n" + password));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Whether a farm has a bypass password set.</summary>
    public static bool IsSet(string? hash) => hash is { Length: 64 };

    /// <summary>Whether what was typed is the farm's password. Never true when none is set.</summary>
    public static bool Matches(string? salt, string? hash, string? typed)
    {
        if (!IsSet(hash) || string.IsNullOrEmpty(typed)) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Hash(salt, typed)), Encoding.ASCII.GetBytes(hash!.ToLowerInvariant()));
    }
}
