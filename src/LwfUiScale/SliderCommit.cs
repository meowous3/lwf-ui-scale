using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LwfUiScale
{
    /// <summary>
    /// Applies the scale when the drag ends rather than while it moves.
    ///
    /// Rescaling every canvas is a layout pass across the whole UI, and doing it per frame of a
    /// drag makes the slider itself stutter under the cursor — the thing being dragged is part of
    /// what is being rebuilt. The readout follows the handle continuously; only the apply waits.
    ///
    /// Both handlers are needed. <c>IEndDragHandler</c> covers dragging the handle,
    /// <c>IPointerUpHandler</c> covers a click on the track, which moves the value without ever
    /// starting a drag.
    /// </summary>
    internal sealed class SliderCommit : MonoBehaviour, IEndDragHandler, IPointerUpHandler
    {
        private Slider _slider;

        private void Awake()
        {
            _slider = GetComponent<Slider>();
        }

        public void OnEndDrag(PointerEventData eventData) => Commit();

        public void OnPointerUp(PointerEventData eventData) => Commit();

        private void Commit()
        {
            if (_slider == null) return;
            Plugin.Set(Mathf.RoundToInt(_slider.value));
        }
    }
}
