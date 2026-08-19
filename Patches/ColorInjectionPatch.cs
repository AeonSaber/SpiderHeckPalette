using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Logger = Silk.Logger;

namespace SpiderHeckPalette.Patches
{
    /// <summary>
    /// Registers custom colors as real swatches in the vanilla customization menu.
    ///
    /// Everything downstream is then vanilla: carousel UI, preview, apply, ID-based save,
    /// restore on boot, and network sync (SkinData carries raw Colors, so unmodded peers
    /// still render the correct color in-match).
    ///
    /// Hook choice matters. The verified init order is:
    ///     OnEnable  -> CreateMasterList()                       // allCustomizations = hatList + colorList
    ///     Load()    -> unlockedCustomizations = save.Load(...)
    ///               -> SetUnlockState(all, unlocked)            // recomputes isUnlocked
    ///                    -> UpdateUnlockedLists()               // builds _unlockedColorList
    ///     SetupScreen() -> panels read the lists
    ///
    /// So a plain Postfix on Load() is too late: _unlockedColorList (what the carousel reads) has
    /// already been built. We therefore finish the job ourselves - inject, register the IDs in the
    /// in-memory unlocked list, then re-run CreateMasterList + SetUnlockState. That still lands
    /// before SetupScreen().
    ///
    /// Note SetUnlockState does `if (!e.isTempItem) e.isUnlocked = unlockedIds.Contains(e.ID)`, so
    /// setting isUnlocked alone would simply be overwritten - the ID must be in the list.
    /// </summary>
    [HarmonyPatch(typeof(CustomizationMenu), "Load")]
    public static class CustomizationMenu_Load_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(CustomizationMenu __instance)
        {
            try { PaletteInjector.Inject(__instance); }
            catch (Exception ex) { Logger.LogError($"[Palette] Injection failed: {ex}"); }
        }
    }

    public static class PaletteInjector
    {
        // Created once and reused; ScriptableObjects would otherwise be collected between scenes.
        private static readonly Dictionary<string, ColorConfig> _configs =
            new Dictionary<string, ColorConfig>(StringComparer.OrdinalIgnoreCase);

        private static bool _loggedOnce;

        public static void Inject(CustomizationMenu menu)
        {
            if (menu == null || PaletteConfig.Colors.Count == 0) return;

            var colorList = AccessTools.Field(typeof(CustomizationMenu), "colorList")
                                       .GetValue(menu) as IList;
            var unlockedIds = AccessTools.Field(typeof(CustomizationMenu), "unlockedCustomizations")
                                         .GetValue(menu) as IList;
            if (colorList == null || unlockedIds == null)
            {
                Logger.LogError("[Palette] colorList/unlockedCustomizations not reachable - aborting.");
                return;
            }

            int added = 0;
            foreach (var entry in PaletteConfig.Colors)
            {
                var config = GetOrCreate(entry);
                if (config == null) continue;

                if (!ContainsId(colorList, entry.Id)) { colorList.Add(config); added++; }

                // Membership of _unlockedColorList keys off this ID list, and SetUnlockState
                // recomputes isUnlocked from it. Purely in-memory: Load() re-reads the save
                // every time, so nothing is ever written to the player's save file.
                if (!unlockedIds.Contains(entry.Id)) unlockedIds.Add(entry.Id);
            }

            // Rebuild the derived lists so our swatches are present before SetupScreen runs.
            AccessTools.Method(typeof(CustomizationMenu), "CreateMasterList")?.Invoke(menu, null);

            var allCustomizations = AccessTools.Field(typeof(CustomizationMenu), "allCustomizations").GetValue(menu);
            AccessTools.Method(typeof(CustomizationMenu), "SetUnlockState")
                       ?.Invoke(menu, new object[] { allCustomizations, unlockedIds });

            if (!_loggedOnce || added > 0)
            {
                Logger.LogInfo($"[Palette] Registered {PaletteConfig.Colors.Count} swatch(es) " +
                               $"({added} newly added to colorList).");
                _loggedOnce = true;
            }
        }

        private static ColorConfig GetOrCreate(ColorEntry entry)
        {
            if (_configs.TryGetValue(entry.Id, out var existing) && existing != null) return existing;

            if (!PaletteConfig.TryParseHex(entry.Primary, out var body) ||
                !PaletteConfig.TryParseHex(entry.Secondary, out var legs))
            {
                return null;
            }

            // CreateInstance, not `new` - ColorConfig is a ScriptableObject.
            var config = ScriptableObject.CreateInstance<ColorConfig>();
            config.name = entry.Id;
            config.bodyColor = body;
            config.legColor = legs;
            config.ID = entry.Id;
            config.isUnlocked = true;
            config.isSeen = true;
            config.isTempItem = false;

            // Survive scene loads without being saved into any asset.
            config.hideFlags = HideFlags.HideAndDontSave;

            _configs[entry.Id] = config;
            return config;
        }

        private static bool ContainsId(IList list, string id)
        {
            foreach (var o in list)
            {
                if (o is CustomizationEntity e &&
                    string.Equals(e.ID, id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
