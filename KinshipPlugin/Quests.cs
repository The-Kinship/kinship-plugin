using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace KinshipPlugin;

public sealed class QuestNow { public string Type { get; set; } = ""; public string Npc { get; set; } = ""; public string Item { get; set; } = ""; public string Hint { get; set; } = ""; }
public sealed class QuestGift { public string Name { get; set; } = ""; public string Icon { get; set; } = ""; public string Description { get; set; } = ""; public string Flavor { get; set; } = ""; }
public sealed class QuestStepView
{
    public string Type { get; set; } = "talk"; public string Npc { get; set; } = ""; public string Text { get; set; } = ""; public string Hint { get; set; } = "";
    public string Item { get; set; } = ""; public QuestGift? Gift { get; set; }
}
public sealed class QuestPlayer { public string Name { get; set; } = ""; public int Step { get; set; } public bool Done { get; set; } }
/// <summary>One quest. Players get QuestId/Step/Current; Advocates get Id/Steps/Players.</summary>
public sealed class QuestView
{
    public string QuestId { get; set; } = ""; public string Id { get; set; } = ""; public string Title { get; set; } = ""; public string Intro { get; set; } = "";
    public int Step { get; set; } public int Total { get; set; } public bool Done { get; set; } public QuestNow? Current { get; set; }
    public List<QuestStepView>? Steps { get; set; } public List<QuestPlayer>? Players { get; set; }
}
public sealed class TalkLine { public string Quest { get; set; } = ""; public bool Ok { get; set; } public string Text { get; set; } = ""; public bool Finished { get; set; } public List<string>? Gained { get; set; } }
public sealed record TalkResult(string Npc, List<TalkLine> Lines, string Error);

/// <summary>What the Advocate is typing for one step.</summary>
public sealed class StepForm
{
    public string Type = "talk", Npc = "", Text = "", Hint = "", Item = "", GiftName = "", GiftIcon = "game:20001", GiftDesc = "";
}
public sealed class QuestForm
{
    public string Id = "", Title = "", Intro = "";
    public List<StepForm> Steps = new() { new StepForm() };
}
public sealed record QuestSnap(List<QuestView> Mine, List<QuestView> Admin, string Error, string Notice, bool Busy);

/// <summary>Quests: the server keeps the quests and each player's place. Talking to your targeted NPC moves you along.</summary>
public sealed class QuestState
{
    public const int MaxSteps = 12;
    private readonly object gate = new();
    private List<QuestView> mine = new(), admin = new();
    private string error = "", notice = "";
    private bool busy;
    private DateTime lastList = DateTime.MinValue;
    private TalkResult? talk;

    public QuestSnap Snapshot() { lock (gate) { return new QuestSnap(mine.ToList(), admin.ToList(), error, notice, busy); } }
    public TalkResult? LastTalk() { lock (gate) { return talk; } }
    public void DismissTalk() { lock (gate) { talk = null; } }
    public List<QuestView> Active() { lock (gate) { return mine.Where(q => !q.Done).ToList(); } }
    public void ClearMessages() { lock (gate) { error = ""; notice = ""; } }

    public void RefreshIfDue(int seconds)
    {
        bool due;
        lock (gate) { due = !busy && Plugin.Config.ItemsToken.Length > 0 && (DateTime.UtcNow - lastList).TotalSeconds > seconds; if (due) lastList = DateTime.UtcNow; }
        if (due) _ = RefreshAsync();
    }
    public async Task RefreshAsync()
    {
        try
        {
            var r = await ItemsState.SendAsync(HttpMethod.Get, "/quests/mine", null).ConfigureAwait(false);
            lock (gate) { mine = r.Quests ?? new(); lastList = DateTime.UtcNow; }
        }
        catch (Exception) { /* keep what we have */ }
    }

    public async Task TalkAsync(string npc)
    {
        lock (gate) { if (busy) return; busy = true; error = ""; notice = ""; }
        try
        {
            var r = await ItemsState.SendAsync(HttpMethod.Post, "/quests/talk", new { npc }).ConfigureAwait(false);
            lock (gate) { talk = new TalkResult(npc, r.Results ?? new(), ""); }
        }
        catch (CombatException ex) { lock (gate) { talk = new TalkResult(npc, new(), ex.Message); } }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Quest talk failed"); lock (gate) { talk = new TalkResult(npc, new(), "Couldn't reach the server. Check your connection and try again."); } }
        finally { lock (gate) { busy = false; } }
        await RefreshAsync().ConfigureAwait(false);
        _ = Plugin.Items.RefreshAsync(true);
    }

    // ---------- Advocate tools (creation password) ----------
    private async Task<bool> Admin(string path, object body, string done, bool reload = true)
    {
        var pw = Plugin.Config.CreatePassword;
        lock (gate) { if (busy) return false; if (pw.Length == 0) { error = "Enter the creation password in the Advocate tools first."; return false; } busy = true; error = ""; notice = ""; }
        try
        {
            var r = await ItemsState.SendAsync(HttpMethod.Post, "/quests/admin/" + path, body).ConfigureAwait(false);
            lock (gate) { if (path == "list") admin = r.Quests ?? new(); else notice = done; }
        }
        catch (CombatException ex) { lock (gate) { error = ex.Message; } return false; }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Quest request failed"); lock (gate) { error = "Couldn't reach the server. Check your connection and try again."; } return false; }
        finally { lock (gate) { busy = false; } }
        if (reload && path != "list") await AdminListAsync().ConfigureAwait(false);
        return true;
    }
    private object Auth(Dictionary<string, object?> d) { d["adminPassword"] = Plugin.Config.CreatePassword; return d; }
    public Task<bool> AdminListAsync() => Admin("list", Auth(new()), "");
    public Task<bool> AdminSaveAsync(QuestForm f)
    {
        var steps = f.Steps.Select(s =>
        {
            var d = new Dictionary<string, object?> { ["type"] = s.Type, ["npc"] = s.Npc.Trim(), ["text"] = s.Text.Trim(), ["hint"] = s.Hint.Trim() };
            if (s.Type == "give") d["item"] = s.Item.Trim();
            if (s.Type == "receive") d["gift"] = new Dictionary<string, object?> { ["name"] = s.GiftName.Trim(), ["icon"] = s.GiftIcon, ["description"] = s.GiftDesc.Trim() };
            return d;
        }).ToList();
        var body = new Dictionary<string, object?> { ["title"] = f.Title.Trim(), ["intro"] = f.Intro.Trim(), ["steps"] = steps };
        if (f.Id.Length > 0) body["questId"] = f.Id;
        return Admin("save", Auth(body), "Quest saved.");
    }
    public Task<bool> AdminDeleteAsync(string id) => Admin("delete", Auth(new() { ["questId"] = id }), "Quest deleted.");
    public Task<bool> AdminAssignAsync(string id, string to) => Admin("assign", Auth(new() { ["questId"] = id, ["to"] = to }), $"Quest given to {to}.");
    public Task<bool> AdminUnassignAsync(string id, string to) => Admin("unassign", Auth(new() { ["questId"] = id, ["to"] = to }), $"Quest taken from {to}.");
}
