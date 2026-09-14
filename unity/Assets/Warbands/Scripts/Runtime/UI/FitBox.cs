using UnityEngine;

namespace Warbands.UI
{
    /// Коробка 1920×1080 внутри родителя: равномерно ужимается, если родитель ниже/уже (низкий канвас телефона), но не растёт.
    public sealed class FitBox : MonoBehaviour
    {
        void LateUpdate()
        {
            var self = (RectTransform)transform; var parent = self.parent as RectTransform; if (parent == null) return;
            float sc = Mathf.Min(1f, parent.rect.width / Theme.W, parent.rect.height / Theme.H);
            if (Mathf.Abs(self.localScale.x - sc) > 0.001f) self.localScale = new Vector3(sc, sc, 1f);
        }
    }
}
