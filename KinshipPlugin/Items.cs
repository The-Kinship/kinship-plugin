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
    public string Requires { get; set; } = ""; public bool RequiresConsume { get; set; } public int Holds { get; set; }
}
public sealed class ItemReply
{
    public string? Token { get; set; } public string? Name { get; set; } public string? RecoveryCode { get; set; }
    public int Capacity { get; set; } public List<ItemView>? Items { get; set; } public List<string>? Members { get; set; }
    public string? Text { get; set; } public bool Consumed { get; set; } public string? Error { get; set; }
    public List<StashView>? Stashes { get; set; } public StashView? Stash { get; set; } public List<ItemView>? StashItems { get; set; }
    public List<SignView>? Signs { get; set; } public SignView? Sign { get; set; }
    public List<string>? Gained { get; set; } public string? KeyUsed { get; set; }
}
public sealed class ItemForm
{
    public string Name = "", Icon = "", Description = "", Flavor = "", UseLabel = "", UseText = "", Requires = "";
    public bool Consumable, RequiresConsume;
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
    private string error = "", notice = "";
    private bool busy;
    private DateTime lastRefresh = DateTime.MinValue;
    private bool loadedOnce;

    // DM tools (they use the creation password)
    private List<ItemView> adminItems = new();
    private List<string> adminMembers = new();
    private List<StashView> adminStashes = new();
    private string adminError = "", adminNotice = "";
    private bool adminBusy, adminLoaded;
    public (List<ItemView> Items, List<string> Members, string Error, string Notice, bool Busy, bool Loaded) AdminSnapshot()
    {
        lock (gate) { return (adminItems.ToList(), adminMembers.ToList(), adminError, adminNotice, adminBusy, adminLoaded); }
    }
    public List<StashView> AdminStashes() { lock (gate) { return adminStashes.ToList(); } }

    public ItemsSnap Snapshot()
    {
        lock (gate) { return new ItemsSnap(Plugin.Config.ItemsToken.Length > 0, Plugin.Config.ItemsName, capacity, items.ToList(), members.ToList(), error, notice, busy, Plugin.Config.ItemsRecovery); }
    }
    private void SetError(string m) { lock (gate) { error = m; notice = ""; } }
    public void ClearMessages() { lock (gate) { error = ""; notice = ""; } }

    internal static async Task<ItemReply> SendAsync(HttpMethod method, string path, object? body)
    {
        using var req = new HttpRequestMessage(method, CombatState.BaseUrl + "/api" + path);
        if (Plugin.Config.ItemsToken.Length > 0) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Plugin.Config.ItemsToken);
        if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
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
        Plugin.Config.ItemsToken = ""; Plugin.Config.Save();
        lock (gate) { items = new(); loadedOnce = false; }
    }
    /// <summary>Lets the stash screen update your inventory after you put something in or take something out.</summary>
    public void SetInventory(List<ItemView> list) { lock (gate) { items = list; lastRefresh = DateTime.UtcNow; } }
    public void SignOut() { SignOutLocal(); ClearMessages(); }

    public async Task RegisterAsync(string name, string memberPassword)
    {
        var r = await Run(() => SendAsync(HttpMethod.Post, "/items/register", new { name = name.Trim(), memberPassword }), false).ConfigureAwait(false);
        if (r?.Token == null) return;
        Plugin.Config.ItemsToken = r.Token; Plugin.Config.ItemsName = r.Name ?? name.Trim(); Plugin.Config.ItemsRecovery = r.RecoveryCode ?? "";
        Plugin.Config.Save();
        lock (gate) { notice = "You're in. Write down your recovery code below."; loadedOnce = false; }
        await RefreshAsync(true).ConfigureAwait(false);
    }

    public async Task RecoverAsync(string name, string code)
    {
        var r = await Run(() => SendAsync(HttpMethod.Post, "/items/recover", new { name = name.Trim(), recoveryCode = code.Trim() }), false).ConfigureAwait(false);
        if (r?.Token == null) return;
        Plugin.Config.ItemsToken = r.Token; Plugin.Config.ItemsName = r.Name ?? name.Trim(); Plugin.Config.ItemsRecovery = code.Trim().ToUpperInvariant();
        Plugin.Config.Save();
        lock (gate) { notice = "Welcome back."; loadedOnce = false; }
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
        bool due;
        lock (gate) { due = !busy && Plugin.Config.ItemsToken.Length > 0 && (!loadedOnce || (DateTime.UtcNow - lastRefresh).TotalSeconds > 15); if (due) lastRefresh = DateTime.UtcNow; }
        if (due) _ = RefreshAsync(true);
    }

    public async Task LoadMembersAsync()
    {
        try { var r = await SendAsync(HttpMethod.Get, "/items/members", null).ConfigureAwait(false); lock (gate) { members = r.Members ?? new(); } }
        catch (Exception) { /* the give list stays as it was */ }
    }

    // ---------- DM tools ----------
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
        catch (Exception ex) { Plugin.Log.Warning(ex, "DM request failed"); lock (gate) { adminError = "Couldn't reach the server. Try again."; } }
        finally { lock (gate) { adminBusy = false; } }
        return null;
    }

    public async Task AdminLoadAsync()
    {
        var r = await RunAdmin(pw => SendAsync(HttpMethod.Post, "/items/admin/list", new { adminPassword = pw })).ConfigureAwait(false);
        if (r != null) lock (gate) { adminItems = r.Items ?? new(); adminMembers = r.Members ?? new(); adminStashes = r.Stashes ?? new(); adminLoaded = true; }
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
    public async Task AdminRemoveMemberAsync(string name)
    {
        var r = await RunAdmin(pw => SendAsync(HttpMethod.Post, "/items/admin/remove-member", new { adminPassword = pw, name }), $"{name} was removed.").ConfigureAwait(false);
        if (r != null) await AdminLoadAsync().ConfigureAwait(false);
    }
    public async Task<bool> AdminGrantAsync(ItemForm f, string to)
    {
        var r = await RunAdmin(pw => SendAsync(HttpMethod.Post, "/items/admin/grant", new
        {
            adminPassword = pw, to, name = f.Name.Trim(), icon = f.Icon, description = f.Description.Trim(), flavor = f.Flavor.Trim(),
            useLabel = f.UseLabel.Trim(), useText = f.UseText.Trim(), requires = f.Requires.Trim(), requiresConsume = f.RequiresConsume, consumable = f.Consumable
        }), $"Gift sent to {to}.").ConfigureAwait(false);
        return r != null;
    }

    private static object Body(ItemForm f) => new { name = f.Name.Trim(), icon = f.Icon, description = f.Description.Trim(), flavor = f.Flavor.Trim(), useLabel = f.UseLabel.Trim(), useText = f.UseText.Trim(), requires = f.Requires.Trim(), requiresConsume = f.RequiresConsume, consumable = f.Consumable };

    public async Task<bool> CreateAsync(ItemForm f)
    {
        var r = await Run(() => SendAsync(HttpMethod.Post, "/items", Body(f))).ConfigureAwait(false);
        if (r != null) lock (gate) { notice = "Item created."; }
        return r != null;
    }
    public async Task<bool> UpdateAsync(string id, ItemForm f)
    {
        var r = await Run(() => SendAsync(HttpMethod.Post, $"/items/{id}/update", Body(f))).ConfigureAwait(false);
        if (r != null) lock (gate) { notice = "Item saved."; }
        return r != null;
    }
    public async Task DeleteAsync(string id) { var r = await Run(() => SendAsync(HttpMethod.Post, $"/items/{id}/delete", new { })).ConfigureAwait(false); if (r != null) lock (gate) { notice = "Item deleted."; } }
    public async Task GiveAsync(string id, string to) { var r = await Run(() => SendAsync(HttpMethod.Post, $"/items/{id}/give", new { to })).ConfigureAwait(false); if (r != null) lock (gate) { notice = $"Given to {to}."; } }
    public async Task UseAsync(string id)
    {
        var r = await Run(() => SendAsync(HttpMethod.Post, $"/items/{id}/use", new { })).ConfigureAwait(false);
        if (r?.Text == null) return;
        var text = r.Text;
        if (r.Gained is { Count: > 0 }) text += "\n\nYou receive: " + string.Join(", ", r.Gained) + ".";
        if (!string.IsNullOrEmpty(r.KeyUsed)) text += "\n\n(" + r.KeyUsed + " was used up.)";
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
