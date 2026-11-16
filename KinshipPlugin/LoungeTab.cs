using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

/// <summary>The Embershade Lounge: hours, menu, clock in and out, selling, and (for managers) menu, pay and the receipt book.</summary>
public sealed class LoungeTab
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.5f, 1f);

    private readonly IconPicker picker = new("loungepick");
    private int qty = 1;
    private string customer = "";
    private string hoursEdit = ""; private int rateEdit; private bool infoLoaded;
    private string? editId; private string mName = "", mDesc = ""; private int mPrice = 100; private bool mGift; private string gIcon = "game:20001", gName = "", gDesc = "", gLabel = "", gText = "";
    private bool confirmNight; private string? confirmPay;

    public void Draw()
    {
        Plugin.Lounge.RefreshIfDue(20);
        picker.Draw();
        var (v, error, notice, busy) = Plugin.Lounge.Snapshot();
        if (Plugin.Config.Me().ItemsToken.Length == 0) { ImGui.TextWrapped("Sign in on the Inventory tab first."); return; }
        if (v == null) { ImGui.TextColored(Dim, "Loading the lounge..."); return; }
        if (!infoLoaded) { infoLoaded = true; hoursEdit = v.Hours; rateEdit = v.Rate; }

        ImGui.TextColored(Gold, "The Embershade Lounge");
        if (v.Hours.Length > 0) ImGui.TextWrapped(v.Hours); else ImGui.TextColored(Dim, "Opening days and hours have not been set yet.");
        ImGui.TextColored(Dim, "Allow up to a minute for any action to go through.");
        if (error.Length > 0) ImGui.TextColored(Red, error);
        else if (notice.Length > 0) ImGui.TextColored(Green, notice);
        if (busy) ImGui.TextColored(Dim, "Working...");

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, "On shift");
        if (v.Working.Count == 0) ImGui.TextColored(Dim, "Nobody is clocked in.");
        foreach (var w in v.Working) ImGui.Text($"{w.Name}  (since {Local(w.Since)})");
        ImGui.Spacing();
        var onShift = v.Me.Since.Length > 0;
        if (onShift)
        {
            ImGui.TextColored(Green, $"You are clocked in since {Local(v.Me.Since)}  ({Mins(v.Me.Since)} min so far)");
            if (ImGui.Button("Clock out##lg")) _ = Plugin.Lounge.ClockAsync(false);
        }
        else if (ImGui.Button("Clock in##lg")) _ = Plugin.Lounge.ClockAsync(true);
        if (v.Me.UnpaidGold > 0) { ImGui.SameLine(); ImGui.TextColored(Dim, $"Waiting for pay: {v.Me.UnpaidMinutes} min = {v.Me.UnpaidGold:N0} gold"); }

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, "Menu");
        if (!onShift) ImGui.TextColored(Dim, "Clock in to start selling.");
        ImGui.SetNextItemWidth(70f); ImGui.InputInt("How many##lgq", ref qty, 1, 5); qty = Math.Clamp(qty, 1, 20);
        ImGui.SetNextItemWidth(180f); ImGui.InputText("Hand item to (optional)##lgc", ref customer, 40);
        ImGui.TextColored(Dim, "If a menu item comes with an in-game item, type the customer's name to give it to them when you sell.");
        if (v.Menu.Count == 0) ImGui.TextColored(Dim, "The menu is empty.");
        foreach (var m in v.Menu)
        {
            ImGui.PushID(m.Id);
            ImGui.Text($"{m.Name}  -  {m.Price:N0} gil" + (m.Gift != null ? "  (comes with an item)" : ""));
            if (m.Description.Length > 0) ImGui.TextColored(Dim, m.Description);
            if (!onShift) ImGui.BeginDisabled();
            if (ImGui.SmallButton("Sell")) _ = Plugin.Lounge.SellAsync(m.Id, qty, "");
            if (m.Gift != null) { ImGui.SameLine(); if (customer.Trim().Length == 0) ImGui.BeginDisabled(); if (ImGui.SmallButton("Sell and give item")) _ = Plugin.Lounge.SellAsync(m.Id, qty, customer.Trim()); if (customer.Trim().Length == 0) ImGui.EndDisabled(); }
            if (!onShift) ImGui.EndDisabled();
            ImGui.SameLine(); if (ImGui.SmallButton("Copy description")) ImGui.SetClipboardText(m.Description.Length > 0 ? $"{m.Name}: {m.Description}" : m.Name);
            ImGui.PopID();
            ImGui.Spacing();
        }

        ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, "My sales tonight");
        ImGui.Text($"{v.Me.SoldCount} sold, {v.Me.SoldTotal:N0} gil");
        ImGui.SameLine(); if (ImGui.SmallButton("Undo my last sale")) _ = Plugin.Lounge.UndoAsync();
        ImGui.TextColored(Dim, "Undo works for sales from the last 15 minutes. It does not take back an item you handed over.");

        if (!v.CanManage) return;
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        if (!ImGui.CollapsingHeader("Manager##lgm")) return;
        DrawManager(v);
    }

    private void DrawManager(LoungeView v)
    {
        ImGui.TextColored(Gold, "Hours and pay");
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 8f); ImGui.InputText("##lghours", ref hoursEdit, 300);
        ImGui.TextColored(Dim, "Days open and hours, in your own words. Everyone sees this at the top.");
        ImGui.SetNextItemWidth(140f); ImGui.InputInt("Gold per hour##lgrate", ref rateEdit, 500, 5000); rateEdit = Math.Clamp(rateEdit, 0, 1000000);
        ImGui.TextColored(Dim, "Each shift locks in the rate at the moment of clock in. Shifts are capped at 12 hours.");
        if (ImGui.Button("Save hours and pay##lgsave")) _ = Plugin.Lounge.SaveInfoAsync(hoursEdit.Trim(), rateEdit);

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, editId == null ? "Add a menu item" : "Edit menu item");
        ImGui.SetNextItemWidth(220f); ImGui.InputText("Name##lgn", ref mName, 40);
        ImGui.SetNextItemWidth(140f); ImGui.InputInt("Price (gil)##lgp", ref mPrice, 10, 100); mPrice = Math.Clamp(mPrice, 0, 1000000);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 8f); ImGui.InputText("##lgd", ref mDesc, 300);
        ImGui.TextColored(Dim, "Description, so anyone can copy it when a customer asks what it is.");
        ImGui.Checkbox("Comes with an in-game item##lgg", ref mGift);
        if (mGift)
        {
            var wrap = ItemIcons.Wrap(gIcon);
            if (wrap != null) { ImGui.Image(wrap.Handle, new Vector2(32f, 32f)); ImGui.SameLine(); }
            if (ImGui.SmallButton("Choose icon##lgi")) picker.Open(i => gIcon = i);
            ImGui.SetNextItemWidth(220f); ImGui.InputText("Item name##lgin", ref gName, 40);
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 8f); ImGui.InputText("Item description##lgid", ref gDesc, 300);
            ImGui.SetNextItemWidth(120f); ImGui.InputText("Button word##lgil", ref gLabel, 20);
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 8f); ImGui.InputText("What happens##lgit", ref gText, 300);
        }
        var ok = mName.Trim().Length > 0 && (!mGift || gName.Trim().Length > 0);
        if (!ok) ImGui.BeginDisabled();
        if (ImGui.Button((editId == null ? "Add to menu" : "Save changes") + "##lgadd")) { _ = Plugin.Lounge.SaveMenuAsync(editId, mName.Trim(), mPrice, mDesc.Trim(), mGift ? new LoungeGift { Name = gName, Icon = gIcon, Description = gDesc, UseLabel = gLabel, UseText = gText } : null); ResetEditor(); }
        if (!ok) ImGui.EndDisabled();
        if (editId != null) { ImGui.SameLine(); if (ImGui.Button("Cancel##lgcan")) ResetEditor(); }

        ImGui.Spacing();
        foreach (var m in v.Menu)
        {
            ImGui.PushID("mm" + m.Id);
            ImGui.Text($"{m.Name} - {m.Price:N0}"); ImGui.SameLine();
            if (ImGui.SmallButton("Edit")) { editId = m.Id; mName = m.Name; mPrice = m.Price; mDesc = m.Description; mGift = m.Gift != null; gIcon = m.Gift?.Icon is { Length: > 0 } gi ? gi : "game:20001"; gName = m.Gift?.Name ?? ""; gDesc = m.Gift?.Description ?? ""; gLabel = m.Gift?.UseLabel ?? ""; gText = m.Gift?.UseText ?? ""; }
            ImGui.SameLine(); if (ImGui.SmallButton("Remove")) _ = Plugin.Lounge.DeleteMenuAsync(m.Id);
            ImGui.PopID();
        }

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, "Receipt book, tonight");
        var t = v.Tonight;
        if (t == null || t.Items.Count == 0) ImGui.TextColored(Dim, "Nothing sold yet.");
        else
        {
            foreach (var l in t.Items) ImGui.Text($"{l.Count} x {l.Name}  =  {l.Total:N0} gil");
            ImGui.TextColored(Green, $"Total: {t.Count} sold, {t.Total:N0} gil expected");
        }
        if (!confirmNight) { if (ImGui.SmallButton("Close the night and start a new one##lgnn")) confirmNight = true; }
        else
        {
            ImGui.TextColored(Red, "Clear the receipt book and start a new night?");
            if (ImGui.SmallButton("Yes, start a new night##lgny")) { _ = Plugin.Lounge.NewNightAsync(); confirmNight = false; }
            ImGui.SameLine(); if (ImGui.SmallButton("Cancel##lgnc")) confirmNight = false;
        }

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, "Staff");
        var staff = v.Staff ?? new();
        if (staff.Count == 0) ImGui.TextColored(Dim, "Nobody has an open shift or pay waiting.");
        foreach (var s in staff)
        {
            ImGui.PushID("st" + s.Name);
            ImGui.Text(s.Name); ImGui.SameLine();
            ImGui.TextColored(Dim, (s.ClockedIn ? "clocked in" : "off shift") + (s.Gold > 0 || s.Minutes > 0 ? $", owed {s.Gold:N0} gold for {s.Minutes} min" : ""));
            if (s.ClockedIn) { ImGui.SameLine(); if (ImGui.SmallButton("Clock out")) _ = Plugin.Lounge.ClockOutAsync(s.Name); }
            if (!s.ClockedIn && s.Minutes > 0)
            {
                ImGui.SameLine();
                if (confirmPay == s.Name)
                {
                    if (ImGui.SmallButton($"Pay {s.Gold:N0} gold now")) { _ = Plugin.Lounge.PayAsync(s.Name); confirmPay = null; }
                    ImGui.SameLine(); if (ImGui.SmallButton("No")) confirmPay = null;
                }
                else if (ImGui.SmallButton("Pay")) confirmPay = s.Name;
            }
            ImGui.PopID();
        }
        ImGui.TextColored(Dim, "Pay goes into the worker's personal bank. Clock someone out before paying them.");
    }

    private void ResetEditor() { editId = null; mName = ""; mDesc = ""; mPrice = 100; mGift = false; gIcon = "game:20001"; gName = ""; gDesc = ""; gLabel = ""; gText = ""; }
    private static string Local(string iso) => DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d.ToLocalTime().ToString("h:mm tt") : "";
    private static int Mins(string iso) => DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? (int)Math.Max(0, (DateTime.UtcNow - d.ToUniversalTime()).TotalMinutes) : 0;
}
