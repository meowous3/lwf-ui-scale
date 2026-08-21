using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LwfUiScale
{
    /// <summary>
    /// Applies a scale multiplier to every canvas in the scene.
    ///
    /// The lever is <c>referenceResolution</c>, not <c>scaleFactor</c>. A CanvasScaler in
    /// <c>ScaleWithScreenSize</c> mode — which is what this game's canvases use — computes the
    /// canvas scale from the reference resolution and the window size on every layout pass and
    /// ignores <c>scaleFactor</c> entirely, so writing that would do nothing. Halving the
    /// reference resolution doubles everything drawn against it, which is the same relationship
    /// the other way round: reference divided by scale.
    ///
    /// Scalers in <c>ConstantPixelSize</c> mode have no reference resolution and do use
    /// <c>scaleFactor</c>, so those get the multiplier applied there instead.
    ///
    /// The unscaled value is remembered per scaler the first time it is seen, so scaling is
    /// always computed from the game's own setting rather than compounding on the last result.
    /// </summary>
    internal static class UiScale
    {
        private readonly struct Original
        {
            internal readonly Vector2 ReferenceResolution;
            internal readonly float ScaleFactor;

            internal Original(CanvasScaler scaler)
            {
                ReferenceResolution = scaler.referenceResolution;
                ScaleFactor = scaler.scaleFactor;
            }
        }

        // Keyed by the component itself. A destroyed Unity object still works as a dictionary
        // key — its hash does not change — while comparing it to null reports true, which is
        // exactly what pruning needs.
        private static readonly Dictionary<CanvasScaler, Original> Originals =
            new Dictionary<CanvasScaler, Original>();

        /// <summary>Applies the current setting to every scaler that exists right now.</summary>
        internal static int ApplyAll(float scale)
        {
            var scalers = Object.FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include,
                                                                 FindObjectsSortMode.None);
            var touched = 0;
            foreach (var scaler in scalers)
            {
                if (Apply(scaler, scale)) touched++;
            }

            return touched;
        }

        internal static bool Apply(CanvasScaler scaler, float scale)
        {
            if (scaler == null || scale <= 0f) return false;

            if (!Originals.TryGetValue(scaler, out var original))
            {
                original = new Original(scaler);
                Originals[scaler] = original;
            }

            // Only when it actually differs. Assigning either of these marks the canvas dirty
            // and costs a full layout rebuild, so writing the same value on a sweep rebuilt every
            // canvas in the game once a second — which reads as a periodic stutter.
            switch (scaler.uiScaleMode)
            {
                case CanvasScaler.ScaleMode.ScaleWithScreenSize:
                    var wanted = original.ReferenceResolution / scale;
                    if ((scaler.referenceResolution - wanted).sqrMagnitude < 0.0001f) return false;
                    scaler.referenceResolution = wanted;
                    return true;

                case CanvasScaler.ScaleMode.ConstantPixelSize:
                    var factor = original.ScaleFactor * scale;
                    if (Mathf.Approximately(scaler.scaleFactor, factor)) return false;
                    scaler.scaleFactor = factor;
                    return true;

                default:
                    // ConstantPhysicalSize measures in real-world units and has no meaningful
                    // hook here; leaving it alone is better than pretending otherwise.
                    return false;
            }
        }

        /// <summary>Drops entries for scalers the scene has destroyed, so the table cannot grow
        /// across scene loads.</summary>
        internal static void Prune()
        {
            List<CanvasScaler> dead = null;
            foreach (var pair in Originals)
            {
                if (pair.Key == null) (dead ??= new List<CanvasScaler>()).Add(pair.Key);
            }

            if (dead == null) return;
            foreach (var scaler in dead) Originals.Remove(scaler);
        }
    }
}
