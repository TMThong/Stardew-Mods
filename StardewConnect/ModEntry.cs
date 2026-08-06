using System;
using System.Linq;
using StardewConnect.Configuration;
using StardewConnect.Networking;
using StardewConnect.Rooms;
using StardewConnect.UI;
using StardewConnect.Utilities;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace StardewConnect
{
    /// <summary>
    /// Mod entry point. It wires the pieces together and owns their lifetime; all real work
    /// lives in the Networking, Rooms and UI namespaces.
    /// </summary>
    public class ModEntry : Mod
    {
        private ModConfig config;
        private MainThreadDispatcher dispatcher;
        private ISignalingClient signaling;
        private IWebRtcTransport transport;
        private NetworkMessageRouter router;
        private RoomSession session;

        public override void Entry(IModHelper helper)
        {
            this.config = helper.ReadConfig<ModConfig>();
            this.config.Normalise();
            helper.WriteConfig(this.config);

            Translations.Initialise(helper.Translation);
            ClipboardHelper.Initialise(this.Monitor);

            this.dispatcher = new MainThreadDispatcher(this.Monitor);
            this.signaling = new SignalingClient(this.Monitor, this.config);
            this.transport = this.CreateTransport();
            this.router = new NetworkMessageRouter(this.transport, this.Monitor);
            this.session = new RoomSession(this.signaling, this.transport, this.router, this.dispatcher, this.config, this.Monitor);
            this.session.RoomOpened += this.OnRoomOpened;

            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
            helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;
            helper.Events.Input.ButtonPressed += this.OnButtonPressed;

            this.RegisterConsoleCommands(helper);

            if (this.config.IsInsecureRemoteUrl())
            {
                this.Monitor.Log(
                    $"The signaling URL '{this.config.SignalingServerUrl}' is plaintext ws://. Use wss:// for anything other than a local test server.",
                    LogLevel.Warn);
            }

            this.Monitor.Log($"Stardew Connect ready. Press {this.config.OpenMenuKey} to open the menu.", LogLevel.Info);
        }

        /// <summary>Picks the peer-to-peer transport implementation.</summary>
        private IWebRtcTransport CreateTransport()
        {
            if (this.config.UseMockWebRtcTransport)
            {
                this.Monitor.Log("Using the mock transport: signaling runs end to end, but no game data is delivered.", LogLevel.Warn);
                return new MockWebRtcTransport(this.Monitor);
            }

            return new WebRtcTransport(this.Monitor, this.config);
        }

        // ------------------------------------------------------------------ SMAPI events

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            this.dispatcher.CaptureMainThread();
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            // Everything network related hops onto the game thread here.
            this.dispatcher.Pump();
        }

        private void OnReturnedToTitle(object sender, ReturnedToTitleEventArgs e)
        {
            if (this.session.State.IsInRoom)
            {
                this.Monitor.Log("Returned to title; leaving the Stardew Connect room.", LogLevel.Info);
                this.session.LeaveRoom();
            }
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (e.Button != this.config.OpenMenuKey)
                return;

            if (Game1.activeClickableMenu != null)
                return;

            if (!Context.IsWorldReady && Game1.currentMinigame != null)
                return;

            this.Helper.Input.Suppress(e.Button);
            Game1.activeClickableMenu = new StardewConnectMenu(this.session, this.config);
        }

        private void OnRoomOpened(object sender, EventArgs e)
        {
            // Nothing to force here: the host menu reads the session state every frame. The
            // event exists so future features (a toast, an auto-invite) have a hook.
            this.Monitor.Log($"Room {this.session.State.RoomId} is open and waiting for players.", LogLevel.Debug);
        }

        // ------------------------------------------------------------------ console commands

        private void RegisterConsoleCommands(IModHelper helper)
        {
            helper.ConsoleCommands.Add(
                "sdc_status",
                "Show the current Stardew Connect session state.",
                (_, _) =>
                {
                    RoomState state = this.session.State;
                    this.Monitor.Log($"Connection : {state.Connection}", LogLevel.Info);
                    this.Monitor.Log($"Signaling  : {this.signaling.State} ({this.config.SignalingServerUrl})", LogLevel.Info);
                    this.Monitor.Log($"Role       : {state.Role}", LogLevel.Info);
                    this.Monitor.Log($"Room       : {(state.IsInRoom ? state.RoomId : "-")}", LogLevel.Info);
                    this.Monitor.Log($"Local peer : {(state.IsInRoom ? state.LocalPeerId : "-")}", LogLevel.Info);
                    this.Monitor.Log($"Players    : {state.PlayerCount}/{state.MaxPlayers}", LogLevel.Info);

                    foreach (PeerSession peer in state.Peers)
                        this.Monitor.Log($"  peer {peer.ShortId}: {peer.State}{(peer.LastError.Length > 0 ? $" ({peer.LastError})" : string.Empty)}", LogLevel.Info);

                    if (!string.IsNullOrEmpty(state.ErrorMessage))
                        this.Monitor.Log($"Last error : {state.ErrorMessage}", LogLevel.Warn);
                });

            helper.ConsoleCommands.Add(
                "sdc_host",
                "Create a Stardew Connect room without opening the menu.",
                (_, _) => this.dispatcher.Invoke(() => this.session.StartHosting()));

            helper.ConsoleCommands.Add(
                "sdc_join",
                "Join a Stardew Connect room. Usage: sdc_join <roomId> <password>",
                (_, args) =>
                {
                    if (args.Length < 2)
                    {
                        this.Monitor.Log("Usage: sdc_join <roomId> <password>", LogLevel.Error);
                        return;
                    }

                    string roomId = args[0];
                    string password = string.Join(" ", args.Skip(1));
                    this.dispatcher.Invoke(() => this.session.JoinRoom(roomId, password));
                });

            helper.ConsoleCommands.Add(
                "sdc_leave",
                "Leave the current room (or close it when hosting).",
                (_, _) => this.dispatcher.Invoke(() => this.session.LeaveRoom()));
        }

        // ------------------------------------------------------------------ teardown

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    this.session?.Dispose();
                    this.transport?.Dispose();
                    this.signaling?.Dispose();
                    this.dispatcher?.Clear();
                }
                catch (Exception error)
                {
                    this.Monitor.Log($"Error while shutting Stardew Connect down: {error.Message}", LogLevel.Debug);
                }
            }

            base.Dispose(disposing);
        }
    }
}
