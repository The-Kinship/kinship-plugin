using System.Collections.Generic;
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
    private string posName = ""; private string callMode = "", callReason = "", callPatron = ""; private bool callIc = true; private bool confirmNight; private string? confirmPay;
    private string svcCustomer = "", svcName = ""; private int svcCustom = 45, svcPrice = 10000; private string? editSvc;
    private string patronAdd = "", venueLabel = "", adEdit = "", nightDate = "", nightTime = "20:00", nightNote = "", rosterName = ""; private int venueRadius = 40, nightHours = 4; private string? editNight; private bool adLoaded;
    private static readonly (string Label, string Prefix)[] AdChannels = BuildChannels();
    private static (string, string)[] BuildChannels()
    {
        var l = new List<(string, string)> { ("Shout (the whole area)", "/shout "), ("Yell", "/yell "), ("Say", "/say "), ("Free Company", "/fc "), ("Party", "/p ") };
        for (var i = 1; i <= 8; i++) l.Add(($"Linkshell {i}", $"/l{i} "));
        for (var i = 1; i <= 8; i++) l.Add(($"Cross-world linkshell {i}", $"/cwl{i} "));
        return l.ToArray();
    }
    private bool pfLoaded; private string pfEdit = ""; private int pfMinutes = 55;
    private string nightText = "", nightTitle = "";

    /// <summary>The whole Lounge window: a header that every tab shares, then Menu, Time clock, Cash register and (for managers) Management.</summary>
    public void Draw()
    {
        Plugin.Lounge.RefreshIfDue(20);
        picker.Draw();
        var (v, error, notice, busy) = Plugin.Lounge.Snapshot();
        if (Plugin.Config.Me().ItemsToken.Length == 0) { ImGui.TextWrapped("Sign in on the Inventory tab (switch to the Toolbox, then RP) first."); return; }
        if (v == null) { ImGui.TextColored(Dim, "Loading the lounge..."); return; }
        if (!infoLoaded) { infoLoaded = true; hoursEdit = v.Hours; rateEdit = v.Rate; }

        ImGui.TextColored(Gold, "The Embershade Lounge");
        if (v.Hours.Length > 0) ImGui.TextWrapped(v.Hours); else ImGui.TextColored(Dim, "Opening days and hours have not been set yet.");
        if (!v.NightOpen) ImGui.TextColored(Gold, "The night is closed. A manager starts the next one.");
        var shift = v.Me.Since.Length > 0;
        if (!shift) ImGui.BeginDisabled();
        if (ImGui.SmallButton("Call manager##lgcm")) callMode = callMode == "manager" ? "" : "manager";
        ImGui.SameLine(); if (ImGui.SmallButton("Call security##lgcs")) callMode = callMode == "security" ? "" : "security";
        ImGui.SameLine(); if (ImGui.SmallButton("Ban a customer##lgcb")) callMode = callMode == "ban" ? "" : "ban";
        if (!shift) { ImGui.EndDisabled(); ImGui.SameLine(); ImGui.TextColored(Dim, "Clock in to use these."); }
        DrawCall(v);
        ImGui.Spacing();
        if (ImGui.BeginTabBar("lounge_tabs", ImGuiTabBarFlags.FittingPolicyScroll))
        {
            if (ImGui.BeginTabItem("Menu")) { DrawMenu(v); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Time clock")) { DrawClock(v); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Cash register")) { DrawRegister(v); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Services")) { DrawServices(v); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Patrons")) { Plugin.Lounge.RefreshIfDue(6); DrawPatrons(v); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Schedule")) { DrawSchedule(v); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Party Finder and Ads")) { DrawPf(v); ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing(); DrawAdvert(v); ImGui.EndTabItem(); }
            if (v.CanManage && ImGui.BeginTabItem("Management")) { DrawManager(v); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
        {
            ImGui.Spacing();
            var boxStart = ImGui.GetCursorScreenPos();
            var boxWidth = ImGui.GetContentRegionAvail().X;
            ImGui.Dummy(new Vector2(0f, 4f));
            ImGui.Indent(8f);
            ImGui.BeginGroup();
            ImGui.TextColored(Gold, "Active calls");
            if (v.Calls.Count == 0) ImGui.TextColored(Dim, "No active calls.");
            foreach (var c in v.Calls)
            {
                ImGui.PushID("call" + c.Id);
                ImGui.TextWrapped(c.Text);
                ImGui.Text("    status: " + (c.Responder.Length == 0 ? "OPEN, nobody assigned yet" : $"{c.Responder} assigned"));
                ImGui.SameLine(); ImGui.TextColored(Dim, $"({Ago(c.At)} ago)");
                if (c.Responder.Length == 0 && v.Me.Since.Length > 0) { ImGui.SameLine(); if (ImGui.SmallButton("I'll take it")) _ = Plugin.Lounge.RespondAsync(c.Id); }
                if (v.CanClearCalls) { ImGui.SameLine(); if (ImGui.SmallButton("Clear")) _ = Plugin.Lounge.ClearCallAsync(c.Id); }
                ImGui.PopID();
            }
            ImGui.EndGroup();
            ImGui.Unindent(8f);
            ImGui.Dummy(new Vector2(0f, 4f));
            var boxEnd = ImGui.GetItemRectMax();
            ImGui.GetWindowDrawList().AddRect(boxStart, new Vector2(boxStart.X + boxWidth, boxEnd.Y), ImGui.GetColorU32(Gold), 4f, ImDrawFlags.None, 2f);
            ImGui.Spacing();
        }
        {
            // Messages live at the bottom so nothing above them jumps around while you click.
            ImGui.Spacing(); ImGui.Separator();
            if (error.Length > 0) ImGui.TextColored(Red, error);
            else if (notice.Length > 0) ImGui.TextColored(Green, notice);
            if (busy) ImGui.TextColored(Dim, "Working...");
        }
    }

    private void DrawMenu(LoungeView v)
    {
        ImGui.TextColored(Dim, "Press Copy description to copy just the description, so you can paste it when a customer asks what it is.");
        ImGui.Spacing();
        if (v.Menu.Count == 0) ImGui.TextColored(Dim, "The menu is empty.");
        foreach (var m in v.Menu)
        {
            ImGui.PushID("menu" + m.Id);
            ImGui.Text($"{m.Name}  -  {m.Price:N0} gil" + (m.Gift != null ? "  (comes with an item)" : ""));
            if (m.Description.Length > 0) ImGui.TextWrapped(m.Description);
            if (ImGui.SmallButton("Copy description")) ImGui.SetClipboardText(m.Description.Length > 0 ? m.Description : m.Name);
            ImGui.PopID();
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        }
    }

    private void DrawClock(LoungeView v)
    {
        ImGui.TextColored(Gold, "On shift");
        if (v.Working.Count == 0) ImGui.TextColored(Dim, "Nobody is clocked in.");
        foreach (var w in v.Working) ImGui.Text($"{w.Name}{(w.Roles.Count > 0 ? "  [" + string.Join(", ", w.Roles) + "]" : "")}  (since {Local(w.Since)})");
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        var onShift = v.Me.Since.Length > 0;
        if (onShift)
        {
            ImGui.TextColored(Green, $"You are clocked in since {Local(v.Me.Since)}  ({Mins(v.Me.Since)} min so far)");
            if (ImGui.Button("Clock out##lg")) _ = Plugin.Lounge.ClockAsync(false);
        }
        else
        {
            ImGui.TextColored(Dim, "You are not clocked in.");
            if (ImGui.Button("Clock in##lg")) _ = Plugin.Lounge.ClockAsync(true);
        }
        ImGui.Spacing();
        if (v.Me.UnpaidGold > 0) ImGui.TextColored(Gold, $"Waiting for pay: {v.Me.UnpaidMinutes} min = {v.Me.UnpaidGold:N0} gold. A manager will pay you into your bank.");
        else ImGui.TextColored(Dim, "No pay waiting.");
        ImGui.TextColored(Dim, "A shift counts up to 12 hours. Allow up to a minute for an action to go through.");
    }

    private void DrawRegister(LoungeView v)
    {
        var onShift = v.Me.Since.Length > 0;
        if (!onShift) ImGui.TextColored(Dim, "Clock in on the Time clock tab to start selling.");
        ImGui.SetNextItemWidth(70f); ImGui.InputInt("How many##lgq", ref qty, 1, 5); qty = Math.Clamp(qty, 1, 20);
        ImGui.SetNextItemWidth(180f); ImGui.InputText("Hand item to (optional)##lgc", ref customer, 40);
        ImGui.TextColored(Dim, "If a menu item comes with an in-game item, type the customer's name to give it to them when you sell.");
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        if (v.Menu.Count == 0) ImGui.TextColored(Dim, "The menu is empty.");
        foreach (var m in v.Menu)
        {
            ImGui.PushID("reg" + m.Id);
            ImGui.Text($"{m.Name}  -  {m.Price:N0} gil" + (m.Gift != null ? "  (comes with an item)" : ""));
            if (!onShift) ImGui.BeginDisabled();
            if (ImGui.SmallButton("Sell")) _ = Plugin.Lounge.SellAsync(m.Id, qty, "");
            if (m.Gift != null) { ImGui.SameLine(); var noName = customer.Trim().Length == 0; if (noName) ImGui.BeginDisabled(); if (ImGui.SmallButton("Sell and give item")) _ = Plugin.Lounge.SellAsync(m.Id, qty, customer.Trim()); if (noName) ImGui.EndDisabled(); }
            if (!onShift) ImGui.EndDisabled();
            ImGui.PopID();
            ImGui.Spacing();
        }
        ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, "My sales tonight");
        ImGui.Text($"{v.Me.SoldCount} sold, {v.Me.SoldTotal:N0} gil");
        ImGui.SameLine(); if (ImGui.SmallButton("Undo my last sale")) _ = Plugin.Lounge.UndoAsync();
        ImGui.TextColored(Dim, "Undo works for sales from the last 15 minutes. It does not take back an item you handed over.");
    }

    private void DrawServices(LoungeView v)
    {
        var me = Plugin.Config.Me().ItemsName;
        var onShift = v.Me.Since.Length > 0;
        var receipt = Plugin.Lounge.LastReceipt();
        if (receipt != null)
        {
            ImGui.TextColored(Gold, "Receipt");
            ImGui.TextUnformatted(receipt.ToText());
            if (ImGui.SmallButton("Copy receipt##svcr")) ImGui.SetClipboardText(receipt.ToText());
            ImGui.SameLine(); if (ImGui.SmallButton("Dismiss##svcrd")) Plugin.Lounge.DismissReceipt();
            ImGui.TextColored(Dim, "This is also added to the Lounge receipt book.");
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        }
        var ses = v.Session;
        if (ses != null)
        {
            ImGui.TextColored(Gold, "Your session");
            ImGui.Text(ses.Name + (ses.Customer.Length > 0 ? $" with {ses.Customer}" : "") + $"  -  {ses.Rate:N0} gil per hour, {Dur.Of(ses.Minutes)} booked");
            if (DateTime.TryParse(ses.EndsAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var end))
            {
                var left = end.ToUniversalTime() - DateTime.UtcNow;
                if (left.TotalSeconds > 0) ImGui.TextColored(Green, $"Time left: {(int)left.TotalMinutes}:{left.Seconds:00}");
                else ImGui.TextColored(Red, "Time is up.");
            }
            ImGui.TextColored(Dim, $"Due so far: {Math.Round(ses.Rate * ses.Minutes / 60.0):N0} gil");
            if (ImGui.Button("Add 30 minutes##svc30")) _ = Plugin.Lounge.ExtendAsync(30);
            ImGui.SameLine(); if (ImGui.Button("Add 1 hour##svc60")) _ = Plugin.Lounge.ExtendAsync(60);
            ImGui.SameLine(); if (ImGui.Button("Close session##svcend")) _ = Plugin.Lounge.EndSessionAsync();
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        }

        var mine = v.Services.Where(x => string.Equals(x.Who, me, StringComparison.OrdinalIgnoreCase)).ToList();
        if (ses == null)
        {
            ImGui.TextColored(Gold, "Start a service");
            if (!onShift) ImGui.TextColored(Dim, "Clock in on the Time clock tab first.");
            else if (mine.Count == 0) ImGui.TextColored(Dim, "Add a service below, then you can start it here.");
            ImGui.SetNextItemWidth(180f); ImGui.InputText("Customer (optional)##svcc", ref svcCustomer, 40);
            ImGui.SetNextItemWidth(90f); ImGui.InputInt("Custom minutes##svcm", ref svcCustom, 5, 15); svcCustom = Math.Clamp(svcCustom, 15, 480);
            if (!onShift) ImGui.BeginDisabled();
            foreach (var x in mine)
            {
                ImGui.PushID("st" + x.Id);
                ImGui.Text($"{x.Name}  -  {x.Price:N0} gil per hour");
                if (ImGui.SmallButton("30 min")) _ = Plugin.Lounge.StartSessionAsync(x.Id, 30, svcCustomer.Trim());
                ImGui.SameLine(); if (ImGui.SmallButton("1 hour")) _ = Plugin.Lounge.StartSessionAsync(x.Id, 60, svcCustomer.Trim());
                ImGui.SameLine(); if (ImGui.SmallButton("2 hours")) _ = Plugin.Lounge.StartSessionAsync(x.Id, 120, svcCustomer.Trim());
                ImGui.SameLine(); if (ImGui.SmallButton("Custom")) _ = Plugin.Lounge.StartSessionAsync(x.Id, svcCustom, svcCustomer.Trim());
                ImGui.PopID();
            }
            if (!onShift) ImGui.EndDisabled();
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        }

        ImGui.TextColored(Gold, editSvc == null ? "My services" : "Edit service");
        ImGui.TextColored(Dim, "Each staff member sets their own services and prices. The price is per hour; the total is worked out from the time booked.");
        foreach (var x in mine)
        {
            ImGui.PushID("ms" + x.Id);
            ImGui.Text($"{x.Name}  -  {x.Price:N0} gil per hour"); ImGui.SameLine();
            if (ImGui.SmallButton("Edit")) { editSvc = x.Id; svcName = x.Name; svcPrice = x.Price; }
            ImGui.SameLine(); if (ImGui.SmallButton("Remove")) _ = Plugin.Lounge.DeleteServiceAsync(x.Id);
            ImGui.PopID();
        }
        ImGui.SetNextItemWidth(220f); ImGui.InputText("Service name##svcn", ref svcName, 40);
        ImGui.SetNextItemWidth(140f); ImGui.InputInt("Price per hour (gil)##svcp", ref svcPrice, 1000, 10000); svcPrice = Math.Clamp(svcPrice, 0, 1000000);
        var okSvc = svcName.Trim().Length > 0;
        if (!okSvc) ImGui.BeginDisabled();
        if (ImGui.Button((editSvc == null ? "Add service" : "Save service") + "##svcadd")) { _ = Plugin.Lounge.SaveServiceAsync(editSvc, svcName.Trim(), svcPrice); editSvc = null; svcName = ""; svcPrice = 10000; }
        if (!okSvc) ImGui.EndDisabled();
        if (editSvc != null) { ImGui.SameLine(); if (ImGui.Button("Cancel##svccan")) { editSvc = null; svcName = ""; svcPrice = 10000; } }

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, "Who offers what");
        if (v.Services.Count == 0) ImGui.TextColored(Dim, "Nobody has added a service yet.");
        foreach (var g in v.Services.GroupBy(x => x.Who))
            ImGui.TextWrapped($"{g.Key}: " + string.Join(";  ", g.Select(x => $"{x.Name} ({x.Price:N0} gil per hour)")));
        if (v.Sessions.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(Gold, "Busy right now");
            foreach (var a in v.Sessions)
            {
                var until = DateTime.TryParse(a.EndsAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var e) ? e.ToLocalTime().ToString("h:mm tt") : "";
                ImGui.Text($"{a.Who}: {a.Name} until {until}");
            }
        }
    }

    private void DrawCall(LoungeView v)
    {
        if (callMode.Length == 0) return;
        if (callMode == "manager")
        {
            ImGui.Text("Ask a manager to come over. Is it:");
            if (ImGui.RadioButton("IC (in character)##cic", callIc)) callIc = true;
            ImGui.SameLine(); if (ImGui.RadioButton("OOC (out of character)##cooc", !callIc)) callIc = false;
            if (ImGui.Button("Send to the manager on shift##csm")) { _ = Plugin.Lounge.CallManagerAsync(callIc); callMode = ""; }
        }
        else if (callMode == "security")
        {
            ImGui.Text("Send a call to everyone on shift with the Security position.");
            if (ImGui.Button("Send to security##css")) { _ = Plugin.Lounge.CallSecurityAsync(); callMode = ""; }
        }
        else
        {
            ImGui.Text("Ask a manager to remove someone from the property and block them.");
            ImGui.SetNextItemWidth(200f); ImGui.InputText("Customer name##cbn", ref callPatron, 40);
            if (v.Patrons.Count > 0 && ImGui.BeginCombo("##cbp", "pick from the venue"))
            {
                foreach (var p in v.Patrons) if (ImGui.Selectable(p.Name + "##cbs" + p.Name)) callPatron = p.Name;
                ImGui.EndCombo();
            }
            ImGui.SetNextItemWidth(320f); ImGui.InputText("Reason (griefing, refusing to leave...)##cbr", ref callReason, 200);
            var ok = callPatron.Trim().Length > 0;
            if (!ok) ImGui.BeginDisabled();
            if (ImGui.Button("Send to the manager on shift##csb")) { _ = Plugin.Lounge.CallBanAsync(callPatron.Trim(), callReason.Trim()); callMode = ""; callPatron = ""; callReason = ""; }
            if (!ok) ImGui.EndDisabled();
        }
        ImGui.SameLine(); if (ImGui.Button("Cancel##ccan")) callMode = "";
    }

    private static string Ago(string iso)
    {
        if (!DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t)) return "";
        var m = (int)Math.Max(0, (DateTime.UtcNow - t.ToUniversalTime()).TotalMinutes);
        return m < 1 ? "just now" : m < 60 ? $"{m} min" : $"{m / 60} h {m % 60} min";
    }

    private void DrawPatrons(LoungeView v)
    {
        var onShift = v.Me.Since.Length > 0;
        var waiting = v.Patrons.Where(p => p.Helper.Length == 0 && p.HelpedAt.Length == 0).ToList();
        var busy = v.Patrons.Where(p => p.Helper.Length > 0).ToList();
        var done = v.Patrons.Where(p => p.Helper.Length == 0 && p.HelpedAt.Length > 0).ToList();
        ImGui.TextColored(Gold, $"In the venue: {v.Patrons.Count}");
        ImGui.Text($"Waiting for help: {waiting.Count}   Being helped: {busy.Count}   Helped already: {done.Count}");
        if (v.Venue == null) ImGui.TextColored(Dim, "No venue spot is set, so nobody is detected automatically. A manager can set it below. You can still add people by hand.");
        else
        {
            var here = WorldInfo.CurrentRoom == v.Venue.Room;
            ImGui.TextColored(here ? Green : Dim, here ? "You are at the venue: people nearby are detected automatically while you are clocked in." : "You are not at the venue right now. Someone clocked in at the venue spot keeps this list up to date.");
        }
        if (!onShift) ImGui.TextColored(Dim, "Clock in on the Time clock tab to help people.");
        ImGui.SetNextItemWidth(180f); ImGui.InputText("##patronadd", ref patronAdd, 40);
        ImGui.SameLine();
        if (!onShift || patronAdd.Trim().Length == 0) ImGui.BeginDisabled();
        if (ImGui.Button("Add by name##patronaddb")) { _ = Plugin.Lounge.PatronAddAsync(patronAdd.Trim()); patronAdd = ""; }
        if (!onShift || patronAdd.Trim().Length == 0) ImGui.EndDisabled();
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        if (v.Patrons.Count == 0) ImGui.TextColored(Dim, "Nobody is in the venue.");
        void Row(LoungePatron p, string state)
        {
            ImGui.PushID("pt" + p.Name);
            ImGui.Text(p.Name);
            ImGui.SameLine(); ImGui.TextColored(Dim, $"here {Ago(p.ArrivedAt)}");
            ImGui.Text("    " + state);
            if (!onShift) ImGui.BeginDisabled();
            if (p.Helper.Length == 0) { if (ImGui.SmallButton("Help")) _ = Plugin.Lounge.PatronHelpAsync(p.Name); }
            else if (p.Mine) { if (ImGui.SmallButton("Done, helped")) _ = Plugin.Lounge.PatronDoneAsync(p.Name); ImGui.SameLine(); if (ImGui.SmallButton("Unhelp")) _ = Plugin.Lounge.PatronUnhelpAsync(p.Name); }
            else if (v.CanManage) { if (ImGui.SmallButton("Unhelp (manager)")) _ = Plugin.Lounge.PatronUnhelpAsync(p.Name); }
            if (p.Manual) { ImGui.SameLine(); if (ImGui.SmallButton("Remove")) _ = Plugin.Lounge.PatronRemoveAsync(p.Name); }
            if (!onShift) ImGui.EndDisabled();
            ImGui.PopID();
        }
        foreach (var p in waiting) Row(p, "WAITING FOR HELP");
        foreach (var p in busy) Row(p, p.Mine ? "BEING HELPED BY YOU" : $"BEING HELPED BY {p.Helper}");
        foreach (var p in done) Row(p, $"Helped by {p.HelpedBy} ({Ago(p.HelpedAt)} ago)");

        if (v.CanManage)
        {
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            if (ImGui.CollapsingHeader("Venue spot (managers)##venue"))
            {
                ImGui.TextWrapped("Stand inside the venue (in your house, or on the plot) and press Set venue here. Staff who are clocked in at that same place will see who is nearby. Stand in the same kind of place the staff will be: inside the house if you set it inside.");
                if (v.Venue != null) ImGui.TextColored(Green, $"Set: {(v.Venue.Label.Length > 0 ? v.Venue.Label + ", " : "")}radius {v.Venue.Radius}");
                ImGui.SetNextItemWidth(200f); ImGui.InputText("Name for it (optional)##vlabel", ref venueLabel, 60);
                ImGui.SetNextItemWidth(100f); ImGui.InputInt("Radius##vrad", ref venueRadius, 5, 10); venueRadius = Math.Clamp(venueRadius, 5, 300);
                if (ImGui.Button("Set venue here##vset")) _ = Plugin.Lounge.SetVenueAsync(venueLabel.Trim(), venueRadius);
                if (v.Venue != null) { ImGui.SameLine(); if (ImGui.Button("Clear##vclr")) _ = Plugin.Lounge.ClearVenueAsync(); }
            }
        }
    }

    private void DrawSchedule(LoungeView v)
    {
        ImGui.TextColored(Dim, "Upcoming nights. Times are shown in your own time zone. Press I can work to put your name down; a manager confirms who is working.");
        if (v.Schedule.Count == 0) ImGui.TextColored(Dim, "Nothing is scheduled yet.");
        foreach (var n in v.Schedule)
        {
            ImGui.PushID("nt" + n.Id);
            ImGui.Spacing();
            if (DateTime.TryParse(n.StartsAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var s) && DateTime.TryParse(n.EndsAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var e))
                ImGui.TextColored(Gold, $"{s.ToLocalTime():dddd, MMM d}   {s.ToLocalTime():h:mm tt} to {e.ToLocalTime():h:mm tt}");
            if (n.Note.Length > 0) ImGui.TextWrapped(n.Note);
            var working = n.Staff.Where(x => x.Status == "working").Select(x => x.Name).ToList();
            var avail = n.Staff.Where(x => x.Status != "working").Select(x => x.Name).ToList();
            ImGui.Text("Working: " + (working.Count == 0 ? "nobody yet" : string.Join(", ", working)));
            if (avail.Count > 0) ImGui.TextColored(Dim, "Can work: " + string.Join(", ", avail));
            if (n.Mine.Length == 0) { if (ImGui.SmallButton("I can work")) _ = Plugin.Lounge.RsvpAsync(n.Id, true); }
            else { ImGui.TextColored(Green, n.Mine == "working" ? "You are on the roster." : "You said you can work."); ImGui.SameLine(); if (ImGui.SmallButton("Take me off")) _ = Plugin.Lounge.RsvpAsync(n.Id, false); }
            if (v.CanManage)
            {
                if (ImGui.SmallButton("Edit"))
                {
                    editNight = n.Id; nightNote = n.Note;
                    if (DateTime.TryParse(n.StartsAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var s2) && DateTime.TryParse(n.EndsAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var e2))
                    { nightDate = s2.ToLocalTime().ToString("yyyy-MM-dd"); nightTime = s2.ToLocalTime().ToString("HH:mm"); nightHours = Math.Clamp((int)Math.Round((e2 - s2).TotalHours), 1, 24); }
                }
                ImGui.SameLine(); if (ImGui.SmallButton("Remove night")) _ = Plugin.Lounge.DeleteNightAsync(n.Id);
                foreach (var x in n.Staff)
                {
                    ImGui.PushID("r" + x.Name);
                    ImGui.Text("    " + x.Name + (x.Status == "working" ? " (working)" : " (can work)"));
                    if (x.Status != "working") { ImGui.SameLine(); if (ImGui.SmallButton("Confirm")) _ = Plugin.Lounge.SetRosterAsync(n.Id, x.Name, "working"); }
                    ImGui.SameLine(); if (ImGui.SmallButton("Remove")) _ = Plugin.Lounge.SetRosterAsync(n.Id, x.Name, "");
                    ImGui.PopID();
                }
                ImGui.SetNextItemWidth(150f); ImGui.InputText("##rname", ref rosterName, 40); ImGui.SameLine();
                if (ImGui.SmallButton("Put on roster") && rosterName.Trim().Length > 0) { _ = Plugin.Lounge.SetRosterAsync(n.Id, rosterName.Trim(), "working"); rosterName = ""; }
            }
            ImGui.Separator();
            ImGui.PopID();
        }
        if (v.CanManage)
        {
            ImGui.Spacing();
            ImGui.TextColored(Gold, editNight == null ? "Add a night" : "Edit this night");
            if (nightDate.Length == 0) nightDate = DateTime.Now.AddDays(1).ToString("yyyy-MM-dd");
            ImGui.SetNextItemWidth(110f); ImGui.InputText("Date (yyyy-mm-dd)##nd", ref nightDate, 10);
            ImGui.SetNextItemWidth(70f); ImGui.InputText("Start (24 hour, like 20:00)##nt", ref nightTime, 5);
            ImGui.SetNextItemWidth(100f); ImGui.InputInt("Hours open##nh", ref nightHours, 1, 1); nightHours = Math.Clamp(nightHours, 1, 24);
            ImGui.SetNextItemWidth(320f); ImGui.InputText("Note (optional)##nn", ref nightNote, 200);
            var ok = DateTime.TryParse(nightDate + " " + nightTime, out var local);
            if (!ok) ImGui.TextColored(Red, "That date or time is not valid.");
            if (!ok) ImGui.BeginDisabled();
            if (ImGui.Button((editNight == null ? "Add night" : "Save night") + "##nsave"))
            {
                var start = DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();
                _ = Plugin.Lounge.SaveNightAsync(editNight, start.ToString("o"), start.AddHours(nightHours).ToString("o"), nightNote.Trim());
                editNight = null; nightNote = "";
            }
            if (!ok) ImGui.EndDisabled();
            if (editNight != null) { ImGui.SameLine(); if (ImGui.Button("Cancel##ncan")) { editNight = null; nightNote = ""; } }
        }
    }

    private void DrawAdvert(LoungeView v)
    {
        if (!adLoaded) { adLoaded = true; adEdit = v.AdText; }
        ImGui.TextColored(Gold, "Advert");
        if (v.AdText.Length == 0) ImGui.TextColored(Dim, "No advert has been written yet.");
        else ImGui.TextWrapped(v.AdText);
        var ch = Math.Clamp(Plugin.Config.LoungeAdChannel, 0, AdChannels.Length - 1);
        ImGui.SetNextItemWidth(240f);
        if (ImGui.BeginCombo("Chat channel##adch", AdChannels[ch].Label))
        {
            for (var i = 0; i < AdChannels.Length; i++)
                if (ImGui.Selectable(AdChannels[i].Label, i == ch)) { Plugin.Config.LoungeAdChannel = i; Plugin.Config.Save(); }
            ImGui.EndCombo();
        }
        ImGui.TextColored(Dim, "Shout reaches the whole area (in a housing ward, that ward). In the game's chat window the channel is picked by the start of the line.");
        if (v.AdText.Length == 0) ImGui.BeginDisabled();
        if (ImGui.Button("Copy advert for chat##adcopy")) ImGui.SetClipboardText(AdChannels[ch].Prefix + v.AdText);
        if (v.AdText.Length == 0) ImGui.EndDisabled();
        ImGui.TextColored(Dim, "Then click in the game's chat box, press Ctrl+V and press Enter. The plugin never sends chat for you.");
        if (v.CanManage)
        {
            ImGui.Spacing();
            ImGui.InputTextMultiline("##adedit", ref adEdit, 400, new System.Numerics.Vector2(420f, 70f));
            ImGui.TextColored(Dim, $"{adEdit.Length}/400");
            if (ImGui.Button("Save advert##adsave")) _ = Plugin.Lounge.SaveAdAsync(adEdit.Trim());
        }
    }

    private void DrawPf(LoungeView v)
    {
        if (!pfLoaded) { pfLoaded = true; pfEdit = v.Pf.Text; pfMinutes = v.Pf.Minutes; }
        ImGui.TextColored(Gold, "Party Finder listing for tonight");
        if (v.Pf.Text.Length == 0) ImGui.TextColored(Dim, "No listing has been written yet.");
        else
        {
            ImGui.TextWrapped(v.Pf.Text);
            ImGui.TextColored(Dim, $"{v.Pf.Text.Length} / 192 characters");
            if (ImGui.Button("Copy the listing##pfc")) ImGui.SetClipboardText(v.Pf.Text);
            ImGui.SameLine(); if (ImGui.Button("I posted it##pfp")) _ = Plugin.Lounge.PostedAsync();
            ImGui.TextColored(Dim, "Copy it, paste it into the Party Finder description, then press I posted it so everyone knows and you get a reminder.");
        }
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        if (v.Pf.PostedAt.Length > 0 && DateTime.TryParse(v.Pf.PostedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var at))
        {
            var ago = (int)Math.Max(0, (DateTime.UtcNow - at.ToUniversalTime()).TotalMinutes);
            ImGui.Text($"Last posted by {v.Pf.PostedBy} at {at.ToLocalTime():h:mm tt} ({ago} min ago)");
            var left = v.Pf.Minutes - ago;
            if (left > 0) ImGui.TextColored(Green, $"Refresh the listing in about {left} min.");
            else ImGui.TextColored(Red, "It is time to refresh the Party Finder listing.");
            ImGui.TextColored(Dim, "If you posted it, the plugin also tells you in chat when the time is up.");
        }
        else ImGui.TextColored(Dim, "Nobody has posted the listing yet tonight.");
        if (!v.CanManage) return;
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, "Write the listing (managers)");
        ImGui.InputTextMultiline("##pfedit", ref pfEdit, 192, new Vector2(ImGui.GetContentRegionAvail().X - 8f, 70f));
        ImGui.TextColored(Dim, $"{pfEdit.Length} / 192 characters. That is the longest a Party Finder description can be.");
        ImGui.SetNextItemWidth(100f); ImGui.InputInt("Remind to refresh after (minutes)##pfm", ref pfMinutes, 5, 10); pfMinutes = Math.Clamp(pfMinutes, 5, 120);
        if (ImGui.Button("Save the listing##pfs")) _ = Plugin.Lounge.SavePfAsync(pfEdit.Trim(), pfMinutes);
    }

    private void DrawManager(LoungeView v)
    {
        if (ImGui.CollapsingHeader("Hours and pay##lgh1", ImGuiTreeNodeFlags.DefaultOpen))
        {
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 8f); ImGui.InputText("##lghours", ref hoursEdit, 300);
        ImGui.TextColored(Dim, "Days open and hours, in your own words. Everyone sees this at the top.");
        ImGui.SetNextItemWidth(140f); ImGui.InputInt("Gold per hour##lgrate", ref rateEdit, 500, 5000); rateEdit = Math.Clamp(rateEdit, 0, 1000000);
        ImGui.TextColored(Dim, "Each shift locks in the rate at the moment of clock in. Shifts are capped at 12 hours.");
        if (ImGui.Button("Save hours and pay##lgsave")) _ = Plugin.Lounge.SaveInfoAsync(hoursEdit.Trim(), rateEdit);
        }

        if (ImGui.CollapsingHeader("Menu editor##lgh2"))
        {
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
        }

        if (ImGui.CollapsingHeader("Receipt book, tonight##lgh3", ImGuiTreeNodeFlags.DefaultOpen))
        {
        var t = v.Tonight;
        if (t == null || (t.Items.Count == 0 && t.Services.Count == 0)) ImGui.TextColored(Dim, "Nothing sold yet.");
        else
        {
            foreach (var l in t.Items)
            {
                ImGui.Text($"{l.Count} x {l.Name}  =  {l.Total:N0} gil");
                if (l.By.Count > 0) { ImGui.Indent(14f); ImGui.TextColored(Dim, string.Join(",  ", l.By.Select(b => $"{b.Name} sold {b.Count}"))); ImGui.Unindent(14f); }
            }
            ImGui.TextColored(Green, $"Items: {t.Count} sold, {t.Total:N0} gil");
        }
        if (t != null && t.Services.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(Gold, "Services booked");
            foreach (var x in t.Services) ImGui.Text($"{x.Who}: {x.Name}{(x.Customer.Length > 0 ? " for " + x.Customer : "")}, {Dur.Of(x.Minutes)} at {x.Rate:N0} per hour = {x.Total:N0} gil");
            ImGui.TextColored(Green, $"Services: {t.Services.Count} booked, {t.ServiceTotal:N0} gil");
        }
        if (t != null && (t.Items.Count > 0 || t.Services.Count > 0)) ImGui.TextColored(Green, $"Grand total expected: {t.Grand:N0} gil");
        if (!v.NightOpen)
        {
            ImGui.TextColored(Gold, "The night is closed. Selling is switched off until you start a new night.");
            if (ImGui.Button("Start a new night##lgstart")) _ = Plugin.Lounge.StartNightAsync();
        }
        else if (!confirmNight) { if (ImGui.Button("Close the night##lgnn")) confirmNight = true; }
        else
        {
            ImGui.TextColored(Red, "Close the night? The receipt book is saved to Past nights and cleared.");
            if (ImGui.SmallButton("Yes, close the night##lgny")) { _ = Plugin.Lounge.CloseNightAsync(); confirmNight = false; }
            ImGui.SameLine(); if (ImGui.SmallButton("Cancel##lgnc")) confirmNight = false;
        }
        }

        if (ImGui.CollapsingHeader("Past nights##lgh5"))
        {
            ImGui.TextColored(Dim, "Every night you close is saved here with who sold what. A text copy is also saved on the computer of whoever closes the night.");
            var rows = v.Nights ?? new();
            if (rows.Count == 0) ImGui.TextColored(Dim, "No nights have been closed yet.");
            foreach (var n in rows)
            {
                ImGui.PushID("pn" + n.Id);
                var when = DateTime.TryParse(n.ClosedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d.ToLocalTime().ToString("ddd MMM d yyyy, h:mm tt") : n.ClosedAt;
                ImGui.Text($"{when}  -  {n.Count} sold, {n.Total:N0} gil"); ImGui.SameLine();
                if (ImGui.SmallButton("View")) { _ = Plugin.Lounge.ViewNightAsync(n.Id); nightTitle = n.Id; }
                ImGui.PopID();
            }
            var open = Plugin.Lounge.OpenNight();
            if (open != null)
            {
                if (nightTitle != open.ClosedAt) { nightTitle = open.ClosedAt; nightText = open.ToText(); }
                ImGui.Spacing();
                ImGui.InputTextMultiline("##nighttext", ref nightText, 8000, new Vector2(ImGui.GetContentRegionAvail().X - 8f, 220f));
                if (ImGui.SmallButton("Copy this text##nc")) ImGui.SetClipboardText(open.ToText());
                ImGui.SameLine(); if (ImGui.SmallButton("Close##nx")) { Plugin.Lounge.CloseNight(); nightText = ""; nightTitle = ""; }
            }
        }

        if (ImGui.CollapsingHeader("Positions##lghpos"))
        {
            ImGui.TextColored(Dim, "Who is the manager, security, bartender and so on. The Call manager and Call security buttons go to people on shift with those positions.");
            foreach (var r in v.Roles) ImGui.Text($"{r.Name}: {string.Join(", ", r.Roles)}");
            ImGui.SetNextItemWidth(180f); ImGui.InputText("Name (exactly as registered)##posn", ref posName, 40);
            var cur = v.Roles.FirstOrDefault(r => string.Equals(r.Name, posName.Trim(), StringComparison.OrdinalIgnoreCase))?.Roles ?? new List<string>();
            if (posName.Trim().Length > 0)
                foreach (var role in LoungeState.Positions)
                {
                    var on = cur.Contains(role);
                    if (ImGui.Checkbox(role + "##pos" + role, ref on))
                    {
                        var next = cur.Where(x => x != role).ToList(); if (on) next.Add(role);
                        _ = Plugin.Lounge.SetRolesAsync(posName.Trim(), next);
                    }
                }
        }
        if (ImGui.CollapsingHeader("Staff and pay##lgh4", ImGuiTreeNodeFlags.DefaultOpen))
        {
        var staff = v.Staff ?? new();
        if (staff.Count == 0) ImGui.TextColored(Dim, "Nobody has an open shift or pay waiting.");
        foreach (var s in staff)
        {
            ImGui.PushID("st" + s.Name);
            ImGui.Text(s.Name); ImGui.SameLine();
            ImGui.TextColored(Dim, (s.ClockedIn ? "clocked in" : "off shift") + (s.Gold > 0 || s.Minutes > 0 ? $", owed {s.Gold:N0} gold for {s.Minutes} min" : ""));
            var busySvc = v.Sessions.FirstOrDefault(x => string.Equals(x.Who, s.Name, StringComparison.OrdinalIgnoreCase));
            if (busySvc != null) { ImGui.SameLine(); ImGui.TextColored(Dim, $"in session: {busySvc.Name}"); ImGui.SameLine(); if (ImGui.SmallButton("Close session")) _ = Plugin.Lounge.EndSessionAsync(s.Name); }
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
    }

    private void ResetEditor() { editId = null; mName = ""; mDesc = ""; mPrice = 100; mGift = false; gIcon = "game:20001"; gName = ""; gDesc = ""; gLabel = ""; gText = ""; }
    private static string Local(string iso) => DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d.ToLocalTime().ToString("h:mm tt") : "";
    private static int Mins(string iso) => DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? (int)Math.Max(0, (DateTime.UtcNow - d.ToUniversalTime()).TotalMinutes) : 0;
}
