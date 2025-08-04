using HarmonyLib;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Newtonsoft.Json;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using xTile;

namespace XNBArchive
{
    public class ModEntry : Mod
    {
        internal Config config;
        internal ITranslationHelper i18n => Helper.Translation;

        internal static string AltDirectorySeparatorChar = "/";

        internal ContentManager Content;

        public Assembly[] AssembliesReferences;

        public override void Entry(IModHelper helper)
        {
            config = helper.ReadConfig<Config>();

            Helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            ICollection<Assembly> assemblies = new List<Assembly>();
            foreach (var reference in Directory.GetFiles(Helper.DirectoryPath.Replace("\\", AltDirectorySeparatorChar + "") + AltDirectorySeparatorChar + "Reference", "*", SearchOption.AllDirectories))
            {
                FileInfo fileInfo = new FileInfo(reference);
                if (fileInfo.Exists)
                {
                    if (fileInfo.Name.EndsWith(".exe") || fileInfo.Name.EndsWith(".dll"))
                    {
                        try
                        {
                            var assembly_ = Assembly.Load(System.IO.File.ReadAllBytes(reference));
                            foreach (var m in assembly_.Modules)
                            {
                                IEnumerable<Module> modules = this.GetType().Assembly.Modules;
                                if (!modules.Contains(m)) modules.AddItem(m);
                            }
                            assemblies.Add(assembly_);
                            Monitor.Log($"Load {fileInfo.Name} OK", LogLevel.Alert);
                        }
                        catch (Exception ex)
                        {
                            this.Monitor.Log("" + ex.GetBaseException(), LogLevel.Error);
                            this.Monitor.Log($"Load {fileInfo.Name} failed", LogLevel.Error);
                            this.Monitor.Log($"Please check if the file is a valid assembly file.", LogLevel.Error);
                            this.Monitor.Log($"If you think this is a bug, please report it on the mod's GitHub page.", LogLevel.Error);
                            return;
                        }
                    }
                }
            }
            AssembliesReferences = assemblies.ToArray();
        }

        public void OnGameLaunched(object sender, EventArgs eventArgs)
        {
            Content = new FakeContentManager(Game1.game1.Content.ServiceProvider, "Root");
            Helper.ConsoleCommands.Add("xnb_pack", "Helps you extract existing xnb files.", packAction);
            Helper.ConsoleCommands.Add("xnb_unpack", "Help you compress existing data files into xnb files.", unpackAction);
        }

        private bool IsPacking = false;
        private bool IsUnpacking = false;


        void packAction(string cmd, string[] options)
        {
            if (!IsPacking)
            {
                IsPacking = true;

                pack();
            }
        }

        void unpackAction(string cmd, string[] options)
        {
            if (!IsUnpacking)
            {
                IsUnpacking = true;

                unpack();
            }
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
                                    Type contentCompilerType = this.AssembliesReferences.FirstOrDefault(a => a.GetType("Microsoft.Xna.Framework.Content.Pipeline.Serialization.Compiler.ContentCompiler") != null)
                                        ?.GetType("Microsoft.Xna.Framework.Content.Pipeline.Serialization.Compiler.ContentCompiler");
                                    if (contentCompilerType == null)
                                        throw new NullReferenceException("ContentCompiler type not found in referenced assemblies.");
                                    object contentCompiler = Activator.CreateInstance(contentCompilerType);
                                    var methodInfo = contentCompiler.GetType().GetMethod("Compile", BindingFlags.Public | BindingFlags.Instance);
                                    if (methodInfo != null)
                                    {
                                        // Use reflection to call the Compile method
                                        // The parameters are: stream, asset, targetPlatform, graphicsProfile, isDebug, contentIdentity, and outputName
                                        // We pass null for contentIdentity and outputName as they are not used in this context
                                        methodInfo.Invoke(contentCompiler, new object[] { stream, obj, TargetPlatform, GraphicsProfile.HiDef, false, string.Empty, string.Empty });
                                    }
                                }
                                Log($"Successfully packaged the {dir + AltDirectorySeparatorChar + fileInfo.Name} file", LogLevel.Info);
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
                catch (Exception e)
                {

                    Log($"Packing the {dir + AltDirectorySeparatorChar + fileInfo.Name} file failed", LogLevel.Error);
                    Log($"{e.GetBaseException()}", LogLevel.Error);
                }
            }
            IsPacking = false;
        }



        public object TargetPlatform
        {
            get
            {
                // Use reflection to get the TargetPlatform enum value by name
                var platformName = Constants.TargetPlatform switch
                {
                    GamePlatform.Android => "Android",
                    GamePlatform.Linux => "DesktopGL",
                    GamePlatform.Mac => "MacOSX",
                    GamePlatform.Windows => "Windows",
                    _ => "DesktopGL"
                };
                Type targetPlatformType = this.AssembliesReferences.FirstOrDefault(a => a.GetType("Microsoft.Xna.Framework.Content.Pipeline.TargetPlatform") != null)
                    ?.GetType("Microsoft.Xna.Framework.Content.Pipeline.TargetPlatform");
                if (targetPlatformType == null)
                    throw new NullReferenceException("TargetPlatform type not found in referenced assemblies.");
                var value = Enum.Parse(targetPlatformType, platformName);
                return value;
            }
        }

        public string packPath => Helper.DirectoryPath.Replace("\\", AltDirectorySeparatorChar + "") + AltDirectorySeparatorChar + "pack";
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
                        Log($"Successfully extracted the {dir + AltDirectorySeparatorChar + fileInfo.Name} file", LogLevel.Info);
                    }
                    else
                    {
                        throw new FileNotFoundException();
                    }
                }
                catch (Exception e)
                {
                    Log($"Extracting the {dir + AltDirectorySeparatorChar + fileInfo.Name} file failed", LogLevel.Error);
#if DEBUG
                    Log($"{e.GetBaseException()}", LogLevel.Error);
#endif
                }
            }
            IsUnpacking = false;
        }

        private void Log(string v, LogLevel error)
        {
            Monitor.Log(v, error);
        }
    }
}
