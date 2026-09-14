using UnityEngine;

namespace Warbands.UI
{
    /// Корень экрана внутри Screen.safeArea (ui-spec/01). Фон арены живёт вне него.
    public sealed class SafeArea : MonoBehaviour
    {
        RectTransform rt; Rect last; Vector4 lastInsets; float nextPoll;
        void Awake() { rt = GetComponent<RectTransform>(); Apply(); }
        void OnRectTransformDimensionsChange() { if (rt != null) Apply(); }
        void Update()
        {
            if (Screen.safeArea != last) { Apply(); return; }
            if (Time.unscaledTime >= nextPoll) { nextPoll = Time.unscaledTime + 0.5f; if (WebHost.SafeInsets != lastInsets) Apply(); }   // чёлка в браузере (env(safe-area-inset-*))
        }
        void Apply()
        {
            var sa = Screen.safeArea; last = sa;
            if (Screen.width <= 0 || Screen.height <= 0) return;
            var ins = WebHost.SafeInsets; lastInsets = ins;
            if (ins != Vector4.zero)
            {
                float l = Mathf.Max(sa.xMin, ins.x), r = Mathf.Min(sa.xMax, Screen.width - ins.y), b = Mathf.Max(sa.yMin, ins.w), t = Mathf.Min(sa.yMax, Screen.height - ins.z);
                if (r > l && t > b) sa = Rect.MinMaxRect(l, b, r, t);
            }
            rt.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height);
            rt.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }
    }
}
