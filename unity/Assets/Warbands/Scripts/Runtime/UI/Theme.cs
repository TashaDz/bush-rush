using UnityEngine;
using TMPro;

namespace Warbands.UI
{
    /// Токены ui-spec/05-tokens.md. Единственный источник цветов и размеров для uGUI-экранов.
    public static class Theme
    {
        static Color C(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }
        public static Color A(Color c, float a) { c.a = a; return c; }

        // поверхности
        public static readonly Color Ink = C("#0b1030"), InkDeep = C("#05070f"), Panel = C("#16205a"), PanelDeep = C("#101a44"),
            PanelRaise = C("#1d2b74"), Stroke = C("#2a376f"), StrokeLight = C("#3a4a9e");
        // акценты
        public static readonly Color Gold = C("#f4c23c"), GoldLight = C("#ffe9a8"), Bronze = C("#b9832f"), Bevel = C("#a9761b"), Cyan = C("#8ce8ff");
        // механики
        public static readonly Color TeamPlayer = C("#35d6a4"), TeamEnemy = C("#e2453f"), TargetEnemy = C("#ff6b63"), TargetAlly = C("#6ff06a"),
            HpPlayer = C("#56e88f"), HpPlayerDark = C("#24b465"), HpEnemy = C("#ff6b63"), HpEnemyDark = C("#d02b32"),
            Soul = C("#6ff06a"), SoulBright = C("#b6ff7a"), Magic = C("#c8a8ff"), MagicBtn = C("#7c5cff"), Physical = C("#ffd9a0"),
            Undead = C("#7de3d2"), Arcane = C("#c8a0ff"), Living = C("#ffd9b0"), Warn = C("#ffb35c");
        public const float Disabled = 0.45f;
        // UI v2 (автор 07.09, soulbound-ui-v2.md): плоский стиль, один ink с разной альфой, без обводок
        public static readonly Color Ink2 = C("#14162C"), Gold2 = C("#FFD83D"), Pink = C("#FF8AD0"), Green = C("#45DD6A"), Mint = C("#7DFF8F"), Teal = C("#7DFFD8"), Red2 = C("#FF5D5D"), Orange = C("#FF9D2E");
        public static Color Panel2(float a) => A(Ink2, a);
        public static Color White(float a) => new Color(1f, 1f, 1f, a);
        public static Color Accent2(int side) => side == 0 ? Gold2 : Pink;
        public static Color Hp2(int side) => side == 0 ? Green : Red2;
        // текст
        public static readonly Color Text = C("#f2f6ff"), TextDim = C("#cdd7ff"), TextMuted = C("#8b98d4"), TextCaption = C("#7f8dc8"), TextOnGold = C("#3a2606");
        // арена
        public static readonly Color ArenaTop = C("#16207a"), ArenaMid = C("#2733a8"), ArenaBottom = C("#3644c4");
        // кнопки действий
        public static readonly Color AttackBtn = C("#5b46c4"), AttackBtnLight = C("#8f7fe8"), SkillBtn = C("#17402f"), SkillBtnLight = C("#2e6f52"), SkillStroke = C("#2f7a4a");

        // канвас 01-canvas-layout.md
        // Портрет (автор 10.09, ветка route-drawing): канвас 1080×1920, игрок снизу, враг сверху.
        public const float W = 1080f, H = 1920f, VpTop = 140f, VpHeight = 860f;
        public static readonly float[] ZoneX = { 0.18f, 0.38f, 0.62f, 0.82f };   // (legacy, ландшафт) player rear, player front, enemy front, enemy rear
        public static readonly float[] RowN = { 0.34f, 0.58f, 0.82f };
        public static float RowY(int row) => VpTop + RowN[row] * VpHeight;      // (legacy)
        /// Портрет: зоны — доли высоты канваса до низа слота (0 player rear, 1 player front, 2 enemy front, 3 enemy rear); колонки — доли ширины по слотам.
        public static readonly float[] ZoneY = { 0.745f, 0.625f, 0.36f, 0.24f };
        public static readonly float[] ColX = { 0.21f, 0.48f, 0.75f };
        /// Доля высоты канваса от верха до низа слота: на телефонах канвас ниже 1080 юнитов, поле раскладывается по долям (автор 07.09)
        public static float RowFrac(int row) => RowY(row) / H;   // (legacy)
        public const float FieldMidFrac = 0.49f, GroundFrac = 0.17f;   // середина поля между армиями; линия земли фона в портрете
        // река по центру поля и мост (автор 07.09): ближники бегут через мост
        public const float RiverW = 200f, BridgeY = VpTop + VpHeight * 0.52f, BridgeW = 300f, BridgeH = 120f;
        public static readonly Color Water = new Color(0.22f, 0.55f, 0.85f), WaterDark = new Color(0.16f, 0.42f, 0.72f), Bridge = new Color(0.62f, 0.45f, 0.28f), BridgeDark = new Color(0.45f, 0.31f, 0.18f), Stone = new Color(0.55f, 0.55f, 0.6f);

        static TMP_FontAsset font, fontHeavy;
        public static void ResetFonts() { font = null; fontHeavy = null; }
        public static TMP_FontAsset Font
        {
            get
            {
                if (font == null)
                {
                    if (UiRoot.Pack != null && UiRoot.Pack.fontBold != null) font = UiRoot.Pack.fontBold;
                    if (font == null) font = TMP_Settings.defaultFontAsset;
                    if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
                }
                return font;
            }
        }
        /// Заголовки и кнопки: Baloo 2 ExtraBold, если пак подключён.
        public static TMP_FontAsset FontHeavy
        {
            get
            {
                if (fontHeavy == null) fontHeavy = UiRoot.Pack != null && UiRoot.Pack.fontHeavy != null ? UiRoot.Pack.fontHeavy : Font;
                return fontHeavy;
            }
        }
        public static bool HasPack => UiRoot.Pack != null && UiRoot.Pack.btnPrimary != null;

        static readonly System.Collections.Generic.Dictionary<(Material, int), Material> outlined = new System.Collections.Generic.Dictionary<(Material, int), Material>();
        /// Общий материал с тёмной обводкой для надписей поверх яркого фона (07.09).
        public static void Outline(TMPro.TMP_Text t, float width = 0.3f)
        {
            var src = t.fontSharedMaterial; if (src == null) return;
            var key = (src, Mathf.RoundToInt(width * 100f));
            if (!outlined.TryGetValue(key, out var m)) { m = new Material(src) { name = src.name + " Outline" }; m.SetFloat(TMPro.ShaderUtilities.ID_OutlineWidth, width); m.SetColor(TMPro.ShaderUtilities.ID_OutlineColor, InkDeep); outlined[key] = m; }
            t.fontSharedMaterial = m;
        }
        static readonly System.Collections.Generic.Dictionary<(Material, int), Material> haloed = new System.Collections.Generic.Dictionary<(Material, int), Material>();
        /// Обводка + мягкая тёмная тень-ореол (underlay): для надписей прямо на поле, где обводки мало (07.09).
        public static void Halo(TMPro.TMP_Text t, float width = 0.3f) => Halo(t, width, InkDeep);
        /// Цветная обводка + тёмный ореол: белый текст с зелёной/красной обводкой читается на любом фоне и сохраняет цвет стороны.
        public static void Halo(TMPro.TMP_Text t, float width, Color outline)
        {
            var src = t.fontSharedMaterial; if (src == null) return;
            var key = (src, Mathf.RoundToInt(width * 100f) * 1000 + (Mathf.RoundToInt(outline.r * 9f) * 100 + Mathf.RoundToInt(outline.g * 9f) * 10 + Mathf.RoundToInt(outline.b * 9f)));
            if (!haloed.TryGetValue(key, out var m))
            {
                m = new Material(src) { name = src.name + " Halo" };
                m.SetFloat(TMPro.ShaderUtilities.ID_OutlineWidth, width); m.SetColor(TMPro.ShaderUtilities.ID_OutlineColor, outline);
                m.EnableKeyword("UNDERLAY_ON");
                m.SetColor(TMPro.ShaderUtilities.ID_UnderlayColor, A(InkDeep, 0.85f));
                m.SetFloat(TMPro.ShaderUtilities.ID_UnderlayOffsetX, 0f); m.SetFloat(TMPro.ShaderUtilities.ID_UnderlayOffsetY, -0.35f);
                m.SetFloat(TMPro.ShaderUtilities.ID_UnderlayDilate, 0.55f); m.SetFloat(TMPro.ShaderUtilities.ID_UnderlaySoftness, 0.5f);
                haloed[key] = m;
            }
            t.fontSharedMaterial = m;
        }

        public static Color SideColor(int side) => side == 0 ? TeamPlayer : TeamEnemy;
        public static Color HpColor(int side) => side == 0 ? HpPlayer : HpEnemy;
    }
}
