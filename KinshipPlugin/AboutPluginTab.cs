using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

/// <summary>"About Plugin": why it exists, links, and a way to report a bug or abuse to the SMs.</summary>
public sealed class AboutPluginTab
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.5f, 1f);
    private const string DefaultSite = "https://the-kinship.github.io/kinship/";

    private bool abuse;
    private string about = "", text = "";

    public void Draw()
    {
        ImGui.TextColored(Gold, "About this plugin");
        ImGui.Spacing();
        ImGui.TextWrapped("The Kinship plugin is a passion project. It was started for our Free Company to make our RP more immersive. The community didn't have one with all the features I wanted, so I made one!");
        ImGui.Spacing();
        ImGui.TextWrapped("It's my first plugin, and I hope everyone enjoys it.");
        ImGui.Spacing();
        ImGui.TextWrapped("What it does: it only reads game information (like where you are standing) and keeps everything else in its own RP system. It never plays the game for you and never changes anything in the game.");

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        var tutorial = AboutTab.Link("tutorial_url");
        if (tutorial.StartsWith("http")) { if (ImGui.Button("Tutorial (opens your browser)##aptut")) AboutTab.OpenLink(tutorial); }
        else { ImGui.BeginDisabled(); ImGui.Button("Tutorial (coming soon)##aptut"); ImGui.EndDisabled(); }
        ImGui.SameLine();
        var site = AboutTab.Link("website_url");
        if (!site.StartsWith("http")) site = DefaultSite;
        if (ImGui.Button("The Kinship website##apsite")) AboutTab.OpenLink(site);

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, "Report a bug or abuse");
        ImGui.TextWrapped("Something not working, or someone misusing the system? Tell the SMs here. Your character name is sent with it so we can follow up.");
        ImGui.Spacing();
        if (ImGui.RadioButton("A bug##apk1", !abuse)) abuse = false;
        ImGui.SameLine();
        if (ImGui.RadioButton("Abuse##apk2", abuse)) abuse = true;
        if (abuse) { ImGui.Text("Who is it about?"); ImGui.SetNextItemWidth(240f); ImGui.InputText("##apabout", ref about, 40); }
        ImGui.Text(abuse ? "What happened?" : "What did not work, and what were you doing?");
        ImGui.InputTextMultiline("##aptext", ref text, 500, new Vector2(ImGui.GetContentRegionAvail().X - 8f, 90f));
        ImGui.TextColored(Dim, $"{text.Length}/500");

        var s = ReportState.I.Snapshot();
        var signedIn = Plugin.Config.Me().ItemsToken.Length > 0;
        if (s.Busy || !signedIn || text.Trim().Length == 0) ImGui.BeginDisabled();
        if (ImGui.Button("Send to the SMs##apsend", new Vector2(160f, 0f)))
        {
            var k = abuse ? "abuse" : "bug"; var a = abuse ? about : ""; var t = text;
            _ = Task_Send(k, a, t);
        }
        if (s.Busy || !signedIn || text.Trim().Length == 0) ImGui.EndDisabled();
        if (!signedIn) ImGui.TextColored(Dim, "Sign in on the RP tab first so the SMs know who sent it.");
        if (s.Error.Length > 0) ImGui.TextColored(Red, s.Error);
        if (s.Notice.Length > 0) ImGui.TextColored(Green, s.Notice);

        ImGui.Spacing(); ImGui.Separator();
        var v = typeof(Plugin).Assembly.GetName().Version;
        ImGui.TextColored(Dim, "Version " + (v == null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}"));
    }

    private async System.Threading.Tasks.Task Task_Send(string kind, string who, string body)
    {
        if (await ReportState.I.SendAsync(kind, who, body).ConfigureAwait(false)) { text = ""; about = ""; }
    }
}

/// <summary>SM Tools: read, tick off and delete reports. Uses the creation password.</summary>
public static class ReportsAdmin
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.5f, 1f);

    public static void Draw()
    {
        if (!ImGui.CollapsingHeader("Reports from members")) return;
        ImGui.TextWrapped("Bugs and abuse reports sent from the About Plugin tab. Uses the creation password.");
        UiUtil.CreatePasswordInput();
        var a = ReportState.I.Admin();
        if (a.Error.Length > 0) ImGui.TextColored(Red, a.Error);
        else if (a.Notice.Length > 0) ImGui.TextColored(Green, a.Notice);
        if (a.Busy) ImGui.TextColored(Dim, "Working...");
        else if (ImGui.Button((a.Loaded ? "Reload reports" : "Load reports") + "##repload")) _ = ReportState.I.AdminLoadAsync();
        if (!a.Loaded) return;
        ImGui.Spacing();
        ImGui.TextColored(Gold, $"Reports ({a.List.Count})");
        if (a.List.Count == 0) ImGui.TextColored(Dim, "Nothing reported.");
        foreach (var r in a.List)
        {
            ImGui.PushID((int)r.Id);
            var when = DateTime.TryParse(r.CreatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d.ToLocalTime().ToString("MMM d, h:mm tt") : r.CreatedAt;
            ImGui.TextColored(r.Done ? Dim : (r.Kind == "abuse" ? Red : Gold), (r.Kind == "abuse" ? "[Abuse] " : "[Bug] ") + r.Member + (r.About.Length > 0 ? " about " + r.About : "") + (r.Done ? "  (done)" : ""));
            ImGui.TextColored(Dim, when);
            ImGui.PushTextWrapPos(0f); ImGui.TextUnformatted(r.Text); ImGui.PopTextWrapPos();
            if (ImGui.SmallButton(r.Done ? "Reopen" : "Mark done")) _ = ReportState.I.AdminDoneAsync(r.Id);
            ImGui.SameLine();
            if (ImGui.SmallButton("Delete")) _ = ReportState.I.AdminDeleteAsync(r.Id);
            ImGui.PopID();
            ImGui.Separator();
        }
    }
}
