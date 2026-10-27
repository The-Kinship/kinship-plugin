using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace KinshipPlugin;

/// <summary>A small window showing what another character is wearing. Opened from their roster profile.</summary>
public sealed class ArmoryViewWindow : Window, IDisposable
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);
    private const float Slot = 40f;

    public ArmoryViewWindow() : base("Armory##KinshipArmoryView", ImGuiWindowFlags.NoCollapse)
    {
        Size = new Vector2(300, 460);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Show(string name) { IsOpen = true; _ = Plugin.Armory.LookAsync(name); }
    public void Dispose() { }

    public override void Draw()
    {
        var l = Plugin.Armory.Look();
        ImGui.TextColored(Gold, l.Name.Length > 0 ? l.Name : "Armory");
        if (l.Loading) { ImGui.TextColored(Dim, "Loading..."); return; }
        if (l.Error.Length > 0) { ImGui.PushTextWrapPos(0f); ImGui.TextColored(Red, l.Error); ImGui.PopTextWrapPos(); return; }
        ImGui.TextColored(Dim, $"Gear bonus: Attack {Signed(l.Bonus.Atk)}   Heal {Signed(l.Bonus.Heal)}");
        ImGui.Spacing();
        ItemView? hovered = null;
        foreach (var (slot, label) in ArmoryState.Layout)
        {
            var worn = l.Slots.FirstOrDefault(x => x.Slot == slot)?.Item;
            if (worn != null)
            {
                var wrap = ItemIcons.Wrap(worn.Icon);
                if (wrap != null) ImGui.Image(wrap.Handle, new Vector2(Slot, Slot)); else ImGui.Button("?##av" + slot, new Vector2(Slot, Slot));
                if (ImGui.IsItemHovered()) hovered = worn;
            }
            else { ImGui.BeginDisabled(); ImGui.Button("##av" + slot, new Vector2(Slot, Slot)); ImGui.EndDisabled(); }
            ImGui.SameLine();
            ImGui.BeginGroup(); ImGui.TextColored(Dim, label); ImGui.Text(worn?.Name ?? "Empty"); ImGui.EndGroup();
        }
        if (hovered != null) ItemsTab.DrawTooltip(hovered);
    }

    private static string Signed(int n) => n > 0 ? "+" + n : n.ToString();
}
