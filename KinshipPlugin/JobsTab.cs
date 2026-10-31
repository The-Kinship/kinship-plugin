using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

/// <summary>The Jobs board: quests the SMs have posted. Take one and it moves to your Quests tab, where you play it like any quest.</summary>
public sealed class JobsTab
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.5f, 1f);

    public void Draw()
    {
        Plugin.Quests.JobsRefreshIfDue(60);
        var s = Plugin.Quests.Snapshot();
        var (list, jobsError, loaded) = Plugin.Quests.JobsSnap();

        ImGui.TextColored(Gold, "Jobs");
        ImGui.TextWrapped("Work posted for members. Take a job and it moves to your Quests tab, where you do it for the reward. Check back now and then for new ones.");
        if (ImGui.SmallButton("Refresh##jobs")) _ = Plugin.Quests.JobsAsync();
        if (jobsError.Length > 0) ImGui.TextColored(Red, jobsError);
        else if (s.Error.Length > 0) ImGui.TextColored(Red, s.Error);
        else if (s.Notice.Length > 0) ImGui.TextColored(Green, s.Notice);
        ImGui.Separator();

        if (!loaded && jobsError.Length == 0) { ImGui.TextColored(Dim, "Loading the board..."); return; }
        if (list.Count == 0) { ImGui.TextColored(Dim, "No jobs on the board right now. Check back later."); return; }
        foreach (var j in list)
        {
            ImGui.TextColored(Gold, j.Title);
            ImGui.SameLine(); ImGui.TextColored(Dim, j.FirstCome ? "first come, first served" : "open to everyone");
            if (j.Intro.Length > 0) { ImGui.PushTextWrapPos(ImGui.GetFontSize() * 30f); ImGui.TextUnformatted(j.Intro); ImGui.PopTextWrapPos(); }
            ImGui.TextColored(Dim, j.Steps == 1 ? "1 step" : $"{j.Steps} steps");
            if (s.Busy) ImGui.BeginDisabled();
            if (ImGui.Button("Take job##job" + j.QuestId)) _ = Plugin.Quests.TakeJobAsync(j);
            if (s.Busy) ImGui.EndDisabled();
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        }
    }
}
