using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace KinshipPlugin;

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
    public bool ChatEcho { get; set; } = true;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
