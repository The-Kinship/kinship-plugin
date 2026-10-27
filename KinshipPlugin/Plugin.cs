using System;
using System.Linq;
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
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IDataManager GameData { get; private set; } = null!;
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;

    internal static Configuration Config { get; private set; } = null!;
    internal static DataStore Data { get; private set; } = null!;
    internal static ImageCache Images { get; private set; } = null!;
    internal static CombatState Combat { get; private set; } = null!;
    internal static SceneState Scene { get; private set; } = null!;
    internal static ItemsState Items { get; private set; } = null!;
    internal static StashState Stashes { get; private set; } = null!;
    internal static SignState Signs { get; private set; } = null!;
    internal static BankState Bank { get; private set; } = null!;
    internal static StatusState Status { get; private set; } = null!;
    internal static QuestState Quests { get; private set; } = null!;
    internal static IFontHandle PopupFont { get; private set; } = null!;

    private static ImageViewerWindow? viewer;
    /// <summary>Opens a website image in the large viewer window.</summary>
    internal static void ShowImage(string file) { viewer?.Show(file); }

    private static MainWindow? mainRef;
    /// <summary>Opens or closes the main window (the top bar button uses this).</summary>
    internal static void ToggleMainWindow() { mainRef?.Toggle(); }
    /// <summary>Opens the main window (used after joining a combat from an RP popup).</summary>
    internal static void ShowMain() { if (mainRef != null) mainRef.IsOpen = true; }

    private static PopupWindow? popupWindow;
    /// <summary>Opens the RP popup window (called when a popup arrives).</summary>
    internal static void ShowPopups() { if (popupWindow != null) popupWindow.IsOpen = true; }

    /// <summary>The logged in character's permanent ID (0 when nobody is logged in).</summary>
    internal static ulong ContentId() { try { return PlayerState.ContentId; } catch (Exception) { return 0; } }

    private ulong seenCharacter = ulong.MaxValue;
    internal static string CharacterName()
    {
        try { return ObjectTable.LocalPlayer?.Name.ToString() ?? ""; } catch (Exception) { return ""; }
    }
    /// <summary>The first word of a name, so "Sesen Vale" and "Sesen" count as the same character.</summary>
    private static string FirstWord(string s) => s.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

    /// <summary>Runs every frame. When you log in as a different character, switches to that character's own saved sign-in and starts their data fresh.</summary>
    private void CheckCharacter()
    {
        var id = ContentId();
        var name = id == 0 ? "" : CharacterName();
        if (id != 0 && name.Length == 0) return;            // still loading in
        if (id == seenCharacter) return;
        seenCharacter = id;
        Config.SetCharacter(id, name);
        // The sign-in saved before characters were separated goes to the character it was made for: the one with the same name,
        // or the first one to log in if it never registered a name. Otherwise the sign-in screen offers it as a button.
        if (id != 0 && Config.HasLegacy())
        {
            var legacy = Config.ItemsName;
            if (legacy.Length == 0 || string.Equals(FirstWord(legacy), FirstWord(name), StringComparison.OrdinalIgnoreCase)) Config.ClaimLegacy();
        }
        Config.Save();
        ResetForCharacter();
    }

    internal static void ResetStates() => ResetForCharacter();

    private static void ResetForCharacter()
    {
        Scene?.Dispose();
        Combat = new CombatState();
        Scene = new SceneState();
        Items = new ItemsState();
        Stashes = new StashState();
        Signs = new SignState();
        Bank = new BankState();
        Status = new StatusState();
        Quests = new QuestState();
        SectData.ClearSession();
        CharacterId = ContentId();
    }
    /// <summary>Changes when the logged in character changes, so screens can clear what was typed for the last one.</summary>
    internal static ulong CharacterId { get; private set; }

    private readonly WindowSystem windows = new("KinshipPlugin");
    private readonly MainWindow main;
    private readonly NoticeWindow notice;
    private readonly PopupWindow popup;
    private readonly StashOverlay overlay;
    private readonly SignOverlay signOverlay;
    private readonly SignReaderWindow signReader;
    private readonly StatusBar statusBar;
    private readonly QuestWindow questWindow;
    private readonly QuestOverlay questOverlay;
    private readonly QuestReplyWindow questReply;
    private readonly QuestOfferWindow questOffer;
    private readonly QuestFightWindow questFight;
    private readonly StashReaderWindow stashReader;
    private readonly TopBarMenu topMenu;
    private readonly TopBar topBar;

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Data = new DataStore();
        Images = new ImageCache();
        Combat = new CombatState();
        Scene = new SceneState();
        Items = new ItemsState();
        Stashes = new StashState();
        Signs = new SignState();
        Bank = new BankState();
        Status = new StatusState();
        Quests = new QuestState();
        PopupFont = PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(28f)));

        main = new MainWindow();
        notice = new NoticeWindow(OpenMain);
        windows.AddWindow(main);
        mainRef = main;
        popup = new PopupWindow();
        popupWindow = popup;
        windows.AddWindow(notice);
        windows.AddWindow(popup);
        viewer = new ImageViewerWindow();
        windows.AddWindow(viewer);
        overlay = new StashOverlay();
        windows.AddWindow(overlay);
        signOverlay = new SignOverlay();
        windows.AddWindow(signOverlay);
        signReader = new SignReaderWindow();
        windows.AddWindow(signReader);
        statusBar = new StatusBar();
        windows.AddWindow(statusBar);
        questOverlay = new QuestOverlay();
        windows.AddWindow(questOverlay);
        questWindow = new QuestWindow();
        windows.AddWindow(questWindow);
        questReply = new QuestReplyWindow();
        windows.AddWindow(questReply);
        questOffer = new QuestOfferWindow();
        windows.AddWindow(questOffer);
        questFight = new QuestFightWindow();
        windows.AddWindow(questFight);

        stashReader = new StashReaderWindow();
        windows.AddWindow(stashReader);
        topMenu = new TopBarMenu();
        windows.AddWindow(topMenu);
        topBar = new TopBar(topMenu);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open The Kinship window."
        });

        PluginInterface.UiBuilder.Draw += CheckCharacter;
        PluginInterface.UiBuilder.Draw += topBar.Update;
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
        PluginInterface.UiBuilder.Draw -= CheckCharacter;
        PluginInterface.UiBuilder.Draw -= topBar.Update;
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
        signOverlay.Dispose();
        signReader.Dispose();
        statusBar.Dispose();
        questWindow.Dispose();
        questOverlay.Dispose();
        questReply.Dispose();
        questOffer.Dispose();
        questFight.Dispose();
        stashReader.Dispose();
        topMenu.Dispose();
        topBar.Dispose();
        Scene.Dispose();
        PopupFont.Dispose();
        Images.Dispose();
    }
}
