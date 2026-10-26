using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace KinshipPlugin;

/// <summary>A small window that appears when you stand close to a sign. Read it to see what it says.</summary>
public sealed class SignReaderWindow : Window, IDisposable
{
    private const float Width = 380f;
    private static readonly Vector4 Parchment = new(0.86f, 0.80f, 0.62f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);

    public SignReaderWindow() : base("Signs nearby##KinshipSignReader",
        ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing)
    {
        RespectCloseHotkey = false;
        ShowCloseButton = false;
        IsOpen = true;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(Width, 40f), MaximumSize = new Vector2(Width, 2000f) };
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        var vp = Dalamud.Interface.Utility.ImGuiHelpers.MainViewport;
        Position = new Vector2(vp.Pos.X + vp.Size.X / 2f - Width / 2f, vp.Pos.Y + vp.Size.Y * 0.62f);
        PositionCondition = ImGuiCond.FirstUseEver;
    }

    private static bool AnyNear()
    {
        var territory = Plugin.ClientState.TerritoryType;
        return Plugin.Signs.All.Any(s => s.Territory == territory && s.Near());
    }

    public override bool DrawConditions()
    {
        if (Plugin.Config.ItemsToken.Length == 0) return false;
        return Plugin.Signs.Reading != null || AnyNear();
    }

    public override void Draw()
    {
        var reading = Plugin.Signs.Reading;
        if (reading != null) { DrawReading(reading); return; }

        ImGui.TextColored(Parchment, "Something here");
        var near = Plugin.Signs.All.Where(s => s.Territory == Plugin.ClientState.TerritoryType && s.Near())
            .OrderBy(s => s.Distance() ?? 0f).Take(5).ToList();
        foreach (var s in near)
        {
            ImGui.Text(s.Name);
            ImGui.SameLine();
            if (ImGui.SmallButton("Look##r" + s.Id)) _ = Plugin.Signs.ReadAsync(s.Id);
        }
        var err = Plugin.Signs.Snapshot().Error;
        if (err.Length > 0) ImGui.TextColored(new Vector4(0.95f, 0.4f, 0.35f, 1f), err);
    }

    private static void DrawReading(SignView s)
    {
        ImGui.TextColored(Parchment, s.Name);
        ImGui.Separator();
        ImGui.PushTextWrapPos(Width - 16f);
        ImGui.TextUnformatted(s.Description ?? "");
        ImGui.PopTextWrapPos();
        ImGui.Separator();
        ImGui.TextColored(Dim, $"left by {s.OwnerName}  ·  {s.Zone}");
        if (ImGui.SmallButton("Close##sr")) Plugin.Signs.CloseReading();
        ImGui.SameLine();
        if (s.MapX > 0 && ImGui.SmallButton("Show on map##sr")) SignState.ShowOnMap(s);
    }
}
