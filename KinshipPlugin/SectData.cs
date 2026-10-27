using System.Collections.Generic;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace KinshipPlugin;

public sealed record SectInfo(string Key, string Name, string Leader, string Image, Vector4 Color, string Blurb, string Hash, bool Hidden = false);

public static class SectData
{
    private const string Salt = "kinship:";

    private static Vector4 Hex(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1f);

    // Passwords are stored as hashes, the same as on the website.
    public static readonly Dictionary<string, SectInfo> All = new()
    {
        ["onyx"]  = new("onyx",  "The Onyx Blood Guard", "The Prefect",            "onyx.jpg",       Hex(0x8b, 0x5c, 0xf6), "Magic, research, and the occult.",    "13f4452695183739eb80a1a250be23147b44575ef3659698e1b084a8a074cbe5"),
        ["iron"]  = new("iron",  "The Iron Vanguard",    "The Vanguard Commander", "vanguard.jpg",   Hex(0x8f, 0xa6, 0xcf), "Military, patrols, and peacekeeping.", "e5e5517567dd14197466174be604ca1e7da814d301130f8905f9d78d53f838b0"),
        ["life"]  = new("life",  "The Life Watch",       "The Life Warden",        "lifewatch.jpg",  Hex(0x4f, 0x9d, 0x5d), "Healing of body and mind.",            "1845ef570a25ca2cf3d34645b5b05d31d7866eb6067a4d040929da6f76dc81d2"),
        ["ember"] = new("ember", "The Ember Court",      "The Baron / Baroness",   "ember-court.jpg",Hex(0xd9, 0x47, 0x2b), "Social RP, events, and welcomes.",     "edf229697b75a44502a466456593e7297c3306f921f4a898b173e99fcade09ea"),
        ["crypt"] = new("crypt", "The Onyx Crypt",       "",                       "onyx.jpg",       Hex(0x8b, 0x5c, 0xf6), "",                                     "9cad0d76f349ee9d23b3a9d877db38d9450708b2aa669292560044a4a7c2f4bc", true),
    };

    public static readonly string[] Order = { "onyx", "iron", "life", "ember" };

    public static bool CheckPassword(string key, string password)
    {
        if (!All.TryGetValue(key, out var sect)) return false;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(Salt + password.Trim().ToLowerInvariant()));
        return System.Convert.ToHexString(bytes).ToLowerInvariant() == sect.Hash;
    }

    public static bool IsUnlocked(string key) => Plugin.Config.Me().Unlocked.Contains(key);

    public static void SetUnlocked(string key, bool remember)
    {
        if (!remember) { SessionUnlocked.Add(key); return; }
        if (!Plugin.Config.Me().Unlocked.Contains(key)) Plugin.Config.Me().Unlocked.Add(key);
        Plugin.Config.Save();
    }

    // Unlocked just for this game session (when "remember" is off).
    public static readonly HashSet<string> SessionUnlocked = new();
    public static bool IsOpenNow(string key) => IsUnlocked(key) || SessionUnlocked.Contains(key);
}
