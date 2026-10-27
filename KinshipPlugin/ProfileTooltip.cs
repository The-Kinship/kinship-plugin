using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Windowing;

namespace KinshipPlugin;

/// <summary>A small card that follows the mouse while you point at a registered character in the world.</summary>
public sealed class ProfileTooltip : Window, IDisposable
{
    private ProfileView? current;
    private Vector2 lastSize = new(320f, 200f);

    public ProfileTooltip() : base("Profile##KinshipProfileTip",
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse |
        ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus |
        ImGuiWindowFlags.NoNav | ImGuiWindowFlags.AlwaysAutoResize)
    {
        RespectCloseHotkey = false;
        ShowCloseButton = false;
        IsOpen = true;
        BgAlpha = 0.92f;
    }

    public void Dispose() { }

    private static ProfileView? Pick()
    {
        if (!Plugin.Config.ProfileTips || Plugin.Config.Me().ItemsToken.Length == 0) return null;
        try
        {
            if (ImGui.GetIO().WantCaptureMouse) return null;   // the mouse is over a window, not the world
            IGameObject? o = Plugin.TargetManager.MouseOverTarget;
            if (o == null && Plugin.Config.ProfileTipsTarget) o = Plugin.TargetManager.Target;
            if (o is not IPlayerCharacter pc) return null;
            Plugin.Profiles.RefreshIfDue(180);
            Plugin.Status.RefreshIfDue(30);
            return Plugin.Profiles.Find(pc.Name.ToString());
        }
        catch (Exception) { return null; }
    }

    public override bool DrawConditions() { current = Pick(); return current != null; }

    public override void PreDraw()
    {
        var mouse = ImGui.GetMousePos();
        var vp = Dalamud.Interface.Utility.ImGuiHelpers.MainViewport;
        var pos = new Vector2(mouse.X + 20f, mouse.Y + 20f);
        if (pos.X + lastSize.X > vp.Pos.X + vp.Size.X) pos.X = mouse.X - lastSize.X - 20f;
        if (pos.Y + lastSize.Y > vp.Pos.Y + vp.Size.Y) pos.Y = mouse.Y - lastSize.Y - 20f;
        Position = pos; PositionCondition = ImGuiCond.Always;
    }

    public override void Draw()
    {
        if (current == null) return;
        var roster = Plugin.Data.Roster?.FirstOrDefault(r => ProfileState.Same(r.Name, current.Name));
        ProfileCard.Draw(current, roster == null ? null : new ProfileView { Age = roster.Age, Race = roster.Race, Gender = roster.Gender });
        lastSize = ImGui.GetWindowSize();
    }
}
