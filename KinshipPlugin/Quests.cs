using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace KinshipPlugin;

public sealed class QuestPlace
{
    public string Zone { get; set; } = ""; public int Territory { get; set; } public uint Map { get; set; } public string Room { get; set; } = "";
    public float X { get; set; } public float Y { get; set; } public float Z { get; set; }
    public float MapX { get; set; } public float MapY { get; set; } public int Radius { get; set; }
    public float? Distance()
    {
        var p = WorldInfo.PlayerPos();
        if (p == null || Plugin.ClientState.TerritoryType != Territory || WorldInfo.CurrentRoom != Room) return null;
        return System.Numerics.Vector3.Distance(p.Value, new System.Numerics.Vector3(X, Y, Z));
    }
}
public sealed class QuestGrant { public string Name { get; set; } = ""; public string Icon { get; set; } = ""; public string Description { get; set; } = ""; public int Minutes { get; set; } public bool IsDebuff { get; set; } public int Atk { get; set; } public int Heal { get; set; } }
public sealed class QuestFightSpec { public int NpcHp { get; set; } = 30; public int NpcDie { get; set; } = 8; public int NpcBonus { get; set; } public int PlayerHp { get; set; } = 30; public int PlayerDie { get; set; } = 8; public int PlayerBonus { get; set; } }
public sealed class QuestFight
{
    public string QuestId { get; set; } = ""; public string Npc { get; set; } = ""; public string PlayerName { get; set; } = "";
    public int PlayerHp { get; set; } public int PlayerMax { get; set; } public int NpcHp { get; set; } public int NpcMax { get; set; }
    public List<string> Log { get; set; } = new(); public string Status { get; set; } = "active"; public TalkLine? Result { get; set; }
}
public sealed class QuestOffer { public string QuestId { get; set; } = ""; public string Title { get; set; } = ""; public string Intro { get; set; } = ""; public string Text { get; set; } = ""; public string ItemId { get; set; } = ""; }
public sealed class QuestStart { public string QuestId { get; set; } = ""; public QuestPlace Place { get; set; } = new(); }
public sealed class QuestNow { public string Type { get; set; } = ""; public string Npc { get; set; } = ""; public string Item { get; set; } = ""; public string Hint { get; set; } = ""; public QuestPlace? Place { get; set; } }
public sealed class QuestGift { public string Name { get; set; } = ""; public string Icon { get; set; } = ""; public string Description { get; set; } = ""; public string Flavor { get; set; } = ""; }
public sealed class QuestVariant { public string Gender { get; set; } = ""; public string Status { get; set; } = ""; public string Text { get; set; } = ""; }
/// <summary>An area notice: words that pop up when you walk into a spot, whatever step you are on. Players get Index/Place/Repeat; Advocates also get Text.</summary>
public sealed class QuestNoticeView { public int Index { get; set; } public QuestPlace Place { get; set; } = new(); public bool Repeat { get; set; } public string Text { get; set; } = ""; }
public sealed class QuestStepView
{
    public List<QuestVariant>? Variants { get; set; }
    public string Type { get; set; } = "talk"; public string Npc { get; set; } = ""; public string Text { get; set; } = ""; public string Hint { get; set; } = "";
    public string Item { get; set; } = ""; public QuestGift? Gift { get; set; } public QuestPlace? Place { get; set; } public bool Once { get; set; } public QuestFightSpec? Fight { get; set; }
    public string Needs { get; set; } = ""; public string NeedText { get; set; } = ""; public bool TakesNeeded { get; set; } public QuestGrant? Grants { get; set; }
}
public sealed class QuestPlayer { public string Name { get; set; } = ""; public int Step { get; set; } public bool Done { get; set; } }
public sealed class QuestClaim { public int Step { get; set; } public string Name { get; set; } = ""; }
/// <summary>One quest. Players get QuestId/Step/Current; Advocates get Id/Steps/Players.</summary>
public sealed class QuestView
{
    public string QuestId { get; set; } = ""; public string Id { get; set; } = ""; public string Title { get; set; } = ""; public string Intro { get; set; } = "";
    public int Step { get; set; } public int Total { get; set; } public bool Done { get; set; } public bool Failed { get; set; } public QuestNow? Current { get; set; }
    public List<QuestStepView>? Steps { get; set; } public List<QuestPlayer>? Players { get; set; } public List<QuestClaim>? Claims { get; set; }
    public QuestPlace? StartPlace { get; set; } public string StartText { get; set; } = ""; public List<QuestNoticeView>? Notices { get; set; }
}
public sealed class TalkLine { public string Quest { get; set; } = ""; public bool Ok { get; set; } public bool Failed { get; set; } public string Text { get; set; } = ""; public bool Finished { get; set; } public List<string>? Gained { get; set; } public string? StatusGained { get; set; } }
public sealed record TalkResult(string Npc, List<TalkLine> Lines, string Error);

/// <summary>What the Advocate is typing for one step.</summary>
public sealed class VariantForm { public string Gender = "", Status = "", Text = ""; }
public sealed class StepForm
{
    public List<VariantForm> Variants = new();
    public string Type = "talk", Npc = "", Text = "", Hint = "", Item = "", GiftName = "", GiftIcon = "game:20001", GiftDesc = "";
    public Spot? Place; public bool Once; public int Radius = 5;   // Radius: yalms around the spot, for Arrive steps
    public int NpcHp = 30, NpcDie = 8, NpcBonus, PlayerHp = 30, PlayerDie = 8, PlayerBonus;
    public string Needs = "", NeedText = "", GrantName = "", GrantDesc = "", GrantIcon = "game:10205";
    public int GrantMinutes, GrantAtk, GrantHeal; public bool TakesNeeded, GrantDebuff;
    public bool AtSpot => Type == "look" || Type == "listen" || Type == "search" || Type == "arrive";
}
public sealed class NoticeForm { public Spot? Place; public int Radius = 10; public string Text = ""; public bool Repeat; }
public sealed class QuestForm
{
    public List<NoticeForm> Notices = new();
    public string Id = "", Title = "", Intro = "", StartText = "";
    public bool HasStart; public Spot? StartPlace;
    public List<StepForm> Steps = new() { new StepForm() };
}
public sealed record QuestSnap(List<QuestView> Mine, List<QuestView> Admin, string Error, string Notice, bool Busy);

/// <summary>Quests: the server keeps the quests and each player's place. Talking to your targeted NPC moves you along.</summary>
public sealed class QuestState
{
    public const int MaxSteps = 12;
    public const float ActionRange = 8f;   // how close you must be for the words to show and the buttons to work

    public static string Phrase(string type) => type == "fight" ? "Danger lurks here..." : type == "listen" ? "You hear something..." : type == "search" ? "Something may be hidden here..." : "You notice something...";
    /// <summary>True when one of your quests is waiting on this action and you are standing near its spot.</summary>
    public bool Ready(string type) => Active().Any(q => q.Current is { Place: not null } c && c.Type == type && c.Place.Distance() is { } d && d <= ActionRange);
    private readonly object gate = new();
    private List<QuestView> mine = new(), admin = new();
    private string error = "", notice = "";
    private bool busy;
    private DateTime lastList = DateTime.MinValue;
    private TalkResult? talk;
    private List<QuestStart> starts = new();
    private QuestOffer? offer;
    private QuestFight? fight;

    public QuestFight? LastFight() { lock (gate) { return fight; } }
    public void DismissFight() { lock (gate) { fight = null; } }
    /// <summary>The fight step you are on, if it can start right now (standing at its spot, or targeting its enemy).</summary>
    public QuestNow? FightStep() => Active().Select(q => q.Current).FirstOrDefault(c => c is { Type: "fight" });
    public bool FightReady(string target)
    {
        var c = FightStep();
        if (c == null) return false;
        if (c.Place != null) return c.Place.Distance() is { } d && d <= ActionRange;
        return target.Length > 0 && string.Equals(c.Npc.Trim(), target.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public async Task FightAsync(string target, bool attack, string questId = "")
    {
        lock (gate) { if (busy) return; busy = true; error = ""; notice = ""; }
        try
        {
            var r = attack
                ? await ItemsState.SendAsync(HttpMethod.Post, "/quests/fight/attack", new { questId, gender = MyGender() }).ConfigureAwait(false)
                : await ItemsState.SendAsync(HttpMethod.Post, "/quests/fight/start", new { npc = target, territory = (int)Plugin.ClientState.TerritoryType, gender = MyGender() }).ConfigureAwait(false);
            lock (gate)
            {
                if (r.Fight != null) fight = r.Fight;
                else if (!string.IsNullOrEmpty(r.Text)) talk = new TalkResult(target, new(), r.Text);
                else { fight = null; talk = new TalkResult(target, new(), ""); }
            }
            if (r.Fight is { Status: "won" }) { await RefreshAsync().ConfigureAwait(false); _ = Plugin.Items.RefreshAsync(true); _ = Plugin.Status.RefreshAsync(); }
        }
        catch (CombatException ex) { lock (gate) { error = ex.Message; } }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Quest fight failed"); lock (gate) { error = "Couldn't reach the server. Check your connection and try again."; } }
        finally { lock (gate) { busy = false; } }
    }

    public QuestOffer? LastOffer() { lock (gate) { return offer; } }
    public void SetOffer(QuestOffer o) { lock (gate) { offer = o; } }
    public void DismissOffer() { lock (gate) { offer = null; } }
    public List<QuestStart> Starts() { lock (gate) { return starts.ToList(); } }
    /// <summary>The start spot you are standing near, if any.</summary>
    public QuestStart? StartNear() { lock (gate) { return starts.FirstOrDefault(s => s.Place.Distance() is { } d && d <= ActionRange); } }

    public async Task OfferAsync(string questId)
    {
        lock (gate) { if (busy) return; busy = true; error = ""; notice = ""; }
        try
        {
            var r = await ItemsState.SendAsync(HttpMethod.Post, "/quests/offer", new { questId }).ConfigureAwait(false);
            lock (gate) { if (r.Offer != null) offer = r.Offer; else talk = new TalkResult("", new(), ""); }
        }
        catch (CombatException ex) { lock (gate) { error = ex.Message; } }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Quest offer failed"); lock (gate) { error = "Couldn't reach the server. Check your connection and try again."; } }
        finally { lock (gate) { busy = false; } }
    }
    public async Task AcceptAsync(QuestOffer o)
    {
        lock (gate) { if (busy) return; busy = true; error = ""; notice = ""; offer = null; }
        try
        {
            await ItemsState.SendAsync(HttpMethod.Post, "/quests/accept", new { questId = o.QuestId, itemId = o.ItemId }).ConfigureAwait(false);
            lock (gate) { notice = $"Quest started: {o.Title}."; }
        }
        catch (CombatException ex) { lock (gate) { error = ex.Message; } }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Quest accept failed"); lock (gate) { error = "Couldn't reach the server. Check your connection and try again."; } }
        finally { lock (gate) { busy = false; } }
        await RefreshAsync().ConfigureAwait(false);
        _ = Plugin.Items.RefreshAsync(true);
    }

    public QuestSnap Snapshot() { lock (gate) { return new QuestSnap(mine.ToList(), admin.ToList(), error, notice, busy); } }
    public TalkResult? LastTalk() { lock (gate) { return talk; } }
    public void DismissTalk() { lock (gate) { talk = null; } }
    public List<QuestView> Active() { lock (gate) { return mine.Where(q => !q.Done && !q.Failed).ToList(); } }
    public List<QuestView> Failed() { lock (gate) { return mine.Where(q => q.Failed).ToList(); } }

    public async Task AbandonAsync(string questId)
    {
        lock (gate) { if (busy) return; busy = true; error = ""; notice = ""; }
        try { await ItemsState.SendAsync(HttpMethod.Post, "/quests/abandon", new { questId }).ConfigureAwait(false); }
        catch (CombatException ex) { lock (gate) { error = ex.Message; } }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Quest delete failed"); lock (gate) { error = "Couldn't reach the server. Check your connection and try again."; } }
        finally { lock (gate) { busy = false; } }
        await RefreshAsync().ConfigureAwait(false);
    }
    public void ClearMessages() { lock (gate) { error = ""; notice = ""; } }

    public void RefreshIfDue(int seconds)
    {
        if (Idle.Asleep) return;
        bool due;
        lock (gate) { due = !busy && Plugin.Config.Me().ItemsToken.Length > 0 && (DateTime.UtcNow - lastList).TotalSeconds > seconds; if (due) lastList = DateTime.UtcNow; }
        if (due) _ = RefreshAsync();
    }
    public async Task RefreshAsync()
    {
        try
        {
            var r = await ItemsState.SendAsync(HttpMethod.Get, "/quests/mine", null).ConfigureAwait(false);
            lock (gate) { mine = r.Quests ?? new(); lastList = DateTime.UtcNow; }
            var st = await ItemsState.SendAsync(HttpMethod.Get, "/quests/starts", null).ConfigureAwait(false);
            lock (gate) { starts = st.Starts ?? new(); }
        }
        catch (Exception) { /* keep what we have */ }
    }

    /// <summary>Look, listen or search where you are standing. The server checks the zone and what your step needs.</summary>
    /// <summary>The gender listed for your character on the faction roster (not your character's base gender), or an empty string if there is none.</summary>
    public static string MyGender()
    {
        var people = Plugin.Data.Roster;
        if (people == null) return "";
        var registered = Plugin.Config.Me().ItemsName.Trim();
        var character = Plugin.CharacterName().Trim();
        static string First(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var found = people.FirstOrDefault(p => p.Name.Equals(registered, StringComparison.OrdinalIgnoreCase) || p.Name.Equals(character, StringComparison.OrdinalIgnoreCase))
            ?? people.FirstOrDefault(p => First(registered).Length > 0 && First(p.Name).Equals(First(registered), StringComparison.OrdinalIgnoreCase))
            ?? people.FirstOrDefault(p => First(character).Length > 0 && First(p.Name).Equals(First(character), StringComparison.OrdinalIgnoreCase));
        return found?.Gender.Trim() ?? "";
    }

    public Task ActAsync(string type) => TalkAsync("", type);

    // ---------- walking into places ----------
    // Arrive steps and area notices fire by themselves when you walk into their radius. Nothing here needs a button.
    private readonly Dictionary<string, bool> insideNow = new();   // was I inside this spot at the last check?
    private readonly HashSet<string> shownOnce = new();            // area notices that don't repeat, already shown this session
    private DateTime lastWatch = DateTime.MinValue;
    private readonly Dictionary<string, string> noticeText = new();   // words fetched ahead of time as you approach, so they show the instant you walk in
    private readonly HashSet<string> noticeFetching = new();
    private readonly Dictionary<string, DateTime> noticeRetry = new();       // after a failed or empty answer, wait before asking again

    /// <summary>Called every frame while quests are active; looks at where you stand a couple of times a second.</summary>
    public void WatchPlaces()
    {
        if (Idle.Asleep) return;
        if ((DateTime.UtcNow - lastWatch).TotalMilliseconds < 150) return;
        lastWatch = DateTime.UtcNow;
        foreach (var q in Active())
        {
            var key = q.QuestId + ":step" + q.Step;
            if (q.Current is { Type: "arrive", Place.Radius: > 0 } c)
            {
                var inside = c.Place!.Distance() is { } d && d <= c.Place.Radius;
                insideNow.TryGetValue(key, out var was);
                if (inside && !was)
                {
                    bool busyNow; lock (gate) { busyNow = busy; }
                    if (busyNow) continue;                      // another request is running: try again on the next check instead of missing it
                    insideNow[key] = true;
                    _ = ActAsync("arrive");                      // the server moves the quest on and sends the words
                }
                else insideNow[key] = inside;
            }
            foreach (var n in q.Notices ?? new())
            {
                if (n.Place.Radius <= 0) continue;
                var nk = q.QuestId + ":notice" + n.Index;
                var dist = n.Place.Distance();
                var inside = dist is { } d && d <= n.Place.Radius;
                if (dist is { } near && near <= n.Place.Radius + 30f) _ = FetchNoticeAsync(q, n, nk);   // close enough to get ready
                insideNow.TryGetValue(nk, out var was); insideNow[nk] = inside;
                if (!inside || was) continue;
                if (!n.Repeat && !shownOnce.Add(nk)) continue;
                string? ready; lock (gate) { noticeText.TryGetValue(nk, out ready); }
                if (!string.IsNullOrEmpty(ready)) lock (gate) { talk = new TalkResult("", new() { new TalkLine { Quest = q.Title, Ok = true, Text = ready } }, ""); }
                else _ = FetchNoticeAsync(q, n, nk, show: true);   // not loaded yet: fetch it and show it as soon as it arrives
            }
        }
    }

    private async Task FetchNoticeAsync(QuestView q, QuestNoticeView n, string key, bool show = false)
    {
        lock (gate) { if (noticeText.ContainsKey(key) && !show) return; if (!show && noticeRetry.TryGetValue(key, out var until) && DateTime.UtcNow < until) return; if (!noticeFetching.Add(key)) return; }
        try
        {
            var r = await ItemsState.SendAsync(HttpMethod.Post, "/quests/notice", new { questId = q.QuestId, index = n.Index, territory = n.Place.Territory }).ConfigureAwait(false);
            lock (gate)
            {
                if (!string.IsNullOrEmpty(r.Text)) noticeText[key] = r.Text; else noticeRetry[key] = DateTime.UtcNow.AddSeconds(30);
                if (show && !string.IsNullOrEmpty(r.Text)) talk = new TalkResult("", new() { new TalkLine { Quest = q.Title, Ok = true, Text = r.Text } }, "");
            }
        }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Area notice failed"); lock (gate) { noticeRetry[key] = DateTime.UtcNow.AddSeconds(30); } }
        finally { lock (gate) { noticeFetching.Remove(key); } }
    }

    public async Task TalkAsync(string npc, string? type = null)
    {
        lock (gate) { if (busy) return; busy = true; error = ""; notice = ""; }
        try
        {
            var r = type == null
                ? await ItemsState.SendAsync(HttpMethod.Post, "/quests/talk", new { npc, gender = MyGender() }).ConfigureAwait(false)
                : await ItemsState.SendAsync(HttpMethod.Post, "/quests/act", new { type, territory = (int)Plugin.ClientState.TerritoryType, gender = MyGender() }).ConfigureAwait(false);
            lock (gate) { talk = new TalkResult(npc, r.Results ?? new(), ""); }
        }
        catch (CombatException ex) { lock (gate) { talk = new TalkResult(npc, new(), ex.Message); } }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Quest talk failed"); lock (gate) { talk = new TalkResult(npc, new(), "Couldn't reach the server. Check your connection and try again."); } }
        finally { lock (gate) { busy = false; } }
        await RefreshAsync().ConfigureAwait(false);
        _ = Plugin.Items.RefreshAsync(true);
        _ = Plugin.Status.RefreshAsync();
    }

    // ---------- Advocate tools (creation password) ----------
    private async Task<bool> Admin(string path, object body, string done, bool reload = true)
    {
        var pw = Plugin.Config.CreatePassword;
        lock (gate) { if (busy) return false; if (pw.Length == 0) { error = "Enter the creation password in the SM Tools tab first."; return false; } busy = true; error = ""; notice = ""; }
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
            var alt = s.Variants.Where(v => v.Text.Trim().Length > 0).Select(v => new Dictionary<string, object?> { ["gender"] = v.Gender.Trim(), ["status"] = v.Status.Trim(), ["text"] = v.Text.Trim() }).ToList();
            if (alt.Count > 0) d["variants"] = alt;
            if (s.Needs.Trim().Length > 0) { d["needs"] = s.Needs.Trim(); d["needText"] = s.NeedText.Trim(); d["takesNeeded"] = s.TakesNeeded; }
            if (s.GrantName.Trim().Length > 0) d["grants"] = new Dictionary<string, object?> { ["name"] = s.GrantName.Trim(), ["icon"] = s.GrantIcon, ["description"] = s.GrantDesc.Trim(), ["minutes"] = s.GrantMinutes, ["isDebuff"] = s.GrantDebuff, ["atk"] = s.GrantAtk, ["heal"] = s.GrantHeal };
            if (s.Type == "fight")
                d["fight"] = new Dictionary<string, object?> { ["npcHp"] = s.NpcHp, ["npcDie"] = s.NpcDie, ["npcBonus"] = s.NpcBonus, ["playerHp"] = s.PlayerHp, ["playerDie"] = s.PlayerDie, ["playerBonus"] = s.PlayerBonus };
            if ((s.AtSpot || s.Type == "fight") && s.Place != null)
                d["place"] = new Dictionary<string, object?> { ["zone"] = s.Place.Zone, ["territory"] = (int)s.Place.Territory, ["map"] = (int)s.Place.Map, ["room"] = s.Place.Room, ["x"] = s.Place.Pos.X, ["y"] = s.Place.Pos.Y, ["z"] = s.Place.Pos.Z, ["mapX"] = s.Place.MapX, ["mapY"] = s.Place.MapY };
            if (s.Type == "arrive" && d.TryGetValue("place", out var ap) && ap is Dictionary<string, object?> apd) apd["radius"] = Math.Clamp(s.Radius, 1, 100);
            if ((s.Type == "receive" || s.Type == "search" || s.Type == "fight") && s.GiftName.Trim().Length > 0)
            {
                d["gift"] = new Dictionary<string, object?> { ["name"] = s.GiftName.Trim(), ["icon"] = s.GiftIcon, ["description"] = s.GiftDesc.Trim() };
                d["once"] = s.Once;
            }
            return d;
        }).ToList();
        var body = new Dictionary<string, object?> { ["title"] = f.Title.Trim(), ["intro"] = f.Intro.Trim(), ["steps"] = steps };
        if (f.HasStart && f.StartPlace != null)
        {
            var p = f.StartPlace;
            body["startPlace"] = new Dictionary<string, object?> { ["zone"] = p.Zone, ["territory"] = (int)p.Territory, ["map"] = (int)p.Map, ["room"] = p.Room, ["x"] = p.Pos.X, ["y"] = p.Pos.Y, ["z"] = p.Pos.Z, ["mapX"] = p.MapX, ["mapY"] = p.MapY };
            body["startText"] = f.StartText.Trim();
        }
        body["notices"] = f.Notices.Where(n => n.Place != null && n.Text.Trim().Length > 0).Select(n => new Dictionary<string, object?>
        {
            ["text"] = n.Text.Trim(), ["repeat"] = n.Repeat,
            ["place"] = new Dictionary<string, object?> { ["zone"] = n.Place!.Zone, ["territory"] = (int)n.Place.Territory, ["map"] = (int)n.Place.Map, ["room"] = n.Place.Room, ["x"] = n.Place.Pos.X, ["y"] = n.Place.Pos.Y, ["z"] = n.Place.Pos.Z, ["mapX"] = n.Place.MapX, ["mapY"] = n.Place.MapY, ["radius"] = Math.Clamp(n.Radius, 1, 100) }
        }).ToList();
        if (f.Id.Length > 0) body["questId"] = f.Id;
        return Admin("save", Auth(body), "Quest saved.");
    }
    public Task<bool> AdminResetClaimsAsync(string id) => Admin("reset-claims", Auth(new() { ["questId"] = id }), "Finds reset. They can be taken again.");
    public Task<bool> AdminDeleteAsync(string id) => Admin("delete", Auth(new() { ["questId"] = id }), "Quest deleted.");
    public Task<bool> AdminAssignAsync(string id, string to) => Admin("assign", Auth(new() { ["questId"] = id, ["to"] = to }), $"Quest given to {to}.");
    public Task<bool> AdminUnassignAsync(string id, string to) => Admin("unassign", Auth(new() { ["questId"] = id, ["to"] = to }), $"Quest taken from {to}.");
}
