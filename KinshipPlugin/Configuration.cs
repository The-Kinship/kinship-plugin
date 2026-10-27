using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace KinshipPlugin;

[Serializable]
public class Profile
{
    public string Label { get; set; } = "";
    public string ItemsName { get; set; } = "";
    public string ItemsToken { get; set; } = "";
    public string ItemsRecovery { get; set; } = "";
    public string CombatName { get; set; } = "";
    public string CombatCode { get; set; } = "";
    public string CombatToken { get; set; } = "";
    public string CombatMemberId { get; set; } = "";
    public string SceneName { get; set; } = "";
    public string SceneCode { get; set; } = "";
    public string SceneToken { get; set; } = "";
    public string SceneMemberId { get; set; } = "";
    public string SceneRole { get; set; } = "";
    public string SceneTitle { get; set; } = "";
    public List<string> Unlocked { get; set; } = new();
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Show the small plot notice when you log in.</summary>
    public bool ShowPlotNotice { get; set; } = true;

    /// <summary>Height of the crest in the main window, in pixels.</summary>
    public int CrestHeight { get; set; } = 200;

    /// <summary>Sects you have unlocked on this computer (never stores the password itself).</summary>
    public List<string> Unlocked { get; set; } = new();

    /// <summary>Combat: who you are in your current room (so it survives restarts).</summary>
    public string CombatName { get; set; } = "";
    public string CombatCode { get; set; } = "";
    public string CombatToken { get; set; } = "";
    public string CombatMemberId { get; set; } = "";

    /// <summary>RP scenes: the scene you last joined or run (popups themselves always start OFF at login).</summary>
    public string SceneName { get; set; } = "";
    public string SceneCode { get; set; } = "";
    public string SceneToken { get; set; } = "";
    public string SceneMemberId { get; set; } = "";
    public string SceneRole { get; set; } = "";
    public string SceneTitle { get; set; } = "";
    /// <summary>The creation password (only DMs need it). Stored on this computer only.</summary>
    public string CreatePassword { get; set; } = "";
    public string BankPassword { get; set; } = "";
    /// <summary>RP items: who you are in the RP system, and how to get back in.</summary>
    public string ItemsName { get; set; } = "";
    public string ItemsToken { get; set; } = "";
    public string ItemsRecovery { get; set; } = "";

    /// <summary>Show the name tags of nearby stashes in the world.</summary>
    public bool StashLabels { get; set; } = true;
    public bool SignLabels { get; set; } = true;
    public bool StatusBar { get; set; } = true;
    /// <summary>A small window to open a stash when you stand on it.</summary>
    public bool StashPopup { get; set; } = true;
    /// <summary>The little Kinship button in the server info bar at the top of the screen.</summary>
    public bool TopBarButton { get; set; } = true;
    public bool QuestWindow { get; set; } = true;

    public bool ChatEcho { get; set; } = true;

    /// <summary>Everything that belongs to one character: who they are in the item system, their combat and scene seats, and their unlocked sects.</summary>
    public Dictionary<string, Profile> Profiles { get; set; } = new();
    /// <summary>True once the sign-in saved before characters were separated has been given to a character.</summary>
    public bool LegacyClaimed { get; set; }

    private static readonly Profile NoOne = new();

    private Profile current = NoOne;

    /// <summary>The saved data for the character who is logged in right now (an empty, unsaved one at the title screen). Safe to call from any thread.</summary>
    public Profile Me() => current;

    /// <summary>Called on the game thread whenever the logged in character changes (0 means nobody is logged in).</summary>
    public void SetCharacter(ulong contentId, string name)
    {
        if (contentId == 0) { current = NoOne; return; }
        var key = contentId.ToString();
        if (!Profiles.TryGetValue(key, out var p))
        {
            p = new Profile();
            Profiles[key] = p;
        }
        if (name.Length > 0) p.Label = name;
        current = p;
    }

    /// <summary>A recovery code saved for any character on this computer (your other characters can join it), or an empty string.</summary>
    public string KnownRecoveryCode()
    {
        foreach (var p in Profiles.Values) if (p.ItemsRecovery.Length > 0) return p.ItemsRecovery;
        return !LegacyClaimed ? ItemsRecovery : "";
    }

    /// <summary>True while a sign-in from before characters were separated is still waiting for a character.</summary>
    public bool HasLegacy() => !LegacyClaimed && (ItemsToken.Length > 0 || CombatToken.Length > 0 || SceneToken.Length > 0 || Unlocked.Count > 0);

    /// <summary>Gives the old sign-in (items, combat seat, scene seat, sects) to the current character, then clears it so nobody else can take it.</summary>
    public void ClaimLegacy()
    {
        var p = Me();
        if (p == NoOne) return;
        p.ItemsName = ItemsName; p.ItemsToken = ItemsToken; p.ItemsRecovery = ItemsRecovery;
        p.CombatName = CombatName; p.CombatCode = CombatCode; p.CombatToken = CombatToken; p.CombatMemberId = CombatMemberId;
        p.SceneName = SceneName; p.SceneCode = SceneCode; p.SceneToken = SceneToken; p.SceneMemberId = SceneMemberId; p.SceneRole = SceneRole; p.SceneTitle = SceneTitle;
        p.Unlocked = new List<string>(Unlocked);
        ItemsName = ""; ItemsToken = ""; ItemsRecovery = "";
        CombatName = ""; CombatCode = ""; CombatToken = ""; CombatMemberId = "";
        SceneName = ""; SceneCode = ""; SceneToken = ""; SceneMemberId = ""; SceneRole = ""; SceneTitle = "";
        Unlocked = new List<string>();
        LegacyClaimed = true;
        Save();
    }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
