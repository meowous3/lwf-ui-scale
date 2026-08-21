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

            // The pull-down's own label is dark text on the same tan bar, which is exactly the
            // problem the percentage has. Taking its colour is more reliable than picking one.
            var cellText = cell.GetComponentInChildren<TMP_Text>(includeInactive: true);
            _valueColor = cellText != null ? cellText.color : (Color?)null;

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

            PadForOverflow(row);
            Report(row, group);
        }



        /// <summary>
        /// Sets the group's header to name this setting, and centres the percentage on the
        /// slider's bar.
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
                // A child of the slider, not a sibling of it.
                //
                // The container's VerticalLayoutGroup has childControlHeight off, so it spaces
                // rows by their own rect height and nothing else — a row is free to overflow its
                // box, and the layout will not notice. ScreenMode holds a header and one bar in
                // 150 units; adding a third child made this row's contents taller than the box it
                // is still measured by, and the excess drew over the setting above. Inside the
                // slider, the number costs the row no height at all.
                var copy = Object.Instantiate(header.gameObject, sliderObject.transform);
                copy.name = "LwfUiScaleValue";

                // The header is a bar, not bare text: cloning it brought its background along,
                // which drew as a second slider-shaped strip. Only the glyphs are wanted.
                foreach (var graphic in copy.GetComponentsInChildren<Graphic>(includeInactive: true))
                {
                    if (!(graphic is TMP_Text)) Object.Destroy(graphic);
                }

                _valueText = copy.GetComponent<TMP_Text>()
                             ?? copy.GetComponentInChildren<TMP_Text>(includeInactive: true);
                _valueText.alignment = TextAlignmentOptions.Center;
                _valueText.textWrappingMode = TextWrappingModes.NoWrap;
                if (_valueColor.HasValue) _valueText.color = _valueColor.Value;

                // Filling the slider, so the number reads as centred on the bar.
                var rect = copy.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.anchoredPosition = Vector2.zero;
                rect.localScale = Vector3.one;
                rect.SetAsLastSibling();
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
        private static Color? _valueColor;
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

        /// <summary>
        /// Prints what decides this row's position: the container's layout settings, and every
        /// sibling's measurements beside our own.
        ///
        /// Three attempts to place the row by hand all failed, each on a guess about what was
        /// arranging it. This prints the inputs instead.
        /// </summary>
        private static void Report(GameObject row, GameObject group)
        {
            var parent = row.transform.parent;
            var parentRect = parent as RectTransform;

            Plugin.Log.LogInfo($"layout| container '{parent.name}' rect "
                               + $"{parentRect?.rect.width:0.#}x{parentRect?.rect.height:0.#}");

            foreach (var component in parent.GetComponents<Component>())
            {
                Plugin.Log.LogInfo($"layout|   component {component.GetType().Name}");
            }

            if (parent.GetComponent<VerticalLayoutGroup>() is VerticalLayoutGroup v)
            {
                Plugin.Log.LogInfo($"layout|   vertical spacing={v.spacing} padding={v.padding.top}/{v.padding.bottom} "
                                   + $"controlH={v.childControlHeight} forceH={v.childForceExpandHeight} "
                                   + $"controlW={v.childControlWidth} forceW={v.childForceExpandWidth} "
                                   + $"align={v.childAlignment}");
            }

            if (parent.GetComponent<ContentSizeFitter>() is ContentSizeFitter f)
            {
                Plugin.Log.LogInfo($"layout|   fitter v={f.verticalFit} h={f.horizontalFit}");
            }

            foreach (Transform child in parent)
            {
                var rect = child as RectTransform;
                var element = child.GetComponent<LayoutElement>();
                var mark = child == row.transform ? " <-- ours" : (child == group.transform ? " <-- source" : "");

                Plugin.Log.LogInfo(
                    $"layout|   child '{child.name}' active={child.gameObject.activeSelf} "
                    + $"pos={rect?.anchoredPosition} size={rect?.rect.width:0.#}x{rect?.rect.height:0.#} "
                    + $"anchors={rect?.anchorMin}-{rect?.anchorMax} "
                    + $"element={(element == null ? "none" : $"min{element.minHeight}/pref{element.preferredHeight}/flex{element.flexibleHeight}/ignore{element.ignoreLayout}")}"
                    + mark);
            }
        }

        /// <summary>
        /// Adds a spacer when the setting above draws outside its own box.
        ///
        /// FrameRateControll declares 200 units and stacks a toggle, a label and a pull-down
        /// inside it; the pull-down hangs below that. Nothing followed it before, so the overflow
        /// never showed. The container spaces rows by declared height alone — childControlHeight
        /// is off — so it cannot see the difference, and no amount of positioning this row avoids
        /// being drawn over.
        ///
        /// The gap is measured rather than chosen: the lowest pixel any descendant of the
        /// previous row actually occupies, against where that row claims to end.
        /// </summary>
        private static void PadForOverflow(GameObject row)
        {
            var parent = row.transform.parent as RectTransform;
            if (parent == null) return;

            var index = row.transform.GetSiblingIndex();
            RectTransform previous = null;
            for (var i = index - 1; i >= 0; i--)
            {
                var candidate = parent.GetChild(i) as RectTransform;
                if (candidate != null && candidate.gameObject.activeInHierarchy)
                {
                    previous = candidate;
                    break;
                }
            }

            if (previous == null) return;

            var declaredBottom = Bottom(previous);
            var actualBottom = declaredBottom;
            foreach (var child in previous.GetComponentsInChildren<RectTransform>(includeInactive: false))
            {
                actualBottom = Mathf.Min(actualBottom, Bottom(child));
            }

            var overflow = declaredBottom - actualBottom;
            Plugin.Log.LogInfo($"layout| '{previous.name}' declares bottom {declaredBottom:0.#}, "
                               + $"draws to {actualBottom:0.#} (overflow {overflow:0.#})");

            if (overflow <= 1f) return;

            var spacer = new GameObject("LwfUiScaleSpacer", typeof(RectTransform));
            var rect = spacer.GetComponent<RectTransform>();
            rect.SetParent(parent, worldPositionStays: false);
            rect.SetSiblingIndex(index);
            rect.sizeDelta = new Vector2(0f, overflow);
        }

        /// <summary>The lowest edge of a rect, in the world units the layout is measured in.</summary>
        private static float Bottom(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Mathf.Min(corners[0].y, corners[3].y);
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
