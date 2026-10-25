using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace KinshipPlugin;

/// <summary>A resizable window that shows one website image large (used for the plot example).</summary>
public sealed class ImageViewerWindow : Window, IDisposable
{
    private string file = "";

    public ImageViewerWindow() : base("The Kinship  -  image##KinshipViewer", ImGuiWindowFlags.NoScrollbar)
    {
        Size = new Vector2(960, 580);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Show(string imageFile) { file = imageFile; IsOpen = true; }
    public void Dispose() { }

    public override void Draw()
    {
        var wrap = Plugin.Images.Get(UiUtil.Img(file));
        if (wrap == null) { ImGui.Text("Loading image..."); return; }
        var avail = ImGui.GetContentRegionAvail();
        var scale = Math.Min(avail.X / wrap.Width, avail.Y / wrap.Height);
        var size = new Vector2(wrap.Width * scale, wrap.Height * scale);
        ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(Math.Max(0, (avail.X - size.X) / 2f), Math.Max(0, (avail.Y - size.Y) / 2f)));
        ImGui.Image(wrap.Handle, size);
    }
}
