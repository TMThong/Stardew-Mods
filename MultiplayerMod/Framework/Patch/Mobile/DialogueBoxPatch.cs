using HarmonyLib;
using StardewValley.BellsAndWhistles;
using StardewValley;
using StardewValley.Menus;
using StardewValleyMod.Shared.FastHarmony;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiplayerMod.Framework.Patch.Mobile
{
    internal class DialogueBoxPatch : IPatch
    {
        public Type TypePatch => typeof(DialogueBox);


        public void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(TypePatch, "releaseLeftClick", new Type[] { typeof(int), typeof(int) }), prefix: new HarmonyMethod(this.GetType(), nameof(prefix_releaseLeftClick)));
        }

        private static bool prefix_releaseLeftClick(int x, int y, DialogueBox __instance)
        {

            var hasBeenClickedField = ModUtilities.Helper.Reflection.GetField<bool>(__instance, "hasBeenClicked");
            if (hasBeenClickedField.GetValue() && !__instance.transitioning)
            {
                hasBeenClickedField.SetValue(false);
                if (__instance.characterIndexInDialogue < __instance.getCurrentString().Length - 1)
                {
                    __instance.characterIndexInDialogue = __instance.getCurrentString().Length - 1;
                    goto RET;
                }
                if (__instance.isQuestion)
                {
                    if (__instance.selectedResponse == -1)
                    {
                        goto RET;
                    }
                    var responseMadeField = ModUtilities.Helper.Reflection.GetField<bool>(__instance, "responseMade");
                    responseMadeField.SetValue(true);
                    __instance.questionFinishPauseTimer = (Game1.eventUp ? 600 : 200);
                    __instance.transitioning = true;
                    __instance.transitionX = -1;
                    __instance.transitioningBigger = true;
                    if (__instance.characterDialogue != null)
                    {
                        __instance.characterDialoguesBrokenUp.Pop();
                        __instance.characterDialogue.chooseResponse(__instance.responses[__instance.selectedResponse]);
                        __instance.characterDialoguesBrokenUp.Push("");
                        Game1.playSound("smallSelect");
                    }
                    else
                    {
                        var tryOutroMethod = ModUtilities.Helper.Reflection.GetMethod(__instance, "tryOutro");
                        Game1.dialogueUp = false;
                        if (Game1.eventUp && Game1.currentLocation.afterQuestion == null)
                        {
                            Game1.playSound("smallSelect");
                            Game1.currentLocation.currentEvent.answerDialogue(Game1.currentLocation.lastQuestionKey, __instance.selectedResponse);
                            __instance.selectedResponse = -1;
                            tryOutroMethod.Invoke();
                            goto RET;
                        }
                        if (Game1.currentLocation.answerDialogue(__instance.responses[__instance.selectedResponse]))
                        {
                            Game1.playSound("smallSelect");
                        }
                        __instance.selectedResponse = -1;
                        tryOutroMethod.Invoke();
                        goto RET;
                    }
                }
                else if (__instance.characterDialogue == null)
                {
                    __instance.dialogues.RemoveAt(0);
                    if (__instance.dialogues.Count == 0)
                    {
                        __instance.closeDialogue();
                    }
                    else
                    {
                        var GetWidthMethodStatic = ModUtilities.Helper.Reflection.GetMethod(typeof(DialogueBox), "GetWidth");
                        __instance.width = GetWidthMethodStatic.Invoke<int>();
                        var responseMadeField = ModUtilities.Helper.Reflection.GetField<bool>(__instance, "responseMade");
                        if (responseMadeField.GetValue())
                        {
                            var shrinkFontMethodStatic = ModUtilities.Helper.Reflection.GetMethod(typeof(SpriteText), "shrinkFont");
                            shrinkFontMethodStatic.Invoke(false);
                        }
                        __instance.height = SpriteText.getHeightOfString(__instance.dialogues[0], __instance.width - 8 - 16) + 12 + 12;
                        __instance.x = (int)Utility.getTopLeftPositionForCenteringOnScreen(__instance.width, __instance.height, 0, 0).X;
                        __instance.y = Game1.uiViewport.Height - __instance.height - 32;
                        __instance.xPositionOnScreen = x;
                        __instance.yPositionOnScreen = y;
                        var setUpIconsMethod2 = ModUtilities.Helper.Reflection.GetMethod(__instance, "setUpIcons");
                        setUpIconsMethod2.Invoke();
                    }
                }
                __instance.characterIndexInDialogue = 0;
                if (__instance.characterDialogue != null)
                {
                    int portraitIndex = __instance.characterDialogue.getPortraitIndex();
                    if (__instance.characterDialoguesBrokenUp.Count == 0)
                    {
                        __instance.beginOutro();
                        goto RET;
                    }
                    __instance.characterDialoguesBrokenUp.Pop();
                    if (__instance.characterDialoguesBrokenUp.Count == 0)
                    {
                        if (!__instance.characterDialogue.isCurrentStringContinuedOnNextScreen)
                        {
                            __instance.beginOutro();
                        }
                        __instance.characterDialogue.exitCurrentDialogue();
                    }
                    if (!__instance.characterDialogue.isDialogueFinished() && __instance.characterDialogue.getCurrentDialogue().Length > 0 && __instance.characterDialoguesBrokenUp.Count == 0)
                    {
                        __instance.characterDialoguesBrokenUp.Push(__instance.characterDialogue.getCurrentDialogue());
                    }
                    var checkDialogueMethod = ModUtilities.Helper.Reflection.GetMethod(__instance, "checkDialogue");
                    checkDialogueMethod.Invoke(__instance.characterDialogue);
                    if (__instance.characterDialogue.getPortraitIndex() != portraitIndex)
                    {
                        __instance.newPortaitShakeTimer = ((__instance.characterDialogue.getPortraitIndex() == 1) ? 250 : 50);
                    }
                }
                if (!__instance.transitioning)
                {
                    Game1.playSound("smallSelect");
                }
                var setUpIconsMethod = ModUtilities.Helper.Reflection.GetMethod(__instance, "setUpIcons");
                setUpIconsMethod.Invoke();
                __instance.safetyTimer = 750;
                if (__instance.getCurrentString() != null && __instance.getCurrentString().Length <= 20)
                {
                    __instance.safetyTimer -= 200;
                }
            }
        RET:
            return true;
        }
    }
}
