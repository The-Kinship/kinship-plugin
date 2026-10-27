using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

/// <summary>Your mailbox: read mail, collect gold and items, write mail to another member, take back mail that hasn't been opened.</summary>
public sealed class MailTab
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Coin = new(0.95f, 0.8f, 0.3f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.5f, 1f);
    private const float Slot = 36f;

    private bool writing, membersAsked, confirmDelete;
    private string to = "", subject = "", body = "";
    private bool anonymous;
    private int sendGold;
    private readonly List<(ItemView Item, int Count)> attach = new();

    public void Draw(ItemsSnap inv)
    {
        Plugin.Mail.RefreshIfDue(30);
        if (!membersAsked) { membersAsked = true; _ = Plugin.Items.LoadMembersAsync(); }
        var s = Plugin.Mail.Snapshot();

        if (s.Error.Length > 0) ImGui.TextColored(Red, s.Error);
        else if (s.Notice.Length > 0) ImGui.TextColored(Green, s.Notice);

        if (writing) { DrawWrite(inv, s); return; }
        if (s.Open != null) { DrawOpen(s.Open, s); return; }
        DrawBox(s);
    }

    // ---------- the mailbox ----------
    private void DrawBox(MailSnap s)
    {
        ImGui.TextColored(Gold, $"Mailbox  {s.Inbox.Count}/{s.Capacity}");
        ImGui.SameLine(); if (ImGui.SmallButton("Refresh##mail")) _ = Plugin.Mail.RefreshAsync();
        ImGui.SameLine(); if (ImGui.SmallButton("Write a mail##mail")) { writing = true; Plugin.Mail.ClearMessages(); }
        ImGui.TextColored(Dim, "Mail is kept for 30 days. Gold and items that nobody collects go back to the sender.");
        ImGui.Spacing();
        if (s.Inbox.Count == 0) ImGui.TextColored(Dim, "Your mailbox is empty.");
        foreach (var m in s.Inbox)
        {
            ImGui.TextColored(m.Unread ? Gold : Dim, (m.Unread ? "● " : "   ") + m.Subject);
            ImGui.TextColored(Dim, $"From {m.From}  ·  {m.DaysLeft()} days left" + (m.Gold > 0 ? $"  ·  {m.Gold:N0} gold" : "") + (m.Slots > 0 ? $"  ·  {m.Slots} item" + (m.Slots == 1 ? "" : "s") : ""));
            if (ImGui.SmallButton("Open##mo" + m.Id)) { confirmDelete = false; _ = Plugin.Mail.OpenAsync(m.Id); }
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        }

        if (s.Outbox.Count > 0)
        {
            ImGui.TextColored(Gold, "Sent and not opened yet");
            Wrapped(Dim, "You can take these back until the person opens them.");
            foreach (var m in s.Outbox)
            {
                ImGui.Text(m.Subject); ImGui.SameLine();
                ImGui.TextColored(Dim, $"to {m.To}" + (m.Gold > 0 ? $"  ·  {m.Gold:N0} gold" : "") + (m.Slots > 0 ? $"  ·  {m.Slots} item(s)" : ""));
                if (!s.Busy && ImGui.SmallButton("Take back##mr" + m.Id)) _ = Plugin.Mail.RecallAsync(m.Id);
                ImGui.Spacing();
            }
        }
    }

    // ---------- reading one mail ----------
    private void DrawOpen(MailView m, MailSnap s)
    {
        if (ImGui.SmallButton("< Back##mail")) { Plugin.Mail.CloseOpen(); return; }
        ImGui.TextColored(Gold, m.Subject);
        ImGui.TextColored(Dim, $"From {m.From}" + (m.Returned ? "  (nobody collected it)" : "") + $"  ·  {m.DaysLeft()} days left");
        ImGui.Separator();
        Wrapped(new Vector4(1f, 1f, 1f, 1f), string.IsNullOrWhiteSpace(m.Body) ? "(no message)" : m.Body!);
        ImGui.Spacing();
        var items = m.Items ?? new();
        if (m.Gold > 0 || items.Count > 0)
        {
            ImGui.Separator();
            ImGui.TextColored(Gold, "Attached");
            if (m.Gold > 0) ImGui.TextColored(Coin, $"{m.Gold:N0} gold");
            ItemView? hovered = null;
            for (var i = 0; i < items.Count; i++)
            {
                if (i > 0) ImGui.SameLine();
                var origin = ImGui.GetCursorScreenPos();
                var wrap = ItemIcons.Wrap(items[i].Icon);
                if (wrap != null) ImGui.Image(wrap.Handle, new Vector2(Slot, Slot)); else ImGui.Button("?##mi" + i, new Vector2(Slot, Slot));
                ItemsTab.DrawQty(items[i], origin, Slot);
                if (ImGui.IsItemHovered()) hovered = items[i];
            }
            if (hovered != null) ItemsTab.DrawTooltip(hovered);
            var inv = Plugin.Items.Snapshot();
            if (!s.Busy && ImGui.Button("Collect everything##mail")) _ = Plugin.Mail.CollectAsync(m.Id);
            ImGui.TextColored(Dim, $"Items go into your inventory ({inv.Items.Count}/{inv.Capacity} slots used). Make room first if it's full.");
            ImGui.TextColored(Dim, "You have to collect before you can delete this mail.");
        }
        else
        {
            ImGui.Spacing();
            if (confirmDelete)
            {
                ImGui.TextColored(Red, "Delete this mail?"); ImGui.SameLine();
                if (ImGui.SmallButton("Yes, delete##md")) { confirmDelete = false; _ = Plugin.Mail.DeleteAsync(m.Id); }
                ImGui.SameLine(); if (ImGui.SmallButton("No##md")) confirmDelete = false;
            }
            else if (ImGui.SmallButton("Delete##md")) confirmDelete = true;
        }
    }

    // ---------- writing ----------
    private void DrawWrite(ItemsSnap inv, MailSnap s)
    {
        if (ImGui.SmallButton("< Back##mailw")) { writing = false; return; }
        ImGui.TextColored(Gold, "Write a mail");
        ImGui.SetNextItemWidth(220f);
        if (ImGui.BeginCombo("To##mailto", to.Length > 0 ? to : "Choose a member"))
        {
            foreach (var m in inv.Members) if (ImGui.Selectable(m, m == to)) to = m;
            ImGui.EndCombo();
        }
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 8f); ImGui.InputText("##mailsub", ref subject, 60);
        ImGui.TextColored(Dim, "Subject");
        ImGui.InputTextMultiline("##mailbody", ref body, 1500, new Vector2(ImGui.GetContentRegionAvail().X - 8f, 130f));
        ImGui.Checkbox("Send anonymously##mail", ref anonymous);
        if (anonymous) ImGui.TextColored(Dim, "They won't see your name. The SMs can still see who sent it.");

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.SetNextItemWidth(110f); ImGui.InputInt("Gold to attach##mailgold", ref sendGold, 0);
        if (sendGold < 0) sendGold = 0;
        ImGui.TextColored(Coin, $"Your purse: {Plugin.Items.Gold:N0} gold");

        ImGui.Spacing();
        ImGui.TextColored(Gold, $"Items attached ({attach.Count}/3)");
        for (var i = 0; i < attach.Count; i++)
        {
            var (it, count) = attach[i];
            ImGui.Text($"{it.Name} x{count}");
            ImGui.SameLine(); if (ImGui.SmallButton("-##ma" + i)) { if (count > 1) attach[i] = (it, count - 1); else { attach.RemoveAt(i); break; } }
            ImGui.SameLine(); if (ImGui.SmallButton("+##mp" + i) && count < it.Qty) attach[i] = (it, count + 1);
            ImGui.SameLine(); if (ImGui.SmallButton("Remove##mx" + i)) { attach.RemoveAt(i); break; }
        }
        Wrapped(Dim, "Click an item below to attach it. Click again to add more of the same stack.");
        ItemView? hovered = null;
        for (var i = 0; i < inv.Items.Count; i++)
        {
            if (i % 8 != 0) ImGui.SameLine();
            var it = inv.Items[i];
            var origin = ImGui.GetCursorScreenPos();
            var wrap = ItemIcons.Wrap(it.Icon);
            if (wrap != null) ImGui.Image(wrap.Handle, new Vector2(Slot, Slot)); else ImGui.Button("?##mw" + i, new Vector2(Slot, Slot));
            ItemsTab.DrawQty(it, origin, Slot);
            if (ImGui.IsItemHovered()) hovered = it;
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) Attach(it);
        }
        if (hovered != null) ItemsTab.DrawTooltip(hovered);

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        var ok = to.Length > 0 && subject.Trim().Length > 0 && sendGold <= Plugin.Items.Gold && !s.Busy;
        if (!ok) ImGui.BeginDisabled();
        if (ImGui.Button("Send mail##mail")) _ = Send();
        if (!ok) ImGui.EndDisabled();
        if (sendGold > Plugin.Items.Gold) { ImGui.SameLine(); ImGui.TextColored(Dim, "You don't have that much gold."); }
        ImGui.TextColored(Dim, "Gold and items leave your inventory as soon as you send. You can take mail back until it is opened.");
    }

    private void Attach(ItemView it)
    {
        var at = attach.FindIndex(a => a.Item.Id == it.Id);
        if (at >= 0) { if (attach[at].Count < it.Qty) attach[at] = (attach[at].Item, attach[at].Count + 1); return; }
        if (attach.Count < 3) attach.Add((it, 1));
    }

    private async System.Threading.Tasks.Task Send()
    {
        var ok = await Plugin.Mail.SendAsync(to, subject.Trim(), body.Trim(), anonymous, sendGold, attach.Select(a => (a.Item.Id, a.Count)).ToList()).ConfigureAwait(false);
        if (ok) { subject = ""; body = ""; sendGold = 0; anonymous = false; attach.Clear(); writing = false; }
    }

    private static void Wrapped(Vector4 colour, string text)
    {
        ImGui.PushTextWrapPos(0f);
        ImGui.TextColored(colour, text);
        ImGui.PopTextWrapPos();
    }
}
