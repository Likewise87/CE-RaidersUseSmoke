// File: TSA_SmokeUnderFire_Settings.cs
// Project: TSA.CE.RaidersUseSmoke (Odyssey 1.6 only)

using System;
using UnityEngine;
using Verse;

namespace TSA.CE.SmokeUnderFire
{
    public enum SliderFormat { Fixed0, Fixed1, Fixed2, Multiplier, Percent, PercentDecimal }

    public class SmokeUnderFireSettings : ModSettings
    {
        // Defaults
        public const bool DefaultDebug = false;
        public const float DefaultRadius = 2.6f;    // cells
        public const int DefaultCooldownSec = 5;    // seconds

        // Saved values
        public bool debugLogs = DefaultDebug;
        public float smokeRadius = DefaultRadius;      // 1.0 – 8.0
        public int cooldownSeconds = DefaultCooldownSec; // 1 – 20

        public int CooldownTicks => Mathf.Clamp(cooldownSeconds, 1, 20) * 60;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref debugLogs, "debugLogs", DefaultDebug);
            Scribe_Values.Look(ref smokeRadius, "smokeRadius", DefaultRadius);
            Scribe_Values.Look(ref cooldownSeconds, "cooldownSeconds", DefaultCooldownSec);
            smokeRadius = Mathf.Clamp(smokeRadius, 1f, 8f);
            cooldownSeconds = Mathf.Clamp(cooldownSeconds, 1, 20);
        }

        public void RestoreDefaults()
        {
            debugLogs = DefaultDebug;
            smokeRadius = DefaultRadius;
            cooldownSeconds = DefaultCooldownSec;
        }
    }

    public class SmokeUnderFireMod : Mod
    {
        public static SmokeUnderFireSettings Settings;

        public SmokeUnderFireMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<SmokeUnderFireSettings>();
        }

        public override string SettingsCategory() => "Raiders Use Smoke (CE)";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var ls = new Listing_Standard();
            ls.Begin(inRect);

            DrawResetButton(ls, () => Settings.RestoreDefaults());

            DrawCheckbox(ls, "Enable debug log spam", ref Settings.debugLogs,
                "When enabled, the mod writes helpful messages to the log.",
                defaultValue: SmokeUnderFireSettings.DefaultDebug);

            Settings.smokeRadius = Mathf.Clamp(
                LabeledSlider(ls, "Smoke radius (cells)", Settings.smokeRadius, 1f, 8f,
                    "Radius of the smoke cloud placed when a raider is hit.",
                    0.1f, SliderFormat.Fixed1, SmokeUnderFireSettings.DefaultRadius),
                1f, 8f);

            Settings.cooldownSeconds = Mathf.Clamp(Mathf.RoundToInt(
                LabeledSlider(ls, "Cooldown (seconds)", Settings.cooldownSeconds, 1f, 20f,
                    "Minimum seconds between smoke placements for the same pawn.",
                    1f, SliderFormat.Fixed0, SmokeUnderFireSettings.DefaultCooldownSec)),
                1, 20);

            ls.End();
        }

        // ----- WD-style helpers (kept in Settings.cs; no separate library file) -----

        private static void DrawResetButton(Listing_Standard l, Action onReset)
        {
            if (onReset == null) return;
            l.Gap(4f);
            Rect row = l.GetRect(30f);
            float resetW = Mathf.Min(250f, row.width);
            if (resetW < 120f) resetW = Mathf.Max(100f, row.width * 0.35f);
            Rect resetRect = new Rect(row.x, row.y, resetW, row.height);
            Color prev = GUI.color;
            GUI.color = new Color(1f, 0.5f, 0.5f);
            if (Widgets.ButtonText(resetRect, "Restore defaults")) onReset();
            GUI.color = prev;
            l.Gap(6f);
        }

        private static float LabeledSlider(Listing_Standard l, string label, float val, float min, float max, string tooltip = null, float step = 0.1f, SliderFormat format = SliderFormat.Fixed1, float? defaultValue = null)
        {
            l.Gap(2f);
            Rect r = l.GetRect(24f);
            if (!tooltip.NullOrEmpty()) TooltipHandler.TipRegion(r, TooltipWithDefault(tooltip, defaultValue, format));
            Widgets.Label(r.LeftPart(0.5f), $"{label}: {FormatValue(val, format).Colorize(Color.cyan)}");
            return Widgets.HorizontalSlider(r.RightPart(0.5f), val, min, max, false, null, null, null, step);
        }

        private static void DrawCheckbox(Listing_Standard l, string label, ref bool value, string tooltip = null, float? rowHeight = null, bool? defaultValue = null)
        {
            l.Gap(2f);
            float h = rowHeight ?? 24f;
            Rect r = l.GetRect(h);
            if (!tooltip.NullOrEmpty()) TooltipHandler.TipRegion(r, TooltipWithDefault(tooltip, defaultValue));
            Rect inner = h > 24f ? new Rect(r.x, r.y + (h - 24f) / 2f, r.width, 24f) : r;
            Widgets.CheckboxLabeled(inner, label, ref value);
        }

        private static string TooltipWithDefault(string tooltip, float? defaultValue, SliderFormat format = SliderFormat.Fixed1)
        {
            if (!defaultValue.HasValue) return tooltip;
            return AppendDefault(tooltip, FormatValue(defaultValue.Value, format));
        }

        private static string TooltipWithDefault(string tooltip, bool? defaultValue)
        {
            if (!defaultValue.HasValue) return tooltip;
            return AppendDefault(tooltip, defaultValue.Value ? "On" : "Off");
        }

        private static string AppendDefault(string tooltip, string def)
        {
            string line = $"Default: {def}";
            return tooltip.NullOrEmpty() ? line : tooltip + "\n" + line;
        }

        private static string FormatValue(float val, SliderFormat format) => format switch
        {
            SliderFormat.Fixed0 => val.ToString("F0"),
            SliderFormat.Fixed1 => val.ToString("F1"),
            SliderFormat.Fixed2 => val.ToString("F2"),
            SliderFormat.Multiplier => val.ToString("F2") + "x",
            SliderFormat.Percent => (val * 100f).ToString("F0") + " %",
            SliderFormat.PercentDecimal => (val * 100f).ToString("F1") + " %",
            _ => val.ToString()
        };
    }
}
