using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using FxFilterApi;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace FourIll.EffectsFilter;

public sealed class FxConfig : BasePluginConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public uint Port { get; set; } = 3306;
    public string Database { get; set; } = "vip_global";
    public string User { get; set; } = "fx_filter";
    public string Password { get; set; } = "";
    public int ServerId { get; set; }
}

public sealed class EffectsFilter : BasePlugin, IPluginConfig<FxConfig>, IFxFilter, IFxServerSettings
{
    public override string ModuleName => "4ill.EffectsFilter";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "4ill";
    public FxConfig Config { get; set; } = new();
    public void OnConfigParsed(FxConfig config) => Config = config;

    public readonly FakeConVar<bool> Trails = new("fx_disable_trails", "Disable trails server-wide", false);
    public readonly FakeConVar<bool> Auras = new("fx_disable_auras", "Disable auras, pets and outfits server-wide", false);
    public readonly FakeConVar<bool> C4 = new("fx_disable_c4", "Disable C4 cosmetics server-wide", false);
    public readonly FakeConVar<bool> Kill = new("fx_disable_kill", "Disable kill cosmetics server-wide", false);
    public readonly FakeConVar<bool> Rainbow = new("fx_disable_rainbow", "Disable rainbow model server-wide", false);
    public readonly FakeConVar<bool> NewPlayerHidden = new("fx_new_player_hidden", "Hide others for new players", false);
    private static readonly string[] Names = ["Следы за игроками", "Эффекты вокруг игроков", "Эффекты C4", "Эффекты при убийстве"];
    private static readonly string[] MenuNames = ["Следы за игроками", "Эффекты вокруг игроков", "Эффекты установки и разминирования C4", "Чужие эффекты при убийстве"];
    private static readonly string[] Categories = ["trails", "auras", "c4", "kill"];
    private const string Hint = "Мешают чужие эффекты? Напиши !fx — можно скрыть следы, ауры и эффекты других игроков.";
    private sealed class Session(ulong steam)
    {
        public ulong Steam { get; } = steam;
        public bool[] Hidden { get; set; } = new bool[4];
        public bool Ready { get; set; }
        public bool Loading { get; set; }
        public bool InGame { get; set; }
        public bool HintChecked { get; set; }
    }
    private readonly Dictionary<int, Session> _sessions = new();
    private readonly object _queueLock = new();
    private Task _queue = Task.CompletedTask;
    private bool _active;
    private FxStore _store = null!;

    public bool RainbowDisabled => _active && Rainbow.Value;
    public bool IsHiddenFor(int viewerSlot, FxCategory category) => _active && (uint)category < 4
        && _sessions.TryGetValue(viewerSlot, out var s) && s.Ready && s.Hidden[(int)category];
    public bool IsDisabledOnServer(FxCategory category) => _active && category switch
    {
        FxCategory.Trails => Trails.Value, FxCategory.Auras => Auras.Value,
        FxCategory.C4 => C4.Value, FxCategory.Kill => Kill.Value, _ => false
    };
    public override void Load(bool hotReload)
    {
        _active = true;
        var connection = new MySqlConnectionStringBuilder { Server = Config.Host, Port = Config.Port,
            Database = Config.Database, UserID = Config.User, Password = Config.Password,
            ConnectionTimeout = 5, DefaultCommandTimeout = 10 }.ConnectionString;
        _store = new FxStore(connection, Config.ServerId);
        if (FxFilterProvider.Attach(this))
            Capabilities.RegisterPluginCapability(new PluginCapability<IFxFilter>("4ill:fx"), FxFilterProvider.Get);
        foreach (var name in new[] { "css_fx", "css_effects", "css_hidefx" })
            AddCommand(name, "Скрыть чужие эффекты", (p, _) => { if (Valid(p)) Open(p!, true); });
        RegisterListener<Listeners.OnClientAuthorized>((slot, _) =>
        {
            var p = Utilities.GetPlayerFromSlot(slot);
            // Authorization can arrive after player_connect_full: keep that session.
            if (Valid(p) && !(_sessions.TryGetValue(slot, out var s) && s.Steam == p!.SteamID)) Start(p!);
        });
        // After a map change the client may stay connected without a new OnClientAuthorized.
        RegisterEventHandler<EventPlayerConnectFull>((ev, _) =>
        {
            var p = ev.Userid;
            if (!Valid(p)) return HookResult.Continue;
            if (!_sessions.TryGetValue(p!.Slot, out var s) || s.Steam != p.SteamID) s = Start(p);
            // A map change kills a pending hint timer; the claim in the DB keeps it one-time.
            s.InGame = true; s.HintChecked = false;
            TryHint(p, s);
            return HookResult.Continue;
        });
        RegisterListener<Listeners.OnClientDisconnectPost>(slot => _sessions.Remove(slot));
        RegisterListener<Listeners.OnMapStart>(_ => Server.ExecuteCommand("exec 4ill_fx.cfg"));
        Server.NextWorldUpdate(() =>
        {
            if (!_active) return;
            Server.ExecuteCommand("exec 4ill_fx.cfg");
            foreach (var p in Utilities.GetPlayers())
                if (Valid(p)) Start(p).InGame = p.Connected == PlayerConnectedState.Connected;
        });
    }
    public override void Unload(bool hotReload) { _active = false; FxFilterProvider.Detach(this); _sessions.Clear(); }
    private static bool Valid(CCSPlayerController? p) => p is { IsValid: true, IsBot: false, IsHLTV: false } && p.SteamID != 0;
    private bool Current(CCSPlayerController p, Session s) => _active && Valid(p)
        && p.SteamID == s.Steam && _sessions.GetValueOrDefault(p.Slot) == s;
    // Serialize reads, preference snapshots and metrics: quick consecutive clicks cannot reorder writes.
    private void Enqueue(Func<Task> work)
    {
        lock (_queueLock) _queue = _queue.ContinueWith(async _ =>
        {
            try { await work(); }
            catch (Exception e) { Logger.LogError(e, "EffectsFilter database operation failed"); }
        }, TaskScheduler.Default).Unwrap();
    }
    private Session Start(CCSPlayerController p)
    {
        var s = new Session(p.SteamID);
        _sessions[p.Slot] = s;
        LoadPrefs(p, s);
        return s;
    }
    private void LoadPrefs(CCSPlayerController p, Session s)
    {
        if (s.Loading) return;
        s.Loading = true;
        var newHidden = NewPlayerHidden.Value;
        Enqueue(async () =>
        {
            try
            {
                var hidden = await _store.Load(s.Steam, newHidden);
                Server.NextWorldUpdate(() =>
                {
                    if (!Current(p, s)) return;
                    s.Hidden = hidden; s.Ready = true; s.Loading = false;
                    TryHint(p, s);
                });
            }
            catch
            {
                Server.NextWorldUpdate(() => { if (Current(p, s)) s.Loading = false; });
                throw;
            }
        });
    }
    // The hint is claimed once globally, so print it only to a fully connected player:
    // chat sent during OnClientAuthorized is lost on the loading screen.
    private void TryHint(CCSPlayerController p, Session s)
    {
        if (!s.Ready || !s.InGame || s.HintChecked) return;
        s.HintChecked = true;
        AddTimer(5f, () =>
        {
            if (!Current(p, s)) { s.HintChecked = false; return; }
            Enqueue(async () =>
            {
                var claimed = await _store.ClaimHint(s.Steam);
                if (claimed) Server.NextWorldUpdate(() => { if (Current(p, s)) p.PrintToChat(Hint); });
            });
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }
    private void Open(CCSPlayerController p, bool record)
    {
        if (!_sessions.TryGetValue(p.Slot, out var s) || s.Steam != p.SteamID) s = Start(p);
        if (!s.Ready) { LoadPrefs(p, s); p.PrintToChat("[4ILL] Настройки загружаются. Повтори !fx через несколько секунд."); return; }
        if (record) Enqueue(() => _store.Record(s.Steam, "menu_open"));
        var menu = new CenterHtmlMenu("Чужие эффекты", this);
        for (var i = 0; i < 4; i++)
        {
            var category = i;
            menu.AddMenuOption($"{MenuNames[i]}: {(s.Hidden[i] ? "скрыты" : "показываются")}", (viewer, _) =>
            {
                if (!Current(viewer, s)) return;
                s.Hidden[category] = !s.Hidden[category];
                Save(s, "toggle", Categories[category], s.Hidden[category]);
                viewer.PrintToChat($"[4ILL] {Names[category]}: {(s.Hidden[category] ? "скрыты. Вернуть — !fx" : "показываются.")}");
                Open(viewer, false);
            });
        }
        var hideAll = !s.Hidden.All(h => h);
        menu.AddMenuOption(hideAll ? "Скрыть всё" : "Показать всё", (viewer, _) =>
        {
            if (!Current(viewer, s)) return;
            Array.Fill(s.Hidden, hideAll);
            Save(s, hideAll ? "hide_all" : "show_all", "all", hideAll);
            viewer.PrintToChat(hideAll ? "[4ILL] Все чужие эффекты скрыты. Свои эффекты и привилегии работают как обычно."
                : "[4ILL] Все чужие эффекты снова показываются.");
            Open(viewer, false);
        });
        MenuManager.OpenCenterHtmlMenu(this, p, menu);
    }
    private void Save(Session s, string name, string category, bool hidden)
    {
        var snapshot = s.Hidden.ToArray();
        Enqueue(() => _store.Save(s.Steam, snapshot, name, category, hidden));
    }
}
