using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewConnect.Networking;
using StardewConnect.Rooms;
using StardewConnect.Utilities;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace StardewConnect.UI
{
    /// <summary>
    /// Host view: room credentials, the invite helpers, the peer list with a per-client WebRTC
    /// state, and the signaling status.
    /// </summary>
    internal sealed class HostRoomMenu : IClickableMenu
    {
        private const int MenuWidth = 820;
        private const int MenuHeight = 620;

        private readonly RoomSession session;
        private readonly List<MenuButton> buttons = new List<MenuButton>();

        private MenuButton copyRoomIdButton;
        private MenuButton copyPasswordButton;
        private MenuButton copyInviteButton;
        private MenuButton closeRoomButton;
        private MenuButton backButton;

        private string toast = string.Empty;
        private int toastTicksLeft;
        private bool passwordVisible = true;

        public HostRoomMenu(RoomSession session)
            : base(
                (int)Utility.getTopLeftPositionForCenteringOnScreen(MenuWidth, MenuHeight).X,
                (int)Utility.getTopLeftPositionForCenteringOnScreen(MenuWidth, MenuHeight).Y,
                MenuWidth,
                MenuHeight,
                showUpperRightCloseButton: true)
        {
            this.session = session;
            this.LayoutComponents();
        }

        private void LayoutComponents()
        {
            this.buttons.Clear();

            int buttonWidth = 232;
            int buttonHeight = 64;
            int gap = 16;
            int rowY = this.yPositionOnScreen + this.height - 168;
            int left = this.xPositionOnScreen + 40;

            this.copyRoomIdButton = new MenuButton("copyRoomId", new Rectangle(left, rowY, buttonWidth, buttonHeight), Translations.Get("host.copyRoomId"));
            this.copyPasswordButton = new MenuButton("copyPassword", new Rectangle(left + buttonWidth + gap, rowY, buttonWidth, buttonHeight), Translations.Get("host.copyPassword"));
            this.copyInviteButton = new MenuButton("copyInvite", new Rectangle(left + ((buttonWidth + gap) * 2), rowY, buttonWidth, buttonHeight), Translations.Get("host.copyInvite"));

            int secondRowY = rowY + buttonHeight + gap;
            this.closeRoomButton = new MenuButton("closeRoom", new Rectangle(left, secondRowY, buttonWidth, buttonHeight), Translations.Get("host.closeRoom"));
            this.backButton = new MenuButton("back", new Rectangle(left + buttonWidth + gap, secondRowY, buttonWidth, buttonHeight), Translations.Get("menu.back"));

            this.buttons.Add(this.copyRoomIdButton);
            this.buttons.Add(this.copyPasswordButton);
            this.buttons.Add(this.copyInviteButton);
            this.buttons.Add(this.closeRoomButton);
            this.buttons.Add(this.backButton);
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

        public override void update(GameTime time)
        {
            base.update(time);

            if (this.toastTicksLeft > 0)
            {
                this.toastTicksLeft--;
                if (this.toastTicksLeft == 0)
                    this.toast = string.Empty;
            }
        }

        public override void performHoverAction(int x, int y)
        {
            base.performHoverAction(x, y);

            foreach (MenuButton button in this.buttons)
                button.Hovered = button.Contains(x, y);
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            base.receiveLeftClick(x, y, playSound);

            RoomState state = this.session.State;

            if (this.backButton.Contains(x, y))
            {
                Game1.playSound("smallSelect");
                this.exitThisMenu();
                return;
            }

            if (this.closeRoomButton.Contains(x, y))
            {
                Game1.playSound("bigDeSelect");
                this.session.LeaveRoom();
                this.exitThisMenu();
                return;
            }

            if (!state.IsInRoom)
                return;

            if (this.copyRoomIdButton.Contains(x, y))
            {
                this.Copy(state.RoomId, Translations.Get("host.copiedRoomId"));
                return;
            }

            if (this.copyPasswordButton.Contains(x, y))
            {
                this.Copy(state.Password, Translations.Get("host.copiedPassword"));
                return;
            }

            if (this.copyInviteButton.Contains(x, y))
                this.Copy(this.session.BuildInviteText(), Translations.Get("host.copiedInvite"));
        }

        public override void receiveRightClick(int x, int y, bool playSound = true)
        {
            // Right clicking the password line toggles visibility, for streamers.
            Rectangle passwordLine = new Rectangle(this.xPositionOnScreen + 40, this.yPositionOnScreen + 168, this.width - 80, 40);
            if (passwordLine.Contains(x, y))
            {
                this.passwordVisible = !this.passwordVisible;
                Game1.playSound("smallSelect");
            }
        }

        private void Copy(string text, string successMessage)
        {
            bool copied = ClipboardHelper.TrySetText(text);
            Game1.playSound(copied ? "smallSelect" : "cancel");
            this.toast = copied ? successMessage : Translations.Get("host.copyFailed");
            this.toastTicksLeft = 180;
        }

        public override void draw(SpriteBatch b)
        {
            RoomState state = this.session.State;

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
                Translations.Get("host.title"),
                this.xPositionOnScreen + (this.width / 2),
                this.yPositionOnScreen + 24);

            int left = this.xPositionOnScreen + 40;
            int y = this.yPositionOnScreen + 112;

            string roomId = state.IsInRoom ? state.RoomId : Translations.Get("host.noRoom");
            Utility.drawTextWithShadow(b, $"{Translations.Get("host.roomId")}: {roomId}", Game1.dialogueFont, new Vector2(left, y), Game1.textColor);

            y += 56;
            string password = !state.IsInRoom
                ? "-"
                : this.passwordVisible ? state.Password : new string('*', state.Password.Length);
            Utility.drawTextWithShadow(b, $"{Translations.Get("host.password")}: {password}", Game1.dialogueFont, new Vector2(left, y), Game1.textColor);

            y += 56;
            Utility.drawTextWithShadow(
                b,
                $"{Translations.Get("host.players")}: {state.PlayerCount}/{(state.MaxPlayers > 0 ? state.MaxPlayers : 0)}",
                Game1.smallFont,
                new Vector2(left, y),
                Game1.textColor);

            string countdown = state.Peers.Count == 0 ? MenuText.FormatCountdown(state.ExpiresAt) : string.Empty;
            if (countdown.Length > 0)
            {
                Utility.drawTextWithShadow(
                    b,
                    $"{Translations.Get("host.expiresIn")}: {countdown}",
                    Game1.smallFont,
                    new Vector2(left + 300, y),
                    Game1.textColor * 0.8f);
            }

            y += 44;
            Utility.drawTextWithShadow(
                b,
                $"{Translations.Get("menu.connectionStatus")}: {MenuText.DescribeConnection(state.Connection)}",
                Game1.smallFont,
                new Vector2(left, y),
                Game1.textColor);

            y += 44;
            Utility.drawTextWithShadow(b, Translations.Get("host.peerList"), Game1.smallFont, new Vector2(left, y), Game1.textColor);

            y += 36;
            if (state.Peers.Count == 0)
            {
                Utility.drawTextWithShadow(b, Translations.Get("host.noPeers"), Game1.smallFont, new Vector2(left + 24, y), Game1.textColor * 0.7f);
            }
            else
            {
                foreach (PeerSession peer in state.Peers)
                {
                    string rtt = peer.RoundTripTime.HasValue ? $"  ({peer.RoundTripTime.Value.TotalMilliseconds:F0} ms)" : string.Empty;
                    string line = $"{peer.ShortId}  -  {MenuText.DescribePeer(peer.State)}{rtt}";
                    Utility.drawTextWithShadow(b, line, Game1.smallFont, new Vector2(left + 24, y), MenuText.PeerColor(peer.State));
                    y += 34;
                }
            }

            bool inRoom = state.IsInRoom;
            this.copyRoomIdButton.Enabled = inRoom;
            this.copyPasswordButton.Enabled = inRoom && state.Password.Length > 0;
            this.copyInviteButton.Enabled = inRoom && state.Password.Length > 0;
            this.closeRoomButton.Enabled = inRoom;

            foreach (MenuButton button in this.buttons)
                button.Draw(b);

            if (!string.IsNullOrEmpty(state.ErrorMessage))
            {
                Utility.drawTextWithShadow(
                    b,
                    state.ErrorMessage,
                    Game1.smallFont,
                    new Vector2(left, this.yPositionOnScreen + this.height - 44),
                    Color.DarkRed);
            }
            else if (this.toast.Length > 0)
            {
                Utility.drawTextWithShadow(
                    b,
                    this.toast,
                    Game1.smallFont,
                    new Vector2(left, this.yPositionOnScreen + this.height - 44),
                    Color.DarkGreen);
            }

            base.draw(b);
            this.drawMouse(b);
        }
    }
}
