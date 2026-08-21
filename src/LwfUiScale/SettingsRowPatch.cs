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

            var reference = FindPullDownRow(page.transform);
            if (reference == null)
            {
                Plugin.Log.LogError("UI scale: no existing row on the page to sit beside; no row added.");
                return;
            }

            Build(template, reference);
        }

        /// <summary>
        /// Where this page keeps its rows: the parent of one of them.
        ///
        /// Not the page object itself. The rows sit several levels down, inside the page's
        /// scroll view, under a layout group that positions them — parenting to the page root
        /// put the row outside the panel entirely, at the top-left of the screen.
        /// </summary>
        private static GameObject FindPullDownRow(Transform page)
        {
            var cell = page.GetComponentsInChildren<PullDownCell>(includeInactive: true).FirstOrDefault();
            if (cell == null) return null;

            // Same walk as the slider row: up to the highest ancestor still holding exactly one
            // pull-down, which is a row.
            var row = cell.transform;
            while (row.parent != null
                   && row.parent != page
                   && row.parent.GetComponentsInChildren<PullDownCell>(includeInactive: true).Length == 1)
            {
                row = row.parent;
            }

            return row.gameObject;
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

        private static void Build(GameObject template, GameObject reference)
        {
            var row = Object.Instantiate(template, reference.transform.parent);
            row.name = OurRow;
            row.SetActive(true);
            row.transform.SetAsLastSibling();

            StripLocalisation(row);
            MatchGeometry(row, reference);

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
            FitSlider(row, slider);
            slider.onValueChanged.AddListener(OnSliderMoved);
            if (slider.GetComponent<SliderCommit>() == null) slider.gameObject.AddComponent<SliderCommit>();

            Label(row, slider);
            Plugin.Log.LogInfo($"UI scale: row added at {Plugin.Percent}%.");
        }

        /// <summary>Moves the readout only. The scale itself is applied by
        /// <see cref="SliderCommit"/> when the drag ends.</summary>
        /// <summary>
        /// Makes the copied row the size and shape of a row already on this page.
        ///
        /// The template comes from the Sound tab, which lays its rows out differently — left
        /// unchanged it was wider than the panel and the list did not reserve any height for it,
        /// so it drew over the row above. Copying the reference row's RectTransform and its
        /// LayoutElement hands both decisions back to the page's own layout.
        /// </summary>
        private static void MatchGeometry(GameObject row, GameObject reference)
        {
            var target = row.GetComponent<RectTransform>();
            var source = reference.GetComponent<RectTransform>();
            if (target == null || source == null) return;

            target.anchorMin = source.anchorMin;
            target.anchorMax = source.anchorMax;
            target.pivot = source.pivot;
            target.sizeDelta = source.sizeDelta;
            target.localScale = source.localScale;

            var from = reference.GetComponent<LayoutElement>();
            if (from != null)
            {
                var to = row.GetComponent<LayoutElement>() ?? row.AddComponent<LayoutElement>();
                to.minWidth = from.minWidth;
                to.minHeight = from.minHeight;
                to.preferredWidth = from.preferredWidth;
                to.preferredHeight = from.preferredHeight;
                to.flexibleWidth = from.flexibleWidth;
                to.flexibleHeight = from.flexibleHeight;
                to.ignoreLayout = from.ignoreLayout;
            }
            else
            {
                // No LayoutElement to copy: reserve the reference row's actual height so the
                // list still leaves room for this one.
                var to = row.GetComponent<LayoutElement>() ?? row.AddComponent<LayoutElement>();
                to.preferredHeight = source.rect.height;
                to.preferredWidth = source.rect.width;
            }

            Plugin.Log.LogInfo($"UI scale: row sized {target.sizeDelta} from '{reference.name}' "
                               + $"(rect {source.rect.width}x{source.rect.height}).");
        }

        /// <summary>
        /// Stretches the slider across the row instead of keeping the width it had on the Sound
        /// tab, which overran the panel. Anchored rather than sized, so it follows the row at any
        /// resolution — and inset on the left to leave the label its space.
        /// </summary>
        private static void FitSlider(GameObject row, Slider slider)
        {
            var rect = slider.GetComponent<RectTransform>();
            if (rect == null || rect.parent == row.transform.parent) return;

            const float labelWidth = 0.42f;   // of the row, matching where the label sits
            const float valueWidth = 0.14f;   // reserved on the right for the percentage
            const float rightPad = 8f;

            rect.anchorMin = new Vector2(labelWidth, 0f);
            rect.anchorMax = new Vector2(1f - valueWidth, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(0f, 12f);
            rect.offsetMax = new Vector2(-rightPad, -12f);
        }

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
            _valueText = outside.Length > 1 ? outside[1] : MakeValueText(row, outside.FirstOrDefault());

            RefreshValueText();
        }

        private static GameObject _row;
        private static TMP_Text _valueText;

        /// <summary>
        /// Adds a readout when the copied row has none.
        ///
        /// The volume rows carry only their label, so there was nowhere for the percentage to go
        /// and the slider stood alone with no number. Copying the label rather than building a
        /// text from scratch keeps the font, size and colour the screen already uses.
        /// </summary>
        private static TMP_Text MakeValueText(GameObject row, TMP_Text label)
        {
            if (label == null) return null;

            var copy = Object.Instantiate(label.gameObject, row.transform);
            copy.name = "LwfUiScaleValue";

            var text = copy.GetComponent<TMP_Text>();
            text.alignment = TextAlignmentOptions.Right;
            text.enableWordWrapping = false;

            var rect = copy.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.86f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(0f, 0f);
            rect.offsetMax = new Vector2(-8f, 0f);

            return text;
        }

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
