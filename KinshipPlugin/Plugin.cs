using System;
using System.Threading.Tasks;
using Dalamud.Game.Command;
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

    internal static Configuration Config { get; private set; } = null!;
    internal static DataStore Data { get; private set; } = null!;
    internal static ImageCache Images { get; private set; } = null!;

    private readonly WindowSystem windows = new("KinshipPlugin");
    private readonly MainWindow main;
    private readonly NoticeWindow notice;

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Data = new DataStore();
        Images = new ImageCache();

        main = new MainWindow();
        notice = new NoticeWindow(OpenMain);
        windows.AddWindow(main);
        windows.AddWindow(notice);

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
        Images.Dispose();
    }
}
