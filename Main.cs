using Silk;
using HarmonyLib;
using Logger = Silk.Logger;

namespace SpiderHeckPalette
{
    /// <summary>
    /// Adds arbitrary custom spider colors as normal swatches in the customization menu.
    ///
    /// The color pipeline is value-based end to end - SetSpiderColor takes a Color, and the
    /// network payload (SkinData) carries raw Colors rather than palette indices - so any RGB
    /// value works, including for unmodded peers in a match.
    ///
    /// Disk persistence, however, is ID-based: the game saves the selected ColorConfig's ID.
    /// That is why the mod registers real swatches instead of just calling SetSpiderColor -
    /// registration is what makes vanilla save/restore work for free, and it avoids the
    /// CustomizationElement.SetStartingID NRE that unregistered colors trigger.
    /// </summary>
    [SilkMod("SpiderHeckPalette", new string[] { "AeonS" }, "0.1.0", "0.7.0", "SpiderHeckPalette", 1)]
    public class PaletteMod : SilkMod
    {
        private const string HarmonyId = "com.aeons.spiderheck.palette";

        public override void Initialize()
        {
            Logger.LogInfo("SpiderHeckPalette initializing...");
            try
            {
                PaletteConfig.Load();
                new Harmony(HarmonyId).PatchAll();
                Logger.LogInfo($"[Palette] Ready. Config: {PaletteConfig.ConfigPath}");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[Palette] Initialize failed: {ex}");
            }
        }

        public override void Unload()
        {
            try { new Harmony(HarmonyId).UnpatchSelf(); }
            catch (System.Exception ex) { Logger.LogError($"[Palette] Unload failed: {ex}"); }
        }
    }
}
