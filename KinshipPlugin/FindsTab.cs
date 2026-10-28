using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

/// <summary>The World tab: place a copy of one of your items at a spot, for anyone to find and pick up.</summary>
public sealed class FindsTab
{
    private static readonly Vector4 Parchment = new(0.86f, 0.80f, 0.62f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.5f, 1f);
    private const int MaxMine = 20;

    private string pickedId = "", label = "", text = "", mode = "once", confirmDelete = "";
    private int minutes = 20;

    public void Draw(ItemsSnap inv)
    {
        Plugin.Finds.RefreshIfDue(15);
        var (error, notice, busy) = Plugin.Finds.Messages();
        ImGui.TextColored(Parchment, "World items");
        ImGui.PushTextWrapPos(0f);
        ImGui.TextColored(Dim, "Leave a copy of one of your items out in the world, like a herb by a tree, for anyone to find and pick up. It shows up when someone stands right next to it. You choose whether it can be taken once, comes back after a wait, or never runs out.");
        ImGui.PopTextWrapPos();
        if (!inv.SignedIn) { ImGui.TextColored(Dim, "Sign in on the Inventory tab to use this."); return; }
        if (error.Length > 0) ImGui.TextColored(Red, error);
        else if (notice.Length > 0) ImGui.TextColored(Green, notice);
        ImGui.Spacing();
        DrawMake(inv, busy);
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        DrawList(busy);
    }

    private void DrawMake(ItemsSnap inv, bool busy)
    {
        if (!ImGui.CollapsingHeader("Place a world item here")) return;
        var spot = WorldInfo.Current(out var problem);
        if (spot == null) { ImGui.TextColored(Red, problem.Replace("Stashes", "World items").Replace("a stash", "a world item")); return; }
        ImGui.Text("You are in: " + spot.Zone);
        var chosen = inv.Items.FirstOrDefault(i => i.Id == pickedId);
        ImGui.SetNextItemWidth(260f);
        if (ImGui.BeginCombo("Item to leave a copy of##findit", chosen?.Name ?? "Choose an item"))
        {
            foreach (var i in inv.Items) if (ImGui.Selectable(i.Name + "##fi" + i.Id, i.Id == pickedId)) pickedId = i.Id;
            ImGui.EndCombo();
        }
        ImGui.TextColored(Dim, "Your own item stays with you. People pick up copies of it, including its reagent recipe if it has one.");
        ImGui.SetNextItemWidth(280f); ImGui.InputText("What it looks like##findlabel", ref label, 40);
        ImGui.TextColored(Dim, "The name people see, like \"A pretty frost herb\".");
        ImGui.Text("A few words about it (optional)");
        ImGui.InputTextMultiline("##findtext", ref text, 300, new Vector2(ImGui.GetContentRegionAvail().X - 8f, 60f));
        ImGui.Text("How it works");
        if (ImGui.RadioButton("Once: the first person takes it and it is gone##fm1", mode == "once")) mode = "once";
        if (ImGui.RadioButton("Wait: back after a while, every time someone takes it##fm2", mode == "timer")) mode = "timer";
        if (ImGui.RadioButton("Always: never runs out##fm3", mode == "always")) mode = "always";
        if (mode == "timer")
        {
            ImGui.SetNextItemWidth(110f); ImGui.InputInt("Minutes until it is back##findmin", ref minutes, 1);
            minutes = Math.Clamp(minutes, 1, 10080);
        }
        var mine = Plugin.Finds.All.Count(f => f.Mine);
        if (mine >= MaxMine) ImGui.TextColored(Dim, $"You already have {MaxMine} world items. Delete one to place another.");
        var ok = chosen != null && label.Trim().Length > 0 && !busy && mine < MaxMine;
        if (!ok) ImGui.BeginDisabled();
        if (ImGui.Button("Place it here##findgo")) { _ = Plugin.Finds.CreateAsync(chosen!.Id, label, text, mode, minutes, spot); label = ""; text = ""; }
        if (!ok) ImGui.EndDisabled();
    }

    private void DrawList(bool busy)
    {
        ImGui.TextColored(Parchment, "Yours");
        ImGui.SameLine(); if (ImGui.SmallButton("Refresh##finds")) _ = Plugin.Finds.RefreshAsync();
        var yours = Plugin.Finds.All.Where(f => f.Mine).OrderBy(f => f.Zone).ThenBy(f => f.Label).ToList();
        if (yours.Count == 0) ImGui.TextColored(Dim, "You haven't placed any world items.");
        foreach (var f in yours)
        {
            ImGui.TextColored(Parchment, f.Label);
            var how = f.Mode == "once" ? "once" : f.Mode == "timer" ? $"back after {f.Minutes} min" : "always there";
            var state = f.State == "ready" ? "ready to pick up" : f.State == "gone" ? "taken" : $"waiting, back in {f.SecondsLeft / 60 + 1} min";
            ImGui.TextColored(Dim, $"{f.Zone}  ·  gives: {f.ItemName}  ·  {how}  ·  {state}");
            if (f.MapX > 0 && ImGui.SmallButton("Show on map##fm" + f.Id)) FindState.ShowOnMap(f);
            if (f.State != "ready") { ImGui.SameLine(); if (ImGui.SmallButton("Put it back now##fr" + f.Id)) _ = Plugin.Finds.ResetAsync(f.Id); }
            ImGui.SameLine();
            if (confirmDelete == f.Id)
            {
                ImGui.TextColored(Red, "Delete it?"); ImGui.SameLine();
                if (ImGui.SmallButton("Yes##fy" + f.Id)) { _ = Plugin.Finds.DeleteAsync(f.Id); confirmDelete = ""; }
                ImGui.SameLine(); if (ImGui.SmallButton("No##fn" + f.Id)) confirmDelete = "";
            }
            else if (ImGui.SmallButton("Delete##fd" + f.Id)) confirmDelete = f.Id;
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        }
    }
}
