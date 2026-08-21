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

        /// <summary>Height of the line the percentage sits on, under the slider.</summary>
        private const float ValueLineHeight = 50f;

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

            FitRowHeight(row);
            PadForOverflow(row);
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
                // Its own line under the slider, not a label on top of it.
                //
                // The bar has three regions — tan fill, dark handle, light track — so no text
                // colour reads across all of them, and the handle passes over the middle where a
                // centred number sits. There is no colour to pick; the number has to leave the bar.
                var copy = Object.Instantiate(header.gameObject, sliderObject.transform.parent);
                copy.name = "LwfUiScaleValue";
                copy.transform.SetSiblingIndex(sliderObject.transform.GetSiblingIndex() + 1);

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

                // The slider's own footprint, one line down, so it lines up under the bar.
                var sliderRect = sliderObject.GetComponent<RectTransform>();
                var rect = copy.GetComponent<RectTransform>();
                rect.anchorMin = sliderRect.anchorMin;
                rect.anchorMax = sliderRect.anchorMax;
                rect.pivot = sliderRect.pivot;
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(sliderRect.sizeDelta.x, ValueLineHeight);
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
        /// Grows the row to hold its own contents.
        ///
        /// ScreenMode declares 150 for a 77 header and a 60 bar. This row adds a third line for
        /// the percentage, so 150 no longer covers it — and the container measures rows by
        /// declared height alone, so the excess would spill onto whatever follows. Exactly the
        /// mistake FrameRateControll makes, which is worth not repeating.
        ///
        /// The height is summed from the children and the row's own layout settings rather than
        /// picked, so it stays right if any of those change.
        /// </summary>
        private static void FitRowHeight(GameObject row)
        {
            var rect = row.GetComponent<RectTransform>();
            if (rect == null) return;

            var total = 0f;
            var count = 0;
            foreach (Transform child in row.transform)
            {
                if (!child.gameObject.activeSelf) continue;
                var childRect = child as RectTransform;
                if (childRect == null) continue;
                total += childRect.sizeDelta.y;
                count++;
            }

            var group = row.GetComponent<VerticalLayoutGroup>();
            if (group != null)
            {
                total += group.spacing * Mathf.Max(0, count - 1);
                total += group.padding.top + group.padding.bottom;
            }

            var before = rect.sizeDelta.y;
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, total);

            if (Mathf.Approximately(before, total)) return;
            Plugin.Log.LogInfo($"UI scale: row sized {total:0.#} for {count} children.");
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

            // Measured in the container's own space. GetWorldCorners answers in screen pixels,
            // and a sizeDelta is in canvas units — on a 4K display those differ by the canvas
            // scale, so a spacer sized from the raw figure was about twice what was needed, and
            // changed size with the UI scale setting.
            var declaredBottom = Bottom(previous, parent);
            var actualBottom = declaredBottom;
            foreach (var child in previous.GetComponentsInChildren<RectTransform>(includeInactive: false))
            {
                actualBottom = Mathf.Min(actualBottom, Bottom(child, parent));
            }

            var overflow = declaredBottom - actualBottom;
            // Set rather than derived. The measurement above is right about why the gap is
            // needed, but the overflow is centred in its box and only part of it lands where it
            // matters, so the figure that reads correctly is not the figure that is measured.
            const float needed = 70f;

            var spacer = new GameObject("LwfUiScaleSpacer", typeof(RectTransform));
            var rect = spacer.GetComponent<RectTransform>();
            rect.SetParent(parent, worldPositionStays: false);
            rect.SetSiblingIndex(index);
            rect.sizeDelta = new Vector2(0f, needed);
        }

        /// <summary>The lowest edge of a rect, in <paramref name="space"/>'s local units — the
        /// same units a sizeDelta is written in.</summary>
        private static float Bottom(RectTransform rect, Transform space)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);

            var lowest = float.MaxValue;
            foreach (var corner in corners)
            {
                lowest = Mathf.Min(lowest, space.InverseTransformPoint(corner).y);
            }

            return lowest;
        }

        private static GameObject FindByName(Transform root, string name)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (child.name == name) return child.gameObject;
            }

            return null;
        }


    }
}
