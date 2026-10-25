using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

public static class UiUtil
{
    /// <summary>The creation-password box DMs fill in once before creating a room or scene.</summary>
    public static void CreatePasswordInput()
    {
        var pw = Plugin.Config.CreatePassword;
        ImGui.SetNextItemWidth(240f);
        if (ImGui.InputText("Creation password", ref pw, 100, ImGuiInputTextFlags.Password)) { Plugin.Config.CreatePassword = pw; Plugin.Config.Save(); }
        ImGui.TextColored(new Vector4(0.65f, 0.65f, 0.7f, 1f), "Only needed to start things. Ask your FC leader.");
    }

    /// <summary>Wraps an OOC note in (( )) unless it already is.</summary>
    public static string Ooc(string note)
    {
        var t = note.Trim();
        return t.StartsWith("((") ? t : $"(({t}))";
    }

    /// <summary>Draws wrapped text twice, one pixel apart, which reads as bold.</summary>
    public static void BoldWrapped(string text, Vector4 color, float wrapX = 0f)
    {
        var start = ImGui.GetCursorPos();
        Draw(text, color, wrapX);
        var end = ImGui.GetCursorPos();
        ImGui.SetCursorPos(new Vector2(start.X + 1f, start.Y));
        Draw(text, color, wrapX);
        ImGui.SetCursorPos(end);
    }

    private static void Draw(string text, Vector4 color, float wrapX)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        if (wrapX > 0f) { ImGui.PushTextWrapPos(wrapX); ImGui.TextUnformatted(text); ImGui.PopTextWrapPos(); }
        else ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }
}
