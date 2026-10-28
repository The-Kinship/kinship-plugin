using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

/// <summary>Its own small window for "About this plugin" and the bug/abuse report form. Opened from the ? button on the main window or the top bar menu.</summary>
public sealed class AboutPluginWindow : Window, IDisposable
{
    private readonly AboutPluginTab tab = new();

    public AboutPluginWindow() : base("About this plugin##KinshipAboutPlugin", ImGuiWindowFlags.NoCollapse)
    {
        Size = new Vector2(420f, 520f);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    public void Show(bool report) { IsOpen = true; if (report) tab.JumpToReport(); }

    public override void Draw() { Alpha.Mark(); tab.Draw(); }
}
