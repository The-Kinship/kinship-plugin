using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

/// <summary>The profile card: used by the mouse-over tooltip and by the preview in the Profile tab, so you see exactly what others see.</summary>
public static class ProfileCard
{
    public const float Width = 300f;
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Green = new(0.5f, 0.85f, 0.55f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.45f, 0.4f, 1f);

    public static void Draw(ProfileView p, ProfileView? rosterFallback = null)
    {
        var age = p.Age.Length > 0 ? p.Age : rosterFallback?.Age ?? "";
        var race = p.Race.Length > 0 ? p.Race : rosterFallback?.Race ?? "";
        var gender = p.Gender.Length > 0 ? p.Gender : rosterFallback?.Gender ?? "";
        var startX = ImGui.GetCursorPosX();

        var wrap = p.Portrait.Length > 0 ? Plugin.Images.Get(p.Portrait) : null;
        var hasPortrait = wrap != null;
        if (hasPortrait)
        {
            var scale = Math.Min(110f / wrap!.Width, 150f / wrap.Height);
            ImGui.Image(wrap.Handle, new Vector2(wrap.Width * scale, wrap.Height * scale));
            ImGui.SameLine();
        }
        else if (p.Portrait.Length > 0 && !Plugin.Images.HasFailed(p.Portrait)) { ImGui.TextColored(Dim, "Loading portrait..."); }

        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(startX + Width - (hasPortrait ? 126f : 0f));
        ImGui.TextColored(Gold, p.Name);
        if (age.Length > 0) ImGui.TextUnformatted("Age: " + age);
        if (race.Length > 0) ImGui.TextUnformatted("Race: " + race);
        if (gender.Length > 0) ImGui.TextUnformatted("Gender: " + gender);
        foreach (var f in p.Fields.Where(f => f.Label.Length > 0)) ImGui.TextUnformatted(f.Label + ": " + f.Value);
        ImGui.PopTextWrapPos();
        ImGui.EndGroup();

        ImGui.PushTextWrapPos(startX + Width);
        if (p.Glance.Length > 0)
        {
            ImGui.Separator();
            ImGui.TextColored(Gold, "At first glance");
            ImGui.TextUnformatted(p.Glance);
        }
        var statuses = Plugin.Status.Snapshot().All.Where(s => ProfileState.Same(s.MemberName, p.Name)).OrderByDescending(s => s.IsCombined).ThenBy(s => s.IsDebuff).ToList();
        if (statuses.Count > 0)
        {
            ImGui.Separator();
            foreach (var s in statuses)
            {
                var icon = ItemIcons.Wrap(s.Icon);
                if (icon != null) { ImGui.Image(icon.Handle, new Vector2(18f, 18f)); ImGui.SameLine(); }
                var bonus = s.BonusText();
                ImGui.TextColored(s.IsDebuff ? Red : Green, s.Name + (bonus.Length > 0 ? "  (" + bonus + ")" : ""));
            }
        }
        if (p.Ooc.Length > 0)
        {
            ImGui.Separator();
            ImGui.TextColored(Dim, "OOC");
            ImGui.TextColored(Dim, UiUtil.Ooc(p.Ooc));
        }
        ImGui.PopTextWrapPos();
    }
}
