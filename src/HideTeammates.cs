using Microsoft.Extensions.Configuration;
using Sharp.Modules.ClientPreferences.Shared;
using Sharp.Modules.LocalizerManager.Shared;
using Sharp.Shared;
using Sharp.Shared.Definition;
using Sharp.Shared.Enums;
using Sharp.Shared.HookParams;
using Sharp.Shared.Listeners;
using Sharp.Shared.Managers;
using Sharp.Shared.Objects;
using Sharp.Shared.Types;
using Sharp.Shared.Units;

namespace MS_HideTeammates
{
    public class HideTeammates : IModSharpModule, IGameListener, IClientListener
    {
        public string DisplayName => "Hide Teammates";
        public string DisplayAuthor => "DarkerZ[RUS]";

        readonly float TIMERTIME = 0.3f;

        public HideTeammates(ISharedSystem sharedSystem, string dllPath, string sharpPath, Version version, IConfiguration coreConfiguration, bool hotReload)
        {
            _modSharp = sharedSystem.GetModSharp();
            _modules = sharedSystem.GetSharpModuleManager();
            _convars = sharedSystem.GetConVarManager();
            _clients = sharedSystem.GetClientManager();
            _hooks = sharedSystem.GetHookManager();
            _entities = sharedSystem.GetEntityManager();
            _transmits = sharedSystem.GetTransmitManager();
        }

        private readonly IModSharp _modSharp;
        private readonly ISharpModuleManager _modules;
        private readonly IConVarManager _convars;
        private readonly IClientManager _clients;
        private readonly IHookManager _hooks;
        private readonly IEntityManager _entities;
        private readonly ITransmitManager _transmits;
        private IDisposable? _callback;

        private IModSharpModuleInterface<ILocalizerManager>? _localizer;
        private IModSharpModuleInterface<IClientPreference>? _icp;

        private IConVar? g_cvar_Enable;
        bool g_bEnable = true;
        private IConVar? g_cvar_MaxDistance;
        int g_iMaxDistance = 8000;
        private IConVar? g_cvar_IgnoreAttachments;
        bool g_bIgnoreAttachments = false;

        bool[] g_bHide = new bool[PlayerSlot.MaxPlayerCount];
        int[] g_iDistance = new int[PlayerSlot.MaxPlayerCount];
        bool[] g_bRMB = new bool[PlayerSlot.MaxPlayerCount];

        Guid? g_Timer;

        public bool Init()
        {
            g_cvar_Enable = _convars.CreateConVar("ms_ht_enabled", true, "Disabled/enabled [0/1]", ConVarFlags.Notify);
            if (g_cvar_Enable != null) _convars.InstallChangeHook(g_cvar_Enable, OnCvarEnableChanged);
            g_cvar_MaxDistance = _convars.CreateConVar("ms_ht_maximum", 8000, 1000, 8000, "The maximum distance a player can choose [1000-8000]", ConVarFlags.Notify);
            if (g_cvar_MaxDistance != null) _convars.InstallChangeHook(g_cvar_MaxDistance, OnCvarMaxDistanceChanged);
            g_cvar_IgnoreAttachments = _convars.CreateConVar("ms_ht_hideia", false, "Disabled/enabled ignoring player attachments (ex. prop leader glow) [0/1]", ConVarFlags.Notify);
            if (g_cvar_IgnoreAttachments != null) _convars.InstallChangeHook(g_cvar_IgnoreAttachments, OnCvarIgnoreAttachmentsChanged);

            _modSharp.InstallGameListener(this);
            _clients.InstallClientListener(this);
            _clients.InstallCommandCallback("ht", OnCommandHide);
            _clients.InstallCommandCallback("hide", OnCommandHide);
            _clients.InstallCommandCallback("htall", OnCommandHideAll);
            _clients.InstallCommandCallback("hideall", OnCommandHideAll);
            _hooks.PlayerRunCommand.InstallHookPost(OnPlayerRunCommandPost);
            
            return true;
        }

        public void PostInit()
        {
            CreateTimer();
        }

        public void OnAllModulesLoaded()
        {
            GetClientPrefs();
            GetLocalizer()?.LoadLocaleFile("HideTeammates");
        }

        public void OnLibraryConnected(string name)
        {
            if (name.Equals("ClientPreferences")) GetClientPrefs();
        }

        public void OnLibraryDisconnect(string name)
        {
            if (name.Equals("ClientPreferences")) _icp = null;
        }

        private void OnCookieLoad(IGameClient client)
        {
            if (client == null || !client.IsValid || GetClientPrefs() is not { } cp || !cp.IsLoaded(client.SteamId)) return;

            if (cp.GetCookie(client.SteamId, "HT_Hide") is { } cookie_enabled)
            {
                string sValue = cookie_enabled.GetString();
                if (string.IsNullOrEmpty(sValue) || !Byte.TryParse(sValue, out byte iValue)) iValue = 1;
                if (iValue == 0) g_bHide[client.Slot] = false;
                else g_bHide[client.Slot] = true;
            }
            else
            {
                cp.SetCookie(client.SteamId, "HT_Hide", "0");
                g_bHide[client.Slot] = false;
            }

            if (cp.GetCookie(client.SteamId, "HT_Distance") is { } cookie_distance)
            {
                string sValue = cookie_distance.GetString();
                if (string.IsNullOrEmpty(sValue) || !Int32.TryParse(sValue, out int iValue)) iValue = 0;
                if (iValue <= 0) iValue = 0;
                else if (iValue >= g_iMaxDistance) iValue = g_iMaxDistance;
                g_iDistance[client.Slot] = iValue;
            }
            else
            {
                cp.SetCookie(client.SteamId, "HT_Distance", "0");
                g_iDistance[client.Slot] = 0;
            }
        }

        private void SetCookieValue(IGameClient client)
        {
            if (GetClientPrefs() is { } cp && cp.IsLoaded(client.SteamId))
            {
                cp.SetCookie(client.SteamId, "HT_Hide", g_bHide[client.Slot] ? "1" : "0");
                cp.SetCookie(client.SteamId, "HT_Distance", g_iDistance[client.Slot].ToString());
            }
        }

        public void Shutdown()
        {
            if (g_cvar_Enable != null) _convars.RemoveChangeHook(g_cvar_Enable, OnCvarEnableChanged);
            if (g_cvar_MaxDistance != null) _convars.RemoveChangeHook(g_cvar_MaxDistance, OnCvarMaxDistanceChanged);
            if (g_cvar_IgnoreAttachments != null) _convars.RemoveChangeHook(g_cvar_IgnoreAttachments, OnCvarIgnoreAttachmentsChanged);

            _modSharp.RemoveGameListener(this);
            _clients.RemoveClientListener(this);
            _clients.RemoveCommandCallback("ht", OnCommandHide);
            _clients.RemoveCommandCallback("hide", OnCommandHide);
            _clients.RemoveCommandCallback("htall", OnCommandHideAll);
            _clients.RemoveCommandCallback("hideall", OnCommandHideAll);
            _hooks.PlayerRunCommand.RemoveHookPost(OnPlayerRunCommandPost);

            CloseTimer();
            _callback?.Dispose();
        }

        private void OnCvarEnableChanged(IConVar conVar)
        {
            g_bEnable = conVar.GetBool();
        }

        private void OnCvarMaxDistanceChanged(IConVar conVar)
        {
            g_iMaxDistance = conVar.GetInt32();
        }

        private void OnCvarIgnoreAttachmentsChanged(IConVar conVar)
        {
            g_bIgnoreAttachments = conVar.GetBool();
        }

        public void OnGameActivate() //OnMapStart
        {
            CreateTimer();
        }

        public void OnGameDeactivate() //OnMapEnd
        {
            CloseTimer();
        }

        public void OnClientPutInServer(IGameClient client)
        {
            _modSharp.PushTimer(() =>
            {
                if (client.IsValid && _entities.FindPlayerControllerBySlot(client.Slot) is { ConnectedState: PlayerConnectedState.PlayerConnected } player)
                {
                    _transmits.AddEntityHooks(player, true);
                }
            }, 5);
        }

        public void OnClientDisconnected(IGameClient client)
        {
            g_bHide[client.Slot] = false;
            g_iDistance[client.Slot] = 0;
        }

        private void OnPlayerRunCommandPost(IPlayerRunCommandHookParams @params, HookReturnValue<EmptyHookReturn> @return)
        {
            if (@params.Service.KeyChangedButtons.HasFlag(UserCommandButtons.Attack2))
            {
                g_bRMB[@params.Client.Slot] = @params.Service.KeyButtons.HasFlag(UserCommandButtons.Attack2);
                //Console.WriteLine($"Player[{@params.Client.Slot}] Button: {(g_bRMB[@params.Client.Slot] ? "1" : "0")}");
            }
        }

        private void OnTransmit()
        {
            if (!g_bEnable) return;

            foreach (var target in _entities.GetPlayerControllers(true).ToArray())
            {
                if (!_transmits.IsEntityHooked(target)) continue;

                foreach (var player in _entities.GetPlayerControllers(true).ToArray())
                {
                    var bAttachments = g_bIgnoreAttachments;
                    if (!bAttachments)
                    {
                        if (!(target.GetPawn() is { } pawn && pawn.GetBodyComponent().GetSceneNode() is { } scenenode && scenenode.GetChild() is { } child &&
                            (child.GetOwner() is { } owner && owner.Classname.Equals("prop_dynamic") || child.GetNextSibling() is { } nextsibling && nextsibling.GetOwner() is { } owner2 && owner2.Classname.Equals("prop_dynamic"))))
                            bAttachments = true;
                    }
                    var bHide = target.GetPawn()!.LifeState == LifeState.Alive && player.GetPawn()!.LifeState == LifeState.Alive && g_bHide[player.PlayerSlot] && !g_bRMB[player.PlayerSlot] && target.PlayerSlot != player.PlayerSlot && target.Team == player.Team && bAttachments;
                    if (bHide && g_iDistance[player.PlayerSlot] > 0 && Distance(target.GetPawn()!.GetAbsOrigin(), player.GetPawn()!.GetAbsOrigin()) > g_iDistance[player.PlayerSlot]) bHide = false;
                    //if (!player.IsFakeClient) Console.WriteLine($"[Debug:HT] Player: {player.PlayerName} Target: {target.PlayerName} Hide: {(bHide ? "1" : "0")} Distance: {Distance(target.GetPawn()!.GetAbsOrigin(), player.GetPawn()!.GetAbsOrigin())}");
                    _transmits.SetEntityState(target.Index, player.Index, !bHide, -1); //false - hide, true - Not hide
                }
            }
        }

        void CreateTimer()
        {
            CloseTimer();
            g_Timer = _modSharp.PushTimer(OnTransmit, TIMERTIME, GameTimerFlags.Repeatable);
        }

        void CloseTimer()
        {
            if (g_Timer != null)
            {
                _modSharp.StopTimer((Guid)g_Timer);
                g_Timer = null;
            }
        }

        private ECommandAction OnCommandHide(IGameClient client, StringCommand command)
        {
            if (client.IsValid)
            {
                if (!g_bEnable)
                {
                    ReplyToCommand(client, command.ChatTrigger, "HideTeammates.PluginDisabled");
                    return ECommandAction.Stopped;
                }

                int customdistance = -2;

                if (command.ArgCount > 0) _ = Int32.TryParse(command.GetArg(1), out customdistance);

                if (customdistance >= 0 && customdistance <= g_iMaxDistance)
                {
                    g_bHide[client.Slot] = true;
                    g_iDistance[client.Slot] = customdistance;
                    SetCookieValue(client);

                    if (g_iDistance[client.Slot] == 0) ReplyToCommand(client, command.ChatTrigger, "HideTeammates.EnableAllMap");
                    else ReplyToCommand(client, command.ChatTrigger, "HideTeammates.Enable", g_iDistance[client.Slot]);
                }
                else if (customdistance < -2 || customdistance > g_iMaxDistance)
                {
                    ReplyToCommand(client, command.ChatTrigger, "HideTeammates.Wrong", g_iMaxDistance);
                }
                else if (customdistance == -1)
                {
                    g_bHide[client.Slot] = false;
                    SetCookieValue(client);
                    ReplyToCommand(client, command.ChatTrigger, "HideTeammates.Disable");
                }
                else if (customdistance == -2) //Later can be replaced by a menu
                {
                    g_bHide[client.Slot] = !g_bHide[client.Slot];
                    SetCookieValue(client);
                    if (g_bHide[client.Slot])
                    {
                        if (g_iDistance[client.Slot] == 0) ReplyToCommand(client, command.ChatTrigger, "HideTeammates.EnableAllMap");
                        else ReplyToCommand(client, command.ChatTrigger, "HideTeammates.Enable", g_iDistance[client.Slot]);
                    }
                    else
                    {
                        ReplyToCommand(client, command.ChatTrigger, "HideTeammates.Disable");
                    }
                }
            }
            return ECommandAction.Stopped;
        }

        private ECommandAction OnCommandHideAll(IGameClient client, StringCommand command)
        {
            if (client.IsValid)
            {
                if (!g_bEnable)
                {
                    ReplyToCommand(client, command.ChatTrigger, "HideTeammates.PluginDisabled");
                    return ECommandAction.Stopped;
                }

                g_bHide[client.Slot] = !g_bHide[client.Slot];
                SetCookieValue(client);

                if (g_bHide[client.Slot])
                {
                    if (g_iDistance[client.Slot] == 0) ReplyToCommand(client, command.ChatTrigger, "HideTeammates.EnableAllMap");
                    else ReplyToCommand(client, command.ChatTrigger, "HideTeammates.Enable", g_iDistance[client.Slot]);
                }
                else
                {
                    ReplyToCommand(client, command.ChatTrigger, "HideTeammates.Disable");
                }
            }

            return ECommandAction.Stopped;
        }

        void ReplyToCommand(IGameClient client, bool bChatTrigger, string sMessage, params object[] arg)
        {
            var player = client.GetPlayerController()!;
            if (GetLocalizer() is { } lm)
            {
                var localizer = lm.GetLocalizer(client);
                if (bChatTrigger) player.Print(HudPrintChannel.Chat, $" {ChatColor.Blue}[{ChatColor.Green}HT{ChatColor.Blue}] {ChatColor.White} {ReplaceColorTags(localizer.Format(sMessage, arg))}");
                else player.Print(HudPrintChannel.Console, $"[HT] {ReplaceColorTags(localizer.Format(sMessage, arg), false)}");
            }
        }

        static float Distance(Vector point1, Vector point2)
        {
            float dx = point2.X - point1.X;
            float dy = point2.Y - point1.Y;
            float dz = point2.Z - point1.Z;

            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public static string ReplaceColorTags(string input, bool bChat = true)
        {
            for (var i = 0; i < colorPatterns.Length; i++)
                input = input.Replace(colorPatterns[i], bChat ? colorReplacements[i] : "");

            return input;
        }
        static readonly string[] colorPatterns =
        [
            "{default}", "{darkred}", "{purple}", "{green}", "{lightgreen}", "{lime}", "{red}", "{grey}",
            "{olive}", "{a}", "{lightblue}", "{blue}", "{d}", "{pink}", "{darkorange}", "{orange}",
            "{white}", "{yellow}", "{magenta}", "{silver}", "{bluegrey}", "{lightred}", "{cyan}", "{gray}"
        ];
        static readonly string[] colorReplacements =
        [
            "\x01", "\x02", "\x03", "\x04", "\x05", "\x06", "\x07", "\x08",
            "\x09", "\x0A", "\x0B", "\x0C", "\x0D", "\x0E", "\x0F", "\x10",
            "\x01", "\x09", "\x0E", "\x0A", "\x0D", "\x0F", "\x03", "\x08"
        ];

        private ILocalizerManager? GetLocalizer()
        {
            if (_localizer?.Instance is null)
            {
                _localizer = _modules.GetOptionalSharpModuleInterface<ILocalizerManager>(ILocalizerManager.Identity);
            }
            return _localizer?.Instance;
        }

        private IClientPreference? GetClientPrefs()
        {
            if (_icp?.Instance is null)
            {
                _icp = _modules.GetOptionalSharpModuleInterface<IClientPreference>(IClientPreference.Identity);
                if (_icp?.Instance is { } instance) _callback = instance.ListenOnLoad(OnCookieLoad);
            }
            return _icp?.Instance;
        }

        int IGameListener.ListenerVersion => IGameListener.ApiVersion;
        int IGameListener.ListenerPriority => 0;
        int IClientListener.ListenerVersion => IClientListener.ApiVersion;
        int IClientListener.ListenerPriority => 0;
    }
}
