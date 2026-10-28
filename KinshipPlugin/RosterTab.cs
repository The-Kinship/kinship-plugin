using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

/// <summary>The roster: everyone in the FC with a portrait. Click a name for the full profile.</summary>
public sealed class RosterTab
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly string[] SortNames = { "A to Z", "By sect", "By rank" };

    private int sort;
    private SheetClient.Person? open;

    /// <summary>Matches whatever is typed in the sheet to one of the four sects ("" = no sect, a Kin).</summary>
    public static string SectKey(string sect)
    {
        var t = sect.ToLowerInvariant();
        if (t.Contains("onyx") || t.Contains("blood")) return "onyx";
        if (t.Contains("iron") || t.Contains("vanguard")) return "iron";
        if (t.Contains("life") || t.Contains("watch")) return "life";
        if (t.Contains("ember") || t.Contains("court")) return "ember";
        return "";
    }

    private static int SectIndex(string sect) { var i = Array.IndexOf(SectData.Order, SectKey(sect)); return i < 0 ? 99 : i; }

    /// <summary>Harbinger, then Advocates, then Sect Leaders, then everyone else (the Kin).</summary>
    internal static int RankIndex(string rank)
    {
        var t = rank.ToLowerInvariant();
        if (t.Contains("harbinger")) return 0;
        if (t.Contains("advocate")) return 1;
        if (t.Contains("leader") || t.Contains("prefect") || t.Contains("commander") || t.Contains("warden") || t.Contains("baron")) return 2;
        return 3;
    }

    private static string PortraitUrl(string file)
        => file.StartsWith("http") ? file : ImageCache.SiteImages + "roster/" + file;

    private static string SectLabel(SheetClient.Person p)
    {
        var k = SectKey(p.Sect);
        return k.Length > 0 ? SectData.All[k].Name : (p.Sect.Length > 0 ? p.Sect : "Kin");
    }

    private static Vector4 SectColor(SheetClient.Person p)
    {
        var k = SectKey(p.Sect);
        return k.Length > 0 ? SectData.All[k].Color : Dim;
    }

    public void Draw()
    {
        var people = Plugin.Data.Roster;
        if (people == null) { ImGui.TextColored(Dim, "The roster opens soon."); return; }
        Plugin.Profiles.RefreshIfDue(120);   // brings in each character's tags (SM, Banker, a sect)
        if (open != null) { DrawProfile(open); return; }

        ImGui.TextColored(Gold, "The Kin");
        ImGui.TextColored(Dim, "Click a name to see their profile.");
        ImGui.Spacing();
        ImGui.Text("Sort:");
        for (var i = 0; i < SortNames.Length; i++)
        {
            ImGui.SameLine();
            if (ImGui.RadioButton(SortNames[i] + "##sort", sort == i)) sort = i;
        }
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

        IEnumerable<SheetClient.Person> sorted = sort switch
        {
            1 => people.OrderBy(p => SectIndex(p.Sect)).ThenBy(p => p.Order).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            2 => people.OrderBy(p => RankIndex(p.Rank)).ThenBy(p => p.Order).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            _ => people.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
        };

        if (people.Count == 0) ImGui.TextColored(Dim, "No profiles yet.");
        foreach (var p in sorted)
        {
            ImGui.BeginGroup();
            var wrap = p.Portrait.Length > 0 ? Plugin.Images.Get(PortraitUrl(p.Portrait)) : null;
            if (wrap != null) ImGui.Image(wrap.Handle, new Vector2(56f * wrap.Width / wrap.Height, 56f));
            else ImGui.Dummy(new Vector2(56f, 56f));
            ImGui.EndGroup();
            ImGui.SameLine();
            ImGui.BeginGroup();
            if (ImGui.Selectable(p.Name + "##r" + p.Name, false, ImGuiSelectableFlags.None, new Vector2(0, 0))) open = p;
            ImGui.TextColored(SectColor(p), SectLabel(p));
            if (p.Rank.Length > 0) ImGui.TextColored(Dim, p.Rank);
            if (Plugin.Profiles.Find(p.Name) is { Tags.Count: > 0 } reg) TagBadges.Draw(reg.Tags);
            ImGui.EndGroup();
            ImGui.Spacing();
        }

        ImGui.Separator(); ImGui.Spacing();
        DrawSubmit();
    }

    private static void DrawSubmit()
    {
        var url = AboutTab.Link("profile_form_url");
        if (!url.StartsWith("http")) url = AboutTab.Link("application_url");
        if (url.StartsWith("http"))
        {
            if (ImGui.Button("Submit profile / make changes")) AboutTab.OpenLink(url);
            ImGui.TextColored(Dim, "Opens the form in your browser.");
        }
        else ImGui.TextColored(Dim, "Profile submissions open soon.");
    }

    private void DrawProfile(SheetClient.Person p)
    {
        if (ImGui.Button("< The Kin")) { open = null; return; }
        ImGui.Spacing();
        var wrap = p.Portrait.Length > 0 ? Plugin.Images.Get(PortraitUrl(p.Portrait)) : null;
        if (wrap != null)
        {
            var avail = ImGui.GetContentRegionAvail().X;
            var scale = Math.Min(260f / wrap.Height, avail / wrap.Width);
            var size = new Vector2(wrap.Width * scale, wrap.Height * scale);
            if (avail > size.X) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (avail - size.X) / 2f);
            ImGui.Image(wrap.Handle, size);
        }
        else if (p.Portrait.Length > 0 && !Plugin.Images.HasFailed(PortraitUrl(p.Portrait))) ImGui.TextColored(Dim, "Loading portrait...");
        ImGui.Spacing();
        ImGui.TextColored(Gold, p.Name);
        ImGui.TextColored(SectColor(p), SectLabel(p));
        if (p.Rank.Length > 0) ImGui.TextColored(Dim, p.Rank);
        ImGui.Separator();
        if (p.Race.Length > 0) ImGui.Text("Race: " + p.Race);
        if (p.Age.Length > 0) ImGui.Text("Age: " + p.Age);
        if (p.Bio.Length > 0) { ImGui.Spacing(); ImGui.TextWrapped(p.Bio); }
        if (Plugin.Profiles.Find(p.Name) is { Tags.Count: > 0 } regTags) { ImGui.Spacing(); ImGui.Separator(); TagBadges.Draw(regTags.Tags); }
        ImGui.Spacing();
        if (ImGui.Button("Armory##rosterarmory")) Plugin.ShowArmory(p.Name);
        if (p.Carrd.StartsWith("http"))
        {
            ImGui.SameLine();
            if (ImGui.Button("Open their Carrd")) AboutTab.OpenLink(p.Carrd);
        }
    }
}
