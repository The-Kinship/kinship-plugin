using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace KinshipPlugin;

public sealed class MainWindow : Window, IDisposable
{
    private readonly object gate = new();
    private Dictionary<string, string>? faction;
    private string status = "Loading...";
    private bool loading;

    public MainWindow() : base("The Kinship##KinshipMain", ImGuiWindowFlags.None)
    {
        Size = new Vector2(400, 300);
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
                var data = await SheetClient.FetchFactionAsync().ConfigureAwait(false);
                lock (gate) { faction = data; status = ""; }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "Could not load the faction sheet.");
                lock (gate) { status = "Couldn't load the faction info. Check your connection and press Refresh."; }
            }
            finally { lock (gate) { loading = false; } }
        });
    }

    private static string Get(Dictionary<string, string> d, string key, string fallback = "")
        => d.TryGetValue(key, out var v) && v.Length > 0 ? v : fallback;

    public override void Draw()
    {
        Dictionary<string, string>? f; string s;
        lock (gate) { f = faction; s = status; }

        if (f != null)
        {
            ImGui.TextColored(new Vector4(0.92f, 0.78f, 0.35f, 1f), Get(f, "faction_name", "The Kinship"));
            var tag = Get(f, "tagline");
            if (tag.Length > 0) ImGui.TextWrapped(tag);
            ImGui.Separator();
            ImGui.Text($"{Get(f, "data_center", "Crystal")}  ·  {Get(f, "world", "Mateus")}");
            var members = Get(f, "member_count");
            if (members.Length > 0) ImGui.Text($"Members: {members}");
            var desc = Get(f, "description");
            if (desc.Length > 0) { ImGui.Spacing(); ImGui.TextWrapped(desc); }
        }

        if (s.Length > 0) ImGui.TextWrapped(s);

        ImGui.Spacing();
        if (ImGui.Button("Refresh")) Refresh();
    }
}
