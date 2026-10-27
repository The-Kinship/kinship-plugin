using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

/// <summary>Your purse of gold, paying other members, and (with the bank password) creating or taking gold.</summary>
public sealed class BankTab
{
    private static readonly Vector4 Coin = new(0.95f, 0.8f, 0.3f, 1f);
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.5f, 1f);

    private string payTo = "", mintTo = "", mintNote = "";
    private int payAmount = 1, mintAmount = 10;
    private bool membersAsked;

    public void Draw(ItemsSnap inv)
    {
        Plugin.Bank.RefreshIfDue(15);
        if (!membersAsked) { membersAsked = true; _ = Plugin.Items.LoadMembersAsync(); }
        var s = Plugin.Bank.Snapshot();

        ImGui.TextColored(Gold, "The Kinship Bank");
        ImGui.TextColored(Coin, $"Gold: {Plugin.Items.Gold:N0}");
        ImGui.SameLine(); if (ImGui.SmallButton("Refresh##bank")) _ = Plugin.Bank.RefreshAsync();
        ImGui.TextWrapped("((The in-game exchange rate is 1 gold to 1 gil. To exchange gold for gil, see a banker in game.))");
        if (s.Error.Length > 0) ImGui.TextColored(Red, s.Error);
        else if (s.Notice.Length > 0) ImGui.TextColored(Green, s.Notice);
        ImGui.Spacing();

        DrawPay(inv, s);
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        DrawHistory(s);
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        DrawTools();
    }

    private void DrawPay(ItemsSnap inv, BankSnap s)
    {
        if (!ImGui.CollapsingHeader("Pay someone")) return;
        if (inv.Members.Count == 0) { ImGui.TextColored(Dim, "Nobody else has joined the RP system yet."); return; }
        ImGui.SetNextItemWidth(200f);
        if (ImGui.BeginCombo("To##bankpay", payTo.Length > 0 ? payTo : "Choose a member"))
        {
            foreach (var m in inv.Members) if (ImGui.Selectable(m, m == payTo)) payTo = m;
            ImGui.EndCombo();
        }
        ImGui.SetNextItemWidth(120f); ImGui.InputInt("Gold##bankpay", ref payAmount, 1);
        if (payAmount < 1) payAmount = 1;
        var ok = payTo.Length > 0 && payAmount <= Plugin.Items.Gold && !s.Busy;
        if (!ok) ImGui.BeginDisabled();
        if (ImGui.Button("Pay")) { _ = Plugin.Bank.PayAsync(payTo, payAmount); }
        if (!ok) ImGui.EndDisabled();
        if (payAmount > Plugin.Items.Gold) { ImGui.SameLine(); ImGui.TextColored(Dim, "You don't have that much."); }
    }

    private static void DrawHistory(BankSnap s)
    {
        ImGui.TextColored(Gold, "Recent");
        if (s.History.Count == 0) { ImGui.TextColored(Dim, "Nothing yet."); return; }
        foreach (var h in s.History)
        {
            var sign = h.Delta >= 0 ? "+" : "";
            ImGui.TextColored(h.Delta >= 0 ? Green : Red, $"{sign}{h.Delta:N0}");
            ImGui.SameLine(); ImGui.Text(h.Note);
            if (DateTime.TryParse(h.At, null, System.Globalization.DateTimeStyles.RoundtripKind, out var when))
            { ImGui.SameLine(); ImGui.TextColored(Dim, when.ToLocalTime().ToString("MMM d")); }
        }
    }

    private void DrawTools()
    {
        if (!ImGui.CollapsingHeader("Bank tools")) return;
        ImGui.TextWrapped("For whoever holds the bank password. Creates gold as rewards or pay, and takes gold back when it is exchanged for gil.");
        var pw = Plugin.Config.BankPassword;
        ImGui.SetNextItemWidth(240f);
        if (ImGui.InputText("Bank password", ref pw, 100, ImGuiInputTextFlags.Password)) { Plugin.Config.BankPassword = pw; Plugin.Config.Save(); }
        var d = Plugin.Bank.AdminSnapshot();
        if (d.Error.Length > 0) ImGui.TextColored(Red, d.Error);
        else if (d.Notice.Length > 0) ImGui.TextColored(Green, d.Notice);
        if (d.Busy) ImGui.TextColored(Dim, "Working...");
        else if (ImGui.Button(d.Loaded ? "Reload balances" : "Load balances")) _ = Plugin.Bank.AdminLoadAsync();
        if (!d.Loaded) return;

        ImGui.Spacing();
        ImGui.TextColored(Gold, $"In circulation: {d.Total:N0} gold");
        foreach (var m in d.Balances) { ImGui.Text(m.Name); ImGui.SameLine(); ImGui.TextColored(Coin, $"{m.Gold:N0}"); }

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.SetNextItemWidth(200f);
        if (ImGui.BeginCombo("Member##mint", mintTo.Length > 0 ? mintTo : "Choose a member"))
        {
            foreach (var m in d.Balances) if (ImGui.Selectable(m.Name, m.Name == mintTo)) mintTo = m.Name;
            ImGui.EndCombo();
        }
        ImGui.SetNextItemWidth(120f); ImGui.InputInt("Gold##mint", ref mintAmount, 1);
        if (mintAmount < 1) mintAmount = 1;
        ImGui.SetNextItemWidth(260f); ImGui.InputText("Note##mint", ref mintNote, 60);
        ImGui.TextColored(Dim, "Optional, they see it in their history. e.g. Mission reward.");
        var ok = mintTo.Length > 0 && !d.Busy;
        if (!ok) ImGui.BeginDisabled();
        if (ImGui.Button("Give gold")) { _ = Plugin.Bank.MintAsync(mintTo, mintAmount, mintNote); }
        ImGui.SameLine();
        if (ImGui.Button("Take gold")) { _ = Plugin.Bank.TakeAsync(mintTo, mintAmount, mintNote); }
        if (!ok) ImGui.EndDisabled();

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, "Bank log");
        foreach (var e in d.Recent)
        {
            ImGui.TextColored(e.Delta >= 0 ? Green : Red, $"{(e.Delta >= 0 ? "+" : "")}{e.Delta:N0}");
            ImGui.SameLine(); ImGui.Text($"{e.Name}: {e.Note}");
        }
    }
}
