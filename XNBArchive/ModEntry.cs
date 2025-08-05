using HarmonyLib;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using xTile;

namespace XNBArchive
{
    public class ModEntry : Mod
    {
        // Fields and properties
        private Config config;
        private ContentManager contentManager;
        private Assembly[] assemblyReferences;
        private bool isPacking = false;
        private bool isUnpacking = false;

        private static readonly string AltDirSep = "/";
        private string PackPath => PathCombine(Helper.DirectoryPath, "pack");
        private string UnpackPath => PathCombine(Helper.DirectoryPath, "unpack");
        private ITranslationHelper I18n => Helper.Translation;

        public override void Entry(IModHelper helper)
        {
            config = helper.ReadConfig<Config>();
            Helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            LoadAssemblyReferences();
        }

        private void OnGameLaunched(object sender, EventArgs e)
        {
            contentManager = new FakeContentManager(Game1.game1.Content.ServiceProvider, "Root");
            Helper.ConsoleCommands.Add("xnb_pack", "Extract existing xnb files.", PackCommand);
            Helper.ConsoleCommands.Add("xnb_unpack", "Compress data files into xnb files.", UnpackCommand);
        }

        #region Assembly Loading

        private void LoadAssemblyReferences()
        {
            var assemblies = new List<Assembly>();
            string refDir = PathCombine(Helper.DirectoryPath, "Reference");
            foreach (var filePath in Directory.GetFiles(refDir, "*", SearchOption.AllDirectories))
            {
                if (!IsAssemblyFile(filePath)) continue;
                try
                {
                    var assembly = Assembly.Load(File.ReadAllBytes(filePath));
                    assemblies.Add(assembly);
                    Monitor.Log($"Loaded {Path.GetFileName(filePath)} OK", LogLevel.Alert);
                }
                catch (Exception ex)
                {
                    LogAssemblyLoadError(filePath, ex);
                    return;
                }
            }
            assemblyReferences = assemblies.ToArray();
        }

        private bool IsAssemblyFile(string path) =>
            path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

        private void LogAssemblyLoadError(string file, Exception ex)
        {
            Monitor.Log($"{ex.GetBaseException()}", LogLevel.Error);
            Monitor.Log($"Failed to load {Path.GetFileName(file)}", LogLevel.Error);
            Monitor.Log($"Check if the file is a valid assembly.", LogLevel.Error);
            Monitor.Log($"If this is a bug, report it on the mod's GitHub.", LogLevel.Error);
        }

        #endregion

        #region Console Command Handlers

        private void PackCommand(string cmd, string[] args)
        {
            if (!isPacking)
            {
                isPacking = true;
                Pack();
            }
        }

        private void UnpackCommand(string cmd, string[] args)
        {
            if (!isUnpacking)
            {
                isUnpacking = true;
                Unpack();
            }
        }

        #endregion

        #region Packing

        private void Pack()
        {
            foreach (string jsonFile in Directory.GetFiles(UnpackPath, "*.package.json", SearchOption.AllDirectories))
            {
                string file = NormalizePath(jsonFile);
                string dir = GetSubDirPath(file, UnpackPath);
                try
                {
                    if (!File.Exists(file))
                        throw new FileNotFoundException();

                    var packageData = JsonConvert.DeserializeObject<PackageData>(File.ReadAllText(file));
                    string dataPath = PathCombine(UnpackPath, dir, packageData.FileName);

                    if (!File.Exists(dataPath))
                        throw new FileNotFoundException();

                    object asset = LoadAssetForPacking(packageData, dataPath);
                    string xnbFilePath = GetXnbFilePath(dir, packageData.FileName, asset);

                    WriteXnb(asset, xnbFilePath);

                    LogSuccess($"Packaged {dir}/{Path.GetFileName(file)}");
                }
                catch (Exception ex)
                {
                    LogFailure($"Packing {dir}/{Path.GetFileName(file)}", ex);
                }
            }
            isPacking = false;
        }

        private object LoadAssetForPacking(PackageData packageData, string dataPath)
        {
            if (packageData.FileName.EndsWith(".json"))
                return JsonConvert.DeserializeObject(File.ReadAllText(dataPath), Type.GetType(packageData.Type));
            if (packageData.FileName.EndsWith(".png"))
                return Texture2D.FromFile(Game1.game1.GraphicsDevice, dataPath);
            if (packageData.FileName.EndsWith(".tbin"))
                return xTile.Format.FormatManager.Instance.BinaryFormat.Load(File.Open(dataPath, FileMode.Open));
            throw new NotSupportedException("Unsupported file type.");
        }

        private string GetXnbFilePath(string dir, string fileName, object asset)
        {
            string ext = asset switch
            {
                Texture2D => ".xnb",
                Map => ".xnb",
                _ => ".xnb"
            };
            string replaced = fileName switch
            {
                var n when n.EndsWith(".png") => fileName.Replace(".png", ext),
                var n when n.EndsWith(".tbin") => fileName.Replace(".tbin", ext),
                var n when n.EndsWith(".json") => fileName.Replace(".json", ext),
                _ => fileName
            };
            return PathCombine(PackPath, dir, replaced);
        }

        private void WriteXnb(object asset, string path)
        {
            var xnbFileInfo = new FileInfo(path);
            xnbFileInfo.Directory?.Create();
            using var stream = xnbFileInfo.Open(FileMode.OpenOrCreate);
            var contentCompilerType = assemblyReferences
                .FirstOrDefault(a => a.GetType("Microsoft.Xna.Framework.Content.Pipeline.Serialization.Compiler.ContentCompiler") != null)
                ?.GetType("Microsoft.Xna.Framework.Content.Pipeline.Serialization.Compiler.ContentCompiler");

            if (contentCompilerType == null)
                throw new NullReferenceException("ContentCompiler type not found.");

            var contentCompiler = Activator.CreateInstance(contentCompilerType);
            var methodInfo = contentCompilerType.GetMethod("Compile", BindingFlags.Public | BindingFlags.Instance);

            methodInfo?.Invoke(contentCompiler, new object[] {
                stream, asset, TargetPlatform, GraphicsProfile.HiDef, false, string.Empty, string.Empty
            });
        }

        #endregion

        #region Unpacking

        private void Unpack()
        {
            foreach (string xnbFile in Directory.GetFiles(PackPath, "*.xnb", SearchOption.AllDirectories))
            {
                string file = NormalizePath(xnbFile);
                string dir = GetSubDirPath(file, PackPath);
                try
                {
                    if (!File.Exists(file))
                        throw new FileNotFoundException();

                    object asset = contentManager.Load<object>(file);
                    string extension = asset switch
                    {
                        Texture2D => "png",
                        Map => "tbin",
                        _ => "json"
                    };
                    var packageData = new PackageData
                    {
                        FileName = Path.GetFileName(file).Replace(".xnb", $".{extension}"),
                        Type = asset.GetType().FullName
                    };

                    SaveUnpackedFiles(dir, packageData, asset, extension);
                    LogSuccess($"Extracted {dir}/{Path.GetFileName(file)}");
                }
                catch (Exception ex)
                {
                    LogFailure($"Extracting {dir}/{Path.GetFileName(file)}", ex);
                }
            }
            isUnpacking = false;
        }

        private void SaveUnpackedFiles(string dir, PackageData packageData, object asset, string extension)
        {
            string basePath = PathCombine(UnpackPath, dir, packageData.FileName);
            string packageJsonPath = basePath + ".package.json";
            Directory.CreateDirectory(Path.GetDirectoryName(basePath)!);

            File.WriteAllText(packageJsonPath, JsonConvert.SerializeObject(packageData, Formatting.Indented));

            using var dataStream = File.Open(basePath, FileMode.OpenOrCreate);
            switch (extension)
            {
                case "json":
                    File.WriteAllText(basePath, JsonConvert.SerializeObject(asset, Formatting.Indented));
                    break;
                case "png":
                    (asset as Texture2D)?.SaveAsPng(dataStream, ((Texture2D)asset).Width, ((Texture2D)asset).Height);
                    break;
                case "tbin":
                    xTile.Format.FormatManager.Instance.BinaryFormat.Store((Map)asset, dataStream);
                    break;
            }
        }

        #endregion

        #region Helpers

        private object TargetPlatform
        {
            get
            {
                var platformName = Constants.TargetPlatform switch
                {
                    GamePlatform.Android => "Android",
                    GamePlatform.Linux => "DesktopGL",
                    GamePlatform.Mac => "MacOSX",
                    GamePlatform.Windows => "Windows",
                    _ => "DesktopGL"
                };
                var type = assemblyReferences
                    .FirstOrDefault(a => a.GetType("Microsoft.Xna.Framework.Content.Pipeline.TargetPlatform") != null)
                    ?.GetType("Microsoft.Xna.Framework.Content.Pipeline.TargetPlatform");

                if (type == null)
                    throw new NullReferenceException("TargetPlatform type not found.");
                return Enum.Parse(type, platformName);
            }
        }

        private void LogSuccess(string message) => Monitor.Log(message, LogLevel.Info);
        private void LogFailure(string action, Exception ex)
        {
            Monitor.Log($"{action} failed", LogLevel.Error);
#if DEBUG
            Monitor.Log($"{ex.GetBaseException()}", LogLevel.Error);
#endif
        }

        private static string PathCombine(params string[] paths)
        {
            string combined = Path.Combine(paths);
            return combined.Replace("\\", AltDirSep);
        }

        private static string NormalizePath(string path) => path.Replace("\\", AltDirSep);

        private static string GetSubDirPath(string file, string basePath)
        {
            var fileInfo = new FileInfo(file);
            return file.Replace(basePath + AltDirSep, "").Replace(AltDirSep + fileInfo.Name, "");
        }

        #endregion
    }
}