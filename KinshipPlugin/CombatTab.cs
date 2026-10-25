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

    // create-a-room form
    private string newEncounter = "";

    // DM setup state
    private static readonly string[] Modes = { "random", "manual", "initiative" };
    private static readonly string[] ModeLabels = { "Random order", "Manual order (use the order numbers)", "Roll initiative (d20)" };
    private string loadedFor = "";
    private string sName = "", sLocation = "", sNotes = "";
    private int sMode;
    private string? editId;
    private string fName = "", fDetails = "", fOwnerId = "";
    private int fType;                 // 0 = Player, 1 = NPC
    private int fHp = 10, fMax = 10, fNumber = 1;

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


        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, "Run an encounter (DM)");
        ImGui.TextWrapped("Start a new room. You will be its DM, and you can share the room code or invite link with your players.");
        ImGui.SetNextItemWidth(240f);
        ImGui.InputText("Encounter name", ref newEncounter, 90);
        if (!busy && ImGui.Button("Create room")) _ = Plugin.Combat.CreateAsync(name, newEncounter);
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
            if (isDm) DrawSetup(room, busy);
            else
            {
                ImGui.TextColored(Dim, "Waiting for the DM to start the encounter...");
                ImGui.Spacing();
                foreach (var p in room.People) DrawPerson(p, null, false);
            }
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
                if (ImGui.Button("Reroll initiative")) Plugin.Combat.Act("initiative", new { });
                ImGui.SameLine();
                if (ImGui.Button("Back to setup")) Plugin.Combat.Act("setup", new { });
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

    // ---------- DM setup ----------
    private void DrawSetup(CRoom room, bool busy)
    {
        if (loadedFor != room.Code)
        {
            sName = room.Name; sLocation = room.Location ?? ""; sNotes = room.Notes ?? "";
            sMode = Math.Max(0, Array.IndexOf(Modes, room.Mode));
            loadedFor = room.Code; fNumber = room.People.Count + 1;
        }

        ImGui.TextColored(Gold, "You are the DM. Set up the encounter:");
        ImGui.Spacing();
        ImGui.SetNextItemWidth(260f); ImGui.InputText("Encounter name##s", ref sName, 90);
        ImGui.SetNextItemWidth(260f); ImGui.InputText("Location##s", ref sLocation, 90);
        ImGui.InputTextMultiline("##snotes", ref sNotes, 1000, new Vector2(ImGui.GetContentRegionAvail().X - 8f, 56f));
        ImGui.TextColored(Dim, "Scene notes (everyone sees these)");
        ImGui.SetNextItemWidth(260f);
        if (ImGui.BeginCombo("Turn order##s", ModeLabels[sMode]))
        {
            for (var i = 0; i < Modes.Length; i++)
                if (ImGui.Selectable(ModeLabels[i], i == sMode)) sMode = i;
            ImGui.EndCombo();
        }
        if (ImGui.Button("Save settings"))
            Plugin.Combat.Act("settings", new { name = sName, location = sLocation, notes = sNotes, mode = Modes[sMode] });

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, "The cast");
        if (room.People.Count == 0) ImGui.TextColored(Dim, "No characters yet. Add at least two below.");
        foreach (var p in room.People)
        {
            var owner = p.Type == "Player" ? room.Members.FirstOrDefault(m => m.Id == p.OwnerId)?.Name ?? "unassigned" : "DM";
            ImGui.Text($"{p.Name}  ({p.Type})  HP {p.Hp}/{p.Max}  ·  #{p.Number}  ·  {owner}");
            ImGui.SameLine();
            if (ImGui.SmallButton("Edit##e" + p.Id)) LoadForm(p);
            ImGui.SameLine();
            if (ImGui.SmallButton("Remove##r" + p.Id)) { Plugin.Combat.Act("remove", new { id = p.Id }); if (editId == p.Id) ClearForm(room); }
        }

        ImGui.Spacing();
        ImGui.TextColored(Gold, editId == null ? "Add a character" : "Editing a character");
        ImGui.SetNextItemWidth(220f); ImGui.InputText("Name##f", ref fName, 60);
        if (ImGui.RadioButton("Player", fType == 0)) fType = 0;
        ImGui.SameLine();
        if (ImGui.RadioButton("NPC", fType == 1)) fType = 1;
        ImGui.SetNextItemWidth(110f); ImGui.InputInt("HP##f", ref fHp);
        ImGui.SetNextItemWidth(110f); ImGui.InputInt("Max HP##f", ref fMax);
        ImGui.SetNextItemWidth(110f); ImGui.InputInt("Order number##f", ref fNumber);
        ImGui.TextColored(Dim, "Order number only matters for Manual order.");
        ImGui.SetNextItemWidth(260f); ImGui.InputText("Details##f", ref fDetails, 300);
        fMax = Math.Clamp(fMax, 1, 99999); fHp = Math.Clamp(fHp, 0, fMax); fNumber = Math.Clamp(fNumber, 1, 999);

        if (fType == 0)
        {
            var current = room.Members.FirstOrDefault(m => m.Id == fOwnerId)?.Name ?? "(choose a member)";
            ImGui.SetNextItemWidth(220f);
            if (ImGui.BeginCombo("Played by##f", current))
            {
                foreach (var m in room.Members)
                    if (ImGui.Selectable(m.Name + (m.Role == "dm" ? " (DM)" : "") + "##o" + m.Id, m.Id == fOwnerId)) fOwnerId = m.Id;
                ImGui.EndCombo();
            }
        }

        if (!busy)
        {
            if (ImGui.Button(editId == null ? "Add character" : "Save changes"))
            {
                Plugin.Combat.Act("participant", new
                {
                    id = editId, name = fName, type = fType == 0 ? "Player" : "NPC", max = fMax, hp = fHp,
                    number = fNumber, details = fDetails, ownerId = fType == 0 && fOwnerId.Length > 0 ? fOwnerId : null
                });
                ClearForm(room, room.People.Count + (editId == null ? 2 : 1));
            }
            if (editId != null) { ImGui.SameLine(); if (ImGui.Button("Cancel edit")) ClearForm(room); }
        }

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextWrapped("Players must join the room first (send them the code), then be picked under \"Played by\" before you can start.");
        if (!busy && ImGui.Button("Start the encounter")) Plugin.Combat.Act("start", new { });
    }

    private void LoadForm(CPerson p)
    {
        editId = p.Id; fName = p.Name; fType = p.Type == "NPC" ? 1 : 0; fHp = p.Hp; fMax = p.Max;
        fNumber = p.Number; fDetails = p.Details ?? ""; fOwnerId = p.OwnerId ?? "";
    }

    private void ClearForm(CRoom room, int? nextNumber = null)
    {
        editId = null; fName = ""; fDetails = ""; fOwnerId = ""; fHp = 10; fMax = 10;
        fNumber = nextNumber ?? room.People.Count + 1;
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
