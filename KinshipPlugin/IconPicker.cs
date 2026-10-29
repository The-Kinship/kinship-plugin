using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

/// <summary>The icon picker pop-up, shared by items and statuses. Call Open with what should happen when an icon is chosen, then Draw.</summary>
public sealed class IconPicker
{
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private readonly string id;
    private Action<string>? target;
    private int browseStart = 20000, goTo = 20000, iconNumber = 20001;
    private string category = "All";
    private string group = "";          // "" = browse by number; otherwise an item type from the game
    private string search = "";
    private int groupPage;
    private string cacheKey = "";
    private List<uint> cacheList = new();

    public IconPicker(string id) { this.id = id; }

    public void Open(Action<string> pick) { target = pick; GameIconIndex.EnsureLoaded(); ImGui.OpenPopup(id); }
    private void Choose(string icon) { target?.Invoke(icon); ImGui.CloseCurrentPopup(); }

    public void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2(460f, 400f), ImGuiCond.Appearing);
        if (!ImGui.BeginPopup(id)) return;
        if (ImGui.BeginTabBar("pickertabs"))
        {
            if (ImGui.BeginTabItem("Suggested")) { PickerCurated(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Browse game icons")) { PickerBrowse(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("By number")) { PickerNumber(); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
        ImGui.EndPopup();
    }

    private bool IconCell(string icon, string tip, ref int shown)
    {
        var wrap = ItemIcons.Wrap(icon);
        if (wrap == null) return false;
        if (shown % 8 != 0) ImGui.SameLine();
        ImGui.Image(wrap.Handle, new Vector2(40f, 40f));
        shown++;
        if (ImGui.IsItemHovered()) { ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); ImGui.SetTooltip(tip); }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) { Choose(icon); return true; }
        return false;
    }

    private void PickerCurated()
    {
        var list = Plugin.Data.Icons;
        if (list.Count == 0) { ImGui.TextWrapped("No suggested icons yet. Use Browse game icons or By number."); return; }
        var cats = list.Select(i => i.Category).Where(c => c.Length > 0).Distinct().OrderBy(c => c).ToList();
        cats.Insert(0, "All");
        ImGui.SetNextItemWidth(180f);
        if (ImGui.BeginCombo("Category", category))
        {
            foreach (var c in cats) if (ImGui.Selectable(c, c == category)) category = c;
            ImGui.EndCombo();
        }
        ImGui.BeginChild("curlist", new Vector2(440f, 300f));
        var shown = 0;
        foreach (var i in list.Where(i => category == "All" || i.Category == category))
            if (IconCell(i.Icon, i.Label.Length > 0 ? i.Label : i.Icon, ref shown)) break;
        ImGui.EndChild();
    }

    private void PickerBrowse()
    {
        if (GameIconIndex.Failed) ImGui.TextColored(Dim, "Item names could not be read, so you can only browse by number.");
        else if (!GameIconIndex.Ready) ImGui.TextColored(Dim, "Loading item names...");
        ImGui.SetNextItemWidth(200f);
        if (ImGui.BeginCombo("Group##icongroup", group.Length == 0 ? "Every icon (by number)" : group))
        {
            if (ImGui.Selectable("Every icon (by number)", group.Length == 0)) { group = ""; groupPage = 0; }
            if (GameIconIndex.Ready)
            {
                if (ImGui.Selectable("All items (search by name)", group == "*")) { group = "*"; groupPage = 0; }
                foreach (var c in GameIconIndex.Categories) if (ImGui.Selectable(c, c == group)) { group = c; groupPage = 0; }
            }
            ImGui.EndCombo();
        }
        if (group.Length == 0) { BrowseByNumber(); return; }
        if (!GameIconIndex.Ready) return;

        ImGui.SetNextItemWidth(200f);
        if (ImGui.InputText("Search name##iconsearch", ref search, 40)) groupPage = 0;
        var key = group + "|" + search;
        if (key != cacheKey) { cacheList = GameIconIndex.Search(search, group == "*" ? null : group); cacheKey = key; }
        var list = cacheList;
        const int per = 96;
        var pages = Math.Max(1, (list.Count + per - 1) / per);
        if (groupPage >= pages) groupPage = pages - 1;
        if (ImGui.Button("< Previous") && groupPage > 0) groupPage--;
        ImGui.SameLine(); if (ImGui.Button("Next >") && groupPage < pages - 1) groupPage++;
        ImGui.SameLine(); ImGui.TextColored(Dim, $"Page {groupPage + 1} of {pages}  ({list.Count} icons)");
        ImGui.BeginChild("grouplist", new Vector2(440f, 270f));
        var shown = 0;
        foreach (var icon in list.Skip(groupPage * per).Take(per))
        {
            var nm = GameIconIndex.NameOf(icon);
            if (IconCell("game:" + icon, (nm.Length > 0 ? nm + "\n" : "") + "Icon " + icon, ref shown)) break;
        }
        if (list.Count == 0) ImGui.TextColored(Dim, "Nothing matches that name.");
        ImGui.EndChild();
    }

    private void BrowseByNumber()
    {
        ImGui.TextColored(Dim, "Every game icon, 100 numbers at a time. Gaps are numbers with no icon. Hover for the item name.");
        if (ImGui.Button("< Previous 100") && browseStart >= 100) browseStart -= 100;
        ImGui.SameLine(); if (ImGui.Button("Next 100 >")) browseStart += 100;
        ImGui.SameLine(); ImGui.SetNextItemWidth(90f); ImGui.InputInt("##goto", ref goTo, 0);
        ImGui.SameLine(); if (ImGui.Button("Go") && goTo >= 0) browseStart = goTo / 100 * 100;
        ImGui.Text($"Showing {browseStart} to {browseStart + 99}");
        ImGui.BeginChild("browselist", new Vector2(440f, 270f));
        var shown = 0;
        for (var id = browseStart; id < browseStart + 100; id++)
        {
            var nm = GameIconIndex.NameOf((uint)id);
            if (IconCell("game:" + id, (nm.Length > 0 ? nm + "\n" : "") + "Icon " + id, ref shown)) break;
        }
        if (shown == 0) ImGui.TextColored(Dim, "No icons in this range. Try the next page.");
        ImGui.EndChild();
    }

    private void PickerNumber()
    {
        ImGui.TextWrapped("If you know an icon's number, type it here.");
        ImGui.SetNextItemWidth(140f); ImGui.InputInt("Icon number", ref iconNumber, 0);
        var wrap = iconNumber >= 0 ? ItemIcons.Wrap("game:" + iconNumber) : null;
        if (wrap != null)
        {
            ImGui.Image(wrap.Handle, new Vector2(64f, 64f));
            if (ImGui.Button("Use this icon")) { Choose("game:" + iconNumber); }
        }
        else ImGui.TextColored(Dim, "No icon with that number.");
    }
}
