using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace KinshipPlugin;

public sealed class MainWindow : Window, IDisposable
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private const string DefaultCrest = ImageCache.SiteImages + "kinship-crest.jpg";

    private readonly object gate = new();
    private Dictionary<string, string>? faction;
    private List<SheetClient.Plot>? plots;
    private string status = "Loading...";
    private bool loading;

    public MainWindow() : base("The Kinship##KinshipMain", ImGuiWindowFlags.None)
    {
        Size = new Vector2(430, 380);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    // Refresh every time the window is opened.
    public override void OnOpen() => Refresh();

    private void Refresh()
    {
        lock (gate) { if (loading) return; loading = true; status = "Loading..."; }
        Task.Run(async () =>
        {
            try
            {
                var f = SheetClient.FetchFactionAsync();
                var p = SheetClient.FetchPlotsAsync();
                var fd = await f.ConfigureAwait(false);
                var pd = await p.ConfigureAwait(false);
                lock (gate) { faction = fd; plots = pd; status = ""; }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "Could not load the sheet.");
                lock (gate) { status = "Couldn't load the info. Check your connection and press Refresh."; }
            }
            finally { lock (gate) { loading = false; } }
        });
    }

    private static string Get(Dictionary<string, string> d, string key, string fallback = "")
        => d.TryGetValue(key, out var v) && v.Length > 0 ? v : fallback;

    // Draws an image centered, scaled to fit the given height. Returns false if it isn't ready or failed.
    private static bool DrawCentered(string url, float maxHeight)
    {
        var wrap = Plugin.Images.Get(url);
        if (wrap == null) return false;
        var scale = Math.Min(1f, maxHeight / wrap.Height);
        var size = new Vector2(wrap.Width * scale, wrap.Height * scale);
        var avail = ImGui.GetContentRegionAvail().X;
        if (avail > size.X) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (avail - size.X) / 2f);
        ImGui.Image(wrap.Handle, size);
        return true;
    }

    private static string CrestUrl(Dictionary<string, string> f)
    {
        var custom = Get(f, "logo_url");
        if ((custom.StartsWith("https://") || custom.StartsWith("http://")) && !Plugin.Images.HasFailed(custom))
            return custom;
        return DefaultCrest;
    }

    public override void Draw()
    {
        Dictionary<string, string>? f; List<SheetClient.Plot>? p; string s;
        lock (gate) { f = faction; p = plots; s = status; }

        if (ImGui.BeginTabBar("kinship_tabs"))
        {
            if (ImGui.BeginTabItem("Home")) { DrawHome(f); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Announcements")) { DrawAnnouncements(f); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Plots")) { DrawPlots(p); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }

        ImGui.Separator();
        if (s.Length > 0) ImGui.TextColored(Dim, s);
        if (ImGui.Button("Refresh")) Refresh();
    }

    private static void DrawHome(Dictionary<string, string>? f)
    {
        var crestUrl = f != null ? CrestUrl(f) : DefaultCrest;
        DrawCentered(crestUrl, 120f);
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
