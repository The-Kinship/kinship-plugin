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

    private readonly WindowSystem windows = new("KinshipPlugin");
    private readonly MainWindow main;

    public Plugin()
    {
        main = new MainWindow();
        windows.AddWindow(main);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open The Kinship window."
        });

        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMain;
    }

    private void OnCommand(string command, string args) => ToggleMain();

    private void ToggleMain() => main.Toggle();

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMain;
        CommandManager.RemoveHandler(CommandName);
        windows.RemoveAllWindows();
        main.Dispose();
    }
}
