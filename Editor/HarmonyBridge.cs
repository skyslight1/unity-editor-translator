using System;
using System.Linq;
using System.Reflection;

namespace EditorTranslator
{
    // Reflection-based wrapper around a 0Harmony assembly that is loaded in the editor domain
    // (shipped in Editor/Plugins/0Harmony.dll). We deliberately do NOT reference it at compile
    // time, so the compiler setup stays free of per-machine paths and version pinning.
    internal class HarmonyBridge
    {
        object _harmony;
        MethodInfo _patch;
        MethodInfo _unpatchAll;
        Func<MethodInfo, object> _makeHarmonyMethod;

        public static HarmonyBridge TryCreate(string id)
        {
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, "0Harmony", StringComparison.OrdinalIgnoreCase));
                if (asm == null) return null;

                var harmonyType = asm.GetType("HarmonyLib.Harmony", false);
                var harmonyMethodType = asm.GetType("HarmonyLib.HarmonyMethod", false);
                if (harmonyType == null || harmonyMethodType == null) return null;

                var bridge = new HarmonyBridge();
                bridge._harmony = Activator.CreateInstance(harmonyType, id);

                bridge._patch = harmonyType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name == "Patch")
                    .Where(m =>
                    {
                        var ps = m.GetParameters();
                        return ps.Length >= 2 &&
                               ps[0].ParameterType == typeof(MethodBase) &&
                               ps[1].ParameterType.Name == "HarmonyMethod";
                    })
                    .OrderBy(m => m.GetParameters().Length)
                    .FirstOrDefault();

                bridge._unpatchAll = harmonyType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "UnpatchAll" && m.GetParameters().Length <= 1);

                var ctor = harmonyMethodType.GetConstructor(new[] { typeof(MethodInfo) });
                if (ctor == null || bridge._patch == null || bridge._unpatchAll == null) return null;
                bridge._makeHarmonyMethod = mi => ctor.Invoke(new object[] { mi });

                return bridge;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[EditorTranslator] Failed to initialize Harmony bridge: " + e.Message);
                return null;
            }
        }

        public bool Patch(MethodBase original, MethodInfo prefix, MethodInfo postfix)
        {
            try
            {
                var ps = _patch.GetParameters();
                var args = new object[ps.Length];
                args[0] = original;
                if (ps.Length > 1) args[1] = _makeHarmonyMethod(prefix);
                if (ps.Length > 2 && postfix != null) args[2] = _makeHarmonyMethod(postfix);
                _patch.Invoke(_harmony, args);
                return true;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[EditorTranslator] Patch failed for " + original + ": " + e.Message);
                return false;
            }
        }

        public void UnpatchAll()
        {
            try
            {
                _unpatchAll.Invoke(_harmony, new object[_unpatchAll.GetParameters().Length]);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[EditorTranslator] UnpatchAll failed: " + e.Message);
            }
        }
    }
}
