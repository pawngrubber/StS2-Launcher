using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace STS2Mobile.Patches;

// Extends ModManager to scan an external mods directory on Android so users
// can sideload mods to /storage/emulated/0/StS2Launcher/Mods/ without root.
//
// ModManager's internal mod-loading pipeline is private and not designed for
// external callers, so this reaches in via reflection for the pieces that
// have no public equivalent. As of the public-beta build (buildid 24489008)
// that pipeline reads and loads mods as two separate steps — ReadModsInDirRecursive
// parses manifests into a list, TryLoadMod actually loads one — rather than
// the older single LoadModsInDirRecursive(DirAccess, ModSource) call this
// patch used to hook; the reflection target names and signatures below were
// updated to match by inspecting the shipped assembly, since no source is
// available for MegaCrit.Sts2.Core.Modding.ModManager.
public static class ModLoaderPatches
{
    private static readonly BindingFlags AllStatic =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Apply(Harmony harmony)
    {
        PatchHelper.Patch(
            harmony,
            typeof(ModManager),
            "Initialize",
            postfix: PatchHelper.Method(typeof(ModLoaderPatches), nameof(InitializePostfix))
        );
    }

    // Runs after the original Initialize() to pick up mods from external storage.
    public static void InitializePostfix()
    {
        try
        {
            if (!Directory.Exists(AppPaths.ExternalModsDir))
            {
                PatchHelper.Log(
                    $"[Mods] External mods directory not found: {AppPaths.ExternalModsDir}"
                );
                return;
            }

            PatchHelper.Log($"[Mods] Scanning external mods: {AppPaths.ExternalModsDir}");

            var modsField = typeof(ModManager).GetField("_mods", AllStatic);
            var allMods = (List<Mod>)modsField.GetValue(null);

            var readMethod = typeof(ModManager).GetMethod("ReadModsInDirRecursive", AllStatic);
            var newMods = new List<Mod>();
            readMethod.Invoke(
                null,
                new object[] { AppPaths.ExternalModsDir, ModSource.ModsDirectory, newMods }
            );

            var tryLoadMethod = typeof(ModManager).GetMethod("TryLoadMod", AllStatic);
            foreach (var mod in newMods)
            {
                tryLoadMethod.Invoke(null, new object[] { mod });
                allMods.Add(mod);
            }

            var loadedCount = newMods.Count(m => m.state == ModLoadState.Loaded);
            PatchHelper.Log(
                $"[Mods] External scan complete. {loadedCount}/{newMods.Count} loaded. "
                    + $"Total loaded (all sources): {ModManager.GetLoadedMods().Count()}"
            );
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Mods] Failed to load external mods: {ex}");
        }
    }
}
