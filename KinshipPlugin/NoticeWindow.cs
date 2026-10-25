using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace KinshipPlugin;

/// <summary>The small plot notice that pops up in the corner at login.</summary>
public sealed class NoticeWindow : Window, IDisposable
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private readonly Action openMain;
    private bool placed;

    public NoticeWindow(Action openMain) : base("Current Plot##KinshipNotice", ImGuiWindowFlags.NoCollapse)
    {
        this.openMain = openMain;
        Size = new Vector2(340, 280);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(280, 160),
            MaximumSize = new Vector2(520, 700)
        };
        RespectCloseHotkey = false;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        // First time only: tuck it into the top right corner. After that it stays wherever you drag it.
        if (!placed)
        {
            var vp = ImGuiHelpers.MainViewport;
            Position = new Vector2(vp.Pos.X + vp.Size.X - 360f, vp.Pos.Y + 70f);
            PositionCondition = ImGuiCond.FirstUseEver;
            placed = true;
        }
    }

    public override void Draw()
    {
        var (_, plots, _) = Plugin.Data.Snapshot();

        var wrap = Plugin.Images.Get(ImageCache.SiteImages + "kinship-crest.png");
        if (wrap != null)
        {
            var h = 72f;
            var size = new Vector2(wrap.Width * h / wrap.Height, h);
            var avail = ImGui.GetContentRegionAvail().X;
            if (avail > size.X) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (avail - size.X) / 2f);
            ImGui.Image(wrap.Handle, size);
        }

        if (plots == null || plots.Count == 0)
        {
            ImGui.TextColored(Dim, "No active plot right now.");
        }
        else
        {
            for (var i = 0; i < plots.Count; i++)
            {
                ImGui.TextColored(Gold, plots[i].Title);
                if (plots[i].Summary.Length > 0) ImGui.TextWrapped(plots[i].Summary);
                if (i < plots.Count - 1) { ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing(); }
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        if (ImGui.Button("Open The Kinship")) { openMain(); }
        ImGui.SameLine();
        if (ImGui.Button("Dismiss")) IsOpen = false;
    }
}
