using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewConnect.Rooms;
using StardewConnect.Utilities;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace StardewConnect.UI
{
    /// <summary>Join view: room id, password, and friendly errors when the server says no.</summary>
    internal sealed class JoinRoomMenu : IClickableMenu
    {
        private const int MenuWidth = 700;
        private const int MenuHeight = 460;
        private const int FieldWidth = 380;
        private const int FieldHeight = 48;

        private readonly RoomSession session;
        private readonly TextBox roomIdBox;
        private readonly TextBox passwordBox;

        private MenuButton joinButton;
        private MenuButton cancelButton;
        private string localError = string.Empty;

        public JoinRoomMenu(RoomSession session)
            : base(
                (int)Utility.getTopLeftPositionForCenteringOnScreen(MenuWidth, MenuHeight).X,
                (int)Utility.getTopLeftPositionForCenteringOnScreen(MenuWidth, MenuHeight).Y,
                MenuWidth,
                MenuHeight,
                showUpperRightCloseButton: true)
        {
            this.session = session;

            Texture2D textBoxTexture = Game1.content.Load<Texture2D>("LooseSprites\\textBox");
            this.roomIdBox = new TextBox(textBoxTexture, null, Game1.smallFont, Game1.textColor)
            {
                Width = FieldWidth,
                Height = FieldHeight,
                limitWidth = false
            };
            this.passwordBox = new TextBox(textBoxTexture, null, Game1.smallFont, Game1.textColor)
            {
                Width = FieldWidth,
                Height = FieldHeight,
                limitWidth = false
            };

            this.LayoutComponents();
            this.Focus(this.roomIdBox);
        }

        private void LayoutComponents()
        {
            int left = this.xPositionOnScreen + 48;

            this.roomIdBox.X = left + 200;
            this.roomIdBox.Y = this.yPositionOnScreen + 130;

            this.passwordBox.X = left + 200;
            this.passwordBox.Y = this.yPositionOnScreen + 210;

            int buttonWidth = 220;
            int buttonHeight = 64;
            int buttonY = this.yPositionOnScreen + this.height - 120;

            this.joinButton = new MenuButton("join", new Rectangle(left, buttonY, buttonWidth, buttonHeight), Translations.Get("join.join"));
            this.cancelButton = new MenuButton("cancel", new Rectangle(left + buttonWidth + 24, buttonY, buttonWidth, buttonHeight), Translations.Get("join.cancel"));
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

            this.joinButton.Hovered = this.joinButton.Contains(x, y);
            this.cancelButton.Hovered = this.cancelButton.Contains(x, y);
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            base.receiveLeftClick(x, y, playSound);

            if (BoxBounds(this.roomIdBox).Contains(x, y))
            {
                this.Focus(this.roomIdBox);
                return;
            }

            if (BoxBounds(this.passwordBox).Contains(x, y))
            {
                this.Focus(this.passwordBox);
                return;
            }

            if (this.cancelButton.Contains(x, y))
            {
                Game1.playSound("bigDeSelect");
                this.exitThisMenu();
                return;
            }

            if (this.joinButton.Contains(x, y))
                this.TryJoin();
        }

        public override void receiveKeyPress(Keys key)
        {
            // Deliberately not calling base: while a text box has focus, letters must reach the
            // keyboard dispatcher instead of triggering menu shortcuts.
            switch (key)
            {
                case Keys.Escape:
                    Game1.playSound("bigDeSelect");
                    this.exitThisMenu();
                    break;

                case Keys.Enter:
                    this.TryJoin();
                    break;

                case Keys.Tab:
                    this.Focus(this.roomIdBox.Selected ? this.passwordBox : this.roomIdBox);
                    break;
            }
        }

        private void TryJoin()
        {
            if (this.session.IsBusy)
                return;

            string roomId = (this.roomIdBox.Text ?? string.Empty).Trim().ToUpperInvariant();
            string password = (this.passwordBox.Text ?? string.Empty).Trim();

            if (roomId.Length == 0 || password.Length == 0)
            {
                this.localError = Translations.Get("error.missingCredentials");
                Game1.playSound("cancel");
                return;
            }

            this.localError = string.Empty;
            Game1.playSound("smallSelect");
            this.session.JoinRoom(roomId, password);

            // The password must not linger in the UI once it has been handed over.
            this.passwordBox.Text = string.Empty;
        }

        private void Focus(TextBox box)
        {
            this.roomIdBox.Selected = box == this.roomIdBox;
            this.passwordBox.Selected = box == this.passwordBox;
            Game1.keyboardDispatcher.Subscriber = box;
        }

        private static Rectangle BoxBounds(TextBox box)
        {
            return new Rectangle(box.X, box.Y, box.Width, FieldHeight);
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
                Translations.Get("join.title"),
                this.xPositionOnScreen + (this.width / 2),
                this.yPositionOnScreen + 24);

            int labelLeft = this.xPositionOnScreen + 48;

            Utility.drawTextWithShadow(b, Translations.Get("join.roomId"), Game1.smallFont, new Vector2(labelLeft, this.roomIdBox.Y + 12), Game1.textColor);
            Utility.drawTextWithShadow(b, Translations.Get("join.password"), Game1.smallFont, new Vector2(labelLeft, this.passwordBox.Y + 12), Game1.textColor);

            this.roomIdBox.Draw(b);
            this.passwordBox.Draw(b);

            this.joinButton.Enabled = !this.session.IsBusy;
            this.joinButton.Draw(b);
            this.cancelButton.Draw(b);

            string status = $"{Translations.Get("menu.connectionStatus")}: {MenuText.DescribeConnection(state.Connection)}";
            Utility.drawTextWithShadow(b, status, Game1.smallFont, new Vector2(labelLeft, this.yPositionOnScreen + 290), Game1.textColor);

            string error = this.localError.Length > 0 ? this.localError : state.ErrorMessage;
            if (!string.IsNullOrEmpty(error))
            {
                Utility.drawTextWithShadow(
                    b,
                    error,
                    Game1.smallFont,
                    new Vector2(labelLeft, this.yPositionOnScreen + 330),
                    Color.DarkRed);
            }

            base.draw(b);
            this.drawMouse(b);
        }

        protected override void cleanupBeforeExit()
        {
            base.cleanupBeforeExit();

            // Never leave the password sitting in memory or in the keyboard subscriber.
            this.passwordBox.Text = string.Empty;
            this.roomIdBox.Selected = false;
            this.passwordBox.Selected = false;
            if (ReferenceEquals(Game1.keyboardDispatcher.Subscriber, this.roomIdBox) || ReferenceEquals(Game1.keyboardDispatcher.Subscriber, this.passwordBox))
                Game1.keyboardDispatcher.Subscriber = null;
        }
    }
}
