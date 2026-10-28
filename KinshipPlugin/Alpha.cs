using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace KinshipPlugin;

/// <summary>The alpha test phase: a small mark on every window, and the notice shown at login. Switched off by setting alpha_phase to no in the Faction tab of the sheet.</summary>
public static class Alpha
{
    /// <summary>On unless the sheet's alpha_phase row says no (or off, false, 0, ended).</summary>
    public static bool On
    {
        get
        {
            var v = AboutTab.Link("alpha_phase").ToLowerInvariant();
            return !(v == "no" || v == "off" || v == "false" || v == "0" || v == "ended");
        }
    }

    /// <summary>Writes "Alpha test phase" in small text at the bottom right of the window being drawn. Call it inside a window's Draw.</summary>
    public static void Mark()
    {
        if (!On) return;
        const string text = "Alpha test phase";
        var size = ImGui.CalcTextSize(text);
        var pos = ImGui.GetWindowPos(); var win = ImGui.GetWindowSize();
        ImGui.GetWindowDrawList().AddText(new Vector2(pos.X + win.X - size.X - 12f, pos.Y + win.Y - size.Y - 6f), ImGui.GetColorU32(new Vector4(0.95f, 0.65f, 0.3f, 0.75f)), text);
    }
}

/// <summary>Shown at login during the alpha test, before the disclaimer.</summary>
public sealed class AlphaWindow : Window, IDisposable
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Orange = new(0.95f, 0.65f, 0.3f, 1f);
    private bool understood;

    public AlphaWindow() : base("Alpha test##KinshipAlpha", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings)
    {
        Size = new Vector2(500f, 520f); SizeCondition = ImGuiCond.Always;
    }

    public void Dispose() { }
    public override void OnOpen() { understood = false; }
    public override void OnClose() { Plugin.ShowDisclaimerNow(); }   // the third-party plugin disclaimer follows

    public override void PreDraw()
    {
        var vp = ImGuiHelpers.MainViewport;
        Position = new Vector2(vp.Pos.X + vp.Size.X / 2f - 250f, vp.Pos.Y + vp.Size.Y / 2f - 280f);
        PositionCondition = ImGuiCond.Appearing;
    }

    private static void Heading(string text) { ImGui.Spacing(); ImGui.TextColored(Gold, text); }

    public override void Draw()
    {
        ImGui.TextColored(Orange, "Welcome to the Kinship alpha test");
        ImGui.Spacing();
        ImGui.TextWrapped("You have been invited to a small, select group of testers for this plugin. Thank you for taking the time to help.");

        Heading("What this is");
        ImGui.TextWrapped("An early test version. Some things will be rough, will change without notice, or may stop working.");

        Heading("What you are testing");
        ImGui.TextWrapped("The RP tools and utilities: inventory and items, mail, the armory, profiles and mouse-over cards, stashes and signs, statuses and cures, Pick a fight, combat, scenes and quests.");

        Heading("What feedback is wanted");
        ImGui.TextWrapped("Honest impressions. What was confusing? What broke? What did you enjoy? What would you use again? What is missing? Use the ? button at the top of the main window to report a bug or abuse from inside the plugin.");

        Heading("What you can set aside");
        ImGui.TextWrapped("The tabs made for The Kinship free company (Home, About, Roster, Announcements, Plots and Sects) belong to a private build for that FC. They are not part of this test, so please disregard them. You will not have access to the SM tools.");

        Heading("Your data will be wiped");
        ImGui.PushStyleColor(ImGuiCol.Text, Orange);
        ImGui.TextWrapped("When the alpha test ends, the whole system will be wiped. Your characters, items, mail, profiles and everything else will be permanently erased and cannot be recovered. Please do not put major effort into anything you could not afford to lose. If something matters to you, save a copy somewhere else.");
        ImGui.PopStyleColor();

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.Checkbox("I understand I am part of an alpha test and that my data will be wiped", ref understood);
        ImGui.Spacing();
        if (!understood) ImGui.BeginDisabled();
        if (ImGui.Button("Continue##alpha", new Vector2(140f, 0f))) IsOpen = false;
        if (!understood) ImGui.EndDisabled();
        Alpha.Mark();
    }
}

/// <summary>SM Tools switch: while on, any member can put roll bonuses on wearable gear they make (for testing). Creation password only.</summary>
public static class AlphaGearAdmin
{
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.5f, 1f);
    private static string error = "", notice = ""; private static bool busy, known, on; private static int cap, registered; private static string capText = "0";

    private static async System.Threading.Tasks.Task Call(string path, bool? set, int? limit = null)
    {
        if (Plugin.Config.CreatePassword.Length == 0) { error = "Enter the creation password first."; return; }
        if (busy) return;
        busy = true; error = ""; notice = "";
        try
        {
            var body = new System.Collections.Generic.Dictionary<string, object> { ["adminPassword"] = Plugin.Config.CreatePassword };
            if (set != null) body["on"] = set.Value;
            if (limit != null) body["maxCharacters"] = limit.Value;
            var r = await ItemsState.SendAsync(System.Net.Http.HttpMethod.Post, "/system/admin/" + path, body).ConfigureAwait(false);
            if (r.MaxCharacters != null) { cap = r.MaxCharacters.Value; capText = cap.ToString(); }
            if (r.Characters != null) registered = r.Characters.Value;
            if (r.AlphaGear != null) { on = r.AlphaGear.Value; known = true; ItemsState.AlphaGear = on; notice = on ? "Alpha mode is ON: testers can add bonuses to gear and each character gets 50 starting gold, once." : "Alpha mode is OFF: only Advocate gifts can add bonuses."; }
        }
        catch (CombatException ex) { error = ex.Message; }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Alpha switch failed"); error = "Couldn't reach the server. Check your connection and try again."; }
        finally { busy = false; }
    }

    public static void Draw()
    {
        if (!ImGui.CollapsingHeader("Alpha mode (gear bonuses + starting gold)")) return;
        ImGui.PushTextWrapPos(0f);
        ImGui.TextColored(Dim, "While this is on, anyone can give the wearable gear they make a roll bonus (-5 to +5), so testers can try the Armory and Combat. Turning it on also gives every character 50 gold, once (new characters get it when they register while it is on, and it is never given again). Turn it off after the wipe and only Advocate gifts can do that again. It is off until you turn it on, and the wipe does not change it.");
        ImGui.PopTextWrapPos();
        UiUtil.CreatePasswordInput();
        if (ImGui.Button("Check current setting##alphagetbtn")) _ = Call("alpha-get", null);
        if (known)
        {
            var v = on;
            if (ImGui.Checkbox("Alpha mode on##alphagear", ref v)) _ = Call("alpha", v);
            ImGui.Spacing();
            ImGui.TextUnformatted($"Characters registered: {registered}" + (cap > 0 ? $" of {cap} allowed" : " (no limit)"));
            ImGui.SetNextItemWidth(80f); ImGui.InputText("Most characters allowed##alphacap", ref capText, 5, ImGuiInputTextFlags.CharsDecimal);
            ImGui.SameLine();
            if (ImGui.Button("Save limit##alphacapsave") && int.TryParse(capText, out var n)) _ = Call("alpha", null, n);
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(Dim, "While alpha mode is on, nobody can register once this many characters exist. Set it to your number of testers plus a spare or two. 0 means no limit. Remove a character in the Members tools to free a place.");
            ImGui.PopTextWrapPos();
        }
        if (error.Length > 0) ImGui.TextColored(Red, error);
        if (notice.Length > 0) ImGui.TextColored(Green, notice);
    }
}
