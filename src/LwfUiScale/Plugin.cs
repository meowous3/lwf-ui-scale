using System.Globalization;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LwfUiScale
{
    /// <summary>
    /// Scales the game's UI, set from a row on the Graphic Settings page.
    /// </summary>
    [BepInPlugin(PluginGuid, "LWF UI Scale", "0.2.1")]
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
    /// Reapplies the scale when a scene brings new canvases.
    ///
    /// This used to sweep every second with
    /// <c>FindObjectsByType&lt;CanvasScaler&gt;(FindObjectsInactive.Include)</c>, which walks
    /// every object in the scene including inactive ones. A camera probe caught what that cost:
    /// frame times held at 16.67ms and then spiked to 105ms, once a second, dead on the sweep
    /// interval. Movement is deltaTime-scaled, so the camera covered six frames of ground in one
    /// — a jolt while walking, and nothing at all while standing still.
    ///
    /// Canvases arrive with scenes, so that is when to look for them. Additive loads fire the
    /// same event, and a scaler created outside one is picked up the next time the scale changes.
    /// </summary>
    internal sealed class ScaleKeeper : MonoBehaviour
    {
        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            Apply("startup");
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
        {
            UiScale.Prune();
            Apply(scene.name);
        }

        private static void Apply(string reason)
        {
            var touched = UiScale.ApplyAll(Plugin.Scale);
            if (touched > 0)
            {
                Plugin.Log.LogInfo($"UI scale: applied to {touched} canvas scaler(s) ({reason}).");
            }
        }
    }
}
