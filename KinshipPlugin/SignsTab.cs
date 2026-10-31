using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

/// <summary>Signs and markers: leave a note in the world for others to find.</summary>
public sealed class SignsTab
{
    private static readonly Vector4 Parchment = new(0.86f, 0.80f, 0.62f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.5f, 1f);

    private string newName = "", newText = "";
    private string? managing, confirmDelete;
    private string editName = "", editText = "";

    public void Draw()
    {
        Plugin.Signs.TabOpen();
        Plugin.Signs.RefreshIfDue(15);
        var s = Plugin.Signs.Snapshot();

        ImGui.TextColored(Parchment, "Signs & markers");
        ImGui.TextWrapped("Leave a note in the world: a carving in a tree, a mark on a wall, a scrap of paper, a footprint, a clue. Stand where you want it, give it a name and write what someone finds when they look closer. Anyone who walks up to it can look.");
        var labels = Plugin.Config.SignLabels;
        if (ImGui.Checkbox("Show sign names in the world", ref labels)) { Plugin.Config.SignLabels = labels; Plugin.Config.Save(); }
        if (s.Error.Length > 0) ImGui.TextColored(Red, s.Error);
        else if (s.Notice.Length > 0) ImGui.TextColored(Green, s.Notice);
        ImGui.Spacing();

        DrawMake(s);
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        DrawList(s);
    }

    private int newGold;

    private void DrawMake(SignSnap s)
    {
        if (!ImGui.CollapsingHeader("Make a sign here")) return;
        var spot = WorldInfo.Current(out var problem);
        if (spot == null) { ImGui.TextColored(Red, problem.Replace("Stashes", "Signs").Replace("a stash", "a sign")); return; }
        ImGui.Text("You are in: " + spot.Zone);
        ImGui.SetNextItemWidth(260f); ImGui.InputText("Name##sign", ref newName, 40);
        ImGui.Text("What it says");
        ImGui.InputTextMultiline("##signtext", ref newText, 1000, new Vector2(ImGui.GetContentRegionAvail().X - 8f, 110f));
        ImGui.SetNextItemWidth(110f); ImGui.InputInt("Gold to leave (optional)##signgold", ref newGold, 0);
        if (newGold < 0) newGold = 0;
        ImGui.TextColored(Dim, $"Comes out of your purse ({Plugin.Items.Gold:N0} gold). The first person to read the sign takes it. You can take it back yourself at the sign, or by deleting the sign.");
        var mine = s.Signs.Count(x => x.Mine);
        var MaxMine = Plugin.Signs.MaxMine;
        if (mine >= MaxMine) ImGui.TextColored(Dim, $"You already have {MaxMine} signs. Delete one to make another.");
        var ok = newName.Trim().Length > 0 && newText.Trim().Length > 0 && !s.Busy && mine < MaxMine;
        if (!ok) ImGui.BeginDisabled();
        if (ImGui.Button("Make sign here")) { _ = Plugin.Signs.CreateAsync(newName, newText, spot, newGold); newName = ""; newText = ""; newGold = 0; }
        if (!ok) ImGui.EndDisabled();
    }

    private void DrawList(SignSnap s)
    {
        ImGui.TextColored(Parchment, "Signs");
        ImGui.SameLine(); if (ImGui.SmallButton("Refresh##signs")) _ = Plugin.Signs.RefreshAsync();
        var yours = s.Signs.Where(x => x.Mine).OrderBy(x => x.Zone).ThenBy(x => x.Name).ToList();
        var near = s.Signs.Where(x => !x.Mine).OrderBy(x => x.Distance() ?? float.MaxValue).ToList();

        ImGui.TextColored(Parchment, $"Yours ({yours.Count})");
        if (yours.Count == 0) ImGui.TextColored(Dim, "You haven't made a sign.");
        foreach (var sg in yours) DrawRow(sg, s);

        ImGui.TextColored(Parchment, $"Near you ({near.Count})");
        if (near.Count == 0) ImGui.TextColored(Dim, "No one else's signs are close by.");
        foreach (var sg in near) DrawRow(sg, s);

        var elsewhere = s.Zones.Sum(z => z.Count);
        ImGui.TextColored(Parchment, $"Out in the world ({elsewhere})");
        if (elsewhere == 0) ImGui.TextColored(Dim, "No other signs are out there.");
        else
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(Dim, "Other people's signs are found by walking up to them. Here is how many there are, by zone:");
            ImGui.PopTextWrapPos();
            foreach (var z in s.Zones) ImGui.TextColored(Dim, $"   {z.Zone}: {z.Count}");
        }
    }

    private void DrawRow(SignView sg, SignSnap s)
    {
        var dist = sg.Distance();
        var near = dist is <= SignState.ReadRange;
        ImGui.TextColored(Parchment, sg.Name);
        ImGui.TextColored(Dim, $"{sg.Zone}  ·  by {sg.OwnerName}" + (dist != null ? $"  ·  {dist:0} yalms away" : ""));

        if (!near) ImGui.BeginDisabled();
        if (ImGui.SmallButton("Look##" + sg.Id)) { _ = Plugin.Signs.ReadAsync(sg.Id); }
        if (!near) { ImGui.EndDisabled(); if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Get within 10 yalms to look at it."); }
        if (sg.Mine)
        {
            ImGui.SameLine();
            if (sg.MapX > 0 && ImGui.SmallButton("Show on map##" + sg.Id)) SignState.ShowOnMap(sg);
            ImGui.SameLine();
            if (ImGui.SmallButton((managing == sg.Id ? "Close settings##" : "Settings##") + sg.Id)) Toggle(sg);
            if (managing == sg.Id) DrawManage(sg, s);
        }
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
    }

    private void Toggle(SignView sg)
    {
        confirmDelete = null;
        if (managing == sg.Id) { managing = null; return; }
        managing = sg.Id; editName = sg.Name; editText = "(loading...)";
        _ = Load(sg.Id);
    }

    private async Task Load(string id)
    {
        var full = await Plugin.Signs.FetchAsync(id).ConfigureAwait(false);
        if (full != null && managing == id) editText = full.Description ?? "";
    }

    private void DrawManage(SignView sg, SignSnap s)
    {
        ImGui.Indent();
        ImGui.SetNextItemWidth(240f); ImGui.InputText("Name##en" + sg.Id, ref editName, 40);
        ImGui.InputTextMultiline("##et" + sg.Id, ref editText, 1000, new Vector2(ImGui.GetContentRegionAvail().X - 8f, 100f));
        var ready = !s.Busy && editName.Trim().Length > 0 && editText.Trim().Length > 0 && editText != "(loading...)";
        if (!ready) ImGui.BeginDisabled();
        if (ImGui.SmallButton("Save##sv" + sg.Id)) { _ = Plugin.Signs.UpdateAsync(sg.Id, editName, editText); managing = null; }
        if (!ready) ImGui.EndDisabled();
        var spot = WorldInfo.Current(out _);
        if (spot != null) { ImGui.SameLine(); if (ImGui.SmallButton("Move it here##mv" + sg.Id)) { _ = Plugin.Signs.MoveHereAsync(sg, spot); managing = null; } }
        ImGui.SameLine();
        if (confirmDelete == sg.Id)
        {
            ImGui.TextColored(Red, "Delete this sign?"); ImGui.SameLine();
            if (ImGui.SmallButton("Yes, delete##dl" + sg.Id)) { _ = Plugin.Signs.DeleteAsync(sg.Id); confirmDelete = null; managing = null; }
            ImGui.SameLine(); if (ImGui.SmallButton("No##dn" + sg.Id)) confirmDelete = null;
        }
        else if (ImGui.SmallButton("Delete##dl" + sg.Id)) confirmDelete = sg.Id;
        ImGui.Unindent();
    }
}
