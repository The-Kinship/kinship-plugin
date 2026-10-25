using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace KinshipPlugin;

/// <summary>Holds the latest sheet data so every window shows the same thing.</summary>
public sealed class DataStore
{
    private readonly object gate = new();
    private Dictionary<string, string>? faction;
    private List<SheetClient.Plot>? plots;
    private List<SheetClient.SectRow>? sectRows;
    private string status = "Loading...";
    private Task? current;

    public (Dictionary<string, string>? Faction, List<SheetClient.Plot>? Plots, string Status) Snapshot()
    {
        lock (gate) { return (faction, plots, status); }
    }

    public List<SheetClient.SectRow>? SectRows { get { lock (gate) { return sectRows; } } }

    /// <summary>Reloads the sheet. If a reload is already running, returns that one.</summary>
    public Task Refresh()
    {
        lock (gate)
        {
            if (current != null && !current.IsCompleted) return current;
            status = "Loading...";
            current = Load();
            return current;
        }
    }

    private async Task Load()
    {
        try
        {
            var f = SheetClient.FetchFactionAsync();
            var p = SheetClient.FetchPlotsAsync();
            var fd = await f.ConfigureAwait(false);
            var pd = await p.ConfigureAwait(false);
            lock (gate) { faction = fd; plots = pd; status = ""; }
            try
            {
                var rows = await SheetClient.FetchSectRowsAsync().ConfigureAwait(false);
                lock (gate) { sectRows = rows; }
            }
            catch (Exception ex) { Plugin.Log.Warning(ex, "Could not load the sect content."); }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not load the sheet.");
            lock (gate) { status = "Couldn't load the info. Check your connection and press Refresh."; }
        }
    }
}
