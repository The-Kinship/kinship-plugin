using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

public static class UiUtil
{
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
