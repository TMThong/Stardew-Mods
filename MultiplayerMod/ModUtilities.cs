using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using StardewModdingAPI;
using StardewValley;
using MultiplayerMod;
using MultiplayerMod.Framework;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using StardewValley.Network;
using StardewValley.Tools;
using System.Text.RegularExpressions;

namespace MultiplayerMod.Framework
{

    public static class ModUtilities
    {
        public static bool IsAndroid
        {
            get
            {
                return Constants.TargetPlatform == GamePlatform.Android;
            }
        }

        public static IMonitor ModMonitor;
        public static Multiplayer multiplayer
        {
            get
            {
                return Helper.Reflection.GetField<Multiplayer>(typeof(Game1), "multiplayer").GetValue();
            }
            set
            {
                Helper.Reflection.GetField<Multiplayer>(typeof(Game1), "multiplayer").SetValue(value);
            }
        }
        public static IModHelper Helper;
        internal static Config ModConfig;

        public const  string PATTERN_SIGNALR = @"(?<key>[^=;]+)=(?<value>[^;]+)";

        public static bool TryMatchSignalR(string Text,out Dictionary<string, string> pairs)
        {
            pairs = new Dictionary<string, string>();

            var matches = Regex.Matches(Text, PATTERN_SIGNALR);

            if (matches.Count > 0)
            {
                foreach (Match match in matches)
                {
                    if (!match.Success) return false;
                    string key = match.Groups["key"].Value;
                    string value = match.Groups["value"].Value;
                    pairs[key] = value;
                }
                return true;
            }
            else return false;
        }

        public static bool CheckInvaildKey(this Dictionary<string, string> pairs, params string[] keys) => pairs.Keys.Any(key => !keys.Contains(key));
        #region Reflection
        public static T GetPrivatePropertyValue<T>(this object obj, string propName)
        {
            if (obj == null) throw new ArgumentNullException("obj"); PropertyInfo pi = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (pi == null) throw new ArgumentOutOfRangeException("propName", string.Format("Property {0} was not found in Type {1}", propName, obj.GetType().FullName));
            return (T)pi.GetValue(obj, null);
        }
        public static T GetPrivateFieldValue<T>(this object obj, string propName)
        {
            if (obj == null) throw new ArgumentNullException("obj");
            Type t = obj.GetType(); FieldInfo fi = null; while (fi == null && t != null) { fi = t.GetField(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); t = t.BaseType; }
            if (fi == null) throw new ArgumentOutOfRangeException("propName", string.Format("Field {0} was not found in Type {1}", propName, obj.GetType().FullName)); return (T)fi.GetValue(obj);
        }

        public static void SetPrivatePropertyValue<T>(this object obj, string propName, T val)
        {
            Type t = obj.GetType();
            if (t.GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) == null) throw new ArgumentOutOfRangeException("propName", string.Format("Property {0} was not found in Type {1}", propName, obj.GetType().FullName)); t.InvokeMember(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.SetProperty | BindingFlags.Instance, null, obj, new object[] { val });
        }

        public static void SetPrivateFieldValue<T>(this object obj, string propName, T val)
        {
            if (obj == null) throw new ArgumentNullException("obj");
            Type t = obj.GetType(); FieldInfo fi = null; while (fi == null && t != null) { fi = t.GetField(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); t = t.BaseType; }
            if (fi == null) throw new ArgumentOutOfRangeException("propName", string.Format("Field {0} was not found in Type {1}", propName, obj.GetType().FullName)); fi.SetValue(obj, val);
        }
        public static T GetPrivateMethod<T>(this Type type, string name, BindingFlags bindingFlags, object[] obj)
        {
            var method = type.GetMethod(name, bindingFlags);
            return (T)method.Invoke(null, obj);
        }

        public static T GetPrivateMethodStatic<T>(this Type type, string name, object[] obj) => GetPrivateMethod<T>(type, name, BindingFlags.Static | BindingFlags.NonPublic, obj);
        public static T CreateInstanceProtected<T>(this Type type, object[] obj)
        {
            ConstructorInfo constructor = type.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, obj.ToTypes(), null);
            object o = constructor.Invoke(obj);
            if (o is T t)
            {
                return t;
            }
            return default(T);
        }
        public static T CreateInstanceInternal<T>(this Type type, object[] obj)
        {
            object o = Activator.CreateInstance(type, obj);
            if (o is T t)
            {
                return t;
            }
            return default(T);
        }
        public static T CreateInstance<T>(this Type type, object[] obj)
        {
            ConstructorInfo constructor = type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, obj.ToTypes(), null);
            object o = constructor.Invoke(obj);
            if (o is T t)
            {
                return t;
            }
            return default(T);
        }
        public static T CreateInstance<T>(this Type type, object[] obj, Type[] types)
        {
            ConstructorInfo constructor = type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, types, null);
            object o = constructor.Invoke(obj);
            if (o is T t)
            {
                return t;
            }
            return default(T);
        }


        public static Type[] ToTypes<T>(this T[] value)
        {
            List<Type> types = new List<Type>();
            for (int i = 0; i < value.Length; i++)
            {
                types.Add(value[i].GetType());
            }
            return types.ToArray();
        }


        #endregion
    }
}

