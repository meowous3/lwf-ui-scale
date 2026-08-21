using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using TMPro;
using UI.Settings;
using UnityEngine;
using UnityEngine.UI;
using Utility.Localization;

namespace LwfUiScale
{
    /// <summary>
    /// Adds a UI Scale slider to the settings screen.
    ///
    /// The row is a copy of one the game already has rather than anything built here, so it
    /// inherits the screen's layout and styling. The template is found by looking for a
    /// <see cref="Slider"/> anywhere under the settings view — the volume rows — because the
    /// objects are not reliably named: <c>GraphicSettings</c> looks its own controls up by name
    /// only as a fallback, and in a built scene those fields are already wired, so the names in
    /// the code are not necessarily the names in the hierarchy.
    /// </summary>
    [HarmonyPatch(typeof(GraphicSettings), "OnPageOpen")]
    internal static class SettingsRowPatch
    {
        private const string OurRow = "LwfUiScaleRow";
        private const string RowLabel = "UI Scale";

        private static bool _dumped;

        private static void Postfix(GraphicSettings __instance)
        {
            var page = __instance.gameObject;

            var existing = FindByName(page.transform, OurRow);
            if (existing != null)
            {
                Sync(existing);
                return;
            }

            // The whole settings screen, not just this page: the slider rows live on another
            // tab, and the pages are siblings under one view.
            var view = __instance.GetComponentInParent<SettingsView>(includeInactive: true);
            var root = view != null ? view.transform : page.transform;

            var template = FindSliderRow(root);
            if (template == null)
            {
                Plugin.Log.LogError("UI scale: no slider row found to copy; no row added.");
                Dump(root);
                return;
            }

            Dump(root);

            var host = FindRowHost(page.transform);
            if (host == null)
            {
                Plugin.Log.LogError("UI scale: no existing row on the page to sit beside; no row added.");
                return;
            }

            Build(template, host);
        }

        /// <summary>
        /// Where this page keeps its rows: the parent of one of them.
        ///
        /// Not the page object itself. The rows sit several levels down, inside the page's
        /// scroll view, under a layout group that positions them — parenting to the page root
        /// put the row outside the panel entirely, at the top-left of the screen.
        /// </summary>
        private static Transform FindRowHost(Transform page)
        {
            var cell = page.GetComponentsInChildren<PullDownCell>(includeInactive: true).FirstOrDefault();
            if (cell == null) return null;

            // Same walk as the slider row: up to the highest ancestor still holding exactly one
            // pull-down, which is a row. Its parent is the container every row shares.
            var row = cell.transform;
            while (row.parent != null
                   && row.parent != page
                   && row.parent.GetComponentsInChildren<PullDownCell>(includeInactive: true).Length == 1)
            {
                row = row.parent;
            }

            return row.parent;
        }

        /// <summary>
        /// The row that owns a slider: the highest ancestor still containing exactly one, which
        /// is the row rather than the list that holds every row.
        /// </summary>
        private static GameObject FindSliderRow(Transform root)
        {
            var slider = root.GetComponentsInChildren<Slider>(includeInactive: true).FirstOrDefault();
            if (slider == null) return null;

            var row = slider.transform;
            while (row.parent != null
                   && row.parent != root
                   && row.parent.GetComponentsInChildren<Slider>(includeInactive: true).Length == 1)
            {
                row = row.parent;
            }

            return row.gameObject;
        }

        private static void Build(GameObject template, Transform parent)
        {
            var row = Object.Instantiate(template, parent);
            row.name = OurRow;
            row.SetActive(true);
            row.transform.SetAsLastSibling();

            StripLocalisation(row);

            var slider = row.GetComponentInChildren<Slider>(includeInactive: true);
            if (slider == null)
            {
                Plugin.Log.LogError("UI scale: the copied row lost its slider; no row added.");
                Object.Destroy(row);
                return;
            }

            // A copied row still carries the listeners of whatever it was cloned from, which
            // would set the game's volume as this slider moves.
            slider.onValueChanged.RemoveAllListeners();

            slider.wholeNumbers = true;
            slider.minValue = Plugin.MinPercent;
            slider.maxValue = Plugin.MaxPercent;
            slider.SetValueWithoutNotify(Plugin.Percent);
            slider.onValueChanged.AddListener(OnSliderMoved);
            if (slider.GetComponent<SliderCommit>() == null) slider.gameObject.AddComponent<SliderCommit>();

            Label(row, slider);
            Plugin.Log.LogInfo($"UI scale: row added at {Plugin.Percent}%.");
        }

        /// <summary>Moves the readout only. The scale itself is applied by
        /// <see cref="SliderCommit"/> when the drag ends.</summary>
        private static void OnSliderMoved(float value)
        {
            if (_valueText != null) _valueText.SetText($"{Mathf.RoundToInt(value)}%");
        }

        private static void Sync(GameObject row)
        {
            var slider = row.GetComponentInChildren<Slider>(includeInactive: true);
            slider?.SetValueWithoutNotify(Plugin.Percent);
            RefreshValueText();
        }

        /// <summary>
        /// Names the row and points its numeric readouts at this setting.
        ///
        /// The texts are told apart by position rather than by name: the label is the one
        /// outside the slider's own subtree, and the readouts inside it are the value and the
        /// two end markers.
        /// </summary>
        private static void Label(GameObject row, Slider slider)
        {
            _row = row;
            _valueText = null;

            // Told apart by position, since the objects are not reliably named: the first text
            // outside the slider's subtree is the row's name, and the next is where the copied
            // row showed its value.
            var outside = row.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                             .Where(t => !t.transform.IsChildOf(slider.transform))
                             .ToArray();

            if (outside.Length > 0) outside[0].SetText(RowLabel);
            if (outside.Length > 1) _valueText = outside[1];

            RefreshValueText();
        }

        private static GameObject _row;
        private static TMP_Text _valueText;

        /// <summary>Shows the current percentage on whichever readout the cloned row uses.</summary>
        private static void RefreshValueText()
        {
            if (_valueText != null) _valueText.SetText($"{Plugin.Percent}%");
        }

        private static void StripLocalisation(GameObject row)
        {
            foreach (var link in row.GetComponentsInChildren<TableLinkLocalizedText>(includeInactive: true))
            {
                Object.Destroy(link);
            }

            foreach (var behaviour in row.GetComponentsInChildren<MonoBehaviour>(includeInactive: true))
            {
                if (behaviour != null && behaviour.GetType().Name == "LocalizeStringEvent")
                {
                    Object.Destroy(behaviour);
                }
            }
        }

        private static GameObject FindByName(Transform root, string name)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (child.name == name) return child.gameObject;
            }

            return null;
        }

        /// <summary>
        /// Prints the settings hierarchy once, with the components that matter for finding a
        /// row. Logged whether or not the copy worked: the objects are not named after the code
        /// that uses them, so this is the only way to see what is actually there.
        /// </summary>
        private static void Dump(Transform root)
        {
            if (_dumped) return;
            _dumped = true;

            var sb = new StringBuilder();
            Walk(root, 0, sb);

            // One line per entry rather than one long message: a single multi-line log line gets
            // truncated by the console this is read through, which lost the part that mattered.
            foreach (var line in sb.ToString().Split('\n'))
            {
                if (line.Length > 0) Plugin.Log.LogInfo("hierarchy| " + line);
            }
        }

        private static void Walk(Transform t, int depth, StringBuilder sb)
        {
            if (depth > 8) return;

            var parts = new List<string>();
            if (t.GetComponent<Slider>() != null) parts.Add("Slider");
            if (t.GetComponent<Toggle>() != null) parts.Add("Toggle");
            if (t.GetComponent<PullDownCell>() != null) parts.Add("PullDownCell");
            if (t.GetComponent<TMP_Text>() != null) parts.Add("Text");

            sb.Append(' ', depth * 2).Append(t.name);
            if (parts.Count > 0) sb.Append("  [").Append(string.Join(",", parts)).Append(']');
            sb.Append('\n');

            for (var i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1, sb);
        }
    }
}
