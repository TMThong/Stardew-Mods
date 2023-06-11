﻿using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Content.Pipeline;
using Microsoft.Xna.Framework.Content.Pipeline.Serialization.Compiler;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Newtonsoft.Json;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using System;
using System.IO;
using System.Reflection;
using xTile;
using static HarmonyLib.Code;
using static System.Net.WebRequestMethods;

namespace XNBArchive
{
    public class ModEntry : Mod
    {
        internal Config config;
        internal ITranslationHelper i18n => Helper.Translation;

        internal static string AltDirectorySeparatorChar = "/";

        internal ContentManager Content;

        public override void Entry(IModHelper helper)
        {
            config = helper.ReadConfig<Config>();

            Helper.Events.GameLoop.GameLaunched += OnGameLaunched;

            foreach (var reference in Directory.GetFiles(Helper.DirectoryPath.Replace("\\", AltDirectorySeparatorChar + "") + AltDirectorySeparatorChar + "Reference", "*", SearchOption.AllDirectories))
            {
                FileInfo fileInfo = new FileInfo(reference);
                if (fileInfo.Exists)
                {
                    if (fileInfo.Name.EndsWith(".exe") || fileInfo.Name.EndsWith(".dll"))
                    {
                        try
                        {
                            Assembly.Load(System.IO.File.ReadAllBytes(reference));
                        }
                        catch
                        {

                        }
                    }
                }
            }
        }

        public void OnGameLaunched(object sender, EventArgs eventArgs)
        {
            Content = new FakeContentManager(Game1.game1.Content.ServiceProvider, "Root");
            Helper.ConsoleCommands.Add("xnb_pack", "Helps you extract existing xnb files.", (cmd, option) => { pack(); });
            Helper.ConsoleCommands.Add("xnb_unpack", "Help you compress existing data files into xnb files.", (cmd, option) => { unpack(); });
        }



        public void pack()
        {
            foreach (string file_ in Directory.GetFiles(unpackPath, "*.package.json", SearchOption.AllDirectories))
            {
                var file = file_.Replace("\\", AltDirectorySeparatorChar + "");
                FileInfo fileInfo = new FileInfo(file);
                string dir = file.Replace(unpackPath + AltDirectorySeparatorChar, "").Replace(AltDirectorySeparatorChar + fileInfo.Name, "");
                try
                {
                    if (fileInfo.Exists)
                    {
                        PackageData packageData = JsonConvert.DeserializeObject<PackageData>(System.IO.File.ReadAllText(file));
                        string fileDataPath = unpackPath + AltDirectorySeparatorChar + dir + AltDirectorySeparatorChar + packageData.FileName;
                        if (System.IO.File.Exists(fileDataPath))
                        {
                            object obj = null;
                            if (packageData.FileName.EndsWith(".json"))
                            {
                                Type type = Type.GetType(packageData.Type);
                                obj = JsonConvert.DeserializeObject(System.IO.File.ReadAllText(fileDataPath), type);
                            }
                            else if (packageData.FileName.EndsWith(".png"))
                            {
                                obj = Texture2D.FromFile(Game1.game1.GraphicsDevice, fileDataPath);
                            }
                            else if (packageData.FileName.EndsWith(".tbin"))
                            {
                                obj = xTile.Format.FormatManager.Instance.BinaryFormat.Load(System.IO.File.Open(fileDataPath, FileMode.Open));
                            }

                            if (obj != null)
                            {
                                string xnbFilePath = file;
                                switch (obj)
                                {
                                    case Texture2D:
                                        {
                                            xnbFilePath = packPath + AltDirectorySeparatorChar + dir + AltDirectorySeparatorChar + packageData.FileName.Replace(".png", ".xnb");
                                            break;
                                        }
                                    case Map:
                                        {
                                            xnbFilePath = packPath + AltDirectorySeparatorChar + dir + AltDirectorySeparatorChar + packageData.FileName.Replace(".tbin", ".xnb");
                                            break;
                                        }
                                    default:
                                        {
                                            xnbFilePath = packPath + AltDirectorySeparatorChar + dir + AltDirectorySeparatorChar + packageData.FileName.Replace(".json", ".xnb");
                                            break;
                                        }
                                }
                                FileInfo xnbFileInfo = new FileInfo(xnbFilePath);
                                xnbFileInfo.Directory.Create();
                                using (var stream = xnbFileInfo.Open(FileMode.OpenOrCreate))
                                {
                                    ContentCompiler contentCompiler = new ContentCompiler();
                                    contentCompiler.Compile(stream, obj, TargetPlatform, GraphicsProfile.HiDef, false, "", "");
                                    stream.Close();
                                }
                                Monitor.Log($"Successfully packaged the {dir + AltDirectorySeparatorChar + fileInfo.Name} file", LogLevel.Info);
                            }
                            else
                            {
                                throw new NullReferenceException();
                            }
                        }
                        else
                        {
                            throw new FileNotFoundException();
                        }
                    }
                    else
                    {
                        throw new FileNotFoundException();
                    }
                }
                catch
                {

                    Monitor.Log($"Packing the {dir + AltDirectorySeparatorChar + fileInfo.Name} file failed", LogLevel.Error);
                }
            }
        }



        public TargetPlatform TargetPlatform
        {
            get
            {
                switch (Constants.TargetPlatform)
                {

                    case GamePlatform.Android: return TargetPlatform.Android;
                    case GamePlatform.Linux: return TargetPlatform.DesktopGL;
                    case GamePlatform.Mac: return TargetPlatform.MacOSX;
                    case GamePlatform.Windows: return TargetPlatform.Windows;
                }
                return TargetPlatform.DesktopGL;
            }
        }

        public string packPath => Helper.DirectoryPath.Replace("\\" , AltDirectorySeparatorChar + "") + AltDirectorySeparatorChar + "pack";
        public string unpackPath => Helper.DirectoryPath.Replace("\\", AltDirectorySeparatorChar + "") + AltDirectorySeparatorChar + "unpack";



        public void unpack()
        {

            foreach (var file_ in Directory.GetFiles(packPath, "*.xnb", SearchOption.AllDirectories))
            {
                var file = file_.Replace("\\", AltDirectorySeparatorChar + "");
                FileInfo fileInfo = new FileInfo(file);
                string dir = file.Replace(packPath + AltDirectorySeparatorChar, "").Replace(AltDirectorySeparatorChar + fileInfo.Name, "");
                try
                {
                    if (fileInfo.Exists)
                    {
                        object assets = Content.Load<object>(file);
                        string end = "json";
                        if (assets is Texture2D texture)
                        {
                            end = "png";
                        }
                        else if (assets is xTile.Map map)
                        {
                            end = "tbin";
                        }
                        PackageData packageData = new PackageData();
                        packageData.FileName = fileInfo.Name.Replace(".xnb", "." + end);
                        packageData.Type = assets.GetType().FullName;
                        FileInfo dataFileInfo = new FileInfo(unpackPath + AltDirectorySeparatorChar + dir + AltDirectorySeparatorChar + packageData.FileName);
                        FileInfo packageFileInfo = new FileInfo(unpackPath + AltDirectorySeparatorChar + dir + AltDirectorySeparatorChar + packageData.FileName + ".package.json");
                        dataFileInfo.Directory.Create();
                        packageFileInfo.Directory.Create();
                        using (var st = packageFileInfo.Open(FileMode.OpenOrCreate))
                        {
                            StreamWriter streamWriter = new StreamWriter(st);
                            streamWriter.Write(JsonConvert.SerializeObject(packageData, Formatting.Indented));
                            streamWriter.Close();
                        }
                        var dataStream = dataFileInfo.Open(FileMode.OpenOrCreate);
                        switch (end)
                        {
                            case "json":
                                {
                                    StreamWriter streamWriter = new StreamWriter(dataStream);
                                    streamWriter.Write(JsonConvert.SerializeObject(assets, Formatting.Indented));
                                    streamWriter.Close();
                                    break;
                                }
                            case "png":
                                {
                                    Texture2D texture2D = assets as Texture2D;
                                    texture2D.SaveAsPng(dataStream, texture2D.Width, texture2D.Height);
                                    break;
                                }
                            case "tbin":
                                {
                                    Map map = assets as Map;
                                    xTile.Format.FormatManager.Instance.BinaryFormat.Store(map, dataStream);
                                    break;
                                }
                        }
                        dataStream.Close();
                        Monitor.Log($"Successfully extracted the {dir + AltDirectorySeparatorChar + fileInfo.Name} file", LogLevel.Info);
                    }
                    else
                    {
                        throw new FileNotFoundException();
                    }
                }
                catch(Exception e)
                {
                    Monitor.Log($"Extracting the {dir + AltDirectorySeparatorChar + fileInfo.Name} file failed", LogLevel.Error);
                }
            }
        }
    }
}
