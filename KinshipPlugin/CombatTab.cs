using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

/// <summary>The Combat tab: join a room, watch the encounter, roll, and end turns.</summary>
public sealed class CombatTab
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.5f, 1f);
    private static readonly int[] Dice = { 4, 6, 8, 10, 12, 20, 100 };
    private const string Banner = ImageCache.SiteImages + "kinship-combat-banner.jpg";

    private string name = Plugin.Config.CombatName;
    private string codeInput = "";
    private string kind = "attack";
    private int dieIndex = 5;      // d20
    private int modifier;
    private string targetId = "";

    public void Draw()
    {
        DrawBanner();
        var (room, myId, error, busy) = Plugin.Combat.Snapshot();

        if (!Plugin.Combat.Connected) { DrawConnect(error, busy); return; }

        Plugin.Combat.PollIfDue();
        if (room == null)
        {
            ImGui.TextColored(Dim, "Connecting to the encounter...");
            if (error.Length > 0) ImGui.TextColored(Red, error);
            if (ImGui.Button("Leave room")) Plugin.Combat.Leave();
            return;
        }
        DrawRoom(room, myId, error, busy);
    }

    private static void DrawBanner()
    {
        var wrap = Plugin.Images.Get(Banner);
        if (wrap == null) return;
        var avail = ImGui.GetContentRegionAvail().X;
        var scale = Math.Min(avail / wrap.Width, 90f / wrap.Height);
        var size = new Vector2(wrap.Width * scale, wrap.Height * scale);
        if (avail > size.X) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (avail - size.X) / 2f);
        ImGui.Image(wrap.Handle, size);
        ImGui.Spacing();
    }

    // ---------- not in a room yet ----------
    private void DrawConnect(string error, bool busy)
    {
        ImGui.TextColored(Gold, "Join an encounter");
        ImGui.TextWrapped("Your DM gives you a room code or an invite link. Paste either one here.");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(240f);
        ImGui.InputText("Your name", ref name, 60);
        ImGui.SetNextItemWidth(240f);
        var enter = ImGui.InputText("Room code or link", ref codeInput, 300, ImGuiInputTextFlags.EnterReturnsTrue);

        if (error.Length > 0) ImGui.TextColored(Red, error);
        if (busy) ImGui.TextColored(Dim, "Working...");
        else if (ImGui.Button("Join") || enter) _ = Plugin.Combat.JoinAsync(codeInput, name);

        ImGui.Spacing();
        ImGui.TextColored(Dim, "The DM sets up the encounter. For now that is done on the Kinship Combat website; setting up from inside the game is coming next.");
    }

    // ---------- inside a room ----------
    private void DrawRoom(CRoom room, string myId, string error, bool busy)
    {
        var me = room.Members.FirstOrDefault(m => m.Id == myId);
        var isDm = me?.Role == "dm";
        var actor = room.Order.Count > room.Index ? room.People.FirstOrDefault(p => p.Id == room.Order[room.Index]) : null;
        var inCombat = room.Phase == "combat";
        var canAct = inCombat && actor != null && actor.Hp > 0 && (actor.Type == "NPC" ? isDm : actor.OwnerId == myId);

        ImGui.TextColored(Gold, room.Name);
        ImGui.TextColored(Dim, inCombat ? $"Round {room.Round}  ·  in combat" : "Setting up");
        if (!string.IsNullOrEmpty(room.Location)) ImGui.TextColored(Dim, "Location: " + room.Location);
        ImGui.Text("Room " + room.Code);
        ImGui.SameLine(); if (ImGui.SmallButton("Copy code")) ImGui.SetClipboardText(room.Code);
        ImGui.SameLine(); if (ImGui.SmallButton("Copy invite link")) ImGui.SetClipboardText(CombatState.InviteBase + room.Code);
        ImGui.SameLine(); if (ImGui.SmallButton("Leave room")) { Plugin.Combat.Leave(); return; }
        if (!string.IsNullOrEmpty(room.Notes)) { ImGui.Spacing(); ImGui.TextWrapped(room.Notes); }
        ImGui.Separator();

        if (error.Length > 0) ImGui.TextColored(Red, error);

        // ----- the cast / turn order -----
        if (!inCombat)
        {
            ImGui.TextColored(Dim, isDm
                ? "You are the DM. Add characters and start the encounter on the website for now; doing it here is coming next."
                : "Waiting for the DM to start the encounter...");
            ImGui.Spacing();
            foreach (var p in room.People) DrawPerson(p, null, false);
        }
        else
        {
            for (var i = 0; i < room.Order.Count; i++)
            {
                var p = room.People.FirstOrDefault(x => x.Id == room.Order[i]);
                if (p != null) DrawPerson(p, room, i == room.Index);
            }
        }

        // ----- your controls -----
        if (inCombat && actor != null)
        {
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            if (canAct) DrawRollPanel(room, actor, busy);
            else ImGui.TextColored(Dim, $"Waiting for {actor.Name}'s turn.");

            if (isDm)
            {
                if (!canAct && ImGui.Button("Next turn")) Plugin.Combat.Act("next", new { });
                if (room.UndoAvailable)
                {
                    if (!canAct) ImGui.SameLine();
                    if (ImGui.Button("Undo last action")) Plugin.Combat.Act("undo", new { });
                }
            }
        }

        // ----- what just happened -----
        var lr = room.LastRoll;
        if (lr != null)
        {
            var a = room.People.FirstOrDefault(x => x.Id == lr.ActorId)?.Name ?? "?";
            var t = room.People.FirstOrDefault(x => x.Id == lr.TargetId)?.Name ?? "?";
            var verb = lr.Kind == "heal" ? "heals" : "attacks";
            var mod = lr.Modifier == 0 ? "" : (lr.Modifier > 0 ? $"+{lr.Modifier}" : lr.Modifier.ToString());
            ImGui.Spacing();
            ImGui.TextColored(Gold, "Last roll");
            ImGui.TextWrapped($"{a} {verb} {t}: d{lr.Sides} rolled {lr.Raw}{mod} = {lr.Total}.  {t}: {lr.Before} → {lr.After} HP");
        }

        if (ImGui.CollapsingHeader("Encounter log"))
        {
            foreach (var entry in room.Log.Take(12)) ImGui.TextWrapped(entry.Message);
            if (room.Log.Count == 0) ImGui.TextColored(Dim, "Nothing yet.");
        }
        if (ImGui.CollapsingHeader("Room members"))
        {
            foreach (var m in room.Members)
            {
                ImGui.TextColored(m.Online ? Green : Dim, m.Online ? "●" : "○");
                ImGui.SameLine();
                ImGui.Text(m.Name + (m.Role == "dm" ? "  (DM)" : ""));
            }
        }
    }

    private static void DrawPerson(CPerson p, CRoom? room, bool current)
    {
        var downed = p.Hp <= 0;
        ImGui.TextColored(current ? Gold : Dim, current ? "▶" : " ");
        ImGui.SameLine();
        ImGui.TextColored(downed ? Dim : new Vector4(1, 1, 1, 1), p.Name);
        ImGui.SameLine();
        ImGui.TextColored(Dim, p.Type == "NPC" ? "(NPC)" : "");

        var frac = p.Max > 0 ? Math.Clamp((float)p.Hp / p.Max, 0f, 1f) : 0f;
        var color = frac > 0.5f ? new Vector4(0.25f, 0.65f, 0.35f, 1f) : frac > 0.25f ? new Vector4(0.85f, 0.65f, 0.2f, 1f) : new Vector4(0.8f, 0.25f, 0.2f, 1f);
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, color);
        ImGui.ProgressBar(frac, new Vector2(ImGui.GetContentRegionAvail().X - 8f, 16f), downed ? "Downed" : $"{p.Hp} / {p.Max}");
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    private void DrawRollPanel(CRoom room, CPerson actor, bool busy)
    {
        ImGui.TextColored(Gold, $"Your turn: {actor.Name}");

        if (ImGui.RadioButton("Attack", kind == "attack")) kind = "attack";
        ImGui.SameLine();
        if (ImGui.RadioButton("Heal", kind == "heal")) kind = "heal";

        ImGui.SetNextItemWidth(90f);
        if (ImGui.BeginCombo("Die", "d" + Dice[dieIndex]))
        {
            for (var i = 0; i < Dice.Length; i++)
                if (ImGui.Selectable("d" + Dice[i], i == dieIndex)) dieIndex = i;
            ImGui.EndCombo();
        }
        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("Modifier", ref modifier);
        modifier = Math.Clamp(modifier, -100, 100);

        if (room.People.All(p => p.Id != targetId)) targetId = room.People.FirstOrDefault()?.Id ?? "";
        var target = room.People.FirstOrDefault(p => p.Id == targetId);
        ImGui.SetNextItemWidth(220f);
        if (ImGui.BeginCombo("Target", target == null ? "" : $"{target.Name} ({target.Hp}/{target.Max})"))
        {
            foreach (var p in room.People)
                if (ImGui.Selectable($"{p.Name} ({p.Hp}/{p.Max})##t{p.Id}", p.Id == targetId)) targetId = p.Id;
            ImGui.EndCombo();
        }

        if (busy) { ImGui.TextColored(Dim, "Working..."); return; }
        if (ImGui.Button(kind == "heal" ? "Roll heal" : "Roll attack") && target != null)
            Plugin.Combat.Act("roll", new { kind, targetId, sides = Dice[dieIndex], modifier });
        ImGui.SameLine();
        if (ImGui.Button("End turn")) Plugin.Combat.Act("next", new { });
    }
}
