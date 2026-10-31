using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;

namespace KinshipPlugin;

public sealed class ItemView
{
    public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string Icon { get; set; } = "";
    public string Description { get; set; } = ""; public string Flavor { get; set; } = ""; public string UseLabel { get; set; } = "";
    public string? UseText { get; set; } public bool Consumable { get; set; } public bool CanEdit { get; set; }
    public string? Owner { get; set; } public string? Creator { get; set; }
    public string Requires { get; set; } = ""; public bool RequiresConsume { get; set; } public int Holds { get; set; } public int Qty { get; set; } = 1;
    public string StName { get; set; } = ""; public string StDesc { get; set; } = ""; public string StIcon { get; set; } = "";
    public bool StDebuff { get; set; } public int StMinutes { get; set; } public string Cures { get; set; } = "";
    public string StType { get; set; } = ""; public string StCure { get; set; } = ""; public int StAtk { get; set; } public int StHeal { get; set; }
    public string WearSlot { get; set; } = ""; public int GearAtk { get; set; } public int GearHeal { get; set; } public int LifespanDays { get; set; } public string WornAt { get; set; } = ""; public string BreaksAt { get; set; } = "";
    public bool Carried { get; set; }   // its status is active just by holding it
    public bool CanCombine { get; set; } public int CombineFail { get; set; } public List<string>? CombineWith { get; set; } public ItemView? Result { get; set; }
}
public sealed class ItemReply
{
    public string? Token { get; set; } public string? Name { get; set; } public string? RecoveryCode { get; set; } public List<string>? Characters { get; set; } public bool Linked { get; set; }
    public int Capacity { get; set; } public List<RegisteredPerson>? Registered { get; set; } public List<string>? TagChoices { get; set; } public List<SharedLine>? Shared { get; set; } public List<FindView>? Finds { get; set; } public string? Picked { get; set; } public bool? AlphaGear { get; set; } public int? MaxCharacters { get; set; } public int? CharacterCount { get; set; } public List<ItemView>? Items { get; set; } public List<string>? Members { get; set; }
    public string? Text { get; set; } public bool Consumed { get; set; } public string? Error { get; set; }
    public List<StashView>? Stashes { get; set; } public StashView? Stash { get; set; } public List<ItemView>? StashItems { get; set; }
    public List<SignView>? Signs { get; set; } public SignView? Sign { get; set; }
    public List<ZoneCount>? Zones { get; set; } public int Elsewhere { get; set; }
    public DuelView? Duel { get; set; }
    public List<ReportView>? Reports { get; set; } public List<MarkPerson>? People { get; set; } public WipeInfo? Wipe { get; set; } public List<string>? Removed { get; set; }
    public List<ProfileView>? Profiles { get; set; } public ProfileView? Profile { get; set; }
    public List<ArmorySlot>? Slots { get; set; } public ArmoryBonus? Bonus { get; set; } public List<string>? Broke { get; set; }
    public List<MailView>? Inbox { get; set; } public List<MailView>? Outbox { get; set; } public MailView? Mail { get; set; } public int Unread { get; set; }
    public List<MailState.AdminMail>? Mails { get; set; }
    public List<string>? Gained { get; set; } public string? KeyUsed { get; set; }
    public int? Gold { get; set; } public string? To { get; set; } public long? Total { get; set; }
    public string? StatusGained { get; set; } public string? StatusCured { get; set; } public List<StatusView>? Statuses { get; set; }
    public QuestFight? Fight { get; set; } public int? Claimed { get; set; }
    public QuestOffer? Offer { get; set; } public List<QuestStart>? Starts { get; set; }
    public List<QuestView>? Quests { get; set; } public List<TalkLine>? Results { get; set; } public List<JobView>? Jobs { get; set; } public int? MaxStatuses { get; set; }
    public int? Roll { get; set; } public string? Patient { get; set; } public bool? Success { get; set; } public string? Condition { get; set; } public string? Cure { get; set; }
    public string? Combined { get; set; } public bool Failed { get; set; } public List<string>? Used { get; set; } public int? Copies { get; set; }
    public List<BankEntry>? History { get; set; } public List<BankEntry>? Recent { get; set; } public List<BankMember>? Balances { get; set; }
}
public sealed class ItemForm
{
    public string Name = "", Icon = "", Description = "", Flavor = "", UseLabel = "", UseText = "", Requires = "";
    public string StName = "", StDesc = "", StIcon = "", Cures = "", StType = "", StCure = "", StartsQuest = "";
    public int FailChance;                   // percent chance the combination fails (0 = never)
    public string[] With = { "", "", "" };   // other reagents this combines with
    public ItemForm? Result;                 // what the combination makes
    public int StMinutes, StAtk, StHeal;
    public string WearSlot = ""; public int LifespanDays, GearAtk, GearHeal;   // wearable gear
    public bool Consumable, RequiresConsume, StDebuff, Carried;
}
public sealed record ItemsSnap(bool SignedIn, string Name, int Capacity, List<ItemView> Items, List<string> Members, string Error, string Notice, bool Busy, string Recovery);

/// <summary>RP items: your inventory lives on the server, and nothing here touches the game.</summary>
public sealed class ItemsState
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly object gate = new();
    private List<ItemView> items = new();
    private List<string> members = new();
    private int capacity = 10;
    /// <summary>True while the SM has alpha mode on, so any member can put roll bonuses on gear they make.</summary>
    public static volatile bool AlphaGear;
    private string error = "", notice = "";
    private bool busy;
    private DateTime lastRefresh = DateTime.MinValue;
    private bool loadedOnce;
    private int gold;
    public int Gold { get { lock (gate) { return gold; } } }
    public void SetGold(int g) { lock (gate) { gold = g; } }

    // SM tools (they use the creation password)
    private List<ItemView> adminItems = new();
    private List<string> adminMembers = new();
    private List<StashView> adminStashes = new();
    private List<SignView> adminSigns = new();
    private string adminError = "", adminNotice = "";
    private bool adminBusy, adminLoaded;
    public (List<ItemView> Items, List<string> Members, string Error, string Notice, bool Busy, bool Loaded) AdminSnapshot()
    {
        lock (gate) { return (adminItems.ToList(), adminMembers.ToList(), adminError, adminNotice, adminBusy, adminLoaded); }
    }
    public List<StashView> AdminStashes() { lock (gate) { return adminStashes.ToList(); } }
    public List<SignView> AdminSigns() { lock (gate) { return adminSigns.ToList(); } }

    public ItemsSnap Snapshot()
    {
        lock (gate) { return new ItemsSnap(Plugin.Config.Me().ItemsToken.Length > 0, Plugin.Config.Me().ItemsName, capacity, items.ToList(), members.ToList(), error, notice, busy, Plugin.Config.Me().ItemsRecovery); }
    }
    private void SetError(string m) { lock (gate) { error = m; notice = ""; } }
    public void ClearMessages() { lock (gate) { error = ""; notice = ""; } }

    internal static async Task<ItemReply> SendAsync(HttpMethod method, string path, object? body)
    {
        // Stashes and signs are shown by distance, so the server is told where you stand (same position the game shows you).
        var places = path.StartsWith("/stashes") || path.StartsWith("/signs") || path.StartsWith("/finds");
        if (places && method == HttpMethod.Get && WorldInfo.AtQuery() is { Length: > 0 } q) path += (path.Contains('?') ? "&" : "?") + q;
        using var req = new HttpRequestMessage(method, CombatState.BaseUrl + "/api" + path);
        if (Plugin.Config.Me().ItemsToken.Length > 0) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Plugin.Config.Me().ItemsToken);
        if (body != null)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(body, Json));
            if (places && node is System.Text.Json.Nodes.JsonObject o && WorldInfo.At() is { } at) o["at"] = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(at, Json));
            req.Content = new StringContent(node?.ToJsonString() ?? "{}", Encoding.UTF8, "application/json");
        }
        using var res = await Http.SendAsync(req).ConfigureAwait(false);
        var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
        ItemReply? reply = null;
        try { reply = JsonSerializer.Deserialize<ItemReply>(text, Json); } catch { /* not JSON */ }
        if (!res.IsSuccessStatusCode) throw new CombatException(reply?.Error ?? $"The server answered {(int)res.StatusCode}.", (int)res.StatusCode);
        return reply ?? throw new CombatException("The server sent something unexpected.", 500);
    }

    // Runs one server action with the shared busy flag and error handling. Returns the reply, or null if it failed.
    private async Task<ItemReply?> Run(Func<Task<ItemReply>> action, bool signOutOn401 = true)
    {
        lock (gate) { if (busy) return null; busy = true; error = ""; notice = ""; }
        try
        {
            var r = await action().ConfigureAwait(false);
            if (r.Items != null) lock (gate) { items = r.Items; if (r.Capacity > 0) capacity = r.Capacity; lastRefresh = DateTime.UtcNow; }
            if (r.AlphaGear != null) AlphaGear = r.AlphaGear.Value;
            if (r.Gold != null) lock (gate) { gold = r.Gold.Value; }
            return r;
        }
        catch (CombatException ex)
        {
            if (ex.Status == 401 && signOutOn401) { SignOutLocal(); }
            SetError(ex.Message);
        }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Item request failed"); SetError("Couldn't reach the server. Check your connection and try again."); }
        finally { lock (gate) { busy = false; } }
        return null;
    }

    private void SignOutLocal()
    {
        Plugin.Config.Me().ItemsToken = ""; Plugin.Config.Save();
        lock (gate) { items = new(); loadedOnce = false; }
    }
    /// <summary>Lets the stash screen update your inventory after you put something in or take something out.</summary>
    public void SetInventory(List<ItemView> list) { lock (gate) { items = list; lastRefresh = DateTime.UtcNow; } }
    public void SignOut() { SignOutLocal(); ClearMessages(); }

    /// <summary>Registers this character. With a recovery code it joins that player (one of your other characters) instead of getting a new code.</summary>
    public async Task RegisterAsync(string name, string memberPassword, string joinCode = "")
    {
        object body = joinCode.Length > 0 ? new { name = name.Trim(), memberPassword, recoveryCode = joinCode } : new { name = name.Trim(), memberPassword };
        var r = await Run(() => SendAsync(HttpMethod.Post, "/items/register", body), false).ConfigureAwait(false);
        if (r?.Token == null) return;
        Plugin.Config.Me().ItemsToken = r.Token; Plugin.Config.Me().ItemsName = r.Name ?? name.Trim(); Plugin.Config.Me().ItemsRecovery = r.RecoveryCode ?? joinCode;
        Plugin.Config.Save();
        lock (gate) { notice = r.Linked ? "You're in. This character shares your recovery code with your other characters." : "You're in. Write down your recovery code below."; loadedOnce = false; }
        await RefreshAsync(true).ConfigureAwait(false);
    }

    private List<string> found = new();
    /// <summary>The characters a recovery code covers, after "Find my characters".</summary>
    public List<string> Found() { lock (gate) { return found.ToList(); } }
    public void ClearFound() { lock (gate) { found = new(); } }

    /// <summary>Asks which characters a recovery code covers, so you can pick the one this is.</summary>
    public async Task FindCharactersAsync(string code)
    {
        var r = await Run(() => SendAsync(HttpMethod.Post, "/items/recover", new { recoveryCode = code.Trim() }), false).ConfigureAwait(false);
        lock (gate) { found = r?.Characters ?? new(); if (r != null && found.Count == 0) error = "That code doesn't cover any characters."; }
    }

    public async Task RecoverAsync(string name, string code)
    {
        var r = await Run(() => SendAsync(HttpMethod.Post, "/items/recover", new { name = name.Trim(), recoveryCode = code.Trim() }), false).ConfigureAwait(false);
        if (r?.Token == null) return;
        Plugin.Config.Me().ItemsToken = r.Token; Plugin.Config.Me().ItemsName = r.Name ?? name.Trim(); Plugin.Config.Me().ItemsRecovery = code.Trim().ToUpperInvariant();
        Plugin.Config.Save();
        lock (gate) { notice = "Welcome back."; loadedOnce = false; found = new(); }
        await RefreshAsync(true).ConfigureAwait(false);
    }

    public async Task RefreshAsync(bool quiet = false)
    {
        string before; lock (gate) { before = notice; }
        var r = await Run(() => SendAsync(HttpMethod.Get, "/items/me", null)).ConfigureAwait(false);
        if (r != null) lock (gate) { loadedOnce = true; if (quiet) notice = before; }
    }

    /// <summary>Called while the tab is on screen: loads on first view, then every 15 seconds.</summary>
    public void RefreshIfDue()
    {
        if (Idle.Asleep) return;
        bool due;
        lock (gate) { due = !busy && Plugin.Config.Me().ItemsToken.Length > 0 && (!loadedOnce || (DateTime.UtcNow - lastRefresh).TotalSeconds > 15); if (due) lastRefresh = DateTime.UtcNow; }
        if (due) _ = RefreshAsync(true);
    }

    public async Task LoadMembersAsync()
    {
        try { var r = await SendAsync(HttpMethod.Get, "/items/members", null).ConfigureAwait(false); lock (gate) { members = r.Members ?? new(); } }
        catch (Exception) { /* the give list stays as it was */ }
    }

    // ---------- SM tools ----------
    private async Task<ItemReply?> RunAdmin(Func<string, Task<ItemReply>> action, string? done = null)
    {
        var pw = Plugin.Config.CreatePassword;
        lock (gate) { if (adminBusy) return null; adminBusy = true; adminError = ""; adminNotice = ""; }
        try
        {
            if (pw.Length == 0) throw new CombatException("Enter the creation password first.", 400);
            var r = await action(pw).ConfigureAwait(false);
            lock (gate) { if (done != null) adminNotice = done; }
            return r;
        }
        catch (CombatException ex) { lock (gate) { adminError = ex.Message; } }
        catch (Exception ex) { Plugin.Log.Warning(ex, "SM request failed"); lock (gate) { adminError = "Couldn't reach the server. Try again."; } }
        finally { lock (gate) { adminBusy = false; } }
        return null;
    }

    public async Task AdminLoadAsync()
    {
        var r = await RunAdmin(pw => SendAsync(HttpMethod.Post, "/items/admin/list", new { adminPassword = pw })).ConfigureAwait(false);
        if (r != null) lock (gate) { adminItems = r.Items ?? new(); adminMembers = r.Members ?? new(); adminStashes = r.Stashes ?? new(); adminSigns = r.Signs ?? new(); adminLoaded = true; }
    }
    public async Task AdminDeleteItemAsync(string id)
    {
        var r = await RunAdmin(pw => SendAsync(HttpMethod.Post, "/items/admin/delete", new { adminPassword = pw, itemId = id }), "Item deleted.").ConfigureAwait(false);
        if (r != null) await AdminLoadAsync().ConfigureAwait(false);
    }
    public async Task AdminDeleteStashAsync(string id)
    {
        var r = await RunAdmin(pw => SendAsync(HttpMethod.Post, "/items/admin/delete-stash", new { adminPassword = pw, stashId = id }), "Stash deleted.").ConfigureAwait(false);
        if (r != null) { await AdminLoadAsync().ConfigureAwait(false); await Plugin.Stashes.RefreshAsync().ConfigureAwait(false); }
    }
    public async Task AdminDeleteSignAsync(string id)
    {
        var r = await RunAdmin(pw => SendAsync(HttpMethod.Post, "/signs/admin/delete", new { adminPassword = pw, signId = id }), "Sign deleted.").ConfigureAwait(false);
        if (r != null) { await AdminLoadAsync().ConfigureAwait(false); await Plugin.Signs.RefreshAsync().ConfigureAwait(false); }
    }
    public async Task AdminRemoveMemberAsync(string name)
    {
        var r = await RunAdmin(pw => SendAsync(HttpMethod.Post, "/items/admin/remove-member", new { adminPassword = pw, name }), $"{name} was removed.").ConfigureAwait(false);
        if (r != null) await AdminLoadAsync().ConfigureAwait(false);
    }
    public async Task<bool> AdminGrantAsync(ItemForm f, string to, string stashId, int copies)
    {
        var body = BodyDict(f);
        body["copies"] = copies;
        if (stashId.Length > 0) body["stashId"] = stashId; else body["to"] = to;
        var where = stashId.Length > 0 ? "the stash" : to;
        var r = await RunAdmin(pw => { body["adminPassword"] = pw; return SendAsync(HttpMethod.Post, "/items/admin/grant", body); }, copies > 1 ? $"{copies} copies sent to {where}." : $"Gift sent to {where}.").ConfigureAwait(false);
        return r != null;
    }

    private static Dictionary<string, object?> BodyDict(ItemForm f, bool recipe = true)
    {
        var d = new Dictionary<string, object?>
        {
            ["name"] = f.Name.Trim(), ["icon"] = f.Icon, ["description"] = f.Description.Trim(), ["flavor"] = f.Flavor.Trim(),
            ["useLabel"] = f.UseLabel.Trim(), ["useText"] = f.UseText.Trim(), ["requires"] = f.Requires.Trim(), ["requiresConsume"] = f.RequiresConsume,
            ["stName"] = f.StName.Trim(), ["stDesc"] = f.StDesc.Trim(), ["stIcon"] = f.StIcon, ["stDebuff"] = f.StDebuff, ["stMinutes"] = f.StMinutes, ["startsQuest"] = f.StartsQuest, ["stType"] = f.StType, ["stCure"] = f.StCure.Trim(), ["stAtk"] = f.StAtk, ["stHeal"] = f.StHeal,
            ["cures"] = f.Cures.Trim(), ["consumable"] = f.Consumable, ["carried"] = f.Carried && f.StName.Trim().Length > 0,
            ["wearSlot"] = f.WearSlot, ["lifespanDays"] = f.LifespanDays, ["gearAtk"] = f.GearAtk, ["gearHeal"] = f.GearHeal,
        };
        var with = f.With.Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        if (recipe && with.Count > 0)
        {
            d["with"] = with; d["failChance"] = Math.Clamp(f.FailChance, 0, 99);
            if (f.Result != null) d["result"] = BodyDict(f.Result, false);
        }
        return d;
    }
    private static object Body(ItemForm f) => BodyDict(f);

    public async Task<bool> CreateAsync(ItemForm f, int quantity = 1)
    {
        var body = BodyDict(f);
        if (quantity > 1) body["quantity"] = quantity;
        var r = await Run(() => SendAsync(HttpMethod.Post, "/items", body)).ConfigureAwait(false);
        if (r != null) lock (gate) { notice = quantity > 1 ? $"{quantity} items created." : "Item created."; }
        return r != null;
    }
    public async Task<bool> UpdateAsync(string id, ItemForm f)
    {
        var r = await Run(() => SendAsync(HttpMethod.Post, $"/items/{id}/update", Body(f))).ConfigureAwait(false);
        if (r != null) lock (gate) { notice = "Item saved."; }
        return r != null;
    }
    public async Task DeleteAsync(string id, int count = 1) { var r = await Run(() => SendAsync(HttpMethod.Post, $"/items/{id}/delete", new { count })).ConfigureAwait(false); if (r != null) lock (gate) { notice = count > 1 ? $"{count} items deleted." : "Item deleted."; } }
    public async Task GiveAsync(string id, string to, int count = 1) { var r = await Run(() => SendAsync(HttpMethod.Post, $"/items/{id}/give", new { to, count })).ConfigureAwait(false); if (r != null) lock (gate) { notice = count > 1 ? $"Gave {count} to {to}." : $"Given to {to}."; } }
    public async Task UseAsync(string id)
    {
        var r = await Run(() => SendAsync(HttpMethod.Post, $"/items/{id}/use", new { })).ConfigureAwait(false);
        if (r?.Text == null) return;
        var text = r.Text;
        if (r.Gained is { Count: > 0 }) text += "\n\nYou receive: " + string.Join(", ", r.Gained) + ".";
        if (!string.IsNullOrEmpty(r.KeyUsed)) text += "\n\n(" + r.KeyUsed + " was used up.)";
        if (!string.IsNullOrEmpty(r.StatusGained)) text += "\n\nYou are now affected by: " + r.StatusGained + ".";
        if (!string.IsNullOrEmpty(r.StatusCured)) text += "\n\n" + r.StatusCured + " has been lifted.";
        if (r.Offer != null) Plugin.Quests.SetOffer(r.Offer);
        if (!string.IsNullOrEmpty(r.StatusGained) || !string.IsNullOrEmpty(r.StatusCured)) _ = Plugin.Status.RefreshAsync();
        _ = Plugin.Framework.RunOnFrameworkThread(() => Plugin.Scene.ShowLocal(text));
    }
    public async Task CombineAsync(string id)
    {
        var r = await Run(() => SendAsync(HttpMethod.Post, $"/items/{id}/combine", new { })).ConfigureAwait(false);
        if (r == null || (r.Combined == null && !r.Failed)) return;
        var used = r.Used ?? new();
        var list = used.Count <= 1 ? string.Join("", used) : string.Join(", ", used.Take(used.Count - 1)) + " and " + used[^1];
        var text = r.Failed ? $"You try to combine {list}, but it fails. Everything is used up and nothing is made." : $"You combine {list} and make: {r.Combined}.";
        _ = Plugin.Framework.RunOnFrameworkThread(() => Plugin.Scene.ShowLocal(text));
    }
    public async Task SealAsync(string id, string itemId)
    {
        var r = await Run(() => SendAsync(HttpMethod.Post, $"/items/{id}/seal", new { itemId })).ConfigureAwait(false);
        if (r != null) lock (gate) { notice = "Put inside."; }
    }
    public async Task UnsealAsync(string id)
    {
        var r = await Run(() => SendAsync(HttpMethod.Post, $"/items/{id}/unseal", new { })).ConfigureAwait(false);
        if (r != null) lock (gate) { notice = "Taken out."; }
    }
}

/// <summary>Turns an icon id ("game:12345" or "site:file.png") into something drawable.</summary>
public static class ItemIcons
{
    public static IDalamudTextureWrap? Wrap(string icon)
    {
        if (icon.StartsWith("game:") && uint.TryParse(icon.AsSpan(5), out var id))
        {
            try { if (Plugin.TextureProvider.TryGetFromGameIcon(new GameIconLookup(id), out var tex) && tex.TryGetWrap(out var wrap, out _)) return wrap; }
            catch (Exception) { /* no such icon */ }
            return null;
        }
        if (icon.StartsWith("site:")) return Plugin.Images.Get(ImageCache.SiteImages + "icons/" + icon[5..]);
        return null;
    }
}
