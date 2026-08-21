using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UI.Settings;
using UnityEngine;
using Utility.Localization;

namespace LwfUiScale
{
    /// <summary>
    /// Adds a UI Scale row to the Graphic Settings page.
    ///
    /// The row is a clone of the Resolution row rather than anything built here. Graphic
    /// Settings finds its own controls by GameObject name — <c>FindPullDownCell("Resolution")</c>
    /// looks up a child called "Resolution" and takes the <c>PullDownCell</c> inside it — so a
    /// copy of that object is a complete, correctly-styled row with a working pull-down, and
    /// <c>PullDownCell.Initialize</c> is a public method taking options, a current value and a
    /// callback.
    ///
    /// Two things have to be undone on the copy. Its name would collide with the original, which
    /// is how the page identifies its own controls. And its label is driven by Unity Localization
    /// through <c>TableLinkLocalizedText</c>, which rewrites the text on enable — so that
    /// component is removed before the label is set, or the game would overwrite it.
    /// </summary>
    [HarmonyPatch(typeof(GraphicSettings), "OnPageOpen")]
    internal static class SettingsRowPatch
    {
        private const string SourceRow = "Resolution";
        private const string OurRow = "LwfUiScale";
        private const string RowLabel = "UI Scale";

        private static void Postfix(GraphicSettings __instance)
        {
            var existing = FindChild(__instance, OurRow);
            if (existing != null)
            {
                // The page was reopened. The row survives with it; just resync the shown value.
                Sync(existing);
                return;
            }

            var source = FindChild(__instance, SourceRow);
            if (source == null)
            {
                Plugin.Log.LogError($"UI scale: no '{SourceRow}' row to copy; no row added.");
                return;
            }

            var row = Object.Instantiate(source, source.transform.parent);
            row.name = OurRow;
            row.transform.SetAsLastSibling();

            StripLocalisation(row);
            SetLabel(row);

            var cell = row.GetComponentInChildren<PullDownCell>(includeInactive: true);
            if (cell == null)
            {
                Plugin.Log.LogError("UI scale: the copied row has no PullDownCell; no row added.");
                Object.Destroy(row);
                return;
            }

            cell.Initialize(Options(), Plugin.Label(Plugin.Percent), OnChanged);
            cell.SetInteractable(true);

            Plugin.Log.LogInfo($"UI scale: row added, showing {Plugin.Label(Plugin.Percent)}.");
        }

        private static IReadOnlyList<PullDownOptionData> Options()
        {
            return Plugin.Steps
                .Select(step => new PullDownOptionData(Plugin.Label(step), Plugin.Label(step)))
                .ToArray();
        }

        private static void OnChanged(string value)
        {
            var wanted = Plugin.Steps.FirstOrDefault(step => Plugin.Label(step) == value);
            if (wanted == 0) return;

            Plugin.Set(wanted);
        }

        private static void Sync(GameObject row)
        {
            var cell = row.GetComponentInChildren<PullDownCell>(includeInactive: true);
            cell?.SetValue(Plugin.Label(Plugin.Percent));
        }

        /// <summary>
        /// Removes the components that would rewrite the label from the localisation table. The
        /// copied row carries the Resolution row's key, so left alone it would say "Resolution".
        /// </summary>
        private static void StripLocalisation(GameObject row)
        {
            foreach (var link in row.GetComponentsInChildren<TableLinkLocalizedText>(includeInactive: true))
            {
                Object.Destroy(link);
            }

            // The localisation package's own component, found by name so this plugin does not
            // have to reference Unity.Localization to delete it.
            foreach (var behaviour in row.GetComponentsInChildren<MonoBehaviour>(includeInactive: true))
            {
                if (behaviour == null) continue;
                if (behaviour.GetType().Name == "LocalizeStringEvent") Object.Destroy(behaviour);
            }
        }

        /// <summary>
        /// Writes the row's label. The label is the text outside the pull-down: the cell's own
        /// text shows the selected value, so it is excluded by walking up from it.
        /// </summary>
        private static void SetLabel(GameObject row)
        {
            var cell = row.GetComponentInChildren<PullDownCell>(includeInactive: true);
            var cellRoot = cell != null ? cell.transform : null;

            foreach (var text in row.GetComponentsInChildren<TMP_Text>(includeInactive: true))
            {
                if (cellRoot != null && text.transform.IsChildOf(cellRoot)) continue;
                text.SetText(RowLabel);
                return;
            }

            Plugin.Log.LogWarning("UI scale: no label text found on the copied row.");
        }

        private static GameObject FindChild(Component root, string objectName)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (child.name == objectName) return child.gameObject;
            }

            return null;
        }
    }
}
