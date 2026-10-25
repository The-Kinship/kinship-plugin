using System;
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

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
