using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

/// <summary>The Sects tab: sect list, password prompt, and each sect's page.</summary>
public sealed class SectsTab
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);

    private string? openSect;      // which sect page is showing
    private string? gateSect;      // which sect is asking for a password
    private string? gateReturn;    // where Cancel / success goes back to (null = the list)
    private string password = "";
    private string error = "";
    private bool remember = true;

    private void Gate(string key, string? returnTo)
    {
        gateSect = key; gateReturn = returnTo; password = ""; error = "";
    }

    private void TryOpen(string key, string? returnTo)
    {
        if (SectData.IsOpenNow(key)) openSect = key;
        else Gate(key, returnTo);
    }

    public void Draw()
    {
        if (gateSect != null) { DrawGate(); return; }
        if (openSect != null) { DrawSect(openSect); return; }
        DrawList();
    }

    // ---------- the list of sects ----------
    private void DrawList()
    {
        UiUtil.Centered("sects-emblem.jpg", 120f);
        ImGui.TextColored(Dim, "Four paths, one purpose. Choose a sect and enter with its password.");
        ImGui.Spacing();
        DrawApply();
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        foreach (var key in SectData.Order)
        {
            var s = SectData.All[key];
            var wrap = Plugin.Images.Get(ImageCache.SiteImages + s.Image);
            ImGui.BeginGroup();
            if (wrap != null) ImGui.Image(wrap.Handle, new Vector2(72f * wrap.Width / wrap.Height, 72f));
            else ImGui.Dummy(new Vector2(72f, 72f));
            ImGui.EndGroup();
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.TextColored(s.Color, s.Name);
            ImGui.TextColored(Dim, "Sect Leader: " + s.Leader);
            ImGui.TextWrapped(s.Blurb);
            var unlocked = SectData.IsOpenNow(key);
            if (ImGui.Button((unlocked ? "Open" : "Enter password") + "##" + key)) TryOpen(key, null);
            if (unlocked) { ImGui.SameLine(); ImGui.TextColored(s.Color, "Unlocked"); }
            ImGui.EndGroup();
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        }
    }

    // ---------- apply to a sect ----------
    // Games can't hold a form, so this opens the application page in the browser.
    private static void DrawApply()
    {
        var url = AboutTab.Link("sect_application_url");
        if (!url.StartsWith("http")) url = AboutTab.Link("application_url");
        ImGui.TextColored(Gold, "Not in a sect yet?");
        if (url.StartsWith("http"))
        {
            ImGui.TextWrapped("Chosen your path? Send in your sect application. It opens in your browser.");
            if (ImGui.Button("Apply to a sect")) AboutTab.OpenLink(url);
        }
        else ImGui.TextColored(Dim, "The sect application opens soon. Ask a member of the Ember Court in the meantime.");
    }

    // ---------- the password prompt ----------
    private void DrawGate()
    {
        var key = gateSect!;
        var s = SectData.All[key];
        var wrap = Plugin.Images.Get(ImageCache.SiteImages + s.Image);
        if (wrap != null)
        {
            var size = new Vector2(80f * wrap.Width / wrap.Height, 80f);
            var avail = ImGui.GetContentRegionAvail().X;
            if (avail > size.X) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (avail - size.X) / 2f);
            ImGui.Image(wrap.Handle, size);
        }
        ImGui.TextColored(s.Color, s.Name);
        ImGui.TextColored(Dim, "Members only. Enter the password to continue.");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(220f);
        var entered = ImGui.InputText("##sectpw", ref password, 64, ImGuiInputTextFlags.Password | ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.Checkbox("Remember me on this computer", ref remember);
        if (error.Length > 0) ImGui.TextColored(Red, error);

        if (ImGui.Button("Enter") || entered)
        {
            if (SectData.CheckPassword(key, password))
            {
                SectData.SetUnlocked(key, remember);
                openSect = key; gateSect = null; gateReturn = null; password = ""; error = "";
            }
            else { error = "That isn't right. Try again."; password = ""; }
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            openSect = gateReturn; gateSect = null; gateReturn = null; password = ""; error = "";
        }
    }

    // ---------- one sect's page ----------
    private void DrawSect(string key)
    {
        var s = SectData.All[key];
        var isCrypt = key == "crypt";
        var rows = Plugin.Data.SectRows;

        if (ImGui.Button("< The Sects")) { openSect = null; return; }
        ImGui.Spacing();

        var wrap = Plugin.Images.Get(ImageCache.SiteImages + s.Image);
        if (wrap != null)
        {
            var size = new Vector2(96f * wrap.Width / wrap.Height, 96f);
            var avail = ImGui.GetContentRegionAvail().X;
            if (avail > size.X) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (avail - size.X) / 2f);
            ImGui.Image(wrap.Handle, size);
        }
        ImGui.TextColored(s.Color, isCrypt ? "Welcome to the Onyx Crypt" : s.Name);

        if (rows == null) { ImGui.TextColored(Dim, "Loading... (press Refresh if this stays)"); return; }

        List<SheetClient.SectRow> Mine(string section) =>
            rows.Where(r => r.Sect == key && r.Section == section).OrderBy(r => r.Order).ToList();
        var members = Mine("member");

        if (!isCrypt)
        {
            ImGui.TextColored(Dim, $"Sect Leader: {s.Leader}   ·   Members: {members.Count}");
        }
        ImGui.Separator();

        if (isCrypt)
        {
            Items("Information", Mine("info"), "Nothing here yet.", s);
            Items("Current Plots", Mine("plot"), "No active plots right now.", s);
        }
        else
        {
            Items("Current Plots", Mine("plot"), "No active plots right now.", s);
            Items("Notice Board", Mine("notice"), "No notices right now.", s);
            Items("Sect Information", Mine("info"), "Nothing here yet.", s);
            Rules(Mine("rules"), s);
            Roster(members, s);
        }

        // The hidden way into the crypt, on the Onyx Blood Guard page only.
        if (key == "onyx")
        {
            ImGui.Spacing(); ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.35f, 0.35f, 0.4f, 1f));
            if (ImGui.SmallButton("Knock twice, bleed once")) TryOpenCrypt();
            ImGui.PopStyleColor();
        }
    }

    private void TryOpenCrypt()
    {
        if (SectData.IsOpenNow("crypt")) openSect = "crypt";
        else Gate("crypt", "onyx");
    }

    private static void Heading(string text, SectInfo s)
    {
        ImGui.Spacing();
        ImGui.TextColored(s.Color, text.ToUpperInvariant());
    }

    private static void Items(string title, List<SheetClient.SectRow> rows, string empty, SectInfo s)
    {
        Heading(title, s);
        if (rows.Count == 0) { ImGui.TextColored(Dim, empty); return; }
        foreach (var r in rows)
        {
            if (r.Title.Length > 0) ImGui.Text(r.Title);
            if (r.Text.Length > 0) ImGui.TextWrapped(r.Text);
            ImGui.Spacing();
        }
    }

    private static void Rules(List<SheetClient.SectRow> rows, SectInfo s)
    {
        Heading("Sect Rules", s);
        if (rows.Count == 0) { ImGui.TextColored(Dim, "No rules posted yet."); return; }
        for (var i = 0; i < rows.Count; i++)
        {
            ImGui.TextColored(s.Color, (i + 1) + ".");
            ImGui.SameLine();
            ImGui.BeginGroup();
            if (rows[i].Title.Length > 0) ImGui.Text(rows[i].Title);
            if (rows[i].Text.Length > 0) ImGui.TextWrapped(rows[i].Text);
            ImGui.EndGroup();
            ImGui.Spacing();
        }
    }

    private static void Roster(List<SheetClient.SectRow> members, SectInfo s)
    {
        Heading("Members", s);
        if (members.Count == 0) { ImGui.TextColored(Dim, "No members listed yet."); return; }
        foreach (var m in members)
        {
            ImGui.Text(m.Title);
            if (m.Text.Length > 0) { ImGui.SameLine(); ImGui.TextColored(Dim, "· " + m.Text); }
        }
    }
}
