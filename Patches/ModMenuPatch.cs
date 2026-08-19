using System;
using System.Collections;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Logger = Silk.Logger;

namespace SpiderHeckPalette.Patches
{
    /// <summary>
    /// In-game colour picker, built on Silk's own mod-menu widgets
    /// (Mods menu -> SpiderHeckPalette).
    ///
    /// ModsMenu already provides CreateSlider / CreateButton / CreateParagraph, and CreateParagraph
    /// returns the TMP_Text - so the live preview is just a label we rewrite on every slider tick.
    /// That avoids building custom uGUI entirely, and avoids the controller-navigation work that
    /// would come with it.
    ///
    /// The picker always writes through a registered ColorConfig rather than calling
    /// SetSpiderColor directly: an active colour with no matching ColorConfig makes
    /// CustomizationElement.SetStartingID dereference a null colorConfig and throw.
    ///
    /// Silk exposes no text-input widget, but the game ships one - Announcer.TextInuptPopup
    /// (their typo) - which is used for naming and renaming colours here.
    /// </summary>
    [HarmonyPatch(typeof(Announcer), nameof(Announcer.ModsPopup))]
    public static class Announcer_ModsPopup_Patch
    {
        public static void Postfix(string title, ref ModsMenuPopup __result)
        {
            if (title != "SpiderHeckPalette" || __result == null) return;
            try { PickerMenu.Build(__result); }
            catch (Exception ex) { Logger.LogError($"[Palette] Menu build failed: {ex}"); }
        }
    }

    public static class PickerMenu
    {
        // Retained across menu opens so a part-built colour is not lost.
        private static float _pr = 255f, _pg = 59f, _pb = 31f;
        private static float _sr = 255f, _sg = 179f, _sb = 71f;

        private static TMP_Text _preview;

        private static Color Primary => new Color(_pr / 255f, _pg / 255f, _pb / 255f);
        private static Color Secondary => new Color(_sr / 255f, _sg / 255f, _sb / 255f);

        public static void Build(ModsMenuPopup menu)
        {
            menu.CreateParagraph("<size=180%><b><color=#FF66CC>SpiderHeck Palette</color></b></size>");
            menu.CreateParagraph($"<size=95%><color=#AAAAAA>{PaletteConfig.Colors.Count} colour(s) registered " +
                                 $"({PaletteConfig.PickerColors.Count} from this picker).</color></size>");
            menu.CreateDivider();

            _preview = menu.CreateParagraph(PreviewText());

            menu.CreateParagraph("<size=110%><b>Body colour</b></size>");
            Channel(menu, "<color=#FF6666>Red</color>",   _pr, v => { _pr = v; Refresh(); });
            Channel(menu, "<color=#66FF66>Green</color>", _pg, v => { _pg = v; Refresh(); });
            Channel(menu, "<color=#6699FF>Blue</color>",  _pb, v => { _pb = v; Refresh(); });

            menu.CreateParagraph("<size=110%><b>Leg colour</b></size>");
            Channel(menu, "<color=#FF6666>Red</color>",   _sr, v => { _sr = v; Refresh(); });
            Channel(menu, "<color=#66FF66>Green</color>", _sg, v => { _sg = v; Refresh(); });
            Channel(menu, "<color=#6699FF>Blue</color>",  _sb, v => { _sb = v; Refresh(); });

            menu.CreateDivider();
            menu.CreateButton("Add this colour", () => AddCurrent(menu));
            menu.CreateButton("Copy legs from body", () => { _sr = _pr; _sg = _pg; _sb = _pb; Refresh(); });

            if (PaletteConfig.PickerColors.Count > 0)
            {
                menu.CreateDivider();
                menu.CreateParagraph("<size=110%><b>Picker colours</b></size>");
                foreach (var c in PaletteConfig.PickerColors.ToArray())
                {
                    var id = c.Id;
                    menu.CreateParagraph(Swatch(c.Primary) + Swatch(c.Secondary) +
                                         $"  <b>{c.Name}</b>  <size=80%><color=#888888>{id}</color></size>");
                    menu.CreateButton($"Rename {c.Name}", () => Rename(menu, id));
                    menu.CreateButton($"Remove {c.Name}", () => Remove(menu, id));
                }
            }

            menu.CreateDivider();
            menu.CreateParagraph("<size=90%><color=#AAAAAA>New colours appear in the customization " +
                                 "menu immediately. Renames show up in the customization carousel " +
                                 "once you back out of these mod settings. Removing a colour needs " +
                                 "a game restart to clear it from the carousel.</color></size>");
        }

        /// <summary>
        /// Creates one 0-255 channel slider.
        ///
        /// Two prefab quirks are corrected here:
        ///  - SliderScript.Setup writes the title into its 'sliderDescription' field, but that
        ///    field points at a different TMP object than the label the prefab actually shows
        ///    (which reads "Sound Effects"). So we set every text in the slider except the value
        ///    readout.
        ///  - The slider is continuous by default, producing values like 117.9375. Colour
        ///    channels are integers, so wholeNumbers is forced on.
        /// </summary>
        private static void Channel(ModsMenuPopup menu, string label, float start, UnityAction<float> onChanged)
        {
            var script = menu.CreateSlider(onChanged, label, start, 0f, 255f);
            if (script == null) return;

            try
            {
                if (AccessTools.Field(typeof(SliderScript), "slider").GetValue(script) is Slider slider)
                {
                    slider.wholeNumbers = true;
                }

                // The prefab carries a localization component that re-applies its own term
                // ("Sound Effects" - it was cloned from the audio settings slider) during Start,
                // which is why both our label AND the game's own Setup assignment get clobbered.
                // Disabling it before Start runs stops that. Matched by type name so we do not
                // depend on I2's namespace, and so any similar component is caught too.
                foreach (var comp in script.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (comp == null) continue;
                    var n = comp.GetType().Name;
                    if (n == "Localize" || n == "LocalizationParamsManager") comp.enabled = false;
                }

                ApplyLabel(script, label);

                // Belt and braces: re-apply next frame so the label sticks even if something
                // other than localization is doing the overwriting.
                menu.StartCoroutine(ReapplyLabel(script, label));
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Palette] Slider styling failed: {ex.Message}");
            }
        }

        private static void ApplyLabel(SliderScript script, string label)
        {
            if (script == null) return;

            var valueText = AccessTools.Field(typeof(SliderScript), "sliderValueText").GetValue(script) as TMP_Text;
            foreach (var t in script.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t == null || ReferenceEquals(t, valueText)) continue;
                t.text = label;
            }

            // The visible label may be a legacy uGUI Text rather than TMP.
            foreach (var t in script.GetComponentsInChildren<Text>(true))
            {
                if (t != null) t.text = label;
            }
        }

        private static IEnumerator ReapplyLabel(SliderScript script, string label)
        {
            yield return null;
            ApplyLabel(script, label);
        }

        private static void Refresh()
        {
            if (_preview != null) _preview.text = PreviewText();
        }

        private static string PreviewText()
        {
            string p = PaletteConfig.ToHex(Primary);
            string s = PaletteConfig.ToHex(Secondary);

            // Single line, with visible text AFTER each swatch.
            // <mark> only highlights glyphs that actually get laid out, and TMP trims trailing
            // whitespace at the end of a line - so a mark wrapping nothing but spaces before a
            // newline renders zero-width (invisible). Keeping real text on the same line is why
            // the swatches in the list below always worked.
            return $"{Swatch(p)}<color=#CCCCCC>{p}</color>   {Swatch(s)}<color=#CCCCCC>{s}</color>";
        }

        /// <summary>TMP's mark tag renders a solid colour bar without needing a block glyph.</summary>
        private static string Swatch(string hex)
        {
            var h = (hex ?? "#FFFFFF").TrimStart('#');
            if (h.Length > 6) h = h.Substring(0, 6);
            return $"<mark=#{h}FF>      </mark> ";
        }

        /// <summary>
        /// Prompts for a name using the game's own text-input popup, then commits the colour.
        ///
        /// Silk's mod menu has no input widget, but the game ships one (Announcer.TextInuptPopup -
        /// the typo is theirs). Reusing it means correct focus handling, length validation, and
        /// the controller virtual keyboard, none of which we would get from a hand-built field.
        /// </summary>
        private static void AddCurrent(ModsMenuPopup menu)
        {
            // Captured now: the sliders could be touched again before the popup is confirmed.
            Color primary = Primary, secondary = Secondary;

            try
            {
                // Show which slot is being named - the ID is what vanilla saves against, and it
                // never changes on rename, so it is the stable handle worth surfacing.
                string nextId = PaletteConfig.PeekNextPickerId();

                Announcer.TextInuptPopup(
                    $"Name this colour   ({nextId})",
                    "Add",
                    typed => Commit(menu, primary, secondary, typed),
                    null,
                    24,     // maxLength
                    1,      // minLength
                    false);
            }
            catch (Exception ex)
            {
                // If the popup is unavailable for any reason, still add the colour with a default
                // name rather than losing the work.
                Logger.LogError($"[Palette] Name popup failed ({ex.Message}); adding unnamed.");
                Commit(menu, primary, secondary, null);
            }
        }

        private static void Commit(ModsMenuPopup menu, Color primary, Color secondary, string name)
        {
            var entry = PaletteConfig.AddPickerColor(primary, secondary, name);
            if (entry == null)
            {
                Announcer.InformationPopup("Could not add colour - see the log.");
                return;
            }

            // Make it selectable right away if the customization menu already exists.
            try
            {
                if (CustomizationMenu.instance != null) PaletteInjector.Inject(CustomizationMenu.instance);
            }
            catch (Exception ex) { Logger.LogError($"[Palette] Live inject failed: {ex.Message}"); }

            Announcer.InformationPopup($"Added \"{entry.Name}\" ({entry.Primary} / {entry.Secondary}).");
            RebuildDeferred(menu);
        }

        private static void Rename(ModsMenuPopup menu, string id)
        {
            try
            {
                var current = PaletteConfig.FindById(id);
                string currentName = current != null ? current.Name : "";

                Announcer.TextInuptPopup($"Rename \"{currentName}\"   ({id})", "Rename",
                    typed =>
                    {
                        if (PaletteConfig.RenamePickerColor(id, typed)) RebuildDeferred(menu);
                    },
                    null, 24, 1, false);

                // Prefill with the existing name. The popup API takes no initial value, so the
                // field is set on the live instance once it exists.
                menu.StartCoroutine(PrefillInput(currentName));
            }
            catch (Exception ex) { Logger.LogError($"[Palette] Rename popup failed: {ex.Message}"); }
        }

        private static IEnumerator PrefillInput(string text)
        {
            yield return null;
            try
            {
                var popup = UnityEngine.Object.FindObjectOfType<TextInputPopupScript>();
                if (popup == null) yield break;

                if (AccessTools.Field(typeof(TextInputPopupScript), "inputField")
                        .GetValue(popup) is TMP_InputField field)
                {
                    field.text = text;
                    field.caretPosition = text?.Length ?? 0;
                }
            }
            catch (Exception ex) { Logger.LogError($"[Palette] Prefill failed: {ex.Message}"); }
        }

        private static void Remove(ModsMenuPopup menu, string id)
        {
            if (PaletteConfig.RemovePickerColor(id))
            {
                Announcer.InformationPopup($"Removed {id}. Restart to clear it from the carousel.");
                RebuildDeferred(menu);
            }
        }

        /// <summary>
        /// Rebuilds after the text popup has finished closing.
        ///
        /// Rebuilding straight from the popup's confirm callback runs while that popup is still
        /// tearing down, and the new widgets do not appear until the menu is reopened. ModsMenu
        /// is a MonoBehaviour, so we can just wait a couple of frames.
        /// </summary>
        private static void RebuildDeferred(ModsMenuPopup menu)
        {
            if (menu == null) return;
            try { menu.StartCoroutine(RebuildAfterFrames(menu)); }
            catch (Exception ex)
            {
                Logger.LogError($"[Palette] Deferred rebuild failed, rebuilding inline: {ex.Message}");
                Rebuild(menu);
            }
        }

        private static IEnumerator RebuildAfterFrames(ModsMenuPopup menu)
        {
            yield return null;
            yield return null;
            Rebuild(menu);
        }

        /// <summary>Same teardown-and-rebuild approach SpiderSurge uses for its menu.</summary>
        private static void Rebuild(ModsMenuPopup menu)
        {
            if (menu == null || menu.objectParent == null) return;
            for (int i = menu.objectParent.childCount - 1; i >= 0; i--)
            {
                var child = menu.objectParent.GetChild(i);
                if (child != null) UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
            _preview = null;
            Build(menu);
        }
    }
}

