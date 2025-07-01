using FindObjectMod.Framework;
using FindObjectMod.Framework.Menus;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using System;
using FindObjectMod.Framework.Menus.Components;
using StardewValley.Menus;

namespace FindObjectMod
{
    public class ModEntry : Mod
    {
        public List<ModTool> ModTools { get; } = new();
        public ModConfig config;
        public static ModEntry Instance { get; internal set; }

        public override void Entry(IModHelper helper)
        {
            if (Utilities.IsAndroid)
                Monitor.Log("Mobile version may have errors!", LogLevel.Warn);

            Instance = this;
            config = helper.ReadConfig<ModConfig>();
            var c = config;

            helper.Events.Input.ButtonPressed += (o, e) =>
            {
                if (!Context.IsWorldReady || Game1.activeClickableMenu != null)
                    return;

                if (e.Button == c.KeyOpenMenu)
                {
                    InitOptionsElement();
                    Game1.activeClickableMenu = new ModMenu(helper, Monitor);
                }
                else if ((e.Button == c.KeySelectObject && !Utilities.IsAndroid && c.SearchMode) ||
                         (Utilities.IsAndroid && c.InitiatesObjectSelectionModeForMobile && c.SearchMode))
                {
                    int x = (int)e.Cursor.ScreenPixels.X;
                    int y = (int)e.Cursor.ScreenPixels.Y;
                    var location = Game1.currentLocation;
                    var objects_ = Utilities.GetObjects(location);
                    if (Utilities.isClick(x, y, objects_))
                    {
                        var fi = objects_.FirstOrDefault(p => Utilities.isClick(x, y, p));
                        if (fi != null)
                            ObjectCliked(location, fi);
                    }
                    else if (Utilities.isClick(x, y, Utilities.GetNpcs(null).ToArray()))
                    {
                        var npc_ = Utilities.GetNpcs(null).FirstOrDefault(p => Utilities.isClick(x, y, p));
                        if (npc_ != null)
                            NPCClicked(location, npc_);
                    }
                }
            };

            helper.Events.GameLoop.SaveLoaded += (o, e) =>
            {
                Utilities.SaveKey = Constants.SaveFolderName;
                if (!c.ObjectToFind.ContainsKey(Utilities.SaveKey))
                {
                    c.ObjectToFind[Utilities.SaveKey] = new Dictionary<string, Color>();
                    Helper.WriteConfig(c);
                }
                if (!c.FindCharacter.ContainsKey(Utilities.SaveKey))
                {
                    c.FindCharacter[Utilities.SaveKey] = new Dictionary<string, Color>();
                    Helper.WriteConfig(c);
                }
                ModTools.Clear();
                ModTools.Add(new NPCFind(helper, Monitor, c));
                ModTools.Add(new FindObject(Monitor, helper, c));
                ModTools.ForEach(p => p.Initialization());
            };

            helper.Events.GameLoop.GameLaunched += (o, e) =>
            {
                ModTools.ForEach(p => p.Destroy());
                ModTools.Clear();
            };
        }

        public void ObjectCliked(GameLocation location, StardewValley.Object gameObject)
        {
            var i18n = Helper.Translation;
            string title = ModManifest.Name;
            bool add = !config.ObjectToFind[Utilities.SaveKey].ContainsKey(gameObject.name);
            var responses = new[]
            {
                    add
                        ? new Response("addobject", i18n.Get("AddObject", new { gameObject.displayName }))
                        : new Response("deleteobject", i18n.Get("DeleteObject", new { gameObject.displayName })),
                    new Response("exit", i18n.Get("Exit"))
                };

            void add1(Color cl)
            {
                config.ObjectToFind[Utilities.SaveKey][gameObject.name] = cl;
                Helper.WriteConfig(config);
            }

            Action a111 = null;
            void callBack()
            {
                add1((Game1.activeClickableMenu as ColorPickerMenu).MColor);
                Game1.exitActiveMenu();
                Game1.player.canMove = true;
            }

            void Choose(Farmer f, string k)
            {
                if (k == "addobject")
                {
                    Game1.exitActiveMenu();
                    Game1.activeClickableMenu = new ColorPickerMenu(config.Object, _ => { }, a111 ??= callBack);
                }
                else if (k == "deleteobject")
                {
                    config.ObjectToFind[Utilities.SaveKey].Remove(gameObject.name);
                    Helper.WriteConfig(config);
                }
            }

            location.createQuestionDialogue(title, responses, new GameLocation.afterQuestionBehavior(Choose), null);
        }

        public void NPCClicked(GameLocation location, NPC npc)
        {
            var i18n = Helper.Translation;
            string title = ModManifest.Name;
            bool add = !config.FindCharacter[Utilities.SaveKey].ContainsKey(npc.Name);
            var responses = new[]
            {
                    add
                        ? new Response("addnpc", i18n.Get("AddNPC", new { npc.displayName }))
                        : new Response("deletenpc", i18n.Get("DeleteNPC", new { npc.displayName })),
                    new Response("exit", i18n.Get("Exit"))
                };

            void add1(Color cl)
            {
                config.FindCharacter[Utilities.SaveKey][npc.Name] = cl;
                Helper.WriteConfig(config);
            }

            Action a111 = null;
            void callBack()
            {
                add1((Game1.activeClickableMenu as ColorPickerMenu).MColor);
                Game1.exitActiveMenu();
                Game1.player.canMove = true;
            }

            void Choose(Farmer f, string k)
            {
                if (k == "addnpc")
                {
                    Game1.exitActiveMenu();
                    Game1.activeClickableMenu = new ColorPickerMenu(config.NPC, _ => { }, a111 ??= callBack);
                }
                else if (k == "deletenpc")
                {
                    config.FindCharacter[Utilities.SaveKey].Remove(npc.Name);
                    Helper.WriteConfig(config);
                }
            }

            location.createQuestionDialogue(title, responses, new GameLocation.afterQuestionBehavior(Choose), null);
        }

        public void InitOptionsElement()
        {
            var oe = Utilities.OptionsElements;
            oe.Clear();
            var translation = Helper.Translation;

            void saveConfig() => Helper.WriteConfig(config);
            void initOptions()
            {
                InitOptionsElement();
                (Game1.activeClickableMenu as ModMenu)?.updateElements(oe);
            }

            oe.Add(new OptionsElement(translation.Get("title")));
            oe.Add(new ModOptionsCheckbox(translation.Get("FindQuestObject"), -1, b =>
            {
                config.FindQuestObject = b;
                saveConfig();
                initOptions();
            }, config.FindQuestObject));
            oe.Add(new ModOptionsCheckbox(translation.Get("DrawArea"), -1, b =>
            {
                config.DrawArea = b;
                saveConfig();
            }, config.DrawArea));
            oe.Add(new ModOptionsCheckbox(translation.Get("SearchMode"), -1, b =>
            {
                config.SearchMode = b;
                saveConfig();
            }, config.SearchMode));
            oe.Add(new ModOptionsCheckbox(translation.Get("FindAllNPC"), -1, b =>
            {
                config.FindAllNPC = b;
                saveConfig();
                initOptions();
            }, config.FindAllNPC));
            oe.Add(new ModOptionsCheckbox(translation.Get("FindAllObject"), -1, b =>
            {
                config.FindAllObject = b;
                saveConfig();
                initOptions();
            }, config.FindAllObject));

            if (Utilities.IsAndroid)
            {
                oe.Add(new ModOptionsCheckbox(translation.Get("InitiatesObjectSelectionModeForMobile"), -1, b =>
                {
                    config.InitiatesObjectSelectionModeForMobile = b;
                    saveConfig();
                }, config.InitiatesObjectSelectionModeForMobile));
            }

            oe.Add(new OptionsButton(translation.Get("ResetModConfig"), () =>
            {
                config.reset();
                saveConfig();
                initOptions();
            }));

            if (config.FindAllNPC)
            {
                oe.Add(new ModOptionsColorPicker(translation.Get("Color.Picker", new { name = translation.Get("NPC") }), config.NPC, c =>
                {
                    config.NPC = c;
                    saveConfig();
                }));
                oe.Add(new ModOptionsColorPicker(translation.Get("Color.Picker", new { name = translation.Get("Monsters") }), config.Monsters, c =>
                {
                    config.Monsters = c;
                    saveConfig();
                }));
            }
            if (config.FindAllObject)
            {
                oe.Add(new ModOptionsColorPicker(translation.Get("Color.Picker", new { name = translation.Get("Object") }), config.Object, c =>
                {
                    config.Object = c;
                    saveConfig();
                }));
            }
            if (config.FindQuestObject)
            {
                oe.Add(new ModOptionsColorPicker(translation.Get("Color.Picker", new { name = translation.Get("QuestObject") }), config.QuestObject, c =>
                {
                    config.QuestObject = c;
                    saveConfig();
                }));
            }
            if (config.ObjectToFind[Utilities.SaveKey].Count > 0)
            {
                oe.Add(new OptionsElement(translation.Get("ObjectToFind")));
                oe.Add(new OptionsButton(translation.Get("Clear", new { name = translation.Get("Object") }), () =>
                {
                    config.ObjectToFind[Utilities.SaveKey].Clear();
                    saveConfig();
                    initOptions();
                }));
                foreach (var i in config.ObjectToFind[Utilities.SaveKey].Keys.ToList())
                {
                    var key = i;
                    oe.Add(new ModOptionsColorPicker(translation.Get("Color.Picker", new { name = key }), config.ObjectToFind[Utilities.SaveKey][key], c =>
                    {
                        config.ObjectToFind[Utilities.SaveKey][key] = c;
                        saveConfig();
                    }));
                }
            }
            if (config.FindCharacter[Utilities.SaveKey].Count > 0)
            {
                oe.Add(new OptionsElement(translation.Get("FindCharacter")));
                oe.Add(new OptionsButton(translation.Get("Clear", new { name = translation.Get("NPC") }), () =>
                {
                    config.FindCharacter[Utilities.SaveKey].Clear();
                    saveConfig();
                    initOptions();
                }));
                foreach (var j in config.FindCharacter[Utilities.SaveKey].Keys.ToList())
                {
                    var key = j;
                    oe.Add(new ModOptionsColorPicker(translation.Get("Color.Picker", new { name = key }), config.FindCharacter[Utilities.SaveKey][key], c =>
                    {
                        config.FindCharacter[Utilities.SaveKey][key] = c;
                        saveConfig();
                    }));
                }
            }
        }
    }
}
    