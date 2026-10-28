using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

/// <summary>Notices when you are standing still and not touching anything, so the plugin can stop asking the server for updates until you are back.</summary>
public static class Idle
{
    private const double SleepAfterSeconds = 180;   // 3 minutes with no movement, turning, or mouse use
    private static DateTime lastActive = DateTime.UtcNow;
    private static Vector3 lastPos;
    private static float lastRot;
    private static Vector2 lastMouse;
    private static bool haveLast;
    private static volatile bool asleep;

    /// <summary>True while you have been idle long enough that background requests are paused.</summary>
    public static bool Asleep => asleep || Paused;

    /// <summary>Set by the Pause / Play button. While true the plugin makes no background requests at all. Starts as active each time the game starts.</summary>
    public static volatile bool Paused;

    /// <summary>Called every frame. Cheap: only reads local game and input state, never the network.</summary>
    public static void Update()
    {
        try
        {
            var active = false;
            var me = Plugin.ObjectTable.LocalPlayer;
            if (me != null)
            {
                var p = me.Position; var r = me.Rotation;
                if (haveLast && (Vector3.DistanceSquared(p, lastPos) > 0.0025f || Math.Abs(r - lastRot) > 0.02f)) active = true;
                lastPos = p; lastRot = r;
            }
            var m = ImGui.GetMousePos();
            if (haveLast && (m != lastMouse || ImGui.IsMouseDown(ImGuiMouseButton.Left) || ImGui.IsMouseDown(ImGuiMouseButton.Right))) active = true;
            lastMouse = m;
            haveLast = true;
            if (active) lastActive = DateTime.UtcNow;
            asleep = (DateTime.UtcNow - lastActive).TotalSeconds > SleepAfterSeconds;
        }
        catch (Exception) { asleep = false; }   // if anything is odd, stay awake rather than risk missing something
    }
}
