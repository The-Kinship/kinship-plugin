using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace KinshipPlugin;

public sealed class SEvent { public int Id { get; set; } public string At { get; set; } = ""; public string Text { get; set; } = ""; public string Ooc { get; set; } = ""; public string? Combat { get; set; } }
public sealed class SMember { public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string Role { get; set; } = ""; }
public sealed class SReply
{
    public string? Token { get; set; } public string? MemberId { get; set; } public string? Role { get; set; } public string? Code { get; set; }
    public string? Title { get; set; } public int LastId { get; set; } public List<SEvent> Events { get; set; } = new();
    public List<SMember>? Members { get; set; } public int Delivered { get; set; } public string? Error { get; set; }
}
public sealed class Popup { public int Id { get; init; } public string Text { get; init; } = ""; public string Ooc { get; init; } = ""; public string Combat { get; init; } = ""; }
public sealed record SceneSnap(bool Connected, string Code, string Title, string Role, bool Listening, List<SMember> Members, string Error, string Status, bool Busy);

/// <summary>RP scenes: a SM sends popups, players who joined the scene (and turned popups on) receive them.</summary>
public sealed class SceneState : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly object gate = new();
    private string token, memberId, code, role, title;
    private List<SMember> members = new();
    private int lastId = -1;
    private bool listening;               // always starts OFF at each login
    private string error = "", status = "";
    private bool busy, memberPollInFlight;
    private DateTime lastMemberPoll = DateTime.MinValue;
    private CancellationTokenSource? cts;
    private readonly List<Popup> popups = new();
    private readonly List<Popup> history = new();

    public SceneState()
    {
        token = Plugin.Config.Me().SceneToken; memberId = Plugin.Config.Me().SceneMemberId;
        code = Plugin.Config.Me().SceneCode; role = Plugin.Config.Me().SceneRole; title = Plugin.Config.Me().SceneTitle;
    }

    public SceneSnap Snapshot()
    {
        lock (gate) { return new SceneSnap(token.Length > 0 && code.Length > 0, code, title, role, listening, members.ToList(), error, status, busy); }
    }
    public List<Popup> ActivePopups() { lock (gate) { return popups.ToList(); } }
    public List<Popup> RecentPopups() { lock (gate) { return history.AsEnumerable().Reverse().ToList(); } }
    private int localId;
    /// <summary>Shows a popup that never touches the server (used when an item is used).</summary>
    public void ShowLocal(string text)
    {
        var popup = new Popup { Id = System.Threading.Interlocked.Decrement(ref localId), Text = text, Ooc = "" };
        lock (gate) { popups.Add(popup); history.Add(popup); if (history.Count > 20) history.RemoveAt(0); }
        Plugin.ShowPopups();
    }
    public void Dismiss(int id) { lock (gate) { popups.RemoveAll(p => p.Id == id); } }
    public void DismissAll() { lock (gate) { popups.Clear(); } }
    private void SetError(string m) { lock (gate) { error = m; } }

    private static async Task<SReply> SendAsync(HttpMethod method, string path, object? body, string? bearer)
    {
        using var req = new HttpRequestMessage(method, CombatState.BaseUrl + "/api" + path);
        if (bearer != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        using var res = await Http.SendAsync(req).ConfigureAwait(false);
        var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
        SReply? reply = null;
        try { reply = JsonSerializer.Deserialize<SReply>(text, Json); } catch { /* not JSON */ }
        if (!res.IsSuccessStatusCode) throw new CombatException(reply?.Error ?? $"The server answered {(int)res.StatusCode}.", (int)res.StatusCode);
        return reply ?? throw new CombatException("The server sent something unexpected.", 500);
    }

    private void Adopt(SReply r, string name)
    {
        lock (gate)
        {
            token = r.Token ?? ""; memberId = r.MemberId ?? ""; code = r.Code ?? ""; role = r.Role ?? "player"; title = r.Title ?? "";
            members = r.Members ?? new(); lastId = -1; error = ""; status = "";
        }
        Plugin.Config.Me().SceneToken = token; Plugin.Config.Me().SceneMemberId = memberId; Plugin.Config.Me().SceneCode = code;
        Plugin.Config.Me().SceneRole = role; Plugin.Config.Me().SceneTitle = title; Plugin.Config.Me().SceneName = name;
        Plugin.Config.Save();
    }

    // ---------- start / join / leave ----------
    public async Task CreateAsync(string name, string sceneTitle)
    {
        if (name.Trim().Length == 0) { SetError("Enter the name you want to appear under."); return; }
        lock (gate) { if (busy) return; busy = true; error = ""; }
        try
        {
            var r = await SendAsync(HttpMethod.Post, "/scenes", new { name = name.Trim(), title = sceneTitle.Trim(), createPassword = Plugin.Config.CreatePassword }, null).ConfigureAwait(false);
            Adopt(r, name.Trim());
        }
        catch (CombatException ex) { SetError(ex.Message); }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Scene create failed"); SetError("Couldn't reach the server. Check your connection and try again."); }
        finally { lock (gate) { busy = false; } }
    }

    public async Task JoinAsync(string rawCode, string name)
    {
        var sceneCode = CombatState.ExtractCode(rawCode);
        if (sceneCode == null) { SetError("That doesn't look like a scene code. It is 18 letters and numbers."); return; }
        if (name.Trim().Length == 0) { SetError("Enter the name you want to appear under."); return; }
        lock (gate) { if (busy) return; busy = true; error = ""; }
        try
        {
            var r = await SendAsync(HttpMethod.Post, $"/scenes/{sceneCode}/join", new { name = name.Trim() }, null).ConfigureAwait(false);
            Adopt(r, name.Trim());
            SetListening(true);          // joining is opting in
        }
        catch (CombatException ex) { SetError(ex.Message); }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Scene join failed"); SetError("Couldn't reach the server. Check your connection and try again."); }
        finally { lock (gate) { busy = false; } }
    }

    private void ForgetLocal()
    {
        StopLoop();
        lock (gate) { token = ""; memberId = ""; code = ""; role = ""; title = ""; members = new(); listening = false; lastId = -1; popups.Clear(); }
        Plugin.Config.Me().SceneToken = ""; Plugin.Config.Me().SceneMemberId = ""; Plugin.Config.Me().SceneCode = ""; Plugin.Config.Me().SceneRole = ""; Plugin.Config.Me().SceneTitle = "";
        Plugin.Config.Save();
    }

    public void Leave()
    {
        string tk, cd, rl;
        lock (gate) { tk = token; cd = code; rl = role; }
        if (rl == "player" && tk.Length > 0) _ = Task.Run(async () => { try { await SendAsync(HttpMethod.Post, $"/scenes/{cd}/leave", new { }, tk).ConfigureAwait(false); } catch { /* best effort */ } });
        ForgetLocal(); SetError("");
    }

    public async Task CloseSceneAsync()
    {
        string tk, cd;
        lock (gate) { if (busy || token.Length == 0) return; busy = true; error = ""; tk = token; cd = code; }
        try { await SendAsync(HttpMethod.Post, $"/scenes/{cd}/close", new { }, tk).ConfigureAwait(false); ForgetLocal(); }
        catch (CombatException ex) when (ex.Status == 404) { ForgetLocal(); }
        catch (CombatException ex) { SetError(ex.Message); }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Scene close failed"); SetError("Couldn't reach the server. Try again."); }
        finally { lock (gate) { busy = false; } }
    }

    // ---------- receiving popups (players) ----------
    public void SetListening(bool on)
    {
        lock (gate) { listening = on; status = ""; }
        if (on) StartLoop(); else StopLoop();
    }

    private void StartLoop()
    {
        StopLoop();
        var c = new CancellationTokenSource();
        lock (gate) { cts = c; lastId = -1; }      // the first check only learns where things stand, so old popups never replay
        _ = Task.Run(() => LoopAsync(c.Token));
    }

    private void StopLoop()
    {
        CancellationTokenSource? c;
        lock (gate) { c = cts; cts = null; }
        try { c?.Cancel(); } catch { }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await PollOnceAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Plugin.Log.Warning(ex, "Scene poll failed"); }
            try { await Task.Delay(4000, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        string tk, cd; int after;
        lock (gate) { tk = token; cd = code; after = lastId; }
        if (tk.Length == 0) return;
        try
        {
            var r = await SendAsync(HttpMethod.Get, $"/scenes/{cd}?after={after}", null, tk).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return;
            lock (gate) { lastId = r.LastId; if (error.StartsWith("Can't reach")) error = ""; }
            if (after >= 0) foreach (var ev in r.Events) Enqueue(ev);
        }
        catch (CombatException ex) when (ex.Status == 404) { ForgetLocal(); SetError("This scene was closed by the SM."); }
        catch (CombatException ex) when (ex.Status == 401) { SetListening(false); SetError("You are no longer in this scene. Join again with the code."); }
        catch (CombatException ex) { SetError(ex.Message); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { SetError("Can't reach the server right now. Retrying..."); }
    }

    private void Enqueue(SEvent ev)
    {
        var popup = new Popup { Id = ev.Id, Text = ev.Text, Ooc = ev.Ooc, Combat = ev.Combat ?? "" };
        lock (gate)
        {
            if (popups.Any(p => p.Id == ev.Id)) return;
            popups.Add(popup); history.Add(popup);
            if (history.Count > 20) history.RemoveAt(0);
        }
        Plugin.ShowPopups();
        if (Plugin.Config.ChatEcho)
        {
            var line = "[RP] " + ev.Text + (ev.Ooc.Length > 0 ? " " + UiUtil.Ooc(ev.Ooc) : "");
            _ = Plugin.Framework.RunOnFrameworkThread(() => Plugin.Chat.Print(line));
        }
    }

    // ---------- SM: member list and sending ----------
    public void RefreshMembersIfDue()
    {
        string tk, cd;
        lock (gate)
        {
            if (role != "dm" || token.Length == 0 || memberPollInFlight || (DateTime.UtcNow - lastMemberPoll).TotalSeconds < 4) return;
            memberPollInFlight = true; lastMemberPoll = DateTime.UtcNow; tk = token; cd = code;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var r = await SendAsync(HttpMethod.Get, $"/scenes/{cd}?after=-1", null, tk).ConfigureAwait(false);
                lock (gate) { members = r.Members ?? members; }
            }
            catch (CombatException ex) when (ex.Status == 404) { ForgetLocal(); SetError("This scene no longer exists."); }
            catch (Exception) { /* try again next time */ }
            finally { lock (gate) { memberPollInFlight = false; } }
        });
    }

    public async Task RemoveMemberAsync(string id)
    {
        string tk, cd;
        lock (gate) { if (token.Length == 0) return; tk = token; cd = code; error = ""; }
        try
        {
            await SendAsync(HttpMethod.Post, $"/scenes/{cd}/kick", new { memberId = id }, tk).ConfigureAwait(false);
            lock (gate) { members = members.Where(m => m.Id != id).ToList(); lastMemberPoll = DateTime.MinValue; }
        }
        catch (CombatException ex) { SetError(ex.Message); }
        catch (Exception) { SetError("Couldn't reach the server. Try again."); }
    }

    public async Task<bool> SendPopupAsync(string text, string ooc, List<string>? to, string combat = "")
    {
        string tk, cd;
        lock (gate) { if (busy || token.Length == 0) return false; busy = true; error = ""; status = ""; tk = token; cd = code; }
        try
        {
            var r = await SendAsync(HttpMethod.Post, $"/scenes/{cd}/send", new { text, ooc, to, combat }, tk).ConfigureAwait(false);
            lock (gate) { status = r.Delivered == 1 ? "Sent to 1 person." : $"Sent to {r.Delivered} people."; }
            return true;
        }
        catch (CombatException ex) { SetError(ex.Message); }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Popup send failed"); SetError("Couldn't reach the server. Try again."); }
        finally { lock (gate) { busy = false; } }
        return false;
    }

    public void Dispose() { StopLoop(); }
}
