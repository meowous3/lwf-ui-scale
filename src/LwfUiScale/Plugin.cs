using System.Globalization;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace LwfUiScale
{
    /// <summary>
    /// Scales the game's UI, set from a row on the Graphic Settings page.
    /// </summary>
    [BepInPlugin(PluginGuid, "LWF UI Scale", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "dev.meow.lwfuiscale";

        internal const int MinPercent = 15;
        internal const int MaxPercent = 200;

        internal static ManualLogSource Log;
        private static ConfigEntry<int> _percent;

        /// <summary>Clamped rather than trusted: the config is a text file anyone can edit, and
        /// a zero or negative value would collapse every canvas.</summary>
        internal static int Percent => Mathf.Clamp(_percent.Value, MinPercent, MaxPercent);

        internal static float Scale => Percent / 100f;

        internal static string Label(int percent) =>
            percent.ToString(CultureInfo.InvariantCulture) + "%";

        private void Awake()
        {
            Log = Logger;
            _percent = Config.Bind("UI", "Scale", 100,
                $"Percentage, {MinPercent} to {MaxPercent}. Set it from Settings > Graphic; this "
                + "is where the result is kept.");

            new Harmony(PluginGuid).PatchAll();
            gameObject.AddComponent<ScaleKeeper>();

            Log.LogInfo($"UI scale ready at {Percent}%.");
        }

        /// <summary>Stores a new percentage and applies it immediately.</summary>
        internal static void Set(int percent)
        {
            _percent.Value = Mathf.Clamp(percent, MinPercent, MaxPercent);
            var touched = UiScale.ApplyAll(Scale);
            Log.LogInfo($"UI scale set to {Percent}%, applied to {touched} canvas scaler(s).");
        }
    }

    /// <summary>
    /// Reapplies the scale as canvases appear.
    ///
    /// Every scene brings its own canvases, and the game creates more while running, so a single
    /// pass at startup would only catch whatever existed at that moment. The sweep is cheap and
    /// idempotent — each scaler's unscaled value is remembered the first time it is seen — but it
    /// is not free, so it runs on an interval rather than every frame.
    /// </summary>
    internal sealed class ScaleKeeper : MonoBehaviour
    {
        private const float IntervalSeconds = 1f;

        private float _next;

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + IntervalSeconds;

            UiScale.Prune();
            UiScale.ApplyAll(Plugin.Scale);
        }
    }
}
