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

            // The control comes from the Sound tab, the shape from this page.
            var sliderSource = FindSlider(root);
            var group = FindGroup(page.transform);

            Dump(root);

            if (sliderSource == null)
            {
                Plugin.Log.LogError("UI scale: no slider anywhere in settings to copy; no row added.");
                return;
            }

            if (group == null)
            {
                Plugin.Log.LogError("UI scale: no setting group on this page to copy; no row added.");
                return;
            }

            Build(sliderSource, group);
        }


        /// <summary>
        /// The row that owns a slider: the highest ancestor still containing exactly one, which
        /// is the row rather than the list that holds every row.
        /// </summary>
        /// <summary>The slider itself, not its row — only the control is wanted.</summary>
        private static GameObject FindSlider(Transform root)
        {
            // Scrollbars are Sliders too, and every scroll view has one; a settings slider is
            // distinguished by not being part of one.
            var slider = root.GetComponentsInChildren<Slider>(includeInactive: true)
                             .FirstOrDefault(s => s.GetComponentInParent<ScrollRect>() == null);
            return slider != null ? slider.gameObject : null;
        }

        /// <summary>A setting group on this page: the child of the scroll content that holds a
        /// pull-down, which is a header bar and a control bar laid out the page's way.</summary>
        private static GameObject FindGroup(Transform page)
        {
            var cell = page.GetComponentsInChildren<PullDownCell>(includeInactive: true).FirstOrDefault();
            if (cell == null) return null;

            var group = cell.transform;
            while (group.parent != null
                   && group.parent != page
                   && group.parent.GetComponentsInChildren<PullDownCell>(includeInactive: true).Length == 1)
            {
                group = group.parent;
            }

            return group.gameObject;
        }

        /// <summary>
        /// Builds the row by cloning a whole setting group and swapping its pull-down for a
        /// slider.
        ///
        /// The page's rows are groups — ScreenMode, UsingDisplay, AspectRatio — each a header
        /// bar above a control bar, and each 3415 units wide with its visible bars as narrow
        /// centred children. Copying only the outer size and stretching a slider across it
        /// produced a bar the width of the screen. Cloning the group keeps every one of those
        /// proportions, so the only thing that has to be positioned is the slider, and it
        /// inherits the exact rect the pull-down had.
        /// </summary>
        private static void Build(GameObject sliderSource, GameObject group)
        {
            var row = Object.Instantiate(group, group.transform.parent);
            row.name = OurRow;
            row.SetActive(true);
            row.transform.SetAsLastSibling();

            StripLocalisation(row);

            var cell = row.GetComponentInChildren<PullDownCell>(includeInactive: true);
            if (cell == null)
            {
                Plugin.Log.LogError("UI scale: the cloned group has no pull-down to replace.");
                Object.Destroy(row);
                return;
            }

            // The pull-down's own footprint, which the slider takes over.
            var cellRect = cell.GetComponent<RectTransform>();
            var holder = cellRect.parent;
            var index = cellRect.GetSiblingIndex();
            var anchorMin = cellRect.anchorMin;
            var anchorMax = cellRect.anchorMax;
            var pivot = cellRect.pivot;
            var offsetMin = cellRect.offsetMin;
            var offsetMax = cellRect.offsetMax;
            var anchored = cellRect.anchoredPosition;
            var size = cellRect.sizeDelta;

            Object.DestroyImmediate(cell.gameObject);

            var sliderObject = Object.Instantiate(sliderSource, holder);
            sliderObject.name = "LwfUiScaleSlider";
            sliderObject.SetActive(true);
            sliderObject.transform.SetSiblingIndex(index);

            var sliderRect = sliderObject.GetComponent<RectTransform>();
            sliderRect.anchorMin = anchorMin;
            sliderRect.anchorMax = anchorMax;
            sliderRect.pivot = pivot;
            sliderRect.anchoredPosition = anchored;
            sliderRect.sizeDelta = size;
            sliderRect.offsetMin = offsetMin;
            sliderRect.offsetMax = offsetMax;
            sliderRect.localScale = Vector3.one;

            var slider = sliderObject.GetComponent<Slider>()
                         ?? sliderObject.GetComponentInChildren<Slider>(includeInactive: true);
            if (slider == null)
            {
                Plugin.Log.LogError("UI scale: the copied slider has no Slider component.");
                Object.Destroy(row);
                return;
            }

            // A copy still carries the listeners of whatever it came from, which would set the
            // game's volume as this slider moves.
            slider.onValueChanged.RemoveAllListeners();
            slider.wholeNumbers = true;
            slider.minValue = Plugin.MinPercent;
            slider.maxValue = Plugin.MaxPercent;
            slider.SetValueWithoutNotify(Plugin.Percent);
            slider.onValueChanged.AddListener(OnSliderMoved);
            if (slider.GetComponent<SliderCommit>() == null) slider.gameObject.AddComponent<SliderCommit>();

            Label(row, sliderObject);

            Plugin.Log.LogInfo($"UI scale: row built from '{group.name}' at {Plugin.Percent}%, "
                               + $"slider rect {sliderRect.rect.width}x{sliderRect.rect.height}.");
        }

        /// <summary>
        /// Sets the group's header to name this setting, and puts the percentage at the right of
        /// the slider's bar.
        /// </summary>
        private static void Label(GameObject row, GameObject sliderObject)
        {
            _row = row;
            _valueText = null;

            var header = row.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                            .FirstOrDefault(t => !t.transform.IsChildOf(sliderObject.transform));
            header?.SetText(RowLabel);

            if (header != null)
            {
                var copy = Object.Instantiate(header.gameObject, sliderObject.transform.parent);
                copy.name = "LwfUiScaleValue";
                copy.transform.SetSiblingIndex(sliderObject.transform.GetSiblingIndex() + 1);

                _valueText = copy.GetComponent<TMP_Text>();
                _valueText.alignment = TextAlignmentOptions.Right;
                _valueText.textWrappingMode = TextWrappingModes.NoWrap;

                // Over the right end of the slider's bar, so the number sits with the control
                // rather than floating in the row's empty margins.
                var sliderRect = sliderObject.GetComponent<RectTransform>();
                var rect = copy.GetComponent<RectTransform>();
                rect.anchorMin = sliderRect.anchorMin;
                rect.anchorMax = sliderRect.anchorMax;
                rect.pivot = sliderRect.pivot;
                rect.anchoredPosition = sliderRect.anchoredPosition;
                rect.sizeDelta = sliderRect.sizeDelta;
                rect.offsetMin = sliderRect.offsetMin;
                rect.offsetMax = sliderRect.offsetMax;
                rect.localScale = Vector3.one;
            }

            RefreshValueText();
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
