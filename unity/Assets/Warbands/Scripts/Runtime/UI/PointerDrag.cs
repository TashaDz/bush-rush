using UnityEngine;
using UnityEngine.EventSystems;

namespace Warbands.UI
{
    /// Ловец жеста рисования (ветка route-drawing): нажатие, ведение, отпускание — в экранных координатах.
    public sealed class PointerDrag : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public System.Action<Vector2> Down, Move, Up;
        public void OnPointerDown(PointerEventData e) { Down?.Invoke(e.position); }
        public void OnDrag(PointerEventData e) { Move?.Invoke(e.position); }
        public void OnPointerUp(PointerEventData e) { Up?.Invoke(e.position); }
    }
}
