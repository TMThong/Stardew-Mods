using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StardewModdingAPI.Events;
using StardewValley.BellsAndWhistles;
using StardewValley.Events;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Tools;
using StardewValley;
using xTile.Dimensions;
using xTile.Layers;
using xTile.Tiles;

namespace MultiplayerMod.Framework.Patch.Mobile
{
    internal class SGamePatch : IPatch
    {
        public Type PATCH_TYPE { get; }
        public SGamePatch()
        {
            PATCH_TYPE = typeof(IModHelper).Assembly.GetType("StardewModdingAPI.Framework.SGame");
        }

        public void Apply(Harmony harmony)
        {
            //harmony.Patch(AccessTools.Method(PATCH_TYPE, "DrawImpl"), prefix: new HarmonyMethod(this.GetType(), nameof(prefix_DrawImpl)));
        }

        private static bool prefix_DrawImpl(Game1 __instance, GameTime gameTime, RenderTarget2D target_screen)
        {
            //EventManager events = __instance.Events;
            Game1.showingHealthBar = false;
            bool flag =  __instance.isLocalMultiplayerNewDayActive;
            if (flag)
            {
                __instance.GraphicsDevice.Clear(Game1.bgColor);
            }
            else
            {
                bool flag2 = target_screen != null;
                if (flag2)
                {
                    Game1.SetRenderTarget(target_screen);
                }
                bool isSaving = false;
                if (isSaving)
                {
                    __instance.GraphicsDevice.Clear(Game1.bgColor);
                    Game1.PushUIMode();
                    IClickableMenu activeClickableMenu = Game1.activeClickableMenu;
                    bool flag3 = activeClickableMenu != null;
                     
                    bool flag4 = Game1.overlayMenu != null;
                    if (flag4)
                    {
                        Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, default(Matrix?));
                        Game1.overlayMenu.draw(Game1.spriteBatch);
                        Game1.spriteBatch.End();
                    }
                    Game1.PopUIMode();
                }
                else
                {
                    __instance.GraphicsDevice.Clear(Game1.bgColor);
                    bool flag5 = Game1.activeClickableMenu != null && Game1.options.showMenuBackground && Game1.activeClickableMenu.showWithoutTransparencyIfOptionIsSet() && !__instance.takingMapScreenshot;
                    if (flag5)
                    {
                        Game1.PushUIMode();
                        Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, default(Matrix?));

                        IClickableMenu clickableMenu = null;
                        try
                        {
                            Game1.activeClickableMenu.drawBackground(Game1.spriteBatch);
                             
                        }
                        catch (Exception exception2)
                        {
                           // __instance.Monitor.Log("The " + clickableMenu.GetMenuChainLabel() + " menu crashed while drawing itself. SMAPI will force it to exit to avoid crashing the game.\n" + exception2.GetLogSummary(), LogLevel.Error);
                            Game1.activeClickableMenu.exitThisMenu(true);
                        }
                       // events.Rendered.RaiseEmpty<RenderedEventArgs>();
                        bool flag6 = Game1.specialCurrencyDisplay != null;
                        if (flag6)
                        {
                            Game1.specialCurrencyDisplay.Draw(Game1.spriteBatch);
                        }
                        Game1.spriteBatch.End();
                       // __instance.drawOverlays(Game1.spriteBatch);
                        Game1.PopUIMode();
                    }
                    else
                    {
                        bool emergencyLoading = false;
                        if (emergencyLoading)
                        {
                             
                        }
                        else
                        {
                            bool flag10 = Game1.gameMode == 11;
                            if (flag10)
                            {
                                Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, default(Matrix?));
                                //events.Rendering.RaiseEmpty<RenderingEventArgs>();
                                Game1.spriteBatch.DrawString(Game1.dialogueFont, Game1.content.LoadString("Strings\\StringsFromCSFiles:Game1.cs.3685"), new Vector2(16f, 16f), Color.HotPink);
                                Game1.spriteBatch.DrawString(Game1.dialogueFont, Game1.content.LoadString("Strings\\StringsFromCSFiles:Game1.cs.3686"), new Vector2(16f, 32f), new Color(0, 255, 0));
                               // Game1.spriteBatch.DrawString(Game1.dialogueFont, Game1.parseText(Game1.errorMessage, Game1.dialogueFont, Game1.graphics.GraphicsDevice.Viewport.Width, 1f), new Vector2(16f, 48f), Color.White);
                               // events.Rendered.RaiseEmpty<RenderedEventArgs>();
                                Game1.spriteBatch.End();
                            }
                            else
                            {
                                bool flag11 = Game1.currentMinigame != null;
                                if (flag11)
                                {
                                    
                                    Game1.currentMinigame.draw(Game1.spriteBatch);
                                    bool flag12 = Game1.globalFade && !Game1.menuUp && (!Game1.nameSelectUp || Game1.messagePause);
                                    if (flag12)
                                    {
                                        Game1.PushUIMode();
                                        Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, default(Matrix?));
                                        Game1.spriteBatch.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * ((Game1.gameMode == 0) ? (1f - Game1.fadeToBlackAlpha) : Game1.fadeToBlackAlpha));
                                        Game1.spriteBatch.End();
                                        Game1.PopUIMode();
                                    }
                                    Game1.PushUIMode();
                                    
                                    Game1.PopUIMode();
                                    
                                    Game1.SetRenderTarget(target_screen);
                                }
                                else
                                {
                                    bool showingEndOfNightStuff = Game1.showingEndOfNightStuff;
                                    if (showingEndOfNightStuff)
                                    {
                                        Game1.PushUIMode();
                                        Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, default(Matrix?));
                                        
                                        bool flag13 = Game1.activeClickableMenu != null;
                                        if (flag13)
                                        {
                                            IClickableMenu clickableMenu2 = null;
                                            try
                                            {
                                                 
                                                for (clickableMenu2 = Game1.activeClickableMenu; clickableMenu2 != null; clickableMenu2 = clickableMenu2.GetChildMenu())
                                                {
                                                    clickableMenu2.draw(Game1.spriteBatch);
                                                }
                                                
                                            }
                                            catch (Exception exception3)
                                            {
                                               
                                                Game1.activeClickableMenu.exitThisMenu(true);
                                            }
                                        }
                                        Game1.spriteBatch.End();
                                       
                                        Game1.PopUIMode();
                                    }
                                    else
                                    {
                                        bool flag14 = Game1.gameMode == 6 || (Game1.gameMode == 3 && Game1.currentLocation == null);
                                        if (flag14)
                                        {
                                            Game1.PushUIMode();
                                            __instance.GraphicsDevice.Clear(Game1.bgColor);
                                            Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, default(Matrix?));
                                             
                                            string text = "";
                                            int num = 0;
                                            while ((double)num < gameTime.TotalGameTime.TotalMilliseconds % 999.0 / 333.0)
                                            {
                                                text += ".";
                                                num++;
                                            }
                                            string text2 = Game1.content.LoadString("Strings\\StringsFromCSFiles:Game1.cs.3688");
                                            string text3 = text2 + text;
                                            string text4 = text2 + "... ";
                                            int widthOfString = SpriteText.getWidthOfString(text4, 999999);
                                            int num2 = 64;
                                            int num3 = 64;
                                            int num4 = ViewportExtensions.GetTitleSafeArea(Game1.graphics.GraphicsDevice.Viewport).Bottom - num2;
                                            SpriteText.drawString(Game1.spriteBatch, text3, num3, num4, 999999, widthOfString, num2, 1f, 0.88f, false, 0, text4, -1, 0);
                                            
                                            Game1.spriteBatch.End();
                                         
                                            Game1.PopUIMode();
                                        }
                                        else
                                        {
                                            byte b = 0;
                                            bool flag15 = Game1.gameMode == 0;
                                            if (flag15)
                                            {
                                                Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, default(Matrix?));
                                                bool flag16 = (b += 1) == 1;
                                                if (flag16)
                                                {
                                                  
                                                }
                                            }
                                            else
                                            {
                                                bool flag17 = Game1.gameMode == 3 && Game1.dayOfMonth == 0 && Game1.newDay;
                                                if (flag17)
                                                {
                                                    return false;
                                                }
                                                bool drawLighting = Game1.drawLighting;
                                                if (drawLighting)
                                                {
                                                    Game1.SetRenderTarget(Game1.lightmap);
                                                    __instance.GraphicsDevice.Clear(Color.White * 0f);
                                                    Matrix matrix = Matrix.Identity;
                                                    bool useUnscaledLighting = __instance.useUnscaledLighting;
                                                    if (useUnscaledLighting)
                                                    {
                                                        matrix = Matrix.CreateScale(Game1.options.zoomLevel);
                                                    }
                                                    Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.PointClamp, null, null, null, new Matrix?(matrix));
                                                    bool flag18 = (b += 1) == 1;
                                                    if (flag18)
                                                    {
                                                         
                                                    }
                                                    Color color = (Game1.currentLocation.Name.StartsWith("UndergroundMine") && Game1.currentLocation is MineShaft) ? (Game1.currentLocation as MineShaft).getLightingColor(gameTime) : ((Game1.ambientLight.Equals(Color.White) || (Game1.IsRainingHere(null) && Game1.currentLocation.isOutdoors)) ? Game1.outdoorLight : Game1.ambientLight);
                                                    float scale2 = 1f;
                                                    bool flag19 = Game1.player.hasBuff(26);
                                                    if (flag19)
                                                    {
                                                        bool flag20 = color == Color.White;
                                                        if (flag20)
                                                        {
                                                            color = new Color(0.75f, 0.75f, 0.75f);
                                                        }
                                                        else
                                                        {
                                                            color.R = (byte)Utility.Lerp((float)color.R, 255f, 0.5f);
                                                            color.G = (byte)Utility.Lerp((float)color.G, 255f, 0.5f);
                                                            color.B = (byte)Utility.Lerp((float)color.B, 255f, 0.5f);
                                                        }
                                                        scale2 = 0.33f;
                                                    }
                                                    Game1.spriteBatch.Draw(Game1.staminaRect, Game1.lightmap.Bounds, color);
                                                    foreach (LightSource lightSource in Game1.currentLightSources)
                                                    {
                                                        bool flag21 = (Game1.IsRainingHere(null) || Game1.isDarkOut());
                                                        if (!flag21)
                                                        {
                                                            bool flag22 = lightSource.PlayerID != 0L && lightSource.PlayerID != Game1.player.UniqueMultiplayerID;
                                                            if (flag22)
                                                            {
                                                                Farmer farmerMaybeOffline = Game1.getFarmerMaybeOffline(lightSource.PlayerID);
                                                                bool flag23 = farmerMaybeOffline == null || (farmerMaybeOffline.currentLocation != null && farmerMaybeOffline.currentLocation.Name != Game1.currentLocation.Name) || farmerMaybeOffline.hidden;
                                                                if (flag23)
                                                                {
                                                                    continue;
                                                                }
                                                            }
                                                            bool flag24 = Utility.isOnScreen(lightSource.position, (int)(lightSource.radius * 64f * 4f));
                                                            if (flag24)
                                                            {
                                                                Game1.spriteBatch.Draw(lightSource.lightTexture, Game1.GlobalToLocal(Game1.viewport, lightSource.position) / (float)(Game1.options.lightingQuality / 2), new Microsoft.Xna.Framework.Rectangle?(lightSource.lightTexture.Bounds), lightSource.color.Value * scale2, 0f, new Vector2((float)(lightSource.lightTexture.Bounds.Width / 2), (float)(lightSource.lightTexture.Bounds.Height / 2)), lightSource.radius / (float)(Game1.options.lightingQuality / 2), SpriteEffects.None, 0.9f);
                                                            }
                                                        }
                                                    }
                                                    Game1.spriteBatch.End();
                                                    Game1.SetRenderTarget(target_screen);
                                                }
                                                __instance.GraphicsDevice.Clear(Game1.bgColor);
                                                Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, default(Matrix?));
                                                bool flag25 = (b += 1) == 1;
                                                if (flag25)
                                                {
                                                   // events.Rendering.RaiseEmpty<RenderingEventArgs>();
                                                }
                                             //   events.RenderingWorld.RaiseEmpty<RenderingWorldEventArgs>();
                                                bool flag26 = Game1.background != null;
                                                if (flag26)
                                                {
                                                    Game1.background.draw(Game1.spriteBatch);
                                                }
                                                Game1.currentLocation.drawBackground(Game1.spriteBatch);
                                                Game1.mapDisplayDevice.BeginScene(Game1.spriteBatch);
                                                Game1.currentLocation.Map.GetLayer("Back").Draw(Game1.mapDisplayDevice, Game1.viewport, Location.Origin, false, 4);
                                                Game1.currentLocation.drawWater(Game1.spriteBatch);
                                                Game1.spriteBatch.End();
                                                Game1.spriteBatch.Begin(SpriteSortMode.FrontToBack, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, default(Matrix?));
                                                Game1.currentLocation.drawFloorDecorations(Game1.spriteBatch);
                                                Game1.spriteBatch.End();
                                                Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, default(Matrix?));
                                             //   __instance._farmerShadows.Clear();
                                                bool flag27 = Game1.currentLocation.currentEvent != null && !Game1.currentLocation.currentEvent.isFestival && Game1.currentLocation.currentEvent.farmerActors.Count > 0;
                                                if (flag27)
                                                {
                                                    foreach (Farmer farmer in Game1.currentLocation.currentEvent.farmerActors)
                                                    {
                                                        bool flag28 = (farmer.IsLocalPlayer && Game1.displayFarmer) || !farmer.hidden;
                                                        if (flag28)
                                                        {
                                                   //         __instance._farmerShadows.Add(farmer);
                                                        }
                                                    }
                                                }
                                                else
                                                {
                                                    foreach (Farmer farmer2 in Game1.currentLocation.farmers)
                                                    {
                                                        bool flag29 = (farmer2.IsLocalPlayer && Game1.displayFarmer) || !farmer2.hidden;
                                                        if (flag29)
                                                        {
                                                          //  __instance._farmerShadows.Add(farmer2);
                                                        }
                                                    }
                                                }
                                                bool flag30 = !Game1.currentLocation.shouldHideCharacters();
                                                if (flag30)
                                                {
                                                    bool flag31 = Game1.CurrentEvent == null;
                                                    if (flag31)
                                                    {
                                                        foreach (NPC npc in Game1.currentLocation.characters)
                                                        {
                                                            bool flag32 = !npc.swimming && !npc.HideShadow && !npc.IsInvisible && !__instance.checkCharacterTilesForShadowDrawFlag(npc);
                                                            if (flag32)
                                                            {
                                                                Game1.spriteBatch.Draw(Game1.shadowTexture, Game1.GlobalToLocal(Game1.viewport, npc.GetShadowOffset() + npc.Position + new Vector2((float)(npc.GetSpriteWidthForPositioning() * 4) / 2f, (float)(npc.GetBoundingBox().Height + ((!npc.IsMonster) ? 12 : 0)))), new Microsoft.Xna.Framework.Rectangle?(Game1.shadowTexture.Bounds), Color.White, 0f, new Vector2((float)Game1.shadowTexture.Bounds.Center.X, (float)Game1.shadowTexture.Bounds.Center.Y), Math.Max(0f, (4f + (float)npc.yJumpOffset / 40f) * npc.scale), SpriteEffects.None, Math.Max(0f, (float)npc.getStandingY() / 10000f) - 1E-06f);
                                                            }
                                                        }
                                                    }
                                                    else
                                                    {
                                                        foreach (NPC npc2 in Game1.CurrentEvent.actors)
                                                        {
                                                            bool flag33 = (Game1.CurrentEvent == null || !Game1.CurrentEvent.ShouldHideCharacter(npc2)) && !npc2.swimming && !npc2.HideShadow && !__instance.checkCharacterTilesForShadowDrawFlag(npc2);
                                                            if (flag33)
                                                            {
                                                                Game1.spriteBatch.Draw(Game1.shadowTexture, Game1.GlobalToLocal(Game1.viewport, npc2.GetShadowOffset() + npc2.Position + new Vector2((float)(npc2.GetSpriteWidthForPositioning() * 4) / 2f, (float)(npc2.GetBoundingBox().Height + ((!npc2.IsMonster) ? ((npc2.Sprite.SpriteHeight <= 16) ? -4 : 12) : 0)))), new Microsoft.Xna.Framework.Rectangle?(Game1.shadowTexture.Bounds), Color.White, 0f, new Vector2((float)Game1.shadowTexture.Bounds.Center.X, (float)Game1.shadowTexture.Bounds.Center.Y), Math.Max(0f, 4f + (float)npc2.yJumpOffset / 40f) * npc2.scale, SpriteEffects.None, Math.Max(0f, (float)npc2.getStandingY() / 10000f) - 1E-06f);
                                                            }
                                                        }
                                                    }
                                                     
                                                }
                                                Layer layer = Game1.currentLocation.Map.GetLayer("Buildings");
                                                layer.Draw(Game1.mapDisplayDevice, Game1.viewport, Location.Origin, false, 4);
                                                Game1.mapDisplayDevice.EndScene();
                                                bool flag35 = Game1.currentLocation != null;
                                                if (flag35)
                                                {
                                                  //  Game1.spriteBatch.Draw(Game1.mouseCursors, Game1.GlobalToLocal(Game1.viewport, Game1.currentLocation.tapToMove.targetNPC.Position + new Vector2((float)(Game1.currentLocation.tapToMove.targetNPC.Sprite.SpriteWidth * 4) / 2f - 32f, (float)(Game1.currentLocation.tapToMove.targetNPC.GetBoundingBox().Height + (Game1.currentLocation.tapToMove.targetNPC.IsMonster ? 0 : 12) - 32))), new Microsoft.Xna.Framework.Rectangle?(new Microsoft.Xna.Framework.Rectangle(194, 388, 16, 16)), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.58f);
                                                }
                                                Game1.spriteBatch.End();
                                                Game1.spriteBatch.Begin(SpriteSortMode.FrontToBack, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, default(Matrix?));
                                                bool flag36 = !Game1.currentLocation.shouldHideCharacters();
                                                if (flag36)
                                                {
                                                    bool flag37 = Game1.CurrentEvent == null;
                                                    if (flag37)
                                                    {
                                                        foreach (NPC npc3 in Game1.currentLocation.characters)
                                                        {
                                                            bool flag38 = !npc3.swimming && !npc3.HideShadow && !npc3.isInvisible && __instance.checkCharacterTilesForShadowDrawFlag(npc3);
                                                            if (flag38)
                                                            {
                                                                Game1.spriteBatch.Draw(Game1.shadowTexture, Game1.GlobalToLocal(Game1.viewport, npc3.GetShadowOffset() + npc3.Position + new Vector2((float)(npc3.GetSpriteWidthForPositioning() * 4) / 2f, (float)(npc3.GetBoundingBox().Height + ((!npc3.IsMonster) ? 12 : 0)))), new Microsoft.Xna.Framework.Rectangle?(Game1.shadowTexture.Bounds), Color.White, 0f, new Vector2((float)Game1.shadowTexture.Bounds.Center.X, (float)Game1.shadowTexture.Bounds.Center.Y), Math.Max(0f, (4f + (float)npc3.yJumpOffset / 40f) * npc3.scale), SpriteEffects.None, Math.Max(0f, (float)npc3.getStandingY() / 10000f) - 1E-06f);
                                                            }
                                                        }
                                                    }
                                                    else
                                                    {
                                                        foreach (NPC npc4 in Game1.CurrentEvent.actors)
                                                        {
                                                            bool flag39 = (Game1.CurrentEvent == null || !Game1.CurrentEvent.ShouldHideCharacter(npc4)) && !npc4.swimming && !npc4.HideShadow && __instance.checkCharacterTilesForShadowDrawFlag(npc4);
                                                            if (flag39)
                                                            {
                                                                Game1.spriteBatch.Draw(Game1.shadowTexture, Game1.GlobalToLocal(Game1.viewport, npc4.GetShadowOffset() + npc4.Position + new Vector2((float)(npc4.GetSpriteWidthForPositioning() * 4) / 2f, (float)(npc4.GetBoundingBox().Height + ((!npc4.IsMonster) ? 12 : 0)))), new Microsoft.Xna.Framework.Rectangle?(Game1.shadowTexture.Bounds), Color.White, 0f, new Vector2((float)Game1.shadowTexture.Bounds.Center.X, (float)Game1.shadowTexture.Bounds.Center.Y), Math.Max(0f, (4f + (float)npc4.yJumpOffset / 40f) * npc4.scale), SpriteEffects.None, Math.Max(0f, (float)npc4.getStandingY() / 10000f) - 1E-06f);
                                                            }
                                                        }
                                                    }
                                                    
                                                }
                                                bool flag41 = (Game1.eventUp || Game1.killScreen) && !Game1.killScreen && Game1.currentLocation.currentEvent != null;
                                                if (flag41)
                                                {
                                                    Game1.currentLocation.currentEvent.draw(Game1.spriteBatch);
                                                }
                                                bool flag42 = Game1.player.currentUpgrade != null && Game1.player.currentUpgrade.daysLeftTillUpgradeDone <= 3 && Game1.currentLocation.Name.Equals("Farm");
                                                if (flag42)
                                                {
                                                    Game1.spriteBatch.Draw(Game1.player.currentUpgrade.workerTexture, Game1.GlobalToLocal(Game1.viewport, Game1.player.currentUpgrade.positionOfCarpenter), new Microsoft.Xna.Framework.Rectangle?(Game1.player.currentUpgrade.getSourceRectangle()), Color.White, 0f, Vector2.Zero, 1f, SpriteEffects.None, (Game1.player.currentUpgrade.positionOfCarpenter.Y + 48f) / 10000f);
                                                }
                                                Game1.currentLocation.draw(Game1.spriteBatch);
                                                foreach (Vector2 vector in Game1.crabPotOverlayTiles.Keys)
                                                {
                                                    Tile tile = layer.Tiles[(int)vector.X, (int)vector.Y];
                                                    bool flag43 = tile != null;
                                                    if (flag43)
                                                    {
                                                        Vector2 vector2 = Game1.GlobalToLocal(Game1.viewport, vector * 64f);
                                                        Location location = new Location((int)vector2.X, (int)vector2.Y);
                                                        Game1.mapDisplayDevice.DrawTile(tile, location, (vector.Y * 64f - 1f) / 10000f);
                                                    }
                                                }
                                                bool flag44 = Game1.eventUp && Game1.currentLocation.currentEvent != null;
                                                if (flag44)
                                                {
                                                    string messageToScreen = Game1.currentLocation.currentEvent.messageToScreen;
                                                }
                                                bool flag45 = Game1.player.ActiveObject == null && (Game1.player.UsingTool || Game1.pickingTool) && Game1.player.CurrentTool != null && (!Game1.player.CurrentTool.Name.Equals("Seeds") || Game1.pickingTool);
                                                if (flag45)
                                                {
                                                    Game1.drawTool(Game1.player);
                                                }
                                                bool flag46 = Game1.currentLocation.Name.Equals("Farm");
                                                if (flag46)
                                                {
                                                   // __instance.drawFarmBuildings();
                                                }
                                                bool flag47 = Game1.tvStation >= 0;
                                                if (flag47)
                                                {
                                                    Game1.spriteBatch.Draw(Game1.tvStationTexture, Game1.GlobalToLocal(Game1.viewport, new Vector2(400f, 160f)), new Microsoft.Xna.Framework.Rectangle?(new Microsoft.Xna.Framework.Rectangle(Game1.tvStation * 24, 0, 24, 15)), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1E-08f);
                                                }
                                                bool panMode = Game1.panMode;
                                                if (panMode)
                                                {
                                                    Game1.spriteBatch.Draw(Game1.fadeToBlackRect, new Microsoft.Xna.Framework.Rectangle((int)Math.Floor((double)(Game1.getOldMouseX() + Game1.viewport.X) / 64.0) * 64 - Game1.viewport.X, (int)Math.Floor((double)(Game1.getOldMouseY() + Game1.viewport.Y) / 64.0) * 64 - Game1.viewport.Y, 64, 64), Color.Lime * 0.75f);
                                                    foreach (Warp warp in Game1.currentLocation.warps)
                                                    {
                                                        Game1.spriteBatch.Draw(Game1.fadeToBlackRect, new Microsoft.Xna.Framework.Rectangle(warp.X * 64 - Game1.viewport.X, warp.Y * 64 - Game1.viewport.Y, 64, 64), Color.Red * 0.75f);
                                                    }
                                                }
                                                Game1.mapDisplayDevice.BeginScene(Game1.spriteBatch);
                                                Game1.currentLocation.Map.GetLayer("Front").Draw(Game1.mapDisplayDevice, Game1.viewport, Location.Origin, false, 4);
                                                Game1.mapDisplayDevice.EndScene();
                                                Game1.currentLocation.drawAboveFrontLayer(Game1.spriteBatch);
                                                 
                                                Game1.spriteBatch.End();
                                                Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, default(Matrix?));
                                                bool flag49 = Game1.currentLocation.Map.GetLayer("AlwaysFront") != null;
                                                if (flag49)
                                                {
                                                    Game1.mapDisplayDevice.BeginScene(Game1.spriteBatch);
                                                    Game1.currentLocation.Map.GetLayer("AlwaysFront").Draw(Game1.mapDisplayDevice, Game1.viewport, Location.Origin, false, 4);
                                                    Game1.mapDisplayDevice.EndScene();
                                                }
                                                bool flag50 = Game1.toolHold > 400f && Game1.player.CurrentTool.UpgradeLevel >= 1 && Game1.player.canReleaseTool;
                                                if (flag50)
                                                {
                                                    Color color2 = Color.White;
                                                    switch ((int)(Game1.toolHold / 600f) + 2)
                                                    {
                                                        case 1:
                                                            color2 = Tool.copperColor;
                                                            break;
                                                        case 2:
                                                            color2 = Tool.steelColor;
                                                            break;
                                                        case 3:
                                                            color2 = Tool.goldColor;
                                                            break;
                                                        case 4:
                                                            color2 = Tool.iridiumColor;
                                                            break;
                                                    }
                                                    Game1.spriteBatch.Draw(Game1.littleEffect, new Microsoft.Xna.Framework.Rectangle((int)Game1.player.getLocalPosition(Game1.viewport).X - 2, (int)Game1.player.getLocalPosition(Game1.viewport).Y - ((!Game1.player.CurrentTool.Name.Equals("Watering Can")) ? 64 : 0) - 2, (int)(Game1.toolHold % 600f * 0.08f) + 4, 12), Color.Black);
                                                    Game1.spriteBatch.Draw(Game1.littleEffect, new Microsoft.Xna.Framework.Rectangle((int)Game1.player.getLocalPosition(Game1.viewport).X, (int)Game1.player.getLocalPosition(Game1.viewport).Y - ((!Game1.player.CurrentTool.Name.Equals("Watering Can")) ? 64 : 0), (int)(Game1.toolHold % 600f * 0.08f), 8), color2);
                                                }
                                                bool flag51 = !Game1.IsFakedBlackScreen();
                                                if (flag51)
                                                {
                                                    __instance.drawWeather(gameTime, target_screen);
                                                }
                                                bool flag52 = Game1.farmEvent != null;
                                                if (flag52)
                                                {
                                                    Game1.farmEvent.draw(Game1.spriteBatch);
                                                }
                                                bool flag53 = Game1.currentLocation.LightLevel > 0f && Game1.timeOfDay < 2000;
                                                if (flag53)
                                                {
                                                    Game1.spriteBatch.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * Game1.currentLocation.LightLevel);
                                                }
                                                bool screenGlow = Game1.screenGlow;
                                                if (screenGlow)
                                                {
                                                    Game1.spriteBatch.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Game1.screenGlowColor * Game1.screenGlowAlpha);
                                                }
                                                Game1.currentLocation.drawAboveAlwaysFrontLayer(Game1.spriteBatch);
                                                bool flag54 = Game1.player.CurrentTool != null && Game1.player.CurrentTool is FishingRod && ((Game1.player.CurrentTool as FishingRod).isTimingCast || (Game1.player.CurrentTool as FishingRod).castingChosenCountdown > 0f || (Game1.player.CurrentTool as FishingRod).fishCaught || (Game1.player.CurrentTool as FishingRod).showingTreasure);
                                                if (flag54)
                                                {
                                                    Game1.player.CurrentTool.draw(Game1.spriteBatch);
                                                }
                                                Game1.spriteBatch.End();
                                                Game1.spriteBatch.Begin(SpriteSortMode.FrontToBack, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, default(Matrix?));
                                                bool flag55 = Game1.eventUp && Game1.currentLocation.currentEvent != null;
                                                if (flag55)
                                                {
                                                    foreach (NPC npc5 in Game1.currentLocation.currentEvent.actors)
                                                    {
                                                        bool isEmoting = npc5.isEmoting;
                                                        if (isEmoting)
                                                        {
                                                            Vector2 localPosition = npc5.getLocalPosition(Game1.viewport);
                                                            bool flag56 = npc5.NeedsBirdieEmoteHack();
                                                            if (flag56)
                                                            {
                                                                localPosition.X += 64f;
                                                            }
                                                            localPosition.Y -= 140f;
                                                            bool flag57 = npc5.Age == 2;
                                                            if (flag57)
                                                            {
                                                                localPosition.Y += 32f;
                                                            }
                                                            else
                                                            {
                                                                bool flag58 = npc5.Gender == 1;
                                                                if (flag58)
                                                                {
                                                                    localPosition.Y += 10f;
                                                                }
                                                            }
                                                            Game1.spriteBatch.Draw(Game1.emoteSpriteSheet, localPosition, new Microsoft.Xna.Framework.Rectangle?(new Microsoft.Xna.Framework.Rectangle(npc5.CurrentEmoteIndex * 16 % Game1.emoteSpriteSheet.Width, npc5.CurrentEmoteIndex * 16 / Game1.emoteSpriteSheet.Width * 16, 16, 16)), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, (float)npc5.getStandingY() / 10000f);
                                                        }
                                                    }
                                                }
                                                Game1.spriteBatch.End();
                                                bool flag59 = Game1.drawLighting && !Game1.IsFakedBlackScreen();
                                                if (flag59)
                                                {
                                                    Game1.spriteBatch.Begin(SpriteSortMode.Deferred, null, SamplerState.LinearClamp, null, null, null, default(Matrix?));
                                                    Viewport viewport = __instance.GraphicsDevice.Viewport;
                                                    viewport.Bounds = ((target_screen != null) ? target_screen.Bounds : __instance.GraphicsDevice.PresentationParameters.Bounds);
                                                    __instance.GraphicsDevice.Viewport = viewport;
                                                    float num5 = (float)(Game1.options.lightingQuality / 2);
                                                    bool useUnscaledLighting2 = __instance.useUnscaledLighting;
                                                    if (useUnscaledLighting2)
                                                    {
                                                        num5 /= Game1.options.zoomLevel;
                                                    }
                                                    Game1.spriteBatch.Draw(Game1.lightmap, Vector2.Zero, new Microsoft.Xna.Framework.Rectangle?(Game1.lightmap.Bounds), Color.White, 0f, Vector2.Zero, num5, SpriteEffects.None, 1f);
                                                    bool flag60 = Game1.IsRainingHere(null) && Game1.currentLocation.isOutdoors && !(Game1.currentLocation is Desert);
                                                    if (flag60)
                                                    {
                                                        Game1.spriteBatch.Draw(Game1.staminaRect, viewport.Bounds, Color.OrangeRed * 0.45f);
                                                    }
                                                    Game1.spriteBatch.End();
                                                }
                                                Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, default(Matrix?));
                                               // events.RenderedWorld.RaiseEmpty<RenderedWorldEventArgs>();
                                                bool drawGrid = Game1.drawGrid;
                                                if (drawGrid)
                                                {
                                                    int num6 = -Game1.viewport.X % 64;
                                                    float num7 = (float)(-(float)Game1.viewport.Y % 64);
                                                    for (int i = num6; i < Game1.graphics.GraphicsDevice.Viewport.Width; i += 64)
                                                    {
                                                        Game1.spriteBatch.Draw(Game1.staminaRect, new Microsoft.Xna.Framework.Rectangle(i, (int)num7, 1, Game1.graphics.GraphicsDevice.Viewport.Height), Color.Red * 0.5f);
                                                    }
                                                    for (float num8 = num7; num8 < (float)Game1.graphics.GraphicsDevice.Viewport.Height; num8 += 64f)
                                                    {
                                                        Game1.spriteBatch.Draw(Game1.staminaRect, new Microsoft.Xna.Framework.Rectangle(num6, (int)num8, Game1.graphics.GraphicsDevice.Viewport.Width, 1), Color.Red * 0.5f);
                                                    }
                                                }
                                                bool flag61 = Game1.ShouldShowOnscreenUsernames() && Game1.currentLocation != null;
                                                if (flag61)
                                                {
                                                    Game1.currentLocation.DrawFarmerUsernames(Game1.spriteBatch);
                                                }
                                                bool flag62 = Game1.currentBillboard != 0 && !__instance.takingMapScreenshot;
                                                if (flag62)
                                                {
                                                    __instance.drawBillboard();
                                                }
                                                bool flag63 = !Game1.eventUp && Game1.farmEvent == null && Game1.currentBillboard == 0 && Game1.gameMode == 3 && !__instance.takingMapScreenshot && Game1.isOutdoorMapSmallerThanViewport();
                                                if (flag63)
                                                {
                                                    Game1.spriteBatch.Draw(Game1.fadeToBlackRect, new Microsoft.Xna.Framework.Rectangle(0, 0, -Game1.viewport.X, Game1.graphics.GraphicsDevice.Viewport.Height), Color.Black);
                                                    Game1.spriteBatch.Draw(Game1.fadeToBlackRect, new Microsoft.Xna.Framework.Rectangle(-Game1.viewport.X + Game1.currentLocation.map.Layers[0].LayerWidth * 64, 0, Game1.graphics.GraphicsDevice.Viewport.Width - (-Game1.viewport.X + Game1.currentLocation.map.Layers[0].LayerWidth * 64), Game1.graphics.GraphicsDevice.Viewport.Height), Color.Black);
                                                    Game1.spriteBatch.Draw(Game1.fadeToBlackRect, new Microsoft.Xna.Framework.Rectangle(0, 0, Game1.graphics.GraphicsDevice.Viewport.Width, -Game1.viewport.Y), Color.Black);
                                                    Game1.spriteBatch.Draw(Game1.fadeToBlackRect, new Microsoft.Xna.Framework.Rectangle(0, -Game1.viewport.Y + Game1.currentLocation.map.Layers[0].LayerHeight * 64, Game1.graphics.GraphicsDevice.Viewport.Width, Game1.graphics.GraphicsDevice.Viewport.Height - (-Game1.viewport.Y + Game1.currentLocation.map.Layers[0].LayerHeight * 64)), Color.Black);
                                                }
                                                Game1.spriteBatch.End();
                                                Game1.PushUIMode();
                                                Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, default(Matrix?));
                                                bool flag64 = (Game1.displayHUD || Game1.eventUp) && Game1.currentBillboard == 0 && Game1.gameMode == 3 && !Game1.freezeControls && !Game1.panMode && !Game1.HostPaused && !__instance.takingMapScreenshot;
                                                if (flag64)
                                                {
                                                  //  events.RenderingHud.RaiseEmpty<RenderingHudEventArgs>();
                                                  //  __instance.drawHUD();
                                                  //  events.RenderedHud.RaiseEmpty<RenderedHudEventArgs>();
                                                    bool flag65 = !__instance.takingMapScreenshot;
                                                    if (flag65)
                                                    {
                                                        //__instance.DrawGreenPlacementBoundsMethod.Invoke(Array.Empty<object>());
                                                    }
                                                }
                                                else
                                                {
                                                    bool flag66 = Game1.activeClickableMenu == null;
                                                    if (flag66)
                                                    {
                                                        FarmEvent farmEvent = Game1.farmEvent;
                                                    }
                                                }
                                                bool flag67 = Game1.hudMessages.Count > 0 && !__instance.takingMapScreenshot;
                                                if (flag67)
                                                {
                                                    for (int j = Game1.hudMessages.Count - 1; j >= 0; j--)
                                                    {
                                                        Game1.hudMessages[j].draw(Game1.spriteBatch, j);
                                                    }
                                                }
                                                Game1.spriteBatch.End();
                                                Game1.PopUIMode();
                                                Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, default(Matrix?));
                                            }
                                            bool flag68 = Game1.farmEvent != null;
                                            if (flag68)
                                            {
                                                Game1.farmEvent.draw(Game1.spriteBatch);
                                                Game1.spriteBatch.End();
                                                Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, default(Matrix?));
                                            }
                                            Game1.PushUIMode();
                                            bool flag69 = Game1.dialogueUp && !Game1.nameSelectUp && !Game1.messagePause && (Game1.activeClickableMenu == null || !(Game1.activeClickableMenu is DialogueBox)) && !__instance.takingMapScreenshot;
                                            if (flag69)
                                            {
                                                //__instance.drawDialogueBox();
                                            }
                                            bool flag70 = Game1.progressBar && !__instance.takingMapScreenshot;
                                            if (flag70)
                                            {
                                                Game1.spriteBatch.Draw(Game1.fadeToBlackRect, new Microsoft.Xna.Framework.Rectangle((ViewportExtensions.GetTitleSafeArea(Game1.graphics.GraphicsDevice.Viewport).Width - Game1.dialogueWidth) / 2, ViewportExtensions.GetTitleSafeArea(Game1.graphics.GraphicsDevice.Viewport).Bottom - 128, Game1.dialogueWidth, 32), Color.LightGray);
                                                Game1.spriteBatch.Draw(Game1.staminaRect, new Microsoft.Xna.Framework.Rectangle((ViewportExtensions.GetTitleSafeArea(Game1.graphics.GraphicsDevice.Viewport).Width - Game1.dialogueWidth) / 2, ViewportExtensions.GetTitleSafeArea(Game1.graphics.GraphicsDevice.Viewport).Bottom - 128, (int)(Game1.pauseAccumulator / Game1.pauseTime * (float)Game1.dialogueWidth), 32), Color.DimGray);
                                            }
                                            Game1.spriteBatch.End();
                                            Game1.PopUIMode();
                                            Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, default(Matrix?));
                                            bool flag71 = Game1.eventUp && Game1.currentLocation != null && Game1.currentLocation.currentEvent != null;
                                            if (flag71)
                                            {
                                                Game1.currentLocation.currentEvent.drawAfterMap(Game1.spriteBatch);
                                            }
                                            bool flag72 = !Game1.IsFakedBlackScreen() && Game1.IsRainingHere(null) && Game1.currentLocation != null && Game1.currentLocation.isOutdoors && !(Game1.currentLocation is Desert);
                                            if (flag72)
                                            {
                                                Game1.spriteBatch.Draw(Game1.staminaRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Blue * 0.2f);
                                            }
                                            bool flag73 = (Game1.fadeToBlack || Game1.globalFade) && !Game1.menuUp && (!Game1.nameSelectUp || Game1.messagePause) && !__instance.takingMapScreenshot;
                                            if (flag73)
                                            {
                                                Game1.spriteBatch.End();
                                                Game1.PushUIMode();
                                                Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, default(Matrix?));
                                                Game1.spriteBatch.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * ((Game1.gameMode == 0) ? (1f - Game1.fadeToBlackAlpha) : Game1.fadeToBlackAlpha));
                                                Game1.spriteBatch.End();
                                                Game1.PopUIMode();
                                                Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, default(Matrix?));
                                            }
                                            else
                                            {
                                                bool flag74 = Game1.flashAlpha > 0f && !__instance.takingMapScreenshot;
                                                if (flag74)
                                                {
                                                    bool screenFlash = Game1.options.screenFlash;
                                                    if (screenFlash)
                                                    {
                                                        Game1.spriteBatch.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.White * Math.Min(1f, Game1.flashAlpha));
                                                    }
                                                    Game1.flashAlpha -= 0.1f;
                                                }
                                            }
                                            bool flag75 = (Game1.messagePause || Game1.globalFade) && Game1.dialogueUp && !__instance.takingMapScreenshot;
                                            if (flag75)
                                            {
                                             //   __instance.drawDialogueBox();
                                            }
                                            bool flag76 = !__instance.takingMapScreenshot;
                                            if (flag76)
                                            {
                                                foreach (TemporaryAnimatedSprite temporaryAnimatedSprite in Game1.screenOverlayTempSprites)
                                                {
                                                    temporaryAnimatedSprite.draw(Game1.spriteBatch, true, 0, 0, 1f);
                                                }
                                                Game1.spriteBatch.End();
                                                Game1.PushUIMode();
                                                Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, default(Matrix?));
                                                foreach (TemporaryAnimatedSprite temporaryAnimatedSprite2 in Game1.uiOverlayTempSprites)
                                                {
                                                    temporaryAnimatedSprite2.draw(Game1.spriteBatch, true, 0, 0, 1f);
                                                }
                                                Game1.spriteBatch.End();
                                                Game1.PopUIMode();
                                                Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, default(Matrix?));
                                            }
                                            bool debugMode = Game1.debugMode;
                                            if (debugMode)
                                            {
                                              //  StringBuilder value = __instance.DebugStringBuilderField.GetValue();
                                                 
                                            }
                                            Game1.spriteBatch.End();
                                            Game1.PushUIMode();
                                            Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, default(Matrix?));
                                            bool flag77 = Game1.showKeyHelp && !__instance.takingMapScreenshot;
                                            if (flag77)
                                            {
                                                Game1.spriteBatch.DrawString(Game1.smallFont, Game1.keyHelpString, new Vector2(64f, (float)(Game1.viewport.Height - 64 - (Game1.dialogueUp ? (192 + (Game1.isQuestion ? (Game1.questionChoices.Count * 64) : 0)) : 0)) - Game1.smallFont.MeasureString(Game1.keyHelpString).Y), Color.LightGray, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0.9999999f);
                                            }
                                            bool flag78 = Game1.activeClickableMenu != null && !__instance.takingMapScreenshot;
                                            if (flag78)
                                            {
                                                IClickableMenu clickableMenu3 = null;
                                                try
                                                {
                                                  //  events.RenderingActiveMenu.RaiseEmpty<RenderingActiveMenuEventArgs>();
                                                    for (clickableMenu3 = Game1.activeClickableMenu; clickableMenu3 != null; clickableMenu3 = clickableMenu3.GetChildMenu())
                                                    {
                                                        clickableMenu3.draw(Game1.spriteBatch);
                                                    }
                                                   // events.RenderedActiveMenu.RaiseEmpty<RenderedActiveMenuEventArgs>();
                                                }
                                                catch (Exception exception4)
                                                {
                                                  //  __instance.Monitor.Log("The " + clickableMenu3.GetMenuChainLabel() + " menu crashed while drawing itself. SMAPI will force it to exit to avoid crashing the game.\n" + exception4.GetLogSummary(), LogLevel.Error);
                                                    Game1.activeClickableMenu.exitThisMenu(true);
                                                }
                                            }
                                            else
                                            {
                                                bool flag79 = Game1.farmEvent != null;
                                                if (flag79)
                                                {
                                                    Game1.farmEvent.drawAboveEverything(Game1.spriteBatch);
                                                }
                                            }
                                            bool flag80 = Game1.specialCurrencyDisplay != null;
                                            if (flag80)
                                            {
                                                Game1.specialCurrencyDisplay.Draw(Game1.spriteBatch);
                                            }
                                            bool flag81 = Game1.emoteMenu != null && !__instance.takingMapScreenshot;
                                            if (flag81)
                                            {
                                                Game1.emoteMenu.draw(Game1.spriteBatch);
                                            }
                                            bool flag82 = Game1.HostPaused && !__instance.takingMapScreenshot;
                                            if (flag82)
                                            {
                                                string text5 = Game1.content.LoadString("Strings\\StringsFromCSFiles:DayTimeMoneyBox.cs.10378");
                                                SpriteText.drawStringWithScrollBackground(Game1.spriteBatch, text5, 96, 32, "", 1f, -1, 0);
                                            }
                                          //  events.Rendered.RaiseEmpty<RenderedEventArgs>();
                                            Game1.spriteBatch.End();
                                          //  __instance.drawOverlays(Game1.spriteBatch);
                                            Game1.PopUIMode();
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return false;
        }
    }
}
