using System.Collections.Generic;
using UnityEngine;

namespace Warbands.UI
{
    /// Процедурные спрайты по ui-spec/02-slicing.md, пока нет нарисованных атласов: скруглённые панели и обводки (9-slice),
    /// кольца ровной толщины (Filled/Radial 360), эллипс тени, карет, фигурка бойца. Все белые — цвет задаёт Image.color.
    public static class UiSprites
    {
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        public const float PPU = 100f;

        static Sprite Make(string key, int w, int h, System.Func<float, float, float> coverage, Vector4 border, Vector2? pivot = null)
        {
            if (cache.TryGetValue(key, out var s)) return s;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = key };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float a = Mathf.Clamp01(coverage(x + 0.5f, y + 0.5f));
                    px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            s = Sprite.Create(tex, new Rect(0, 0, w, h), pivot ?? new Vector2(0.5f, 0.5f), PPU, 0, SpriteMeshType.FullRect, border);
            s.name = key;
            cache[key] = s;
            return s;
        }

        // расстояние со знаком до скруглённого прямоугольника (центр в 0,0)
        static float RoundedSdf(float px, float py, float hw, float hh, float r)
        {
            float qx = Mathf.Abs(px) - (hw - r), qy = Mathf.Abs(py) - (hh - r);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }
        static float Edge(float d) => Mathf.Clamp01(0.5f - d);   // 1 px антиалиас

        /// Заливка со скруглением r (9-slice, border = r + 2).
        public static Sprite RoundedFill(int r)
        {
            int size = r * 2 + 8; float half = size / 2f;
            return Make($"fill_r{r}", size, size, (x, y) => Edge(RoundedSdf(x - half, y - half, half, half, r)), Vector4.one * (r + 2));
        }

        /// Обводка толщиной s со скруглением r (9-slice).
        public static Sprite RoundedStroke(int r, int s)
        {
            int size = r * 2 + s * 2 + 8; float half = size / 2f;
            return Make($"stroke_r{r}_s{s}", size, size, (x, y) =>
            {
                float d = RoundedSdf(x - half, y - half, half, half, r);
                return Edge(d) * (1f - Edge(d + s));
            }, Vector4.one * (r + s + 2));
        }

        /// Пунктирная обводка (пустой слот).
        public static Sprite DashedStroke(int w, int h, int r, int s, int dash)
        {
            float hw = w / 2f, hh = h / 2f;
            return Make($"dashed_{w}x{h}_r{r}", w, h, (x, y) =>
            {
                float d = RoundedSdf(x - hw, y - hh, hw, hh, r);
                float ring = Edge(d) * (1f - Edge(d + s));
                float t = (Mathf.Abs(x - hw) > Mathf.Abs(y - hh) * (hw / hh)) ? y : x;   // параметр вдоль ближайшей стороны
                return ring * (Mathf.FloorToInt(t / dash) % 2 == 0 ? 1f : 0f);
            }, Vector4.zero);
        }

        /// Кольцо ровной толщины (не 9-slice) под Image.Filled / Radial 360.
        public static Sprite Ring(int diameter, int stroke)
        {
            float half = diameter / 2f;
            return Make($"ring_{diameter}_{stroke}", diameter, diameter, (x, y) =>
            {
                float d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) - (half - 1f);
                return Edge(d) * (1f - Edge(d + stroke));
            }, Vector4.zero);
        }

        public static Sprite Circle(int diameter)
        {
            float half = diameter / 2f;
            return Make($"circle_{diameter}", diameter, diameter, (x, y) => Edge(Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) - (half - 1f)), Vector4.zero);
        }

        /// Мягкий эллипс контактной тени 192×48.
        /// Гекс pointy-top: заливка (stroke 0) или обводка толщиной stroke px; текстура 112×128 (ширина √3·64).
        public static Sprite Hexagon(int stroke = 0) => Make($"hex_{stroke}", 112, 128, (x, y) =>
        {
            float cx = 56f, cy = 64f, R = 62f;
            float px = x - cx, py = y - cy;
            float ax = Mathf.Abs(px), ay = Mathf.Abs(py);
            float d = Mathf.Max(ax, ay * 0.8660254f + ax * 0.5f) - R * 0.8660254f;   // расстояние до правильного шестиугольника через 3 оси (pointy-top: вершины сверху и снизу)
            if (stroke <= 0) return Edge(d);
            return Mathf.Clamp01(0.5f - d) - Mathf.Clamp01(0.5f - (d + stroke));
        }, Vector4.zero);
        public static Sprite Ellipse() => Make("ellipse", 192, 48, (x, y) =>
        {
            float nx = (x - 96f) / 94f, ny = (y - 24f) / 22f; float d = nx * nx + ny * ny;
            return Mathf.Clamp01((1f - d) * 2.5f);
        }, Vector4.zero);

        /// Кольцо цели под слотом 208×56 с обводкой 4.
        public static Sprite TargetRing() => Make("target_ring", 208, 56, (x, y) =>
        {
            float nx = (x - 104f) / 102f, ny = (y - 28f) / 26f; float d = Mathf.Sqrt(nx * nx + ny * ny);
            float outer = Mathf.Clamp01((1f - d) * 26f), inner = Mathf.Clamp01((1f - d) * 26f - 4f);
            return outer - inner;
        }, Vector4.zero);
        /// Толстое кольцо цели/хода 288×80, обводка 9 px (автор 07.09: декали ярче и контрастнее).
        public static Sprite TargetRingThick() => Make("target_ring_thick", 288, 80, (x, y) =>
        {
            float nx = (x - 144f) / 141f, ny = (y - 40f) / 37f; float d = Mathf.Sqrt(nx * nx + ny * ny);
            float outer = Mathf.Clamp01((1f - d) * 36f), inner = Mathf.Clamp01((1f - d) * 36f - 9f);
            return outer - inner;
        }, Vector4.zero);

        /// Карет (треугольник вниз) 40×28.
        public static Sprite Caret() => Make("caret", 40, 28, (x, y) =>
        {
            float t = y / 28f; float hw = 20f * t;   // острие внизу (y = 0)
            return Mathf.Clamp01(hw - Mathf.Abs(x - 20f) + 0.5f);
        }, Vector4.zero);

        /// Силуэт бойца 40×80 (тело + голова), смотрит вправо: оружие-штрих справа.
        public static Sprite Fighter(bool ranged)
        {
            return Make(ranged ? "fighter_ranged" : "fighter_melee", 40, 80, (x, y) =>
            {
                float body = Edge(RoundedSdf(x - 18f, y - 26f, 12f, 26f, 8f));
                float head = Edge(Mathf.Sqrt((x - 18f) * (x - 18f) + (y - 64f) * (y - 64f)) - 11f);
                float weapon = ranged ? Edge(RoundedSdf(x - 34f, y - 40f, 3f, 16f, 2f)) : Edge(RoundedSdf(x - 35f, y - 46f, 2.5f, 22f, 1.5f));
                return Mathf.Max(body, Mathf.Max(head, weapon));
            }, Vector4.zero, new Vector2(0.45f, 0f));
        }

        /// Силуэт героя 64×128 с посохом/мечом.
        public static Sprite Hero(bool melee)
        {
            return Make(melee ? "hero_melee" : "hero_ranged", 64, 128, (x, y) =>
            {
                float body = Edge(RoundedSdf(x - 28f, y - 44f, 18f, 44f, 10f));
                float head = Edge(Mathf.Sqrt((x - 28f) * (x - 28f) + (y - 104f) * (y - 104f)) - 16f);
                float weapon = melee ? Edge(RoundedSdf(x - 54f, y - 60f, 3.5f, 34f, 2f)) : Edge(RoundedSdf(x - 54f, y - 70f, 3f, 50f, 2f));
                float orb = melee ? 0f : Edge(Mathf.Sqrt((x - 54f) * (x - 54f) + (y - 122f) * (y - 122f)) - 6f);
                return Mathf.Max(Mathf.Max(body, head), Mathf.Max(weapon, orb));
            }, Vector4.zero, new Vector2(0.45f, 0f));
        }

        public static Sprite White() => Make("white", 4, 4, (x, y) => 1f, Vector4.zero);
        /// Вертикальный градиент: непрозрачный снизу, прозрачный сверху (виньетка главного меню на весь экран).
        public static Sprite VGradient() => Make("vgrad", 2, 64, (x, y) => Mathf.Pow(1f - y / 64f, 1.4f), Vector4.zero);
    }
}
