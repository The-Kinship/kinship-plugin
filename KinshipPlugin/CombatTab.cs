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
    private string targetId = "";

    // create-a-room form
    private string newEncounter = "";

    // DM setup state
    private static readonly string[] Modes = { "random", "manual" };
    private static readonly string[] ModeLabels = { "Random order", "Manual order (use the order numbers)" };
    private static readonly string[] Kinds = { "classic", "lastStanding", "spar", "timed" };
    private static readonly string[] KindLabels = { "Classic: players vs NPCs", "Last one standing: free for all", "Spar: 1v1, first to fall loses", "Timed: classic with a clock" };
    private static readonly string[] KindHints =
    {
        "Players win when every NPC is down. NPCs win when every player is down.",
        "Everyone fights everyone. It ends when only one person is left.",
        "A 1v1 duel with exactly two participants. Whoever goes down first loses.",
        "Classic rules plus a clock. If time runs out, the side with more total HP wins."
    };
    private int sKind, sMinutes = 15;
    private string fLoot = "", fMods = "", modsError = "";
    private int seenDrops = -1;
    private string dropNotice = "";
    private DateTime dropUntil = DateTime.MinValue;
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
        UiUtil.CreatePasswordInput();
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

        // A loot popup each time someone with loot goes down.
        if (seenDrops < 0) seenDrops = room.Drops.Count;
        if (room.Drops.Count > seenDrops)
        {
            dropNotice = string.Join("   ", room.Drops.Skip(seenDrops).Select(d => $"{d.Name} dropped loot: {d.Loot}"));
            dropUntil = DateTime.UtcNow.AddSeconds(12);
        }
        seenDrops = room.Drops.Count;
        if (dropNotice.Length > 0 && DateTime.UtcNow < dropUntil) { ImGui.TextColored(Gold, dropNotice); ImGui.Spacing(); }

        ImGui.TextColored(Gold, room.Name);
        var timeLeft = "";
        if (inCombat && !string.IsNullOrEmpty(room.EndsAt) && DateTimeOffset.TryParse(room.EndsAt, out var endsAt))
        {
            var left = endsAt - DateTimeOffset.UtcNow; if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            timeLeft = $"  ·  time left {(int)left.TotalMinutes}:{left.Seconds:00}";
        }
        ImGui.TextColored(Dim, room.Phase == "ended" ? "Encounter over" : inCombat ? $"Round {room.Round}  ·  in combat{timeLeft}" : "Setting up");
        if (!string.IsNullOrEmpty(room.Location)) ImGui.TextColored(Dim, "Location: " + room.Location);
        ImGui.Text("Room " + room.Code);
        ImGui.SameLine(); if (ImGui.SmallButton("Copy code")) ImGui.SetClipboardText(room.Code);
        ImGui.SameLine(); if (ImGui.SmallButton("Copy invite link")) ImGui.SetClipboardText(CombatState.InviteBase + room.Code);
        ImGui.SameLine(); if (ImGui.SmallButton("Leave room")) { Plugin.Combat.Leave(); return; }
        if (!string.IsNullOrEmpty(room.Notes)) { ImGui.Spacing(); ImGui.TextWrapped(room.Notes); }
        ImGui.Separator();

        if (error.Length > 0) ImGui.TextColored(Red, error);

        if (room.Phase == "ended") { DrawEnded(room, isDm, busy); return; }

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
            ImGui.Spacing();
            ImGui.TextColored(Gold, "Last roll");
            var rolled = lr.Mods is { Count: > 0 } ? $"d{lr.Sides} rolled {lr.Raw}, {CMod.Describe(lr.Mods)} = {lr.Total}." : $"d{lr.Sides} rolled {lr.Raw}.";
            ImGui.TextWrapped($"{a} {verb} {t}: {rolled}  {t}: {lr.Before} → {lr.After} HP");
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
            sKind = Math.Max(0, Array.IndexOf(Kinds, room.Kind)); sMinutes = room.Minutes > 0 ? room.Minutes : 15;
            loadedFor = room.Code; fNumber = room.People.Count + 1;
        }

        ImGui.TextColored(Gold, "You are the DM. Set up the encounter:");
        ImGui.Spacing();
        ImGui.SetNextItemWidth(260f); ImGui.InputText("Encounter name##s", ref sName, 90);
        ImGui.SetNextItemWidth(260f); ImGui.InputText("Location##s", ref sLocation, 90);
        ImGui.InputTextMultiline("##snotes", ref sNotes, 1000, new Vector2(ImGui.GetContentRegionAvail().X - 8f, 56f));
        ImGui.TextColored(Dim, "Scene notes (everyone sees these)");
        ImGui.SetNextItemWidth(260f);
        if (ImGui.BeginCombo("Encounter type##s", KindLabels[sKind]))
        {
            for (var i = 0; i < Kinds.Length; i++)
                if (ImGui.Selectable(KindLabels[i], i == sKind)) sKind = i;
            ImGui.EndCombo();
        }
        ImGui.TextColored(Dim, KindHints[sKind]);
        if (Kinds[sKind] == "timed")
        {
            ImGui.SetNextItemWidth(110f); ImGui.InputInt("Minutes##s", ref sMinutes);
            sMinutes = Math.Clamp(sMinutes, 1, 180);
        }
        ImGui.SetNextItemWidth(260f);
        if (ImGui.BeginCombo("Turn order##s", ModeLabels[sMode]))
        {
            for (var i = 0; i < Modes.Length; i++)
                if (ImGui.Selectable(ModeLabels[i], i == sMode)) sMode = i;
            ImGui.EndCombo();
        }
        if (ImGui.Button("Save settings"))
            Plugin.Combat.Act("settings", new { name = sName, location = sLocation, notes = sNotes, mode = Modes[sMode], kind = Kinds[sKind], minutes = sMinutes });

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, "The cast");
        if (room.People.Count == 0) ImGui.TextColored(Dim, "No characters yet. Add at least two below.");
        foreach (var p in room.People)
        {
            var owner = p.Type == "Player" ? room.Members.FirstOrDefault(m => m.Id == p.OwnerId)?.Name ?? "unassigned" : "DM";
            ImGui.Text($"{p.Name}  ({p.Type})  HP {p.Hp}/{p.Max}  ·  #{p.Number}  ·  {owner}" + (string.IsNullOrEmpty(p.Loot) ? "" : $"  ·  loot: {p.Loot}") + (p.Mods is { Count: > 0 } ? $"  ·  mods: {CMod.Describe(p.Mods)}" : ""));
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
        ImGui.SetNextItemWidth(260f); ImGui.InputText("Loot (optional)##f", ref fLoot, 200);
        ImGui.TextColored(Dim, "Hidden from players until this character goes down.");
        ImGui.SetNextItemWidth(260f); ImGui.InputText("Roll modifiers (optional)##f", ref fMods, 200);
        ImGui.TextColored(Dim, "e.g. +1 Sword, -2 Sick. Added to every roll this character makes, for the whole encounter. Check items first.");
        if (modsError.Length > 0) ImGui.TextColored(Red, modsError);
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
                var mods = CMod.Parse(fMods);
                if (mods == null) modsError = "Modifiers look like +1 Sword, -2 Sick (up to 6, each name up to 30 characters).";
                else
                {
                modsError = "";
                Plugin.Combat.Act("participant", new
                {
                    id = editId, name = fName, type = fType == 0 ? "Player" : "NPC", max = fMax, hp = fHp,
                    number = fNumber, details = fDetails, loot = fLoot, mods, ownerId = fType == 0 && fOwnerId.Length > 0 ? fOwnerId : null
                });
                ClearForm(room, room.People.Count + (editId == null ? 2 : 1));
                }
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
        fNumber = p.Number; fDetails = p.Details ?? ""; fLoot = p.Loot ?? ""; fMods = CMod.Describe(p.Mods); modsError = ""; fOwnerId = p.OwnerId ?? "";
    }

    private void ClearForm(CRoom room, int? nextNumber = null)
    {
        editId = null; fName = ""; fDetails = ""; fLoot = ""; fMods = ""; modsError = ""; fOwnerId = ""; fHp = 10; fMax = 10;
        fNumber = nextNumber ?? room.People.Count + 1;
    }

    // ---------- the "Encounter over" summary ----------
    private static void BoldWrapped(string text, Vector4 color)
    {
        // Draws the text twice, one pixel apart, which reads as bold.
        var start = ImGui.GetCursorPos();
        ImGui.PushStyleColor(ImGuiCol.Text, color); ImGui.TextWrapped(text); ImGui.PopStyleColor();
        var end = ImGui.GetCursorPos();
        ImGui.SetCursorPos(new Vector2(start.X + 1f, start.Y));
        ImGui.PushStyleColor(ImGuiCol.Text, color); ImGui.TextWrapped(text); ImGui.PopStyleColor();
        ImGui.SetCursorPos(end);
    }

    private void DrawEnded(CRoom room, bool isDm, bool busy)
    {
        var r = room.Result;
        ImGui.Spacing();
        ImGui.TextColored(Gold, r?.Headline ?? "The encounter is over.");
        if (!string.IsNullOrEmpty(r?.Detail)) ImGui.TextWrapped(r!.Detail);
        var rounds = r?.Rounds ?? room.Round;
        ImGui.TextColored(Dim, $"{room.Name}  ·  {rounds} round{(rounds == 1 ? "" : "s")}");
        ImGui.Separator();

        ImGui.TextColored(Gold, "FINAL STANDINGS");
        foreach (var s in r?.Standings ?? new())
        {
            var winner = r!.Winners.Contains(s.Name);
            ImGui.TextColored(winner ? Gold : new Vector4(1, 1, 1, 1), s.Name);
            ImGui.SameLine(); ImGui.TextColored(Dim, $"({s.Type})");
            if (winner) { ImGui.SameLine(); ImGui.TextColored(Gold, "Winner"); }
            ImGui.SameLine(); ImGui.TextColored(Dim, s.Hp <= 0 ? "·  Downed" : $"·  {s.Hp} / {s.Max} HP");
        }

        if (r != null && r.Loot.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(Gold, "LOOT");
            foreach (var l in r.Loot) ImGui.TextWrapped($"{l.Name} dropped loot: {l.Loot}");
        }

        if (!string.IsNullOrEmpty(r?.Note))
        {
            ImGui.Spacing(); ImGui.Spacing();
            BoldWrapped(r!.Note, new Vector4(1f, 1f, 1f, 1f));
            ImGui.Spacing();
        }

        ImGui.Separator();
        if (isDm && room.UndoAvailable && r?.Reason != "time")
        {
            if (!busy && ImGui.Button("Undo last action")) Plugin.Combat.Act("undo", new { });
            ImGui.SameLine();
        }
        if (isDm) { if (!busy && ImGui.Button("Close encounter")) _ = Plugin.Combat.CloseRoomAsync(); }
        else if (ImGui.Button("Close")) Plugin.Combat.Leave();
    }

    private static void DrawPerson(CPerson p, CRoom? room, bool current)
    {
        var downed = p.Hp <= 0;
        ImGui.TextColored(current ? Gold : Dim, current ? "▶" : " ");
        ImGui.SameLine();
        ImGui.TextColored(downed ? Dim : new Vector4(1, 1, 1, 1), p.Name);
        ImGui.SameLine();
        ImGui.TextColored(Dim, p.Type == "NPC" ? "(NPC)" : "");
        if (p.Mods is { Count: > 0 }) { ImGui.SameLine(); ImGui.TextColored(new Vector4(0.9f, 0.75f, 0.35f, 1f), CMod.Describe(p.Mods)); }

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
            Plugin.Combat.Act("roll", new { kind, targetId, sides = Dice[dieIndex] });
        ImGui.SameLine();
        if (ImGui.Button("Pass turn")) Plugin.Combat.Act("next", new { });
        ImGui.TextColored(Dim, "Rolling uses your turn.");
    }
}
