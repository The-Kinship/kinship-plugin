using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Utility;

namespace KinshipPlugin;

/// <summary>"About": the same Start Here story the website tells, so new members can come back to it.</summary>
public static class AboutTab
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);

    /// <summary>Opens a web page in the player's browser (https only).</summary>
    public static void OpenLink(string url)
    {
        if (url.StartsWith("https://") || url.StartsWith("http://")) Util.OpenLink(url);
    }

    public static string Link(string key)
        => Plugin.Data.Snapshot().Item1 is { } f && f.TryGetValue(key, out var v) ? v.Trim() : "";

    public static void Draw()
    {
        UiUtil.Centered("kin-emblem.jpg", 130f);
        ImGui.TextColored(Gold, "About The Kinship");
        ImGui.TextColored(Dim, "A heavy role-play free company on Crystal \u00b7 Mateus");
        ImGui.Spacing();

        if (ImGui.CollapsingHeader("Who we are", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextColored(Gold, "Quality RP over quantity");
            ImGui.TextWrapped("The Kinship is a heavy RP free company that focuses on giving its members an in-depth, immersive, and inclusive RP environment to thrive in. We value quality RP over quantity of members.");
            ImGui.Spacing();
            ImGui.TextWrapped("Though we follow a very classic \u201cMisfit & Kingdom\u201d style, we try to make sure our RP surpasses the standard, dull, repetitive RP tropes that seem to be everywhere.");
        }

        if (ImGui.CollapsingHeader("What we offer"))
        {
            ImGui.TextColored(Dim, "A story for every kind of roleplayer");
            Item("Company-Wide Story Lines", "Plots that everyone can engage in and participate with.");
            Item("Focused RP Styles", "More specific RP opportunities tailored to a more focused style, like guard-style RP, magic ritual RP, or pure social RP.");
            Item("Member-Driven RP", "Bring your own story lines and plots to the forefront of our company, and engage with plots that help with your own personal character development.");
        }

        if (ImGui.CollapsingHeader("How we're led"))
        {
            Item("The Harbinger", "The FC leader.");
            Item("The Advocates", "Our officers. A limited number.");
            Item("Sect Leaders", "A special group, one for each sect.");
            Item("The Kin", "Standard members. Free to join in any FC-wide plots and enjoy the FC as is.");
            ImGui.Spacing();
            ImGui.TextWrapped("Members enter our FC as Kin. If someone wishes to have a more catered RP experience, we have 4 sects (sections) members can join.");
        }

        if (ImGui.CollapsingHeader("The four sects"))
        {
            UiUtil.Centered("sects-emblem.jpg", 120f);
            ImGui.Spacing();
            foreach (var key in SectData.Order)
            {
                var s = SectData.All[key];
                var wrap = Plugin.Images.Get(ImageCache.SiteImages + s.Image);
                ImGui.BeginGroup();
                if (wrap != null) ImGui.Image(wrap.Handle, new Vector2(64f * wrap.Width / wrap.Height, 64f));
                else ImGui.Dummy(new Vector2(64f, 64f));
                ImGui.EndGroup();
                ImGui.SameLine();
                ImGui.BeginGroup();
                ImGui.TextColored(s.Color, s.Name);
                ImGui.TextColored(Dim, "Sect Leader: " + s.Leader);
                ImGui.TextWrapped(s.Blurb);
                ImGui.EndGroup();
                ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            }
            ImGui.TextWrapped("Each sect plays a vital part in the overall RP of our free company, and each has a special place in our overall RP plots. This helps to ensure everyone is included in some way!");
            ImGui.Spacing();
            ImGui.TextColored(Dim, "Find the full sect pages in the Sects tab.");
        }

        if (ImGui.CollapsingHeader("Example plot"))
        {
            ImGui.TextColored(Dim, "How a plot unfolds, and where each sect comes in.");
            ExamplePlot();
        }

        if (ImGui.CollapsingHeader("Join us"))
        {
            UiUtil.Centered("join-button.jpg", 110f);
            ImGui.Spacing();
            ImGui.TextWrapped("If The Kinship sounds like home, send us a message or head straight to the application.");
            ImGui.Spacing();
            var url = Link("application_url");
            if (url.StartsWith("http"))
            {
                if (ImGui.Button("Apply to join (opens your browser)")) OpenLink(url);
            }
            else ImGui.TextColored(Dim, "The application opens soon. Ask a member of the Ember Court in the meantime.");
        }
    }

    /// <summary>The plot progression graphic. Click it to open it large.</summary>
    public static void ExamplePlot()
    {
        if (UiUtil.FullWidth("plot-example.jpg")) Plugin.ShowImage("plot-example.jpg");
    }

    private static void Item(string title, string text)
    {
        ImGui.Spacing();
        ImGui.TextColored(Gold, title);
        ImGui.TextWrapped(text);
    }
}
