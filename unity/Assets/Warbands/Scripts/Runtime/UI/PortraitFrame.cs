using UnityEngine;

namespace Warbands.UI
{
    /// Портретная колонка (автор 10.09): игра нарисована под 1080×1920, поэтому на широком экране
    /// (десктопный браузер, планшет, телефон боком) содержимое живёт в узкой колонке по центру,
    /// а по бокам — тёмные поля. На портретном экране колонка занимает весь канвас — как раньше.
    /// Полосы по бокам лежат на системном канвасе (Screen Space - Overlay), чтобы перекрывать и частицы FX.
    [DefaultExecutionOrder(-5)]
    public sealed class PortraitFrame : MonoBehaviour
    {
        public const float Aspect = Theme.W / Theme.H;   // 9:16 — шире колонку не растягиваем
        public RectTransform BarLeft, BarRight;

        RectTransform rt, parent;

        void Awake() { Apply(); }
        void OnRectTransformDimensionsChange() { Apply(); }
        void LateUpdate() { Apply(); }

        void Apply()
        {
            if (rt == null) rt = (RectTransform)transform;
            if (parent == null) { parent = rt.parent as RectTransform; if (parent == null) return; }
            float pw = parent.rect.width, ph = parent.rect.height;
            if (pw <= 1f || ph <= 1f) return;

            bool wide = pw > ph * Aspect;
            var size = wide ? new Vector2(Theme.W, Theme.H) : new Vector2(pw, ph);
            float sc = wide ? ph / Theme.H : 1f;   // колонка во всю высоту экрана

            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            if (rt.anchoredPosition != Vector2.zero) rt.anchoredPosition = Vector2.zero;
            if ((rt.sizeDelta - size).sqrMagnitude > 0.25f) rt.sizeDelta = size;
            if (Mathf.Abs(rt.localScale.x - sc) > 0.0001f) rt.localScale = new Vector3(sc, sc, 1f);

            float bar = Mathf.Max(0f, (pw - size.x * sc) * 0.5f);
            Bar(BarLeft, bar); Bar(BarRight, bar);
        }

        static void Bar(RectTransform b, float w)
        {
            if (b == null) return;
            bool on = w > 0.5f;
            if (b.gameObject.activeSelf != on) b.gameObject.SetActive(on);
            if (on && Mathf.Abs(b.sizeDelta.x - w) > 0.5f) b.sizeDelta = new Vector2(w, 0f);
        }
    }
}
