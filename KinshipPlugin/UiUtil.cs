using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

public static class UiUtil
{
    public static string Img(string file) => ImageCache.SiteImages + file;

    /// <summary>Draws a website image centered, no taller than maxHeight. Shows a small note while it loads.</summary>
    public static void Centered(string file, float maxHeight)
    {
        var wrap = Plugin.Images.Get(Img(file));
        if (wrap == null) { if (!Plugin.Images.HasFailed(Img(file))) ImGui.TextColored(new Vector4(0.65f, 0.65f, 0.7f, 1f), "Loading image..."); return; }
        var avail = ImGui.GetContentRegionAvail().X;
        var scale = System.Math.Min(maxHeight / wrap.Height, avail / wrap.Width);
        var size = new Vector2(wrap.Width * scale, wrap.Height * scale);
        if (avail > size.X) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (avail - size.X) / 2f);
        ImGui.Image(wrap.Handle, size);
    }

    /// <summary>Draws a website image at the full width of the window. Click it to open it larger. Returns true if clicked.</summary>
    public static bool FullWidth(string file)
    {
        var wrap = Plugin.Images.Get(Img(file));
        if (wrap == null) { if (!Plugin.Images.HasFailed(Img(file))) ImGui.TextColored(new Vector4(0.65f, 0.65f, 0.7f, 1f), "Loading image..."); return false; }
        var w = ImGui.GetContentRegionAvail().X;
        ImGui.Image(wrap.Handle, new Vector2(w, w * wrap.Height / wrap.Width));
        var hover = ImGui.IsItemHovered();
        if (hover) { ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); ImGui.SetTooltip("Click to enlarge"); }
        return hover && ImGui.IsMouseReleased(ImGuiMouseButton.Left);
    }

    /// <summary>The creation-password box SMs fill in once before creating a room or scene.</summary>
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
