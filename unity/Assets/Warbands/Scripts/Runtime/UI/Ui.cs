using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Warbands.UI
{
    /// Фабрика атомов uGUI по ui-spec/03: панели, кнопки, бары, тексты, раскладки. Координаты — единицы канваса 1920×1080.
    public static class Ui
    {
        public enum Anchor { TopLeft, TopCenter, TopRight, MiddleLeft, Center, MiddleRight, BottomLeft, BottomCenter, BottomRight }

        public static RectTransform Node(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(100, 100);
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// x, y — отступ от якоря внутрь экрана (для верхних якорей y вниз, для нижних — вверх).
        public static RectTransform Place(RectTransform rt, Anchor a, float x, float y, float w, float h)
        {
            Vector2 an, pos;
            switch (a)
            {
                case Anchor.TopLeft: an = new Vector2(0, 1); pos = new Vector2(x, -y); break;
                case Anchor.TopCenter: an = new Vector2(0.5f, 1); pos = new Vector2(x, -y); break;
                case Anchor.TopRight: an = new Vector2(1, 1); pos = new Vector2(-x, -y); break;
                case Anchor.MiddleLeft: an = new Vector2(0, 0.5f); pos = new Vector2(x, -y); break;
                case Anchor.Center: an = new Vector2(0.5f, 0.5f); pos = new Vector2(x, -y); break;
                case Anchor.MiddleRight: an = new Vector2(1, 0.5f); pos = new Vector2(-x, -y); break;
                case Anchor.BottomLeft: an = new Vector2(0, 0); pos = new Vector2(x, y); break;
                case Anchor.BottomCenter: an = new Vector2(0.5f, 0); pos = new Vector2(x, y); break;
                default: an = new Vector2(1, 0); pos = new Vector2(-x, y); break;
            }
            rt.anchorMin = rt.anchorMax = an; rt.pivot = an; rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        /// Якорь в долях родителя с произвольным pivot (слоты поля).
        public static RectTransform PlaceRel(RectTransform rt, float ax, float ay, Vector2 pivot, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(ax, ay); rt.pivot = pivot; rt.anchoredPosition = new Vector2(x, y); rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static Image Image(Transform parent, string name, Sprite sprite, Color color, bool raycast = false)
        {
            var rt = Node(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite; img.color = color; img.raycastTarget = raycast;
            img.type = sprite != null && sprite.border != Vector4.zero ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            img.pixelsPerUnitMultiplier = 1f;
            return img;
        }

        public static Image Fill(Transform parent, string name, Color color) => Image(parent, name, UiSprites.White(), color);

        /// Спрайт с сохранением пропорций (портреты/иконки из арта автора).
        public static Image Icon(Transform parent, string name, Sprite sprite, bool flip = false)
        {
            var img = Image(parent, name, sprite, Color.white);
            img.type = UnityEngine.UI.Image.Type.Simple; img.preserveAspect = true;
            if (flip) img.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            return img;
        }

        /// Панель: заливка + обводка отдельным дочерним Image (цвет рамки меняется тинтом).
        public static Image Panel(Transform parent, string name, int radius, Color fill, Color stroke, int strokeW, out Image strokeImg)
        {
            var pack = UiRoot.Pack;
            bool usePack = Theme.HasPack && fill.a >= 0.95f && pack.panelDark != null;
            Image img;
            if (usePack)
            {
                // бирюзовая панель пака; яркость заливки токена → тинт (PanelRaise светлее PanelDeep)
                float k = Mathf.Clamp(0.62f + fill.grayscale * 1.6f, 0.62f, 1f);
                img = Image(parent, name, pack.panelDark, new Color(k, k, k, 1f));
                radius = 12;   // углы спрайта пака
            }
            else img = Image(parent, name, UiSprites.RoundedFill(radius), fill);
            strokeImg = Image(img.transform, "Stroke", UiSprites.RoundedStroke(radius, strokeW), stroke);
            Stretch(strokeImg.rectTransform);
            if (usePack && strokeW <= 0) strokeImg.enabled = false;
            return img;
        }

        /// Панель из конкретного спрайта пака (HUD, модалка, рамка), без процедурной обводки.
        public static Image PackPanel(Transform parent, string name, Sprite sprite, Color? tint = null)
        {
            var img = Image(parent, name, sprite, tint ?? Color.white);
            img.type = UnityEngine.UI.Image.Type.Sliced;
            return img;
        }
        public static Image Panel(Transform parent, string name, int radius, Color fill, Color stroke, int strokeW) => Panel(parent, name, radius, fill, stroke, strokeW, out _);

        /// UI v2: плоская скруглённая подложка без обводки и пака (белая форма + Image.color).
        public static Image Flat(Transform parent, string name, int radius, Color color) { var i = Image(parent, name, UiSprites.RoundedFill(radius), color); i.type = UnityEngine.UI.Image.Type.Sliced; return i; }
        public static Image FlatCircle(Transform parent, string name, Color color) { var i = Image(parent, name, UiSprites.Circle(128), color); i.type = UnityEngine.UI.Image.Type.Simple; return i; }
        /// UI v2: плоская кнопка-капсула/скругление; нажатие — контент вниз на 4 px, размер не меняется.
        public static Btn FlatButton(Transform parent, string name, int radius, Color fill, string label, float size, Color textColor, Action onClick, float tracking = 0.12f)
        {
            var b = new Btn { FillColor = fill, StrokeColor = new Color(0, 0, 0, 0), SelectedFill = fill };
            b.Fill = Flat(parent, name, radius, fill); b.Rt = b.Fill.rectTransform;
            b.Stroke = Image(b.Rt, "Stroke", UiSprites.RoundedFill(radius), new Color(0, 0, 0, 0)); b.Stroke.enabled = false;
            b.Label = Text(b.Rt, "Label", label, size, textColor, TextAlignmentOptions.Center, FontStyles.Bold, tracking * size, true);
            Stretch(b.Label.rectTransform, 8, 4, 8, 4);
            b.Button = Clickable(b.Rt, () => { if (b.Enabled) onClick?.Invoke(); });
            var cs = b.Button.colors; b.Button.transition = Selectable.Transition.ColorTint; cs.pressedColor = new Color(0.86f, 0.86f, 0.86f); cs.highlightedColor = Color.white; b.Button.colors = cs;
            return b;
        }

        public static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Center, FontStyles style = FontStyles.Bold, float spacing = 0f, bool upper = false)
        {
            var rt = Node(parent, name);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = size >= 26 || (style & FontStyles.Bold) != 0 && size >= 20 ? Theme.FontHeavy : Theme.Font; t.text = text; t.fontSize = size; t.color = color; t.alignment = align;
            t.fontStyle = style | (upper ? FontStyles.UpperCase : 0); t.characterSpacing = spacing; t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal; t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        public static void Ellipsis(TextMeshProUGUI t) { t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis; }

        public static Button Clickable(RectTransform rt, Action onClick)
        {
            var g = rt.GetComponent<Graphic>();
            if (g == null) { var img = rt.gameObject.AddComponent<Image>(); img.color = new Color(0, 0, 0, 0); g = img; }
            g.raycastTarget = true;
            var b = rt.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None; b.targetGraphic = g;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            return b;
        }

        public static CanvasGroup Group(RectTransform rt) { var g = rt.GetComponent<CanvasGroup>(); return g != null ? g : rt.gameObject.AddComponent<CanvasGroup>(); }
        public static void Alpha(RectTransform rt, float a) => Group(rt).alpha = a;

        // ---------- кнопки ----------

        public sealed class Btn
        {
            public RectTransform Rt; public Image Fill, Stroke; public TextMeshProUGUI Label, Sub; public Button Button;
            public Color FillColor, StrokeColor, SelectedStroke = Theme.Gold, SelectedFill;
            public Sprite PackSprite, PackSelected;
            bool enabled = true;
            public bool Enabled => enabled;
            public void SetEnabled(bool on) { enabled = on; Alpha(Rt, on ? 1f : Theme.Disabled); }
            public void SetSelected(bool on)
            {
                Stroke.color = on ? SelectedStroke : StrokeColor; Fill.color = on ? SelectedFill : FillColor;
                if (PackSprite != null) Fill.sprite = on && PackSelected != null ? PackSelected : PackSprite;
            }
            public Image Icon;
            public void SetText(string s) { Label.text = s; }
        }

        public static Btn MakeButton(Transform parent, string name, int radius, Color fill, Color stroke, int strokeW, string label, float size, Color textColor, Action onClick, float spacing = 0.12f, Sprite packSprite = null, Sprite packSelected = null)
        {
            var b = new Btn { FillColor = fill, StrokeColor = stroke, SelectedFill = fill };
            if (packSprite != null)
            {
                b.Fill = PackPanel(parent, name, packSprite); b.Rt = b.Fill.rectTransform;
                b.Stroke = Image(b.Rt, "Stroke", UiSprites.RoundedStroke(22, 4), Theme.A(Theme.Gold, 0f)); Stretch(b.Stroke.rectTransform, -2, -2, -2, -2);
                b.FillColor = Color.white; b.SelectedFill = Color.white; b.StrokeColor = Theme.A(Theme.Gold, 0f); b.PackSprite = packSprite; b.PackSelected = packSelected;
                b.Label = Text(b.Rt, "Label", label, size, textColor, TextAlignmentOptions.Center, FontStyles.Bold, spacing * size, true);
                Stretch(b.Label.rectTransform, 8, 4, 8, 6);
                b.Button = Clickable(b.Rt, () => { if (b.Enabled) onClick?.Invoke(); });
                var cs = b.Button.colors; b.Button.transition = Selectable.Transition.ColorTint; cs.pressedColor = new Color(0.8f, 0.8f, 0.8f); cs.highlightedColor = new Color(1.02f, 1.02f, 1.02f); b.Button.colors = cs;
                return b;
            }
            b.Fill = Panel(parent, name, radius, fill, stroke, strokeW, out b.Stroke);
            b.Rt = b.Fill.rectTransform;
            b.Label = Text(b.Rt, "Label", label, size, textColor, TextAlignmentOptions.Center, FontStyles.Bold, spacing * size, true);
            Stretch(b.Label.rectTransform, 8, 4, 8, 4);
            b.Button = Clickable(b.Rt, () => { if (b.Enabled) onClick?.Invoke(); });
            var colors = b.Button.colors; b.Button.transition = Selectable.Transition.ColorTint; colors.pressedColor = new Color(0.85f, 0.85f, 0.85f); colors.highlightedColor = new Color(1.02f, 1.02f, 1.02f); b.Button.colors = colors;
            return b;
        }

        public static Btn Primary(Transform parent, string name, string label, float size, Action onClick)
        {
            if (Theme.HasPack) return MakeButton(parent, name, 18, Theme.GoldLight, Theme.Gold, 4, label, size, Theme.TextOnGold, onClick, 0.08f, UiRoot.Pack.btnPrimary);
            var b = MakeButton(parent, name, 18, Theme.GoldLight, Theme.Gold, 4, label, size, Theme.TextOnGold, onClick);
            var bevel = Image(b.Rt, "Bevel", UiSprites.RoundedFill(18), Theme.Bevel); bevel.transform.SetAsFirstSibling();
            Stretch(bevel.rectTransform, 0, 8, 0, -8);   // бевель 8 вниз
            // (14.09) раньше здесь кнопка переставлялась в начало детей родителя — уходила под скрим/ловец пальца и не ловила клики; бевель уже первый внутри кнопки
            return b;
        }

        public static Btn Normal(Transform parent, string name, string label, Action onClick, float size = 22)
        {
            if (Theme.HasPack) { var pb = MakeButton(parent, name, 16, Theme.Panel, Theme.Bronze, 3, label, size, Color.white, onClick, 0.08f, UiRoot.Pack.btnNormal, UiRoot.Pack.btnSelected); pb.SelectedStroke = Theme.A(Theme.Gold, 1f); return pb; }
            var b = MakeButton(parent, name, 16, Theme.Panel, Theme.Bronze, 3, label, size, Theme.GoldLight, onClick);
            b.SelectedFill = Theme.PanelRaise;
            return b;
        }

        public static Btn Square(Transform parent, string name, string glyph, Action onClick, Sprite icon = null)
        {
            var b = Theme.HasPack ? MakeButton(parent, name, 14, Theme.Panel, Theme.Bronze, 3, icon != null ? "" : glyph, 26, Color.white, onClick, 0f, UiRoot.Pack.btnNormal)
                                  : MakeButton(parent, name, 14, Theme.Panel, Theme.Bronze, 3, icon != null ? "" : glyph, 26, Theme.GoldLight, onClick, 0f);
            if (icon != null) { b.Icon = Icon(b.Rt, "Icon", icon); Stretch(b.Icon.rectTransform, 14, 12, 14, 14); }
            return b;
        }

        /// Кнопка действия героя 118 высотой: иконочный слот + label + sub.
        public static Btn Action(Transform parent, string name, string label, string sub, Color bg, Color bgLight, Color stroke, Action onClick, Sprite packSprite = null)
        {
            var b = MakeButton(parent, name, 20, bg, stroke, 4, "", 30, Theme.Text, onClick, 0.06f, packSprite);
            var slot = Theme.HasPack ? PackPanel(b.Rt, "IconSlot", UiRoot.Pack.panelDark, new Color(1f, 1f, 1f, 0.85f)) : Panel(b.Rt, "IconSlot", 15, bgLight, Theme.A(Color.black, 0.25f), 2);
            Place(slot.rectTransform, Anchor.MiddleLeft, 18, 0, 62, 62);
            b.Sub = Text(slot.transform, "Icon", label.Length > 0 ? label.Substring(0, 1) : "?", 30, Theme.Text);
            Stretch(b.Sub.rectTransform);
            Place(b.Label.rectTransform, Anchor.TopLeft, 96, 22, 300, 36); b.Label.alignment = TextAlignmentOptions.Left; b.Label.text = label;
            var s = Text(b.Rt, "Sub", sub, 15, Theme.A(Theme.Text, 0.85f), TextAlignmentOptions.Left, FontStyles.Bold, 1.2f, true);
            Place(s.rectTransform, Anchor.TopLeft, 96, 62, 300, 24);
            b.Sub = s;
            return b;
        }

        public static Btn Filter(Transform parent, string name, string label, Action onClick)
        {
            if (Theme.HasPack) return MakeButton(parent, name, 10, Theme.PanelDeep, Theme.Stroke, 2, label, 14, Color.white, onClick, 0.08f, UiRoot.Pack.btnNormal, UiRoot.Pack.btnSelected);
            var b = MakeButton(parent, name, 10, Theme.PanelDeep, Theme.Stroke, 2, label, 14, Theme.TextDim, onClick, 0.08f);
            b.SelectedFill = Theme.PanelRaise;
            return b;
        }

        // ---------- бары ----------

        public sealed class Bar
        {
            public RectTransform Rt; public Image Track, Fill; public TextMeshProUGUI Value; public bool Sliced; public float Pad;
            public void Set(float ratio, string text)
            {
                ratio = Mathf.Clamp01(ratio);
                if (Sliced) { var rt = Fill.rectTransform; float w = Rt.rect.width - Pad * 2f; rt.anchorMax = new Vector2(ratio, 1f); rt.offsetMax = new Vector2(ratio > 0f ? -Pad : 0f, -Pad); rt.gameObject.SetActive(ratio > 0.001f); }
                else Fill.fillAmount = ratio;
                if (Value != null) Value.text = text;
            }
        }

        public static Bar HpBar(Transform parent, string name, int radius, int stroke, Color strokeColor, Color fill, float fontSize)
        {
            var bar = new Bar();
            if (Theme.HasPack && UiRoot.Pack.pill != null)
            {
                bar.Track = PackPanel(parent, name, UiRoot.Pack.pill, new Color(0.35f, 0.35f, 0.4f, 1f)); bar.Rt = bar.Track.rectTransform;
                var st = Image(bar.Rt, "Stroke", UiSprites.RoundedStroke(11, stroke), strokeColor); Stretch(st.rectTransform);
                bar.Fill = PackPanel(bar.Rt, "Fill", UiRoot.Pack.pill, fill); bar.Sliced = true; bar.Pad = stroke;
                Stretch(bar.Fill.rectTransform, stroke, stroke, stroke, stroke);
                bar.Value = Text(bar.Rt, "Value", "", fontSize, Color.white); Stretch(bar.Value.rectTransform); Theme.Halo(bar.Value, 0.24f);
                return bar;
            }
            bar.Track = Panel(parent, name, radius, Theme.A(Theme.Ink, 0.85f), strokeColor, stroke, out _);
            bar.Rt = bar.Track.rectTransform;
            bar.Fill = Image(bar.Rt, "Fill", UiSprites.RoundedFill(Mathf.Max(2, radius - 2)), fill);
            bar.Fill.type = UnityEngine.UI.Image.Type.Filled; bar.Fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal; bar.Fill.fillOrigin = 0;
            Stretch(bar.Fill.rectTransform, stroke, stroke, stroke, stroke);
            bar.Value = Text(bar.Rt, "Value", "", fontSize, Color.white);
            Stretch(bar.Value.rectTransform);
            return bar;
        }

        public static Image Ring(Transform parent, string name, int diameter, int stroke, Color color, bool filled)
        {
            var img = Image(parent, name, UiSprites.Ring(diameter, stroke), color);
            img.rectTransform.sizeDelta = new Vector2(diameter, diameter);
            if (filled) { img.type = UnityEngine.UI.Image.Type.Filled; img.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360; img.fillOrigin = (int)UnityEngine.UI.Image.Origin360.Top; img.fillClockwise = true; }
            return img;
        }

        // ---------- раскладки ----------

        public static HorizontalLayoutGroup HLayout(RectTransform rt, float spacing, int padX = 0, int padY = 0, TextAnchor align = TextAnchor.MiddleLeft, bool expandW = false, bool expandH = true)
        {
            var l = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            l.spacing = spacing; l.padding = new RectOffset(padX, padX, padY, padY); l.childAlignment = align;
            l.childControlWidth = true; l.childControlHeight = true; l.childForceExpandWidth = expandW; l.childForceExpandHeight = expandH;
            return l;
        }
        public static VerticalLayoutGroup VLayout(RectTransform rt, float spacing, int padX = 0, int padY = 0, TextAnchor align = TextAnchor.UpperLeft, bool expandW = true, bool expandH = false)
        {
            var l = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            l.spacing = spacing; l.padding = new RectOffset(padX, padX, padY, padY); l.childAlignment = align;
            l.childControlWidth = true; l.childControlHeight = true; l.childForceExpandWidth = expandW; l.childForceExpandHeight = expandH;
            return l;
        }
        public static GridLayoutGroup Grid(RectTransform rt, int columns, Vector2 cell, Vector2 spacing)
        {
            var g = rt.gameObject.AddComponent<GridLayoutGroup>();
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount; g.constraintCount = columns; g.cellSize = cell; g.spacing = spacing;
            return g;
        }
        public static LayoutElement LE(RectTransform rt, float prefW = -1, float prefH = -1, float flexW = -1, float flexH = -1, float minW = -1, float minH = -1)
        {
            var e = rt.GetComponent<LayoutElement>() ?? rt.gameObject.AddComponent<LayoutElement>();
            e.preferredWidth = prefW; e.preferredHeight = prefH; e.flexibleWidth = flexW; e.flexibleHeight = flexH; e.minWidth = minW; e.minHeight = minH;
            return e;
        }
        public static void Fit(RectTransform rt, bool vertical = true, bool horizontal = false)
        {
            var f = rt.gameObject.AddComponent<ContentSizeFitter>();
            f.verticalFit = vertical ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            f.horizontalFit = horizontal ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
        }

        public static string Short(Sim.SquadDef d) => d.Short;
    }
}
