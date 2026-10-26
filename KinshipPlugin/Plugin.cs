using System;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace KinshipPlugin;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/kinship";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IDataManager GameData { get; private set; } = null!;

    internal static Configuration Config { get; private set; } = null!;
    internal static DataStore Data { get; private set; } = null!;
    internal static ImageCache Images { get; private set; } = null!;
    internal static CombatState Combat { get; private set; } = null!;
    internal static SceneState Scene { get; private set; } = null!;
    internal static ItemsState Items { get; private set; } = null!;
    internal static StashState Stashes { get; private set; } = null!;
    internal static IFontHandle PopupFont { get; private set; } = null!;

    private static ImageViewerWindow? viewer;
    /// <summary>Opens a website image in the large viewer window.</summary>
    internal static void ShowImage(string file) { viewer?.Show(file); }

    private static PopupWindow? popupWindow;
    /// <summary>Opens the RP popup window (called when a popup arrives).</summary>
    internal static void ShowPopups() { if (popupWindow != null) popupWindow.IsOpen = true; }

    private readonly WindowSystem windows = new("KinshipPlugin");
    private readonly MainWindow main;
    private readonly NoticeWindow notice;
    private readonly PopupWindow popup;
    private readonly StashOverlay overlay;

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Data = new DataStore();
        Images = new ImageCache();
        Combat = new CombatState();
        Scene = new SceneState();
        Items = new ItemsState();
        Stashes = new StashState();
        PopupFont = PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(28f)));

        main = new MainWindow();
        notice = new NoticeWindow(OpenMain);
        windows.AddWindow(main);
        popup = new PopupWindow();
        popupWindow = popup;
        windows.AddWindow(notice);
        windows.AddWindow(popup);
        viewer = new ImageViewerWindow();
        windows.AddWindow(viewer);
        overlay = new StashOverlay();
        windows.AddWindow(overlay);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open The Kinship window."
        });

        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMain;

        // Show the plot notice at login (or right now, if you are already logged in).
        ClientState.Login += OnLogin;
        if (ClientState.IsLoggedIn) _ = ShowNoticeAsync();
    }

    private void OnLogin() => _ = ShowNoticeAsync();

    private async Task ShowNoticeAsync()
    {
        if (!Config.ShowPlotNotice) return;
        try { await Data.Refresh().ConfigureAwait(false); }
        catch (Exception ex) { Log.Warning(ex, "Plot notice refresh failed."); return; }
        var (_, plots, _) = Data.Snapshot();
        if (plots is { Count: > 0 }) notice.IsOpen = true;
    }

    private void OnCommand(string command, string args) => ToggleMain();

    private void ToggleMain() => main.Toggle();

    private void OpenMain() => main.IsOpen = true;

    public void Dispose()
    {
        ClientState.Login -= OnLogin;
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMain;
        CommandManager.RemoveHandler(CommandName);
        windows.RemoveAllWindows();
        main.Dispose();
        notice.Dispose();
        popupWindow = null;
        viewer = null;
        popup.Dispose();
        overlay.Dispose();
        Scene.Dispose();
        PopupFont.Dispose();
        Images.Dispose();
    }
}
