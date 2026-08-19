using System;
using HarmonyLib;
using TMPro;
using Logger = Silk.Logger;

namespace SpiderHeckPalette.Patches
{
    /// <summary>
    /// Makes injected swatches show their configured name in the carousel.
    ///
    /// CustomizationEntity.displayName is an I2.Loc LocalizedString, resolved through I2's
    /// term database. Our runtime-created ColorConfigs have no registered term, so vanilla's
    /// label code produces an empty string:
    ///
    ///     previewName.text = entity.displayName;                        // -> ""
    ///     if (!entity.isSeen)
    ///         previewName.text = "&lt;b&gt;" + entity.displayName + "*&lt;/b&gt;";  // -> "&lt;b&gt;*&lt;/b&gt;"
    ///
    /// which renders as a blank label, or a lone "*" for a not-yet-seen entry.
    ///
    /// Registering terms with I2 at runtime would be the "proper" fix, but it means writing into
    /// the game's localization sources and carrying that across language changes. Overwriting the
    /// label after vanilla has set it is smaller, has no persistence footprint, and cannot affect
    /// any entity but ours (we match strictly on our own configured IDs).
    /// </summary>
    [HarmonyPatch(typeof(CustomizationElement), "UpdatePreviewClientRpc", new Type[] { typeof(string), typeof(bool) })]
    public static class CustomizationElement_UpdatePreviewClientRpc_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(CustomizationElement __instance, string id)
        {
            try
            {
                if (__instance == null || string.IsNullOrEmpty(id)) return;

                ColorEntry entry = null;
                foreach (var c in PaletteConfig.Colors)
                {
                    if (string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)) { entry = c; break; }
                }
                if (entry == null) return;   // not one of ours - leave vanilla text alone

                var label = AccessTools.Field(typeof(CustomizationElement), "previewName")
                                       .GetValue(__instance) as TMP_Text;
                if (label == null) return;

                // Preserve vanilla's "new item" marker if it applied it.
                bool markedNew = label.text != null && label.text.Contains("*");
                label.text = markedNew ? $"<b>{entry.Name}*</b>" : entry.Name;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Palette] Preview name patch failed: {ex.Message}");
            }
        }
    }
}
