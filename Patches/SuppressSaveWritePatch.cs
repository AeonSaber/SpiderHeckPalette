using System;
using HarmonyLib;

namespace SpiderHeckPalette.Patches
{
    /// <summary>
    /// Keeps this mod's IDs out of the player's save file.
    ///
    /// The first time a swatch is displayed, vanilla marks it seen and calls
    /// CustomizationMenu.AddSeenCustomizationElement(id), which persists the ID into the
    /// "seen customizations" list on disk. For our runtime-created colors that would leave
    /// stale entries behind if the mod is ever removed.
    ///
    /// Harmless in itself - the game ignores unknown IDs - but this mod is otherwise
    /// entirely non-persistent: unlock state is registered in memory only, and the colours
    /// themselves live on vanilla's ID-based save path via the swatch the player actually
    /// selects. This closes the last incidental write.
    ///
    /// Note this deliberately does NOT touch highscores. SpiderHeckPainPlus tiers save their
    /// own scores through vanilla (key "&lt;configName&gt;-HS"), which is intended - you want a
    /// PL8 highscore to survive a restart.
    /// </summary>
    [HarmonyPatch(typeof(CustomizationMenu), "AddSeenCustomizationElement")]
    public static class CustomizationMenu_AddSeenCustomizationElement_Patch
    {
        /// <returns>false to skip the vanilla method for our own IDs only.</returns>
        [HarmonyPrefix]
        public static bool Prefix(string id)
        {
            if (string.IsNullOrEmpty(id)) return true;

            foreach (var c in PaletteConfig.Colors)
            {
                if (string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }
    }
}
