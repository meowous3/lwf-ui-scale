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

        /// <summary>The offered steps, as percentages. Ordered, because the row is a list.</summary>
        internal static readonly int[] Steps = { 75, 90, 100, 110, 125, 150, 175, 200 };

        internal static ManualLogSource Log;
        private static ConfigEntry<int> _percent;

        /// <summary>Clamped to the offered steps rather than trusted: the config is a text file
        /// and the row can only ever produce one of these.</summary>
        internal static int Percent
        {
            get
            {
                var wanted = _percent.Value;
                var best = Steps[0];
                foreach (var step in Steps)
                {
                    if (Mathf.Abs(step - wanted) < Mathf.Abs(best - wanted)) best = step;
                }

                return best;
            }
        }

        internal static float Scale => Percent / 100f;

        internal static string Label(int percent) =>
            percent.ToString(CultureInfo.InvariantCulture) + "%";

        private void Awake()
        {
            Log = Logger;
            _percent = Config.Bind("UI", "Scale", 100,
                "Percentage. Set it from Settings > Graphic; this is where the result is kept.");

            new Harmony(PluginGuid).PatchAll();
            gameObject.AddComponent<ScaleKeeper>();

            Log.LogInfo($"UI scale ready at {Percent}%.");
        }

        /// <summary>Stores a new percentage and applies it immediately.</summary>
        internal static void Set(int percent)
        {
            _percent.Value = percent;
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
