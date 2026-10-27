using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

/// <summary>Write your character's profile: what other players see when they mouse over you.</summary>
public sealed class ProfileTab
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.5f, 1f);
    private const int MaxFields = 6;

    private ulong loadedFor = ulong.MaxValue;
    private bool formReady, requested;
    private string displayName = "", age = "", race = "", gender = "", portrait = "", glance = "", ooc = "";
    private readonly List<ProfileField> fields = new();

    public void Draw()
    {
        if (loadedFor != Plugin.CharacterId) { loadedFor = Plugin.CharacterId; formReady = false; requested = false; }
        var s = Plugin.Profiles.Snapshot();
        if (!requested) { requested = true; _ = Plugin.Profiles.LoadMineAsync(); Plugin.Profiles.RefreshIfDue(0); }
        if (!formReady)
        {
            if (s.Mine == null) { ImGui.TextColored(Dim, s.Error.Length > 0 ? s.Error : "Loading your profile..."); if (s.Error.Length > 0 && ImGui.Button("Try again##prof")) requested = false; return; }
            Load(s.Mine); formReady = true;
        }

        ImGui.TextColored(Gold, "Your profile");
        ImGui.TextWrapped("This is what other players with the plugin see when they mouse over your character. Leave anything blank to hide it.");
        var tips = Plugin.Config.ProfileTips;
        if (ImGui.Checkbox("Show profiles when I mouse over a character", ref tips)) { Plugin.Config.ProfileTips = tips; Plugin.Config.Save(); }
        var tgt = Plugin.Config.ProfileTipsTarget;
        if (ImGui.Checkbox("Also show the profile of my current target", ref tgt)) { Plugin.Config.ProfileTipsTarget = tgt; Plugin.Config.Save(); }
        var anch = Plugin.Config.ProfileTipAnchored;
        if (ImGui.Checkbox("Keep the card in one place instead of following the mouse", ref anch)) { Plugin.Config.ProfileTipAnchored = anch; Plugin.Config.Save(); if (!anch) ProfileTooltip.Unlocked = false; }
        if (anch)
        {
            ImGui.Indent();
            if (!ProfileTooltip.Unlocked) { if (ImGui.Button("Move the card##pmove")) ProfileTooltip.Unlocked = true; }
            else { if (ImGui.Button("Lock it here##plock")) { ProfileTooltip.Unlocked = false; Plugin.Config.Save(); } ImGui.SameLine(); ImGui.TextColored(Dim, "Drag the card on your screen, then lock it."); }
            ImGui.Unindent();
        }
        ImGui.Spacing(); ImGui.Separator();

        var w = ImGui.GetContentRegionAvail().X - 8f;
        ImGui.Text("Name shown on your card"); ImGui.SetNextItemWidth(260f); ImGui.InputText("##pname", ref displayName, 40);
        ImGui.TextColored(Dim, "Leave empty to use " + (s.Mine?.Name ?? "your registered name") + ". Use your full name here if the game only shows your first name.");
        ImGui.Text("Age"); ImGui.SetNextItemWidth(160f); ImGui.InputText("##page", ref age, 30);
        ImGui.Text("Race"); ImGui.SetNextItemWidth(220f); ImGui.InputText("##prace", ref race, 40);
        ImGui.Text("Gender"); ImGui.SetNextItemWidth(160f); ImGui.InputText("##pgender", ref gender, 30);
        if (ImGui.SmallButton("Fill empty boxes from the roster##proster"))
        {
            var r = Plugin.Data.Roster?.FirstOrDefault(x => ProfileState.Same(x.Name, s.Mine?.Name ?? Plugin.CharacterName()));
            if (r == null) { }
            else { if (age.Length == 0) age = r.Age; if (race.Length == 0) race = r.Race; if (gender.Length == 0) gender = r.Gender; }
        }
        ImGui.SameLine(); ImGui.TextColored(Dim, "If you leave these empty, the roster values are shown.");

        ImGui.Spacing();
        ImGui.Text("Portrait link"); ImGui.SetNextItemWidth(w); ImGui.InputText("##pportrait", ref portrait, 300);
        ImGui.PushTextWrapPos(0f);
        ImGui.TextColored(Dim, "Paste a direct link to an image (https, usually ending in .png or .jpg), for example from Imgur or Discord. Everyone who looks at you loads the picture from that link, so only use an image you are happy to share, and keep it suitable for the Free Company.");
        ImGui.PopTextWrapPos();

        ImGui.Spacing();
        ImGui.Text("At first glance"); ImGui.InputTextMultiline("##pglance", ref glance, 500, new Vector2(w, 72f));
        ImGui.Text("OOC"); ImGui.InputTextMultiline("##pooc", ref ooc, 500, new Vector2(w, 56f));
        ImGui.TextColored(Dim, "Shown in (( )) at the bottom. Good for how you like to RP, your pronouns, or when you are around.");

        ImGui.Spacing();
        ImGui.Text($"Your own fields ({fields.Count}/{MaxFields})");
        var remove = -1;
        for (var i = 0; i < fields.Count; i++)
        {
            var lab = fields[i].Label; var val = fields[i].Value;
            ImGui.SetNextItemWidth(110f); if (ImGui.InputText($"##plabel{i}", ref lab, 24)) fields[i].Label = lab;
            ImGui.SameLine(); ImGui.SetNextItemWidth(Math.Max(120f, w - 190f)); if (ImGui.InputText($"##pvalue{i}", ref val, 80)) fields[i].Value = val;
            ImGui.SameLine(); if (ImGui.SmallButton($"X##pdel{i}")) remove = i;
        }
        if (remove >= 0) fields.RemoveAt(remove);
        if (fields.Count < MaxFields && ImGui.SmallButton("Add a field##padd")) fields.Add(new ProfileField());
        ImGui.TextColored(Dim, "Left box is the title (for example Voice), right box is what it says.");

        ImGui.Spacing();
        if (s.Busy) ImGui.BeginDisabled();
        if (ImGui.Button("Save profile##psave", new Vector2(150f, 0f))) _ = Plugin.Profiles.SaveAsync(Build());
        if (s.Busy) ImGui.EndDisabled();
        if (s.Error.Length > 0) ImGui.TextColored(Red, s.Error);
        if (s.Notice.Length > 0) ImGui.TextColored(Green, s.Notice);

        ImGui.Spacing(); ImGui.Separator();
        ImGui.TextColored(Gold, "Preview (what others see)");
        ImGui.Spacing();
        var preview = Build();
        preview.Name = s.Mine?.Name ?? Plugin.CharacterName();
        var rr = Plugin.Data.Roster?.FirstOrDefault(x => ProfileState.Same(x.Name, preview.Name));
        ProfileCard.Draw(preview, rr == null ? null : new ProfileView { Age = rr.Age, Race = rr.Race, Gender = rr.Gender });
    }

    private void Load(ProfileView p)
    {
        displayName = p.DisplayName; age = p.Age; race = p.Race; gender = p.Gender; portrait = p.Portrait; glance = p.Glance; ooc = p.Ooc;
        fields.Clear(); foreach (var f in p.Fields) fields.Add(new ProfileField { Label = f.Label, Value = f.Value });
    }

    private ProfileView Build() => new()
    {
        DisplayName = displayName, Age = age, Race = race, Gender = gender, Portrait = portrait, Glance = glance, Ooc = ooc,
        Fields = fields.Select(f => new ProfileField { Label = f.Label, Value = f.Value }).ToList(),
    };
}

/// <summary>SM Tools: remove an inappropriate portrait link from a profile. Uses the creation password.</summary>
public static class ProfileAdmin
{
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.5f, 1f);
    private static string name = "";

    public static void Draw()
    {
        if (!ImGui.CollapsingHeader("Profile pictures")) return;
        ImGui.TextWrapped("Removes a character's portrait link if the picture is not appropriate. They can set a new one afterwards. Uses the creation password.");
        UiUtil.CreatePasswordInput();
        ImGui.Text("Character name"); ImGui.SetNextItemWidth(240f); ImGui.InputText("##clrname", ref name, 60);
        if (name.Trim().Length == 0) ImGui.BeginDisabled();
        if (ImGui.Button("Clear their portrait##clrbtn")) _ = Plugin.Profiles.AdminClearPortraitAsync(name);
        if (name.Trim().Length == 0) ImGui.EndDisabled();
        var m = Plugin.Profiles.AdminMessages();
        if (m.Error.Length > 0) ImGui.TextColored(Red, m.Error);
        else if (m.Notice.Length > 0) ImGui.TextColored(Green, m.Notice);
    }
}
