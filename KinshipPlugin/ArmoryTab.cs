using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

/// <summary>What you are wearing, and your wearable items. Click a worn piece to take it off, click a wearable item to put it on.</summary>
public sealed class ArmoryTab
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.5f, 1f);
    private const float Slot = 40f;

    public void Draw(ItemsSnap inv)
    {
        Plugin.Armory.RefreshIfDue(30);
        var s = Plugin.Armory.Snapshot();

        ImGui.TextColored(Gold, "Armory");
        ImGui.SameLine(); if (ImGui.SmallButton("Refresh##armory")) _ = Plugin.Armory.RefreshAsync();
        ImGui.TextWrapped("Wear gear to add its bonus to your rolls. Click a worn piece to take it off. Gear can break after a set time, counted from the first time you wear it.");
        if (s.Error.Length > 0) ImGui.TextColored(Red, s.Error);
        ImGui.Spacing();

        var b = s.Bonus;
        ImGui.TextColored(Gold, $"Gear bonus:  Attack {Signed(b.Atk)}   Heal {Signed(b.Heal)}");
        if (b.Capped) ImGui.TextColored(Dim, "Your gear adds up to more than the limit. All gear together counts for at most +5.");
        ImGui.Spacing();

        ItemView? hovered = null;
        foreach (var (slot, label) in ArmoryState.Layout)
        {
            var worn = s.Slots.FirstOrDefault(x => x.Slot == slot)?.Item;
            var origin = ImGui.GetCursorScreenPos();
            if (worn != null)
            {
                var wrap = ItemIcons.Wrap(worn.Icon);
                if (wrap != null) ImGui.Image(wrap.Handle, new Vector2(Slot, Slot)); else ImGui.Button("?##aw" + slot, new Vector2(Slot, Slot));
                if (ImGui.IsItemHovered()) hovered = worn;
                if (!s.Busy && ImGui.IsItemClicked(ImGuiMouseButton.Left)) _ = Plugin.Armory.UnequipAsync(slot);
            }
            else { ImGui.BeginDisabled(); ImGui.Button("##ae" + slot, new Vector2(Slot, Slot)); ImGui.EndDisabled(); }
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.TextColored(Dim, label);
            ImGui.Text(worn?.Name ?? "Empty");
            ImGui.EndGroup();
        }
        if (hovered != null) { ItemsTab.DrawTooltip(hovered); }

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        var gear = inv.Items.Where(i => i.WearSlot.Length > 0).ToList();
        ImGui.TextColored(Gold, "Wearable items in your bag");
        ImGui.TextColored(Dim, "Click to wear. Right-click a ring to wear it in the second ring slot.");
        if (gear.Count == 0) ImGui.TextColored(Dim, "You have nothing wearable right now.");
        ItemView? over = null;
        for (var i = 0; i < gear.Count; i++)
        {
            if (i % 8 != 0) ImGui.SameLine();
            var it = gear[i];
            var origin = ImGui.GetCursorScreenPos();
            var wrap = ItemIcons.Wrap(it.Icon);
            if (wrap != null) ImGui.Image(wrap.Handle, new Vector2(Slot, Slot)); else ImGui.Button("?##ab" + i, new Vector2(Slot, Slot));
            ItemsTab.DrawQty(it, origin, Slot);
            if (ImGui.IsItemHovered()) over = it;
            if (!s.Busy && ImGui.IsItemClicked(ImGuiMouseButton.Left)) _ = Plugin.Armory.EquipAsync(it.Id);
            if (!s.Busy && it.WearSlot == "ring" && ImGui.IsItemClicked(ImGuiMouseButton.Right)) _ = Plugin.Armory.EquipAsync(it.Id, "ring2");
        }
        if (over != null) ItemsTab.DrawTooltip(over);
        ImGui.TextColored(Dim, $"Your bag: {inv.Items.Count}/{inv.Capacity} slots used. Swapping gear needs a free slot only if the old piece has nowhere to go.");
    }

    private static string Signed(int n) => n > 0 ? "+" + n : n.ToString();
}
