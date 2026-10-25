using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace KinshipPlugin;

// ---- What the combat server sends back ----
public sealed class CMember { public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string Role { get; set; } = ""; public bool Online { get; set; } }
public sealed class CPerson
{
    public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string Type { get; set; } = "";
    public int Max { get; set; } public int Hp { get; set; } public int Number { get; set; }
    public string? Details { get; set; } public string? OwnerId { get; set; } public string? Loot { get; set; }
}
public sealed class CLogEntry { public string Id { get; set; } = ""; public string At { get; set; } = ""; public string Message { get; set; } = ""; }
public sealed class CRoll
{
    public string ActorId { get; set; } = ""; public string TargetId { get; set; } = ""; public string Kind { get; set; } = "";
    public int Sides { get; set; } public int Raw { get; set; } public int Total { get; set; }
    public int Applied { get; set; } public int Before { get; set; } public int After { get; set; }
}
public sealed class CDrop { public string PersonId { get; set; } = ""; public string Name { get; set; } = ""; public string Loot { get; set; } = ""; }
public sealed class CStanding { public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string Type { get; set; } = ""; public int Hp { get; set; } public int Max { get; set; } }
public sealed class CLoot { public string Name { get; set; } = ""; public string Loot { get; set; } = ""; }
public sealed class CResult
{
    public string Reason { get; set; } = ""; public string Headline { get; set; } = ""; public string Detail { get; set; } = "";
    public List<string> Winners { get; set; } = new(); public string Kind { get; set; } = ""; public int Rounds { get; set; }
    public List<CStanding> Standings { get; set; } = new(); public List<CLoot> Loot { get; set; } = new(); public string Note { get; set; } = "";
}
public sealed class CRoom
{
    public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string? Location { get; set; } public string? Notes { get; set; }
    public string Phase { get; set; } = "setup"; public string Mode { get; set; } = "random"; public int Revision { get; set; }
    public string Kind { get; set; } = "classic"; public int Minutes { get; set; } = 15; public string? EndsAt { get; set; }
    public CResult? Result { get; set; } public List<CDrop> Drops { get; set; } = new();
    public List<CMember> Members { get; set; } = new(); public List<CPerson> People { get; set; } = new();
    public List<string> Order { get; set; } = new(); public int Index { get; set; } public int Round { get; set; } = 1;
    public List<CLogEntry> Log { get; set; } = new(); public CRoll? LastRoll { get; set; } public bool UndoAvailable { get; set; }
}
public sealed class CReply { public string? Token { get; set; } public string? MemberId { get; set; } public CRoom? Room { get; set; } public string? Error { get; set; } }

public sealed class CombatException : Exception
{
    public int Status { get; }
    public CombatException(string message, int status) : base(message) { Status = status; }
}

/// <summary>Talks to the Kinship Combat server (the same one the website uses) and remembers the live room.</summary>
public sealed class CombatState
{
    public const string BaseUrl = "https://kinship-combat.thekinship-ffxiv.workers.dev";
    public const string InviteBase = BaseUrl + "/table/index.html?room=";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object gate = new();
    private CRoom? room;
    private string token;
    private string memberId;
    private string code;
    private string error = "";
    private bool busy;
    private bool pollInFlight;
    private DateTime lastPoll = DateTime.MinValue;

    public CombatState()
    {
        token = Plugin.Config.CombatToken;
        memberId = Plugin.Config.CombatMemberId;
        code = Plugin.Config.CombatCode;
    }

    public bool Connected { get { lock (gate) { return token.Length > 0 && code.Length > 0; } } }
    public string Code { get { lock (gate) { return code; } } }

    public (CRoom? Room, string MyId, string Error, bool Busy) Snapshot()
    {
        lock (gate) { return (room, memberId, error, busy); }
    }

    // ---------- plumbing ----------
    private static async Task<CReply> SendAsync(HttpMethod method, string path, object? body, string? bearer)
    {
        using var req = new HttpRequestMessage(method, BaseUrl + "/api" + path);
        if (bearer != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        using var res = await Http.SendAsync(req).ConfigureAwait(false);
        var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
        CReply? reply = null;
        try { reply = JsonSerializer.Deserialize<CReply>(text, Json); } catch { /* not JSON */ }
        if (!res.IsSuccessStatusCode)
            throw new CombatException(reply?.Error ?? $"The combat server answered {(int)res.StatusCode}.", (int)res.StatusCode);
        return reply ?? throw new CombatException("The combat server sent something unexpected.", 500);
    }

    private void SetError(string message) { lock (gate) { error = message; } }

    // ---------- joining and leaving ----------
    /// <summary>Accepts a bare room code or a whole invite link.</summary>
    public static string? ExtractCode(string input)
    {
        var m = Regex.Match(input ?? "", "[A-Fa-f0-9]{18}");
        return m.Success ? m.Value.ToUpperInvariant() : null;
    }

    public async Task JoinAsync(string rawCode, string name)
    {
        var roomCode = ExtractCode(rawCode);
        if (roomCode == null) { SetError("That doesn't look like a room code. It is 18 letters and numbers, or paste the invite link."); return; }
        if (name.Trim().Length == 0) { SetError("Enter the name you want to appear under."); return; }
        lock (gate) { if (busy) return; busy = true; error = ""; }
        try
        {
            var r = await SendAsync(HttpMethod.Post, $"/rooms/{roomCode}/join", new { name = name.Trim() }, null).ConfigureAwait(false);
            if (r.Token == null || r.MemberId == null || r.Room == null) throw new CombatException("The join reply was incomplete.", 500);
            lock (gate) { token = r.Token; memberId = r.MemberId; code = roomCode; room = r.Room; lastPoll = DateTime.UtcNow; }
            Plugin.Config.CombatName = name.Trim();
            Plugin.Config.CombatCode = roomCode; Plugin.Config.CombatToken = r.Token; Plugin.Config.CombatMemberId = r.MemberId;
            Plugin.Config.Save();
        }
        catch (CombatException ex) { SetError(ex.Message); }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Join failed"); SetError("Couldn't reach the combat server. Check your connection and try again."); }
        finally { lock (gate) { busy = false; } }
    }

    /// <summary>Starts a brand new encounter room; you become its DM.</summary>
    public async Task CreateAsync(string name, string encounter)
    {
        if (name.Trim().Length == 0) { SetError("Enter the name you want to appear under."); return; }
        if (encounter.Trim().Length == 0) { SetError("Give the encounter a name."); return; }
        lock (gate) { if (busy) return; busy = true; error = ""; }
        try
        {
            var r = await SendAsync(HttpMethod.Post, "/rooms", new { name = name.Trim(), encounter = encounter.Trim() }, null).ConfigureAwait(false);
            if (r.Token == null || r.MemberId == null || r.Room == null) throw new CombatException("The reply was incomplete.", 500);
            lock (gate) { token = r.Token; memberId = r.MemberId; code = r.Room.Code; room = r.Room; lastPoll = DateTime.UtcNow; }
            Plugin.Config.CombatName = name.Trim();
            Plugin.Config.CombatCode = r.Room.Code; Plugin.Config.CombatToken = r.Token; Plugin.Config.CombatMemberId = r.MemberId;
            Plugin.Config.Save();
        }
        catch (CombatException ex) { SetError(ex.Message); }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Create failed"); SetError("Couldn't reach the combat server. Check your connection and try again."); }
        finally { lock (gate) { busy = false; } }
    }

    /// <summary>DM only: closes the encounter for everyone and forgets the room.</summary>
    public async Task CloseRoomAsync()
    {
        string tk, cd;
        lock (gate) { if (busy || token.Length == 0) return; busy = true; error = ""; tk = token; cd = code; }
        try
        {
            await SendAsync(HttpMethod.Post, $"/rooms/{cd}/close", new { }, tk).ConfigureAwait(false);
            Leave();
        }
        catch (CombatException ex) when (ex.Status == 404) { Leave(); }
        catch (CombatException ex) { SetError(ex.Message); }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Close failed"); SetError("Couldn't reach the combat server. Try again."); }
        finally { lock (gate) { busy = false; } }
    }

    public void Leave()
    {
        lock (gate) { token = ""; memberId = ""; code = ""; room = null; error = ""; }
        Plugin.Config.CombatCode = ""; Plugin.Config.CombatToken = ""; Plugin.Config.CombatMemberId = "";
        Plugin.Config.Save();
    }

    // ---------- live updates (about every 2 seconds, only while the window is drawing) ----------
    public void PollIfDue()
    {
        string tk, cd;
        lock (gate)
        {
            if (token.Length == 0 || pollInFlight || (DateTime.UtcNow - lastPoll).TotalSeconds < 2) return;
            pollInFlight = true; lastPoll = DateTime.UtcNow; tk = token; cd = code;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var r = await SendAsync(HttpMethod.Get, $"/rooms/{cd}", null, tk).ConfigureAwait(false);
                lock (gate)
                {
                    if (r.Room != null && (room == null || r.Room.Revision >= room.Revision)) room = r.Room;
                    if (error.StartsWith("Can't reach")) error = "";
                }
            }
            catch (CombatException ex) when (ex.Status == 404)
            {
                Leave();
                SetError("This encounter was closed by the DM.");
            }
            catch (CombatException ex) when (ex.Status == 401)
            {
                SetError(ex.Message + " Press Leave room and join again.");
            }
            catch (CombatException ex) { SetError(ex.Message); }
            catch (Exception) { SetError("Can't reach the combat server right now. Retrying..."); }
            finally { lock (gate) { pollInFlight = false; } }
        });
    }

    // ---------- doing things in the room ----------
    public void Act(string type, object payload)
    {
        string tk, cd; int revision;
        lock (gate)
        {
            if (busy || room == null || token.Length == 0) return;
            busy = true; error = ""; tk = token; cd = code; revision = room.Revision;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                var body = new { type, payload, revision, actionId = Guid.NewGuid().ToString() };
                var r = await SendAsync(HttpMethod.Post, $"/rooms/{cd}/actions", body, tk).ConfigureAwait(false);
                lock (gate) { if (r.Room != null) { room = r.Room; lastPoll = DateTime.UtcNow; } }
            }
            catch (CombatException ex)
            {
                SetError(ex.Message);
                if (ex.Status == 409) lock (gate) { lastPoll = DateTime.MinValue; }   // refresh right away
            }
            catch (Exception ex) { Plugin.Log.Warning(ex, "Combat action failed"); SetError("Couldn't reach the combat server. Try again."); }
            finally { lock (gate) { busy = false; } }
        });
    }
}
