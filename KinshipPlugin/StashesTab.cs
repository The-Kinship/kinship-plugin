using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

/// <summary>Stashes: make one where you stand, and put things in or take things out when you are close.</summary>
public sealed class StashesTab
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.5f, 1f);
    private const float Slot = 44f;

    private string newName = "";
    private bool newPrivate;
    private string? managing, confirmDelete;
    private string renameText = "";
    private bool managePrivate;

    public void Draw(ItemsSnap inv)
    {
        Plugin.Stashes.RefreshIfDue(15);
        var s = Plugin.Stashes.Snapshot();
        if (s.Open != null) { DrawOpen(s, inv, false); return; }

        ImGui.TextColored(Gold, "Stashes");
        ImGui.TextWrapped("A stash is a spot in the world that holds items. Stand where you want it, name it, and anyone close enough can put things in or take things out.");
        var labels = Plugin.Config.StashLabels;
        if (ImGui.Checkbox("Show stash names in the world", ref labels)) { Plugin.Config.StashLabels = labels; Plugin.Config.Save(); }
        if (s.Error.Length > 0) ImGui.TextColored(Red, s.Error);
        else if (s.Notice.Length > 0) ImGui.TextColored(Green, s.Notice);
        ImGui.Spacing();

        DrawMake(s);
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        DrawList(s);
    }

    // ---------- making a stash ----------
    private void DrawMake(StashSnap s)
    {
        if (!ImGui.CollapsingHeader("Make a stash here")) return;
        var spot = WorldInfo.Current(out var problem);
        if (spot == null) { ImGui.TextColored(Red, problem); return; }
        ImGui.Text("You are in: " + spot.Zone);
        ImGui.SetNextItemWidth(240f); ImGui.InputText("Name##stash", ref newName, 40);
        ImGui.Checkbox("Private (only I can see it)", ref newPrivate);
        var mine = s.Stashes.Count(x => x.Mine);
        if (mine >= 3) ImGui.TextColored(Dim, "You already have 3 stashes. Delete one to make another.");
        var ok = newName.Trim().Length > 0 && !s.Busy && mine < 3;
        if (!ok) ImGui.BeginDisabled();
        if (ImGui.Button("Make stash here")) { _ = Plugin.Stashes.CreateAsync(newName, newPrivate, spot); newName = ""; }
        if (!ok) ImGui.EndDisabled();
    }

    // ---------- the list ----------
    private void DrawList(StashSnap s)
    {
        ImGui.TextColored(Gold, $"All stashes ({s.Stashes.Count})");
        ImGui.SameLine(); if (ImGui.SmallButton("Refresh##stashes")) _ = Plugin.Stashes.RefreshAsync();
        if (s.Stashes.Count == 0) { ImGui.TextColored(Dim, "No stashes yet."); return; }

        var ordered = s.Stashes.OrderBy(x => WorldInfo.DistanceTo(x) ?? float.MaxValue).ThenBy(x => x.Zone).ThenBy(x => x.Name).ToList();
        foreach (var st in ordered)
        {
            var dist = WorldInfo.DistanceTo(st);
            var near = dist is <= WorldInfo.UseRange;
            ImGui.TextColored(Gold, st.Name);
            ImGui.SameLine(); ImGui.TextColored(Dim, $"{st.Count}/{st.Capacity}" + (st.IsPrivate ? "  private" : ""));
            ImGui.TextColored(Dim, $"{st.Zone}  \u00b7  by {st.OwnerName}" + (dist != null ? $"  \u00b7  {dist:0} yalms away" : ""));

            if (!near) ImGui.BeginDisabled();
            if (ImGui.SmallButton("Open##" + st.Id)) { _ = Plugin.Stashes.OpenAsync(st.Id); }
            if (!near) { ImGui.EndDisabled(); if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Get within 10 yalms of the stash to open it."); }
            ImGui.SameLine();
            if (st.MapX > 0 && ImGui.SmallButton("Show on map##" + st.Id)) StashState.ShowOnMap(st);
            if (st.Mine)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton((managing == st.Id ? "Close settings##" : "Settings##") + st.Id)) { managing = managing == st.Id ? null : st.Id; renameText = st.Name; managePrivate = st.IsPrivate; confirmDelete = null; }
                if (managing == st.Id) DrawManage(st, s);
            }
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        }
    }

    private void DrawManage(StashView st, StashSnap s)
    {
        ImGui.Indent();
        ImGui.SetNextItemWidth(220f); ImGui.InputText("Name##ren" + st.Id, ref renameText, 40);
        ImGui.Checkbox("Private##pr" + st.Id, ref managePrivate);
        if (!s.Busy && renameText.Trim().Length > 0 && ImGui.SmallButton("Save##sv" + st.Id)) { _ = Plugin.Stashes.UpdateAsync(st.Id, renameText, managePrivate); managing = null; }
        var spot = WorldInfo.Current(out _);
        if (spot != null) { ImGui.SameLine(); if (ImGui.SmallButton("Move it here##mv" + st.Id)) { _ = Plugin.Stashes.MoveHereAsync(st, spot); managing = null; } }
        if (confirmDelete == st.Id)
        {
            ImGui.TextColored(Red, "Delete this stash? It has to be empty, with no gold in it.");
            if (ImGui.SmallButton("Yes, delete##dl" + st.Id)) { _ = Plugin.Stashes.DeleteAsync(st.Id); confirmDelete = null; managing = null; }
            ImGui.SameLine(); if (ImGui.SmallButton("No##dn" + st.Id)) confirmDelete = null;
        }
        else if (ImGui.SmallButton("Delete##dl" + st.Id)) confirmDelete = st.Id;
        ImGui.Unindent();
    }

    private int goldAmount;

    // ---------- an open stash ----------
    internal void DrawOpen(StashSnap s, ItemsSnap inv, bool popup)
    {
        var st = s.Open!;
        if (ImGui.Button(popup ? "Close##stashpop" : "< All stashes")) { Plugin.Stashes.CloseOpen(); return; }
        ImGui.SameLine(); ImGui.TextColored(Gold, st.Name);
        ImGui.TextColored(Dim, $"{st.Zone}  \u00b7  by {st.OwnerName}  \u00b7  {st.Count}/{st.Capacity}");
        if (s.Error.Length > 0) ImGui.TextColored(Red, s.Error);

        var near = WorldInfo.Near(st);
        if (!near) ImGui.TextColored(Red, "You've moved away. Get within 10 yalms of the stash to use it.");
        ImGui.Spacing();

        ImGui.TextColored(Gold, "In the stash");
        ImGui.TextColored(Dim, "Click an item to take it.");
        ItemView? hovered = null;
        if (!near) ImGui.BeginDisabled();
        for (var i = 0; i < st.Capacity; i++)
        {
            if (i % 8 != 0) ImGui.SameLine();
            if (i < s.OpenItems.Count)
            {
                var it = s.OpenItems[i];
                var wrap = ItemIcons.Wrap(it.Icon);
                var origin = ImGui.GetCursorScreenPos();
                if (wrap != null) ImGui.Image(wrap.Handle, new Vector2(Slot, Slot)); else ImGui.Button("?##so" + i, new Vector2(Slot, Slot));
                ItemsTab.DrawQty(it, origin, Slot);
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) hovered = it;
                if (near && !s.Busy && ImGui.IsItemClicked(ImGuiMouseButton.Left)) _ = Plugin.Stashes.TakeAsync(st.Id, it.Id);
            }
            else { ImGui.BeginDisabled(); ImGui.Button("##se" + i, new Vector2(Slot, Slot)); ImGui.EndDisabled(); }
        }
        if (!near) ImGui.EndDisabled();
        if (hovered != null) ItemsTab.DrawTooltip(hovered);

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, $"Gold in the stash: {st.Gold:N0}");
        if (!near) ImGui.BeginDisabled();
        ImGui.SetNextItemWidth(110f); ImGui.InputInt("Gold##stashgold", ref goldAmount, 0);
        if (goldAmount < 0) goldAmount = 0;
        ImGui.SameLine(); if (ImGui.SmallButton("Put in##sgp") && goldAmount > 0 && !s.Busy) { _ = Plugin.Stashes.PutGoldAsync(st.Id, goldAmount); }
        ImGui.SameLine(); if (ImGui.SmallButton("Take out##sgt") && goldAmount > 0 && !s.Busy) { _ = Plugin.Stashes.TakeGoldAsync(st.Id, goldAmount); }
        ImGui.SameLine(); if (st.Gold > 0 && ImGui.SmallButton("Take all##sga") && !s.Busy) { _ = Plugin.Stashes.TakeGoldAsync(st.Id, st.Gold); }
        if (!near) ImGui.EndDisabled();
        ImGui.TextColored(Dim, $"Your purse: {Plugin.Items.Gold:N0} gold. Anyone who can open this stash can take its gold.");
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, $"Your items  {inv.Items.Count}/{inv.Capacity}");
        ImGui.TextColored(Dim, "Click an item to put it in the stash.");
        hovered = null;
        if (!near) ImGui.BeginDisabled();
        for (var i = 0; i < inv.Capacity; i++)
        {
            if (i % 8 != 0) ImGui.SameLine();
            if (i < inv.Items.Count)
            {
                var it = inv.Items[i];
                var wrap = ItemIcons.Wrap(it.Icon);
                var origin = ImGui.GetCursorScreenPos();
                if (wrap != null) ImGui.Image(wrap.Handle, new Vector2(Slot, Slot)); else ImGui.Button("?##si" + i, new Vector2(Slot, Slot));
                ItemsTab.DrawQty(it, origin, Slot);
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) hovered = it;
                if (near && !s.Busy && ImGui.IsItemClicked(ImGuiMouseButton.Left)) _ = Plugin.Stashes.PutAsync(st.Id, it.Id);
            }
            else { ImGui.BeginDisabled(); ImGui.Button("##sj" + i, new Vector2(Slot, Slot)); ImGui.EndDisabled(); }
        }
        if (!near) ImGui.EndDisabled();
        if (hovered != null) ItemsTab.DrawTooltip(hovered);
    }
}
