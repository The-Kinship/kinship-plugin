using System;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace KinshipPlugin;

/// <summary>
/// A small "Kinship" entry in the game's server info bar at the top of the screen.
/// Left click opens or closes the main window. Right click opens a little menu of options.
/// The bar's text and click types change between Dalamud versions, so those two are set up by reflection and fail quietly.
/// </summary>
public sealed class TopBar : IDisposable
{
    private IDtrBarEntry? entry;
    private volatile bool menuRequested;
    private readonly TopBarMenu menu;

    public TopBar(TopBarMenu menu)
    {
        this.menu = menu;
        try
        {
            entry = Plugin.DtrBar.Get("Kinship");
            SetText("Text", "Kinship");
            SetText("Tooltip", "The Kinship: click to open, right click for options");
            Hook();
            entry.Shown = Plugin.Config.TopBarButton;
        }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Could not add the top bar button."); }
    }

    /// <summary>Called every frame: opens the menu (at the mouse) after a right click, and shows or hides the button.</summary>
    private string lastText = "Kinship";
    public void Update()
    {
        var want = (Alpha.On ? "Kinship (Alpha)" : "Kinship") + (Idle.Paused ? " (Paused)" : "");
        if (entry != null && want != lastText) { lastText = want; SetText("Text", want); }
        if (entry != null && entry.Shown != Plugin.Config.TopBarButton) entry.Shown = Plugin.Config.TopBarButton;
        if (menuRequested) { menuRequested = false; menu.OpenAtMouse(); }
    }

    private static object? ToValue(Type type, string text)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t == typeof(string)) return text;
        try { return Expression.Lambda(Expression.Convert(Expression.Constant(text), t)).Compile().DynamicInvoke(); } catch (Exception) { /* try the next way */ }
        try { return Activator.CreateInstance(t, new object[] { text }); } catch (Exception) { return null; }
    }

    private void SetText(string property, string text)
    {
        try
        {
            var p = entry!.GetType().GetProperty(property);
            if (p == null) return;
            var v = ToValue(p.PropertyType, text);
            if (v != null) p.SetValue(entry, v);
        }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Top bar " + property + " not set."); }
    }

    private void Hook()
    {
        try
        {
            var p = entry!.GetType().GetProperty("OnClick");
            if (p == null) return;
            var delegateType = p.PropertyType;
            var parameters = delegateType.GetMethod("Invoke")!.GetParameters().Select(x => Expression.Parameter(x.ParameterType)).ToArray();
            Expression arg = parameters.Length > 0 ? Expression.Convert(parameters[0], typeof(object)) : Expression.Constant(null, typeof(object));
            var call = Expression.Call(Expression.Constant(this), typeof(TopBar).GetMethod(nameof(Clicked))!, arg);
            p.SetValue(entry, Expression.Lambda(delegateType, call, parameters).Compile());
        }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Top bar click not hooked."); }
    }

    public void Clicked(object? evt)
    {
        var kind = evt?.GetType().GetProperty("ClickType")?.GetValue(evt)?.ToString() ?? "Left";
        if (kind.Contains("Right", StringComparison.OrdinalIgnoreCase)) menuRequested = true;
        else Plugin.ToggleMainWindow();
    }

    public void Dispose()
    {
        try { entry?.Remove(); } catch (Exception) { /* already gone */ }
    }
}

/// <summary>The right-click menu for the top bar button. Appears at the mouse and closes when you click anywhere else.</summary>
public sealed class TopBarMenu : Window, IDisposable
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private int frames;
    private Vector2 at;

    public TopBarMenu() : base("Kinship menu##KinshipTopBarMenu",
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar)
    {
        RespectCloseHotkey = true;
        ShowCloseButton = false;
        IsOpen = false;
    }

    public void Dispose() { }

    public void OpenAtMouse()
    {
        var vp = ImGuiHelpers.MainViewport;
        var m = ImGui.GetMousePos();
        at = new Vector2(Math.Min(m.X, vp.Pos.X + vp.Size.X - 250f), m.Y + 6f);
        frames = 0; IsOpen = true;
    }

    public override void PreDraw()
    {
        if (frames == 0) { Position = at; PositionCondition = ImGuiCond.Always; }
        else { Position = null; }
    }

    private static void Toggle(string label, bool value, Action<bool> set)
    {
        var v = value;
        if (ImGui.Checkbox(label, ref v)) { set(v); Plugin.Config.Save(); }
    }

    public override void Draw()
    {
        frames++;
        if (frames > 2 && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.IsWindowHovered()) { IsOpen = false; return; }
        ImGui.TextColored(Gold, "The Kinship");
        ImGui.Separator();
        if (ImGui.Selectable("Open the Kinship window")) { Plugin.ShowMain(); IsOpen = false; return; }
        if (ImGui.Selectable(Idle.Paused ? "Turn the plugin back on (paused)" : "Pause the plugin")) { Idle.Paused = !Idle.Paused; IsOpen = false; return; }
        if (ImGui.Selectable("About this plugin")) { Plugin.ShowAbout(false); IsOpen = false; return; }
        if (ImGui.Selectable("Report a bug or abuse")) { Plugin.ShowAbout(true); IsOpen = false; return; }
        ImGui.Spacing();
        var c = Plugin.Config;
        Toggle("Quest window", c.QuestWindow, v => c.QuestWindow = v);
        Toggle("Status bar", c.StatusBar, v => c.StatusBar = v);
        Toggle("Stash pop-up", c.StashPopup, v => c.StashPopup = v);
        Toggle("Stash names in the world", c.StashLabels, v => c.StashLabels = v);
        Toggle("Sign names in the world", c.SignLabels, v => c.SignLabels = v);
        ImGui.Separator();
        if (ImGui.Selectable("Hide this button")) { c.TopBarButton = false; c.Save(); IsOpen = false; }
        ImGui.TextColored(Dim, "Bring it back in Settings.");
    }
}
