using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;

namespace KinshipPlugin;

public sealed class MainWindow : Window, IDisposable
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private const string CrestPng = ImageCache.SiteImages + "kinship-crest.png";
    private const string CrestJpg = ImageCache.SiteImages + "kinship-crest.jpg";

    private readonly SectsTab sects = new();
    private readonly CombatTab combat = new();

    public MainWindow() : base("The Kinship##KinshipMain", ImGuiWindowFlags.None)
    {
        Size = new Vector2(440, 520);
        SizeCondition = ImGuiCond.FirstUseEver;

        // Refresh lives in the title bar, top right.
        TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.Sync,
            Click = button => { _ = Plugin.Data.Refresh(); },
            ShowTooltip = () => ImGui.SetTooltip("Refresh")
        });
    }

    public void Dispose() { }

    // Refresh every time the window is opened.
    public override void OnOpen() { _ = Plugin.Data.Refresh(); }

    private static string Get(Dictionary<string, string> d, string key, string fallback = "")
        => d.TryGetValue(key, out var v) && v.Length > 0 ? v : fallback;

    // Draws an image centered, scaled to fit the given height (and the window width).
    private static void DrawCentered(string url, float maxHeight)
    {
        var wrap = Plugin.Images.Get(url);
        if (wrap == null) return;
        var avail = ImGui.GetContentRegionAvail().X;
        var scale = Math.Min(maxHeight / wrap.Height, avail / wrap.Width);
        var size = new Vector2(wrap.Width * scale, wrap.Height * scale);
        if (avail > size.X) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (avail - size.X) / 2f);
        ImGui.Image(wrap.Handle, size);
    }

    // A logo_url in the sheet wins; otherwise the transparent crest; if that can't load, the old jpg.
    private static string CrestUrl(Dictionary<string, string>? f)
    {
        if (f != null)
        {
            var custom = Get(f, "logo_url");
            if ((custom.StartsWith("https://") || custom.StartsWith("http://")) && !Plugin.Images.HasFailed(custom))
                return custom;
        }
        return Plugin.Images.HasFailed(CrestPng) ? CrestJpg : CrestPng;
    }

    public override void Draw()
    {
        var (f, p, s) = Plugin.Data.Snapshot();

        if (ImGui.BeginTabBar("kinship_tabs"))
        {
            if (ImGui.BeginTabItem("Home")) { DrawHome(f); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Announcements")) { DrawAnnouncements(f); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Plots")) { DrawPlots(p); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Sects")) { sects.Draw(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Combat")) { combat.Draw(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Settings")) { DrawSettings(); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }

        if (s.Length > 0) { ImGui.Separator(); ImGui.TextColored(Dim, s); }
    }

    private static void DrawSettings()
    {
        var show = Plugin.Config.ShowPlotNotice;
        if (ImGui.Checkbox("Show the plot notice when I log in", ref show))
        {
            Plugin.Config.ShowPlotNotice = show;
            Plugin.Config.Save();
        }
        ImGui.Spacing();
        var crest = Plugin.Config.CrestHeight;
        if (ImGui.SliderInt("Crest size", ref crest, 60, 400))
        {
            Plugin.Config.CrestHeight = crest;
            Plugin.Config.Save();
        }
        ImGui.Spacing();
        if (ImGui.Button("Forget my unlocked sects"))
        {
            Plugin.Config.Unlocked.Clear();
            SectData.SessionUnlocked.Clear();
            Plugin.Config.Save();
        }
    }

    private static void DrawHome(Dictionary<string, string>? f)
    {
        DrawCentered(CrestUrl(f), Plugin.Config.CrestHeight);
        if (f == null) return;

        ImGui.TextColored(Gold, Get(f, "faction_name", "The Kinship"));
        var tag = Get(f, "tagline");
        if (tag.Length > 0) ImGui.TextWrapped(tag);
        ImGui.Separator();
        ImGui.Text($"{Get(f, "data_center", "Crystal")}  ·  {Get(f, "world", "Mateus")}");
        var members = Get(f, "member_count");
        if (members.Length > 0) ImGui.Text($"Members: {members}");
        var desc = Get(f, "description");
        if (desc.Length > 0) { ImGui.Spacing(); ImGui.TextWrapped(desc); }
    }

    private static void DrawAnnouncements(Dictionary<string, string>? f)
    {
        if (f == null) return;
        var text = Get(f, "announcements");
        if (text.Length == 0) ImGui.TextColored(Dim, "No announcements right now. Check back soon.");
        else ImGui.TextWrapped(text);
    }

    private static void DrawPlots(List<SheetClient.Plot>? plots)
    {
        if (plots == null) return;
        if (plots.Count == 0) { ImGui.TextColored(Dim, "No active plot right now."); return; }
        for (var i = 0; i < plots.Count; i++)
        {
            ImGui.TextColored(Gold, plots[i].Title);
            if (plots[i].Summary.Length > 0) ImGui.TextWrapped(plots[i].Summary);
            if (i < plots.Count - 1) { ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing(); }
        }
    }
}
