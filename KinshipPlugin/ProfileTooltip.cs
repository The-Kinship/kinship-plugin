using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Windowing;

namespace KinshipPlugin;

/// <summary>A small card that follows the mouse while you point at a registered character in the world.</summary>
public sealed class ProfileTooltip : Window, IDisposable
{
    private ProfileView? current;
    private Vector2 lastSize = new(320f, 200f);

    /// <summary>True while the box is unlocked so you can drag it to where you want anchored profiles to appear.</summary>
    internal static bool Unlocked;
    private static readonly ImGuiWindowFlags Basic =
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse |
        ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus |
        ImGuiWindowFlags.NoNav | ImGuiWindowFlags.AlwaysAutoResize;
    private static readonly ImGuiWindowFlags Fixed = Basic | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoMove;

    public ProfileTooltip() : base("Profile##KinshipProfileTip", Fixed)
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
            if (o == null && OverSelf()) o = Plugin.ObjectTable.LocalPlayer;   // the game does not report the mouse over your own character, so work it out from where you stand on screen
            if (o is not IPlayerCharacter pc) return null;
            Plugin.Profiles.RefreshIfDue(180);
            Plugin.Status.RefreshIfDue(30);
            return Plugin.Profiles.Find(pc.Name.ToString());
        }
        catch (Exception) { return null; }
    }

    /// <summary>True when the mouse is over your own character's body on screen (read-only: it only projects your position to the screen).</summary>
    private static bool OverSelf()
    {
        var me = Plugin.ObjectTable.LocalPlayer;
        if (me == null) return false;
        var p = me.Position;
        if (!Plugin.GameGui.WorldToScreen(p, out var feet) || !Plugin.GameGui.WorldToScreen(p + new Vector3(0f, 1.8f, 0f), out var head)) return false;
        var h = Math.Abs(feet.Y - head.Y);
        if (h < 8f) return false;
        var m = ImGui.GetMousePos();
        return Math.Abs(m.X - feet.X) <= h * 0.28f && m.Y >= Math.Min(head.Y, feet.Y) && m.Y <= Math.Max(head.Y, feet.Y);
    }

    public override bool DrawConditions()
    {
        if (Unlocked && Plugin.Config.Me().ItemsToken.Length > 0)
        {
            current = Plugin.Profiles.Snapshot().Mine ?? new ProfileView { Name = "Your character", Glance = "This is where profiles will appear." };
            return true;
        }
        Unlocked = false;
        current = Pick();
        return current != null;
    }

    public override void PreDraw()
    {
        var vp = Dalamud.Interface.Utility.ImGuiHelpers.MainViewport;
        Flags = Unlocked ? Basic : Fixed;
        if (Unlocked || Plugin.Config.ProfileTipAnchored)
        {
            if (Plugin.Config.ProfileTipX == 0f && Plugin.Config.ProfileTipY == 0f)
            {
                Plugin.Config.ProfileTipX = vp.Pos.X + vp.Size.X - 340f; Plugin.Config.ProfileTipY = vp.Pos.Y + vp.Size.Y * 0.3f;
            }
            Position = new Vector2(Plugin.Config.ProfileTipX, Plugin.Config.ProfileTipY);
            PositionCondition = Unlocked ? ImGuiCond.Appearing : ImGuiCond.Always;
            return;
        }
        var mouse = ImGui.GetMousePos();
        var pos = new Vector2(mouse.X + 20f, mouse.Y + 20f);
        if (pos.X + lastSize.X > vp.Pos.X + vp.Size.X) pos.X = mouse.X - lastSize.X - 20f;
        if (pos.Y + lastSize.Y > vp.Pos.Y + vp.Size.Y) pos.Y = mouse.Y - lastSize.Y - 20f;
        Position = pos; PositionCondition = ImGuiCond.Always;
    }

    public override void Draw()
    {
        if (current == null) return;
        if (Unlocked)
        {
            var wp = ImGui.GetWindowPos(); Plugin.Config.ProfileTipX = wp.X; Plugin.Config.ProfileTipY = wp.Y;
            ImGui.TextColored(new Vector4(0.92f, 0.78f, 0.35f, 1f), "Drag me where you want it, then lock it in the Profile tab.");
            ImGui.Separator();
        }
        var roster = Plugin.Data.Roster?.FirstOrDefault(r => ProfileState.Same(r.Name, current.Name));
        ProfileCard.Draw(current, roster == null ? null : new ProfileView { Age = roster.Age, Race = roster.Race, Gender = roster.Gender });
        lastSize = ImGui.GetWindowSize();
    }
}
