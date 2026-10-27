using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace KinshipPlugin;

public sealed class MailView
{
    public string Id { get; set; } = ""; public string From { get; set; } = ""; public string To { get; set; } = ""; public string Subject { get; set; } = "";
    public string? Body { get; set; } public bool Anonymous { get; set; } public int Gold { get; set; } public int Slots { get; set; }
    public bool Unread { get; set; } public bool Returned { get; set; } public string SentAt { get; set; } = ""; public string ExpiresAt { get; set; } = "";
    public List<ItemView>? Items { get; set; }
    public int DaysLeft() => DateTime.TryParse(ExpiresAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? Math.Max(0, (int)Math.Ceiling((t - DateTime.UtcNow).TotalDays)) : 0;
}
public sealed record MailSnap(List<MailView> Inbox, List<MailView> Outbox, MailView? Open, int Unread, int Capacity, string Error, string Notice, bool Busy);

/// <summary>Mail between members: a message, optional gold and up to three item stacks. The mail itself lives on your server.</summary>
public sealed class MailState
{
    private readonly object gate = new();
    private List<MailView> inbox = new(), outbox = new();
    private MailView? open;
    private int unread, capacity = 30, lastUnread = -1;
    private string error = "", notice = "";
    private bool busy;
    private DateTime lastList = DateTime.MinValue, lastPoll = DateTime.MinValue;

    public int Unread { get { lock (gate) { return unread; } } }
    public MailSnap Snapshot() { lock (gate) { return new MailSnap(inbox, outbox, open, unread, capacity, error, notice, busy); } }
    public void ClearMessages() { lock (gate) { error = ""; notice = ""; } }
    public void CloseOpen() { lock (gate) { open = null; } }

    /// <summary>Checks for new mail about once a minute, even when the tab is closed, so the tab can show a badge.</summary>
    public void Poll()
    {
        bool due;
        lock (gate) { due = Plugin.Config.Me().ItemsToken.Length > 0 && (DateTime.UtcNow - lastPoll).TotalSeconds > 60; if (due) lastPoll = DateTime.UtcNow; }
        if (due) _ = PollAsync();
    }
    private async Task PollAsync()
    {
        try
        {
            var r = await ItemsState.SendAsync(HttpMethod.Get, "/mail/count", null).ConfigureAwait(false);
            bool more;
            lock (gate) { more = lastUnread >= 0 && r.Unread > lastUnread; unread = r.Unread; lastUnread = r.Unread; if (r.Capacity > 0) capacity = r.Capacity; }
            if (more) _ = Plugin.Framework.RunOnFrameworkThread(() => Plugin.Chat.Print("[Kinship] You have new mail."));
        }
        catch (Exception) { /* try again next minute */ }
    }

    public void RefreshIfDue(int seconds)
    {
        bool due;
        lock (gate) { due = !busy && Plugin.Config.Me().ItemsToken.Length > 0 && (DateTime.UtcNow - lastList).TotalSeconds > seconds; if (due) lastList = DateTime.UtcNow; }
        if (due) _ = RefreshAsync();
    }
    public async Task RefreshAsync()
    {
        try
        {
            var r = await ItemsState.SendAsync(HttpMethod.Get, "/mail", null).ConfigureAwait(false);
            lock (gate)
            {
                inbox = r.Inbox ?? new(); outbox = r.Outbox ?? new(); lastList = DateTime.UtcNow; if (r.Capacity > 0) capacity = r.Capacity;
                unread = inbox.Count(m => m.Unread); lastUnread = unread;
                if (open != null && inbox.All(m => m.Id != open.Id)) open = null;
            }
        }
        catch (Exception) { /* keep the list we have */ }
    }

    private async Task<ItemReply?> Exec(Func<Task<ItemReply>> call, string? done = null)
    {
        lock (gate) { if (busy) return null; busy = true; error = ""; notice = ""; }
        try
        {
            var r = await call().ConfigureAwait(false);
            lock (gate) { if (done != null) notice = done; }
            if (r.Items != null) Plugin.Items.SetInventory(r.Items);
            return r;
        }
        catch (CombatException ex) { lock (gate) { error = ex.Message; } }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Mail request failed"); lock (gate) { error = "Couldn't reach the server. Check your connection and try again."; } }
        finally { lock (gate) { busy = false; } }
        return null;
    }

    public async Task OpenAsync(string id)
    {
        var r = await Exec(() => ItemsState.SendAsync(HttpMethod.Get, "/mail/" + id, null)).ConfigureAwait(false);
        if (r?.Mail != null) { lock (gate) { open = r.Mail; } await RefreshAsync().ConfigureAwait(false); }
    }
    public async Task<bool> SendAsync(string to, string subject, string body, bool anonymous, int gold, List<(string Id, int Count)> items)
    {
        var r = await Exec(() => ItemsState.SendAsync(HttpMethod.Post, "/mail", new
        {
            to, subject, body, anonymous, gold = gold > 0 ? gold : (int?)null,
            items = items.Select(i => new { itemId = i.Id, count = i.Count }).ToList()
        }), $"Mail sent to {to}.").ConfigureAwait(false);
        if (r == null) return false;
        _ = Plugin.Items.RefreshAsync(true); _ = Plugin.Bank.RefreshAsync(); await RefreshAsync().ConfigureAwait(false);
        return true;
    }
    public async Task CollectAsync(string id)
    {
        var r = await Exec(() => ItemsState.SendAsync(HttpMethod.Post, $"/mail/{id}/collect", new { }), "Collected.").ConfigureAwait(false);
        if (r != null) { _ = Plugin.Bank.RefreshAsync(); await OpenAsync(id).ConfigureAwait(false); }
    }
    public async Task DeleteAsync(string id)
    {
        var r = await Exec(() => ItemsState.SendAsync(HttpMethod.Post, $"/mail/{id}/delete", new { }), "Mail deleted.").ConfigureAwait(false);
        if (r != null) { CloseOpen(); await RefreshAsync().ConfigureAwait(false); }
    }
    public async Task RecallAsync(string id)
    {
        var r = await Exec(() => ItemsState.SendAsync(HttpMethod.Post, $"/mail/{id}/recall", new { }), "Mail taken back.").ConfigureAwait(false);
        if (r != null) { _ = Plugin.Bank.RefreshAsync(); await RefreshAsync().ConfigureAwait(false); }
    }

    // ---------- Advocate tools (creation password) ----------
    public sealed class AdminMail { public string Id { get; set; } = ""; public string From { get; set; } = ""; public string To { get; set; } = ""; public string Subject { get; set; } = ""; public bool Anonymous { get; set; } public int Gold { get; set; } public bool Returned { get; set; } public string SentAt { get; set; } = ""; }
    private List<AdminMail> admin = new();
    private string adminError = "";
    public List<AdminMail> AdminList() { lock (gate) { return admin.ToList(); } }
    public string AdminError() { lock (gate) { return adminError; } }
    public async Task AdminLoadAsync()
    {
        var pw = Plugin.Config.CreatePassword;
        if (pw.Length == 0) { lock (gate) { adminError = "Enter the creation password first."; } return; }
        try
        {
            var r = await ItemsState.SendAsync(HttpMethod.Post, "/mail/admin/list", new { adminPassword = pw }).ConfigureAwait(false);
            lock (gate) { admin = r.Mails ?? new(); adminError = ""; }
        }
        catch (CombatException ex) { lock (gate) { adminError = ex.Message; } }
        catch (Exception) { lock (gate) { adminError = "Couldn't reach the server. Try again."; } }
    }
    public async Task AdminDeleteAsync(string id)
    {
        var pw = Plugin.Config.CreatePassword;
        try { await ItemsState.SendAsync(HttpMethod.Post, "/mail/admin/delete", new { adminPassword = pw, mailId = id }).ConfigureAwait(false); }
        catch (CombatException ex) { lock (gate) { adminError = ex.Message; } return; }
        catch (Exception) { lock (gate) { adminError = "Couldn't reach the server. Try again."; } return; }
        await AdminLoadAsync().ConfigureAwait(false);
    }
}
