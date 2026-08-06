using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewConnect.Configuration;
using StardewConnect.Rooms;
using StardewConnect.Utilities;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace StardewConnect.UI
{
    /// <summary>A labelled, clickable rectangle drawn in the vanilla button style.</summary>
    internal sealed class MenuButton
    {
        public MenuButton(string name, Rectangle bounds, string label)
        {
            this.Component = new ClickableComponent(bounds, name);
            this.Label = label;
        }

        public ClickableComponent Component { get; }

        public string Label { get; set; }

        public bool Enabled { get; set; } = true;

        public bool Hovered { get; set; }

        public Rectangle Bounds
        {
            get => this.Component.bounds;
            set => this.Component.bounds = value;
        }

        public bool Contains(int x, int y)
        {
            return this.Enabled && this.Component.containsPoint(x, y);
        }

        public void Draw(SpriteBatch b)
        {
            Color tint = !this.Enabled
                ? Color.Gray * 0.7f
                : this.Hovered ? Color.Wheat : Color.White;

            IClickableMenu.drawTextureBox(
                b,
                Game1.mouseCursors,
                new Rectangle(432, 439, 9, 9),
                this.Bounds.X,
                this.Bounds.Y,
                this.Bounds.Width,
                this.Bounds.Height,
                tint,
                4f,
                drawShadow: false);

            Vector2 size = Game1.smallFont.MeasureString(this.Label);
            Vector2 position = new Vector2(
                this.Bounds.X + ((this.Bounds.Width - size.X) / 2f),
                this.Bounds.Y + ((this.Bounds.Height - size.Y) / 2f));

            Utility.drawTextWithShadow(
                b,
                this.Label,
                Game1.smallFont,
                position,
                this.Enabled ? Game1.textColor : Game1.textColor * 0.5f);
        }
    }

    /// <summary>Root Stardew Connect menu: host, join, settings and the current connection status.</summary>
    internal sealed class StardewConnectMenu : IClickableMenu
    {
        private const int MenuWidth = 700;
        private const int MenuHeight = 520;

        private readonly RoomSession session;
        private readonly ModConfig config;
        private readonly List<MenuButton> buttons = new List<MenuButton>();

        private MenuButton hostButton;
        private MenuButton joinButton;
        private MenuButton settingsButton;
        private MenuButton closeButton;
        private bool showSettings;
        private string hoverText = string.Empty;

        public StardewConnectMenu(RoomSession session, ModConfig config)
            : base(
                (int)Utility.getTopLeftPositionForCenteringOnScreen(MenuWidth, MenuHeight).X,
                (int)Utility.getTopLeftPositionForCenteringOnScreen(MenuWidth, MenuHeight).Y,
                MenuWidth,
                MenuHeight,
                showUpperRightCloseButton: true)
        {
            this.session = session;
            this.config = config;
            this.LayoutComponents();
        }

        private void LayoutComponents()
        {
            this.buttons.Clear();

            int buttonWidth = 340;
            int buttonHeight = 68;
            int left = this.xPositionOnScreen + ((this.width - buttonWidth) / 2);
            int top = this.yPositionOnScreen + 150;
            int spacing = 84;

            this.hostButton = new MenuButton("host", new Rectangle(left, top, buttonWidth, buttonHeight), Translations.Get("menu.hostGame"));
            this.joinButton = new MenuButton("join", new Rectangle(left, top + spacing, buttonWidth, buttonHeight), Translations.Get("menu.joinGame"));
            this.settingsButton = new MenuButton("settings", new Rectangle(left, top + (spacing * 2), buttonWidth, buttonHeight), Translations.Get("menu.settings"));
            this.closeButton = new MenuButton("close", new Rectangle(left, top + (spacing * 3), buttonWidth, buttonHeight), Translations.Get("menu.close"));

            this.buttons.Add(this.hostButton);
            this.buttons.Add(this.joinButton);
            this.buttons.Add(this.settingsButton);
            this.buttons.Add(this.closeButton);
        }

        public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
        {
            base.gameWindowSizeChanged(oldBounds, newBounds);

            Vector2 topLeft = Utility.getTopLeftPositionForCenteringOnScreen(MenuWidth, MenuHeight);
            this.xPositionOnScreen = (int)topLeft.X;
            this.yPositionOnScreen = (int)topLeft.Y;
            this.initializeUpperRightCloseButton();
            this.LayoutComponents();
        }

        public override void performHoverAction(int x, int y)
        {
            base.performHoverAction(x, y);

            this.hoverText = string.Empty;
            foreach (MenuButton button in this.buttons)
            {
                button.Hovered = button.Contains(x, y);
                if (button.Hovered && !button.Enabled)
                    this.hoverText = Translations.Get("menu.busy");
            }
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            base.receiveLeftClick(x, y, playSound);

            if (this.closeButton.Contains(x, y))
            {
                Game1.playSound("bigDeSelect");
                this.exitThisMenu();
                return;
            }

            if (this.settingsButton.Contains(x, y))
            {
                Game1.playSound("smallSelect");
                this.showSettings = !this.showSettings;
                return;
            }

            if (this.hostButton.Contains(x, y))
            {
                Game1.playSound("smallSelect");
                if (this.session.State.IsInRoom)
                {
                    Game1.activeClickableMenu = new HostRoomMenu(this.session);
                    return;
                }

                this.session.StartHosting();
                Game1.activeClickableMenu = new HostRoomMenu(this.session);
                return;
            }

            if (this.joinButton.Contains(x, y))
            {
                Game1.playSound("smallSelect");
                Game1.activeClickableMenu = new JoinRoomMenu(this.session);
            }
        }

        public override void draw(SpriteBatch b)
        {
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.4f);

            IClickableMenu.drawTextureBox(
                b,
                Game1.menuTexture,
                new Rectangle(0, 256, 60, 60),
                this.xPositionOnScreen,
                this.yPositionOnScreen,
                this.width,
                this.height,
                Color.White);

            SpriteText.drawStringWithScrollCenteredAt(
                b,
                Translations.Get("menu.title"),
                this.xPositionOnScreen + (this.width / 2),
                this.yPositionOnScreen + 24);

            bool busy = this.session.IsBusy;
            this.hostButton.Enabled = !busy;
            this.joinButton.Enabled = !busy && !this.session.State.IsInRoom;
            this.hostButton.Label = this.session.State.IsInRoom && this.session.State.IsHost
                ? Translations.Get("menu.openRoom")
                : Translations.Get("menu.hostGame");

            foreach (MenuButton button in this.buttons)
                button.Draw(b);

            this.DrawStatus(b);

            if (this.showSettings)
                this.DrawSettings(b);

            base.draw(b);

            if (this.hoverText.Length > 0)
                IClickableMenu.drawHoverText(b, this.hoverText, Game1.smallFont);

            this.drawMouse(b);
        }

        private void DrawStatus(SpriteBatch b)
        {
            RoomState state = this.session.State;
            string status = MenuText.DescribeConnection(state.Connection);
            if (!string.IsNullOrEmpty(state.StatusMessage))
                status = $"{status} - {state.StatusMessage}";

            Vector2 position = new Vector2(this.xPositionOnScreen + 48, this.yPositionOnScreen + 96);
            Utility.drawTextWithShadow(b, $"{Translations.Get("menu.connectionStatus")}: {status}", Game1.smallFont, position, Game1.textColor);

            if (!string.IsNullOrEmpty(state.ErrorMessage))
            {
                Vector2 errorPosition = new Vector2(this.xPositionOnScreen + 48, this.yPositionOnScreen + this.height - 76);
                Utility.drawTextWithShadow(b, state.ErrorMessage, Game1.smallFont, errorPosition, Color.DarkRed);
            }
        }

        private void DrawSettings(SpriteBatch b)
        {
            int panelWidth = 620;
            int panelHeight = 220;
            int panelX = this.xPositionOnScreen + ((this.width - panelWidth) / 2);
            int panelY = this.yPositionOnScreen + this.height - panelHeight - 96;

            IClickableMenu.drawTextureBox(
                b,
                Game1.menuTexture,
                new Rectangle(0, 256, 60, 60),
                panelX,
                panelY,
                panelWidth,
                panelHeight,
                Color.White);

            string[] lines =
            {
                $"{Translations.Get("settings.server")}: {this.config.SignalingServerUrl}",
                $"{Translations.Get("settings.maxPlayers")}: {this.config.MaxPlayers}",
                $"{Translations.Get("settings.transport")}: {(this.config.UseMockWebRtcTransport ? Translations.Get("settings.transportMock") : Translations.Get("settings.transportWebRtc"))}",
                $"{Translations.Get("settings.openKey")}: {this.config.OpenMenuKey}",
                Translations.Get("settings.editHint")
            };

            for (int index = 0; index < lines.Length; index++)
            {
                Utility.drawTextWithShadow(
                    b,
                    lines[index],
                    Game1.smallFont,
                    new Vector2(panelX + 32, panelY + 32 + (index * 36)),
                    index == lines.Length - 1 ? Game1.textColor * 0.7f : Game1.textColor);
            }
        }
    }

    /// <summary>Shared text formatting for the Stardew Connect menus.</summary>
    internal static class MenuText
    {
        public static string DescribeConnection(ConnectionState state)
        {
            switch (state)
            {
                case ConnectionState.Disconnected:
                    return Translations.Get("state.disconnected");
                case ConnectionState.ConnectingToSignaling:
                    return Translations.Get("state.connectingToSignaling");
                case ConnectionState.CreatingRoom:
                    return Translations.Get("state.creatingRoom");
                case ConnectionState.WaitingForPlayers:
                    return Translations.Get("state.waitingForPlayers");
                case ConnectionState.JoiningRoom:
                    return Translations.Get("state.joiningRoom");
                case ConnectionState.NegotiatingWebRtc:
                    return Translations.Get("state.negotiatingWebRtc");
                case ConnectionState.Connected:
                    return Translations.Get("state.connected");
                case ConnectionState.Reconnecting:
                    return Translations.Get("state.reconnecting");
                case ConnectionState.Failed:
                    return Translations.Get("state.failed");
                default:
                    return state.ToString();
            }
        }

        public static string DescribePeer(Networking.PeerConnectionState state)
        {
            switch (state)
            {
                case Networking.PeerConnectionState.New:
                    return Translations.Get("peer.new");
                case Networking.PeerConnectionState.CreatingOffer:
                    return Translations.Get("peer.creatingOffer");
                case Networking.PeerConnectionState.WaitingForAnswer:
                    return Translations.Get("peer.waitingForAnswer");
                case Networking.PeerConnectionState.Negotiating:
                    return Translations.Get("peer.negotiating");
                case Networking.PeerConnectionState.Connecting:
                    return Translations.Get("peer.connecting");
                case Networking.PeerConnectionState.Connected:
                    return Translations.Get("peer.connected");
                case Networking.PeerConnectionState.Disconnected:
                    return Translations.Get("peer.disconnected");
                case Networking.PeerConnectionState.Failed:
                    return Translations.Get("peer.failed");
                case Networking.PeerConnectionState.Closed:
                    return Translations.Get("peer.closed");
                default:
                    return state.ToString();
            }
        }

        public static Color PeerColor(Networking.PeerConnectionState state)
        {
            switch (state)
            {
                case Networking.PeerConnectionState.Connected:
                    return Color.DarkGreen;
                case Networking.PeerConnectionState.Failed:
                case Networking.PeerConnectionState.Disconnected:
                case Networking.PeerConnectionState.Closed:
                    return Color.DarkRed;
                default:
                    return Game1.textColor;
            }
        }

        public static string FormatCountdown(DateTimeOffset? expiresAt)
        {
            if (expiresAt == null)
                return string.Empty;

            TimeSpan remaining = expiresAt.Value - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return Translations.Get("host.expired");

            return $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
        }
    }
}
