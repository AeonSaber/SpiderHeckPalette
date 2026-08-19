using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Logger = Silk.Logger;

namespace SpiderHeckPalette
{
    public class ColorEntry
    {
        /// <summary>
        /// Stable identity. Vanilla persistence saves the selected ColorConfig's ID, so changing
        /// this after the fact orphans a player's saved selection (it falls back to a random color).
        /// </summary>
        public string Id { get; set; }
        public string Name { get; set; }
        public string Primary { get; set; }
        public string Secondary { get; set; }
    }

    public class PaletteFile
    {
        public List<ColorEntry> Colors { get; set; } = new List<ColorEntry>();
    }

    /// <summary>
    /// Own YAML rather than Silk's Config API, which is a flat Dictionary&lt;string,object&gt;
    /// and cannot express a list of colors.
    ///
    /// TWO files, deliberately:
    ///   SpiderHeckPalette.yaml         - hand-edited, richly commented, never rewritten by code
    ///   SpiderHeckPalette.picker.yaml  - owned by the in-game picker, rewritten on every add
    /// Serializing YAML strips comments, so the picker owning its own file is what keeps the
    /// documentation in the hand-edited one intact.
    /// </summary>
    public static class PaletteConfig
    {
        private static readonly string ConfigDir = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Silk", "Config", "Mods");

        public static readonly string ConfigPath = Path.Combine(ConfigDir, "SpiderHeckPalette.yaml");
        public static readonly string PickerPath = Path.Combine(ConfigDir, "SpiderHeckPalette.picker.yaml");

        /// <summary>Merged view: hand-edited entries first, then picker-created ones.</summary>
        public static List<ColorEntry> Colors { get; private set; } = new List<ColorEntry>();

        /// <summary>Picker-created entries only - the set this class may rewrite.</summary>
        public static List<ColorEntry> PickerColors { get; private set; } = new List<ColorEntry>();

        private const string DefaultYaml = @"# SpiderHeckPalette - custom spider colors.
#
# Each entry becomes a normal selectable swatch in the vanilla customization menu.
# 'id' MUST be stable: the game saves your selection by ID. Renaming an id orphans
# a saved selection (the game then falls back to a random unlocked color).
#
# primary   = body colour
# secondary = leg colour
#
# Colors added with the in-game picker (Mods menu -> SpiderHeckPalette) are stored
# separately in SpiderHeckPalette.picker.yaml so this file's comments survive.
# Feel free to move entries from that file into this one and rename them.
colors:
  - id: aeons_ember
    name: Ember
    primary: '#FF3B1F'
    secondary: '#FFB347'
  - id: aeons_void
    name: Void
    primary: '#1B1B2F'
    secondary: '#7B2CBF'
  - id: aeons_toxic
    name: Toxic
    primary: '#39FF14'
    secondary: '#0B6E4F'
";

        public static void Load()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                if (!File.Exists(ConfigPath))
                {
                    File.WriteAllText(ConfigPath, DefaultYaml);
                    Logger.LogInfo($"[Palette] Wrote default config: {ConfigPath}");
                }

                var manual = ReadFile(ConfigPath);
                PickerColors = ReadFile(PickerPath);

                var merged = new List<ColorEntry>(manual);
                merged.AddRange(PickerColors);
                Colors = Dedupe(merged);

                Logger.LogInfo($"[Palette] Loaded {Colors.Count} color(s) " +
                               $"({manual.Count} manual, {PickerColors.Count} from picker).");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Palette] Config load failed, continuing with none: {ex.Message}");
                Colors = new List<ColorEntry>();
                PickerColors = new List<ColorEntry>();
            }
        }

        private static List<ColorEntry> ReadFile(string path)
        {
            if (!File.Exists(path)) return new List<ColorEntry>();
            try
            {
                var parsed = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build()
                    .Deserialize<PaletteFile>(File.ReadAllText(path));
                return Validate(parsed?.Colors);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Palette] Failed to read {Path.GetFileName(path)}: {ex.Message}");
                return new List<ColorEntry>();
            }
        }

        /// <summary>
        /// Adds a picker-created color, persists it, and returns it (null on failure).
        ///
        /// The ID stays auto-generated even though the user supplies a name: vanilla saves the
        /// selected swatch by ID, so a rename must never change it or it would orphan a saved
        /// selection.
        /// </summary>
        public static ColorEntry AddPickerColor(Color primary, Color secondary, string name = null)
        {
            try
            {
                string id = PeekNextPickerId();
                name = string.IsNullOrWhiteSpace(name) ? $"Custom {PickerColors.Count + 1}" : name.Trim();

                var entry = new ColorEntry
                {
                    Id = id,
                    Name = name,
                    Primary = ToHex(primary),
                    Secondary = ToHex(secondary)
                };

                PickerColors.Add(entry);
                Colors.Add(entry);
                SavePickerFile();
                Logger.LogInfo($"[Palette] Added '{entry.Id}' ({entry.Primary} / {entry.Secondary}).");
                return entry;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Palette] AddPickerColor failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// The ID the next added color will receive. Exposed so the naming prompt can show which
        /// slot is being named before it exists.
        /// </summary>
        public static string PeekNextPickerId()
        {
            int n = PickerColors.Count + 1;
            string id;
            do { id = $"picker_{n}"; n++; } while (FindById(id) != null);
            return id;
        }

        /// <summary>Renames a picker color. The ID is deliberately left untouched.</summary>
        public static bool RenamePickerColor(string id, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) return false;

            var entry = PickerColors.Find(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            if (entry == null) return false;

            entry.Name = newName.Trim();
            var merged = Colors.Find(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            if (merged != null) merged.Name = entry.Name;

            SavePickerFile();
            Logger.LogInfo($"[Palette] Renamed '{id}' to '{entry.Name}'.");
            return true;
        }

        public static bool RemovePickerColor(string id)
        {
            var entry = PickerColors.Find(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            if (entry == null) return false;

            PickerColors.Remove(entry);
            Colors.RemoveAll(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            SavePickerFile();
            Logger.LogInfo($"[Palette] Removed picker color '{id}'.");
            return true;
        }

        private static void SavePickerFile()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                var yaml = new SerializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build()
                    .Serialize(new PaletteFile { Colors = PickerColors });

                File.WriteAllText(PickerPath,
                    "# Generated by the in-game picker (Mods menu -> SpiderHeckPalette).\n" +
                    "# Safe to hand-edit, but this file is REWRITTEN whenever you add or remove\n" +
                    "# a color in game, so comments here will be lost. Put anything you want to\n" +
                    "# keep in SpiderHeckPalette.yaml instead.\n" + yaml);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Palette] Could not save picker file: {ex.Message}");
            }
        }

        public static ColorEntry FindById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Colors.Find(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private static List<ColorEntry> Dedupe(List<ColorEntry> input)
        {
            var result = new List<ColorEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in input)
            {
                if (!seen.Add(c.Id)) { Logger.LogError($"[Palette] Duplicate id '{c.Id}' ignored."); continue; }
                result.Add(c);
            }
            return result;
        }

        private static List<ColorEntry> Validate(List<ColorEntry> input)
        {
            var result = new List<ColorEntry>();
            if (input == null) return result;

            foreach (var c in input)
            {
                if (c == null || string.IsNullOrEmpty(c.Id))
                {
                    Logger.LogError("[Palette] Skipping entry with no id.");
                    continue;
                }
                if (!TryParseHex(c.Primary, out _) || !TryParseHex(c.Secondary, out _))
                {
                    Logger.LogError($"[Palette] Skipping '{c.Id}': bad hex ('{c.Primary}' / '{c.Secondary}').");
                    continue;
                }
                if (string.IsNullOrEmpty(c.Name)) c.Name = c.Id;
                result.Add(c);
            }
            return result;
        }

        /// <summary>Parses "#RRGGBB" / "RRGGBB" / "#RRGGBBAA". Alpha defaults to opaque.</summary>
        public static bool TryParseHex(string hex, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(hex)) return false;

            var s = hex.Trim().TrimStart('#');
            if (s.Length != 6 && s.Length != 8) return false;

            try
            {
                float r = int.Parse(s.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255f;
                float g = int.Parse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255f;
                float b = int.Parse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255f;
                float a = s.Length == 8
                    ? int.Parse(s.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255f
                    : 1f;
                color = new Color(r, g, b, a);
                return true;
            }
            catch { return false; }
        }

        public static string ToHex(Color c)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
            return $"#{r:X2}{g:X2}{b:X2}";
        }
    }
}
