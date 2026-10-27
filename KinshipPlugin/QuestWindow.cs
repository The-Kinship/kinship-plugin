using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace KinshipPlugin;

/// <summary>A small movable window listing your quests, with an action bar, so the main window can stay closed.</summary>
public sealed class QuestWindow : Window, IDisposable
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.5f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);

    public QuestWindow() : base("Quests##KinshipQuestWindow", ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav)
    {
        RespectCloseHotkey = false;
        IsOpen = true;
        BgAlpha = 0.7f;
        Size = new Vector2(340f, 240f);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(220f, 120f), MaximumSize = new Vector2(700f, 900f) };
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        var vp = ImGuiHelpers.MainViewport;
        Position = new Vector2(vp.Pos.X + vp.Size.X - 330f, vp.Pos.Y + 220f);
        PositionCondition = ImGuiCond.FirstUseEver;
    }

    private bool dismissedStart;

    public override bool DrawConditions()
    {
        if (Plugin.Config.ItemsToken.Length == 0) return false;
        Plugin.Quests.RefreshIfDue(30);
        var near = Plugin.Quests.StartNear() != null;
        if (!near) dismissedStart = false;
        if (near && !dismissedStart) return true;     // shows even with the window turned off, so a quest can be picked up
        return Plugin.Config.QuestWindow && Plugin.Quests.Active().Count > 0;
    }

    public override void OnClose()
    {
        if (Plugin.Quests.Active().Count > 0) { Plugin.Config.QuestWindow = false; Plugin.Config.Save(); }
        else dismissedStart = true;
        IsOpen = true;
    }

    public override void Draw()
    {
        var s = Plugin.Quests.Snapshot();
        var active = Plugin.Quests.Active();

        if (active.Count == 0) ImGui.TextColored(Dim, "Something here catches your eye.");
        // The action bar is pinned to the bottom; the quests scroll above it.
        var barHeight = 64f;
        if (ImGui.BeginChild("##questlist", new Vector2(0f, Math.Max(40f, ImGui.GetContentRegionAvail().Y - barHeight))))
        {
            foreach (var q in active)
            {
                ImGui.TextColored(Gold, q.Title);
                ImGui.SameLine(); ImGui.TextColored(Dim, $"{q.Step + 1}/{q.Total}");
                if (q.Current != null)
                {
                    ImGui.PushTextWrapPos(0f);
                    ImGui.TextUnformatted(QuestsTab.Objective(q.Current));
                    if (q.Current.Place != null)
                    {
                        var d = q.Current.Place.Distance();
                        if (d == null) ImGui.TextColored(Dim, "Not in this zone.");
                        else if (d <= QuestState.ActionRange) ImGui.TextColored(Green, QuestState.Phrase(q.Current.Type));
                        else ImGui.TextColored(Dim, $"{d:0} yalms away");
                    }
                    if (q.Current.Hint.Length > 0) ImGui.TextColored(Dim, q.Current.Hint);
                    ImGui.PopTextWrapPos();
                }
                ImGui.Spacing();
            }
        }
        ImGui.EndChild();

        ImGui.Separator();
        string target = "";
        try { target = Plugin.TargetManager.Target?.Name.TextValue ?? ""; } catch (Exception) { target = ""; }
        var can = target.Length > 0 && !s.Busy;
        if (!can) ImGui.BeginDisabled();
        if (ImGui.Button("Speak##qw")) _ = Plugin.Quests.TalkAsync(target);
        if (!can) ImGui.EndDisabled();
        foreach (var (type, label) in new[] { ("look", "Look"), ("listen", "Listen"), ("search", "Search") })
        {
            ImGui.SameLine();
            var start = type == "look" ? Plugin.Quests.StartNear() : null;
            var stepReady = Plugin.Quests.Ready(type);
            var ready = !s.Busy && (stepReady || start != null);
            if (!ready) ImGui.BeginDisabled();
            if (ImGui.Button(label + "##qw" + type)) { if (stepReady) _ = Plugin.Quests.ActAsync(type); else if (start != null) _ = Plugin.Quests.OfferAsync(start.QuestId); }
            if (!ready) ImGui.EndDisabled();
        }
        ImGui.TextColored(target.Length > 0 ? Gold : Dim, target.Length > 0 ? target : "Target someone to speak");
        if (s.Error.Length > 0) ImGui.TextColored(Red, s.Error);
    }
}

/// <summary>Shows what an NPC said when you spoke to them, in its own small window so it works with the main window closed.</summary>
public sealed class QuestReplyWindow : Window, IDisposable
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.5f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);

    public QuestReplyWindow() : base("Conversation##KinshipQuestReply", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        RespectCloseHotkey = false;
        IsOpen = true;
        BgAlpha = 0.9f;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        var vp = ImGuiHelpers.MainViewport;
        Position = new Vector2(vp.Pos.X + vp.Size.X / 2f - 150f, vp.Pos.Y + vp.Size.Y / 2f - 100f);
        PositionCondition = ImGuiCond.Appearing;
    }

    public override bool DrawConditions() => Plugin.Quests.LastTalk() != null;
    public override void OnClose() { Plugin.Quests.DismissTalk(); IsOpen = true; }

    public override void Draw()
    {
        var t = Plugin.Quests.LastTalk();
        if (t == null) return;
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 26f);
        if (t.Error.Length > 0) ImGui.TextColored(Red, t.Error);
        else if (t.Lines.Count == 0) ImGui.TextUnformatted(t.Npc.Length > 0 ? $"{t.Npc} has nothing to say to you right now." : "You find nothing of note here.");
        else
            foreach (var l in t.Lines)
            {
                ImGui.TextColored(Gold, t.Npc.Length > 0 ? t.Npc + "  ·  " + l.Quest : l.Quest);
                ImGui.TextUnformatted(l.Text);
                if (l.Gained != null) foreach (var g in l.Gained) ImGui.TextColored(Green, "You received: " + g);
                if (!string.IsNullOrEmpty(l.StatusGained)) ImGui.TextColored(Green, "You gain: " + l.StatusGained);
                if (l.Ok && l.Finished) ImGui.TextColored(Green, "Quest complete!");
                ImGui.Spacing();
            }
        ImGui.PopTextWrapPos();
        if (ImGui.Button("Close##qr")) Plugin.Quests.DismissTalk();
    }
}

/// <summary>"Do you want to take this quest?" with Accept and Decline. Shown when you look at a quest spot or use a quest item.</summary>
public sealed class QuestOfferWindow : Window, IDisposable
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);

    public QuestOfferWindow() : base("A quest##KinshipQuestOffer", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        RespectCloseHotkey = false;
        IsOpen = true;
        BgAlpha = 0.92f;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        var vp = ImGuiHelpers.MainViewport;
        Position = new Vector2(vp.Pos.X + vp.Size.X / 2f - 170f, vp.Pos.Y + vp.Size.Y / 2f - 120f);
        PositionCondition = ImGuiCond.Appearing;
    }

    public override bool DrawConditions() => Plugin.Quests.LastOffer() != null;
    public override void OnClose() { Plugin.Quests.DismissOffer(); IsOpen = true; }

    public override void Draw()
    {
        var o = Plugin.Quests.LastOffer();
        if (o == null) return;
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 26f);
        if (o.Text.Length > 0) { ImGui.TextUnformatted(o.Text); ImGui.Spacing(); }
        ImGui.TextColored(Gold, o.Title);
        if (o.Intro.Length > 0) ImGui.TextUnformatted(o.Intro);
        ImGui.Spacing();
        ImGui.TextColored(Dim, "Do you want to take this quest?");
        ImGui.PopTextWrapPos();
        if (ImGui.Button("Accept##qo")) _ = Plugin.Quests.AcceptAsync(o);
        ImGui.SameLine();
        if (ImGui.Button("Decline##qo")) Plugin.Quests.DismissOffer();
    }
}
