using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lumina.Excel.Sheets;

namespace KinshipPlugin;

/// <summary>Names the game's item icons and groups them by item type (armor, weapons, medicine and so on), read from the game's own item list. Built once in the background the first time the icon picker opens.</summary>
public static class GameIconIndex
{
    private static bool started;
    public static volatile bool Ready;
    public static volatile bool Failed;
    private static Dictionary<uint, string> names = new();
    private static Dictionary<string, List<uint>> byCategory = new();
    private static List<string> categories = new();
    private static List<uint> allSorted = new();

    public static void EnsureLoaded()
    {
        if (started) return;
        started = true;
        Task.Run(Build);
    }

    private static void Build()
    {
        try
        {
            var n = new Dictionary<uint, string>();
            var sets = new Dictionary<string, HashSet<uint>>();
            foreach (var item in Plugin.GameData.GetExcelSheet<Item>())
            {
                uint icon = item.Icon;
                if (icon == 0) continue;
                var name = item.Name.ToString();
                if (name.Length == 0) continue;
                var cat = item.ItemUICategory.ValueNullable?.Name.ToString() ?? "";
                if (cat.Length == 0) cat = "Other";
                if (!n.ContainsKey(icon)) n[icon] = name + " (" + cat + ")";
                if (!sets.TryGetValue(cat, out var set)) sets[cat] = set = new HashSet<uint>();
                set.Add(icon);
            }
            var by = new Dictionary<string, List<uint>>();
            foreach (var kv in sets) by[kv.Key] = kv.Value.OrderBy(x => x).ToList();
            names = n; byCategory = by; categories = by.Keys.OrderBy(c => c).ToList(); allSorted = n.Keys.OrderBy(x => x).ToList();
            Ready = true;
        }
        catch (Exception) { Failed = true; }
    }

    public static List<string> Categories => categories;

    /// <summary>The icon numbers in one item type, lowest first. Empty if unknown.</summary>
    public static List<uint> InCategory(string category) => byCategory.TryGetValue(category, out var l) ? l : new List<uint>();

    /// <summary>The icon numbers whose item name contains the text, lowest first (limited to one item type if given).</summary>
    public static List<uint> Search(string text, string? category)
    {
        var pool = category != null && byCategory.TryGetValue(category, out var l) ? l : allSorted;
        if (string.IsNullOrWhiteSpace(text)) return pool;
        return pool.Where(i => names.TryGetValue(i, out var nm) && nm.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public static string NameOf(uint icon) => names.TryGetValue(icon, out var s) ? s : "";
}
