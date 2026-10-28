using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

/// <summary>Diagnosing a sickness and reading for a curse. Used on the Life Watch and Onyx Blood Guard pages, and (during the alpha) on the Status tab so testers can reach it too.</summary>
public static class Readings
{
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);

    // What a failed reading says. One is picked at random each time, so repeat tries don't read the same. They never say whether anything is really there.
    private static readonly string[] MissCommon =
    {
        "Honestly, you don't know.",
        "There doesn't seem to be anything wrong.",
        "You can't seem to find anything amiss.",
        "It's hard to tell what it is. Just use your best judgement.",
        "The signs are too faint to read. You can't say for certain.",
        "You look them over twice and still come up empty.",
    };
    private static readonly string[] MissLife = MissCommon.Concat(new[] { "Nothing you can point to. Maybe it's nothing, maybe you're missing it." }).ToArray();
    private static readonly string[] MissCurse = MissCommon.Concat(new[] { "You can't make anything of it. If there's a curse here, it's hiding well." }).ToArray();

    private static string patient = "", miss = "", lastKey = "";
    private static DiagnoseResult? shown;
    private static DateTime membersLoad = DateTime.MinValue;

    /// <summary>key is "life" (sickness) or "onyx" (curse). The caller draws its own heading.</summary>
    public static void Draw(string key, Vector4 color)
    {
        var kind = key == "life" ? "sickness" : "curse";
        var inv = Plugin.Items.Snapshot();
        if (lastKey == key)
        {
            var fresh = Plugin.Status.TakeDiagnosis();
            if (fresh != null)
            {
                shown = fresh;
                var pool = key == "life" ? MissLife : MissCurse;
                miss = pool[Random.Shared.Next(pool.Length)];   // picked once so it does not change while the window is open
                ImGui.OpenPopup("Diagnosis##dx" + key);
            }
        }

        if (!inv.SignedIn) ImGui.TextColored(Dim, "Sign in on the RP tab to use this.");
        else
        {
            if (inv.Members.Count == 0 && (DateTime.UtcNow - membersLoad).TotalSeconds > 30) { membersLoad = DateTime.UtcNow; _ = Plugin.Items.LoadMembersAsync(); }
            ImGui.TextColored(Dim, key == "life" ? "Pick a patient and try to work out what ails them." : "Pick someone and try to work out what curse they carry.");
            var names = inv.Members.Append(inv.Name).Where(m => m.Length > 0).Distinct().OrderBy(m => m).ToList();
            ImGui.SetNextItemWidth(200f);
            if (ImGui.BeginCombo("Patient##dxp" + key, patient.Length > 0 ? patient : "Choose a name"))
            {
                foreach (var m in names) if (ImGui.Selectable(m, m == patient)) patient = m;
                ImGui.EndCombo();
            }
            var ok = patient.Length > 0 && !Plugin.Status.IsBusy();
            if (!ok) ImGui.BeginDisabled();
            if (ImGui.Button((key == "life" ? "Diagnose" : "Read") + "##dx" + key)) { lastKey = key; _ = Plugin.Status.DiagnoseAsync(patient, kind); }
            if (!ok) ImGui.EndDisabled();
        }

        if (ImGui.BeginPopup("Diagnosis##dx" + key))
        {
            if (shown != null)
            {
                if (shown.Error.Length > 0) ImGui.TextColored(Red, shown.Error);
                else
                {
                    ImGui.TextColored(Dim, $"{shown.Patient}  ·  you rolled {shown.Roll} (d20)");
                    ImGui.Spacing();
                    ImGui.PushTextWrapPos(ImGui.GetFontSize() * 26f);
                    if (shown.Success)
                    {
                        ImGui.TextColored(color, "Condition: " + shown.Condition);
                        ImGui.TextUnformatted(shown.Cure.Length > 0 ? "Cure: " + shown.Cure : "There is no cure you know of.");
                    }
                    else ImGui.TextUnformatted(miss);
                    ImGui.PopTextWrapPos();
                }
                ImGui.Spacing();
                if (ImGui.Button("Close##dxc" + key)) ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }
}
