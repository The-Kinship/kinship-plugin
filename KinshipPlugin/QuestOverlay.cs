using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace KinshipPlugin;

/// <summary>An invisible full-screen layer that writes a few words in the world at a quest spot when you are near it.</summary>
public sealed class QuestOverlay : Window, IDisposable
{
    private static readonly Vector4 Parchment = new(0.86f, 0.80f, 0.62f, 1f);

    public QuestOverlay() : base("Quest spots##KinshipQuestOverlay",
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse |
        ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing |
        ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNav)
    {
        RespectCloseHotkey = false;
        ShowCloseButton = false;
        IsOpen = true;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        var vp = ImGuiHelpers.MainViewport;
        Position = vp.Pos; PositionCondition = ImGuiCond.Always;
        Size = vp.Size; SizeCondition = ImGuiCond.Always;
    }

    public override bool DrawConditions() => Plugin.Config.ItemsToken.Length > 0 && Plugin.Quests.Active().Count > 0;

    public override void Draw()
    {
        foreach (var q in Plugin.Quests.Active())
        {
            var c = q.Current;
            if (c?.Place == null || c.Type is not ("look" or "listen" or "search")) continue;
            var d = c.Place.Distance();
            if (d == null || d > QuestState.ActionRange) continue;
            var pos = new Vector3(c.Place.X, c.Place.Y, c.Place.Z);
            if (!Plugin.GameGui.WorldToScreen(pos + new Vector3(0, 1.0f, 0), out var screen)) continue;
            var text = QuestState.Phrase(c.Type);
            var w = ImGui.CalcTextSize(text).X;
            ImGui.SetCursorScreenPos(new Vector2(screen.X - w / 2f, screen.Y - 8f));
            ImGui.TextColored(Parchment, text);
        }
    }
}
