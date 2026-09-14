using System.Collections.Generic;
using UnityEngine;
using Warbands.Sim;
using EventType = Warbands.Sim.EventType;

namespace Warbands
{
    /// Поле Bush Rush в 3D (автор 14.09, грейбокс): гексы 6×10 плашками на земле, кусты — зелёные блоки, препятствия — серые,
    /// бонусы — шарики, отряды — толпы капсул (синие свои, красные чужие), герои — большие блоки по центру своих краёв.
    /// Камера смотрит от игрока вглубь поля (портрет), на широком экране рисует в колонке 9:16 по центру.
    /// Постановка хода — по событиям сима (BattleRunner.Applied): расчистка бота, бег по гексам, удар, зарастание.
    public sealed class Field3D : MonoBehaviour
    {
        public const float HexW = 1f, HexH = 1.1547f, Pitch = 0.8660254f;
        [SerializeField] BattleRunner runner;
        [SerializeField] RushAssets assets;
        [SerializeField] Camera cam;

        public BattleRunner Runner => runner;
        public Camera Cam => cam;
        public void Setup(BattleRunner r, RushAssets a, Camera c) { runner = r; assets = a; cam = c; }

        // ---------- геометрия ----------
        public static Vector3 CellPos(Cell c) => new Vector3(c.X + 0.5f + ((c.Y & 1) == 1 ? 0.5f : 0f) - (BushGrid.Cols + 0.5f) / 2f, 0f, ((BushGrid.Rows - 1 - c.Y) - (BushGrid.Rows - 1) / 2f) * Pitch);
        /// Гекс под точкой на земле, null — вне поля.
        public static Cell? CellAt(Vector3 p)
        {
            int ry = Mathf.RoundToInt((BushGrid.Rows - 1) / 2f - p.z / Pitch + 0f); ry = BushGrid.Rows - 1 - Mathf.RoundToInt(p.z / Pitch + (BushGrid.Rows - 1) / 2f);
            Cell? best = null; float bd = float.MaxValue;
            for (int y = ry - 1; y <= ry + 1; y++)
            {
                int cx0 = Mathf.FloorToInt(p.x + (BushGrid.Cols + 0.5f) / 2f - ((y & 1) == 1 ? 0.5f : 0f) - 0.5f);
                for (int x = cx0 - 1; x <= cx0 + 1; x++) { var c = new Cell(x, y); var q = CellPos(c); float d = (q.x - p.x) * (q.x - p.x) + (q.z - p.z) * (q.z - p.z); if (d < bd) { bd = d; best = c; } }
            }
            return best.HasValue && BushGrid.Inside(best.Value) && bd <= (HexH / 2f) * (HexH / 2f) * 1.05f ? best : null;
        }
        /// Точка на земле под экранной точкой (луч камеры в плоскость y = 0).
        public bool GroundPoint(Vector2 screen, out Vector3 p)
        {
            p = Vector3.zero; if (cam == null) return false;
            var ray = cam.ScreenPointToRay(screen); if (Mathf.Abs(ray.direction.y) < 1e-4f) return false;
            float t = -ray.origin.y / ray.direction.y; if (t < 0f) return false;
            p = ray.origin + ray.direction * t; return true;
        }

        // ---------- объекты ----------
        Transform root, tilesRoot, unitsRoot;
        readonly MeshRenderer[] tiles = new MeshRenderer[BushGrid.Cols * BushGrid.Rows];
        readonly Transform[] bushes = new Transform[BushGrid.Cols * BushGrid.Rows], obstacles = new Transform[BushGrid.Cols * BushGrid.Rows], bonuses = new Transform[BushGrid.Cols * BushGrid.Rows];
        readonly float[] bushScale = new float[BushGrid.Cols * BushGrid.Rows], bushTarget = new float[BushGrid.Cols * BushGrid.Rows];
        readonly bool[] pendingRegrow = new bool[BushGrid.Cols * BushGrid.Rows], reachMask = new bool[BushGrid.Cols * BushGrid.Rows];
        Mesh hexMesh;
        MaterialPropertyBlock mpb;

        public sealed class UnitView
        {
            public Unit Unit; public Transform Root; public readonly List<Transform> Figs = new List<Transform>();
            public int ShownHp; public Vector3 From, To; public List<Vector3> Path; public float MoveStart, MoveEnd; public bool Moving;
            public float Punch;   // всплеск масштаба при ударе/попадании
            public Vector3 HeadPos => Root.position + Vector3.up * (Unit != null && Unit.IsHero ? 1.5f : 0.75f);
        }
        readonly List<UnitView> views = new List<UnitView>();
        public IReadOnlyList<UnitView> Views => views;
        public UnitView View(Ref r) { foreach (var v in views) if (v.Unit != null && v.Unit.Ref == r) return v; return null; }

        readonly List<(float t, System.Action a)> timed = new List<(float, System.Action)>();
        BattleState shownFor;

        void Awake()
        {
            root = new GameObject("Field").transform; root.SetParent(transform, false);
            mpb = new MaterialPropertyBlock();
            hexMesh = BuildHexMesh();
            // земля
            var ground = new GameObject("Ground", typeof(MeshFilter), typeof(MeshRenderer)); ground.transform.SetParent(root, false);
            ground.GetComponent<MeshFilter>().sharedMesh = assets.cube; ground.GetComponent<MeshRenderer>().sharedMaterial = assets.ground;
            ground.transform.localScale = new Vector3(40f, 0.2f, 40f); ground.transform.localPosition = new Vector3(0f, -0.12f, 0f);
            tilesRoot = new GameObject("Tiles").transform; tilesRoot.SetParent(root, false);
            unitsRoot = new GameObject("Units").transform; unitsRoot.SetParent(root, false);
            for (int y = 0; y < BushGrid.Rows; y++) for (int x = 0; x < BushGrid.Cols; x++)
            {
                var c = new Cell(x, y); int i = BushGrid.Index(c); var p = CellPos(c);
                var tile = new GameObject($"Tile_{x}_{y}", typeof(MeshFilter), typeof(MeshRenderer)); tile.transform.SetParent(tilesRoot, false); tile.transform.localPosition = p + Vector3.up * 0.01f;
                tile.GetComponent<MeshFilter>().sharedMesh = hexMesh; var mr = tile.GetComponent<MeshRenderer>(); mr.sharedMaterial = assets.sand; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; tiles[i] = mr;
                // куст: блок со случайным поворотом и лёгким разбросом размера — органичнее
                var b = Prim("Bush", assets.cube, assets.bush, tilesRoot); float k = 0.8f + Hash(x, y) * 0.15f;
                b.localPosition = p + Vector3.up * 0.22f; b.localRotation = Quaternion.Euler(0f, Hash(y, x) * 60f, 0f); b.localScale = new Vector3(0.78f * k, 0.44f, 0.78f * k);
                bushes[i] = b; bushScale[i] = bushTarget[i] = 1f;
                var o = Prim("Obstacle", assets.cube, assets.obstacle, tilesRoot); o.localPosition = p + Vector3.up * 0.35f; o.localScale = new Vector3(0.9f, 0.7f, 0.9f); o.gameObject.SetActive(false); obstacles[i] = o;
                var bn = Prim("Bonus", assets.sphere, assets.gold, tilesRoot); bn.localPosition = p + Vector3.up * 0.4f; bn.localScale = Vector3.one * 0.38f; bn.gameObject.SetActive(false); bonuses[i] = bn;
            }
            PlaceCamera();
        }

        void Start()
        {
            if (runner != null) { runner.Applied += OnApplied; runner.BattleBegan += OnBattleBegan; }
        }

        static float Hash(int x, int y) { unchecked { uint h = (uint)(x * 374761393) ^ (uint)(y * 668265263); h = (h ^ (h >> 13)) * 1274126177u; return ((h ^ (h >> 16)) & 0xFFFF) / 65535f; } }

        Transform Prim(string name, Mesh mesh, Material mat, Transform parent)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        static Mesh BuildHexMesh()
        {
            // pointy-top, ширина 1: радиус 1/√3, вершины сверху и снизу (по z)
            float r = HexH / 2f * 0.985f; var v = new Vector3[7]; var uv = new Vector2[7]; v[0] = Vector3.zero; uv[0] = new Vector2(0.5f, 0.5f);
            for (int k = 0; k < 6; k++) { float a = Mathf.PI / 6f + k * Mathf.PI / 3f; v[k + 1] = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r); uv[k + 1] = new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f); }
            var tri = new int[18]; for (int k = 0; k < 6; k++) { tri[k * 3] = 0; tri[k * 3 + 1] = 1 + (k + 1) % 6; tri[k * 3 + 2] = 1 + k; }
            var m = new Mesh { vertices = v, uv = uv, triangles = tri }; m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }

        // ---------- камера ----------
        void PlaceCamera()
        {
            if (cam == null) return;
            cam.orthographic = false; cam.fieldOfView = 46f; cam.nearClipPlane = 0.3f; cam.farClipPlane = 60f;
            cam.transform.position = new Vector3(0f, 11.2f, -8.6f);
            cam.transform.LookAt(new Vector3(0f, 0f, 0.2f));
            ApplyViewport();
        }
        int lastW, lastH;
        void ApplyViewport()
        {
            if (cam == null) return;
            float w = Screen.width, h = Screen.height; if (w < 1f || h < 1f) return;
            float aspect = 1080f / 1920f; float colW = h * aspect;
            cam.rect = w > colW ? new Rect((w - colW) / 2f / w, 0f, colW / w, 1f) : new Rect(0f, 0f, 1f, 1f);   // колонка 9:16 по центру, как PortraitFrame
            lastW = Screen.width; lastH = Screen.height;
        }

        // ---------- синхронизация с симом ----------
        void OnBattleBegan()
        {
            timed.Clear();
            var b = runner.Battle; shownFor = b;
            for (int y = 0; y < BushGrid.Rows; y++) for (int x = 0; x < BushGrid.Cols; x++)
            {
                var c = new Cell(x, y); int i = BushGrid.Index(c);
                bool bush = b.Bushes.IsBush(c); bushTarget[i] = bushScale[i] = bush ? 1f : 0f; bushes[i].gameObject.SetActive(bush); bushes[i].localScale = BushScaleVec(i, bushScale[i]);
                obstacles[i].gameObject.SetActive(b.Bushes.IsObstacle(c)); pendingRegrow[i] = false; reachMask[i] = false;
                SetBonus(c, b.Bushes.BonusAt(c), !bush);
                Retint(i);
            }
            foreach (var v in views) Destroy(v.Root.gameObject); views.Clear();
            foreach (var sd in b.Sides)
            {
                foreach (var q in sd.Squads) views.Add(BuildUnit(q));
                if (b.Cfg.rushMode && sd.Hero != null) views.Add(BuildUnit(sd.Hero));
            }
            SetPending(PendingRegrow(b));
        }

        Vector3 BushScaleVec(int i, float k) { float s = 0.8f + Hash(i % BushGrid.Cols, i / BushGrid.Cols) * 0.15f; return new Vector3(0.78f * s * k, 0.44f * k, 0.78f * s * k); }

        UnitView BuildUnit(Unit u)
        {
            var v = new UnitView { Unit = u, ShownHp = u.Hp };
            v.Root = new GameObject(u.Name).transform; v.Root.SetParent(unitsRoot, false); v.Root.position = CellPos(u.Cell);
            LayoutFigs(v);
            return v;
        }
        /// Толпа: капсулы по числу живых бойцов рядами в гексе; герой — один большой блок.
        void LayoutFigs(UnitView v)
        {
            var u = v.Unit; int n = u.IsHero ? 1 : Mathf.Max(0, u.Count);
            while (v.Figs.Count < n)
            {
                var f = Prim("F", u.IsHero ? assets.cube : assets.capsule, u.IsHero ? (u.Side == 0 ? assets.heroBlue : assets.heroRed) : (u.Side == 0 ? assets.blue : assets.red), v.Root);
                v.Figs.Add(f);
            }
            for (int i = 0; i < v.Figs.Count; i++)
            {
                bool on = i < n; v.Figs[i].gameObject.SetActive(on); if (!on) continue;
                if (u.IsHero) { v.Figs[i].localPosition = new Vector3(0f, 0.6f, 0f); v.Figs[i].localScale = new Vector3(0.7f, 1.2f, 0.7f); continue; }
                int cols = Mathf.CeilToInt(Mathf.Sqrt(n * 1.3f)); int r = i / cols, c = i % cols; int inRow = Mathf.Min(cols, n - r * cols);
                float step = Mathf.Min(0.22f, 0.8f / cols); float jx = (Hash(u.Index * 31 + i, u.Side * 7) - 0.5f) * step * 0.5f, jz = (Hash(i, u.Index * 13 + 5) - 0.5f) * step * 0.4f;
                float rows = Mathf.CeilToInt(n / (float)cols);
                v.Figs[i].localPosition = new Vector3((c - (inRow - 1) / 2f) * step + jx, 0.22f, (r - (rows - 1) / 2f) * step * 0.9f + jz + (u.Side == 0 ? 0f : 0f));
                v.Figs[i].localScale = new Vector3(0.16f, 0.22f, 0.16f);
            }
        }

        void SetBonus(Cell c, BonusKind k, bool visible)
        {
            int i = BushGrid.Index(c); var t = bonuses[i]; bool show = k != BonusKind.None && visible;
            t.gameObject.SetActive(show);
            if (show) t.GetComponent<MeshRenderer>().sharedMaterial = k == BonusKind.Double ? assets.gold : assets.mint;
        }

        void Retint(int i)
        {
            var mr = tiles[i]; if (mr == null) return;
            mr.GetPropertyBlock(mpb);
            Color c = pendingRegrow[i] ? new Color(0.66f, 0.84f, 0.46f) : new Color(0.93f, 0.86f, 0.64f);
            if (reachMask[i]) c *= 0.8f;
            mpb.SetColor("_BaseColor", c); mpb.SetColor("_Color", c);
            mr.SetPropertyBlock(mpb);
            var br = bushes[i].GetComponent<MeshRenderer>(); br.GetPropertyBlock(mpb);
            Color g = new Color(0.36f, 0.68f, 0.36f); if (reachMask[i]) g *= 0.78f;
            mpb.SetColor("_BaseColor", g); mpb.SetColor("_Color", g); br.SetPropertyBlock(mpb);
        }

        List<Cell> PendingRegrow(BattleState b)
        {
            var l = new List<Cell>(); if (b == null || b.Bushes == null || b.Cfg.bushRegrowTurns <= 0) return l;
            for (int y = 0; y < BushGrid.Rows; y++) for (int x = 0; x < BushGrid.Cols; x++)
            {
                var c = new Cell(x, y); int i = BushGrid.Index(c);
                if (b.Bushes.IsBush(c) || b.Bushes.IsObstacle(c)) continue;
                if (b.TurnIndex + 1 - b.Bushes.ClearedTurn[i] >= b.Cfg.bushRegrowTurns) l.Add(c);
            }
            return l;
        }
        void SetPending(List<Cell> cells)
        {
            for (int i = 0; i < pendingRegrow.Length; i++) pendingRegrow[i] = false;
            foreach (var c in cells) pendingRegrow[BushGrid.Index(c)] = true;
            for (int i = 0; i < pendingRegrow.Length; i++) Retint(i);
        }
        /// Подсветка радиуса хода ходящего отряда (сквозь кусты, препятствия и враги — стены).
        public void SetReach(IList<Cell> cells)
        {
            for (int i = 0; i < reachMask.Length; i++) reachMask[i] = false;
            if (cells != null) foreach (var c in cells) if (BushGrid.Inside(c)) reachMask[BushGrid.Index(c)] = true;
            for (int i = 0; i < reachMask.Length; i++) Retint(i);
        }

        /// Игрок стёр куст пальцем: тропа появляется сразу (в симе куст стоит до конца хода).
        public void EraseCell(Cell c) { int i = BushGrid.Index(c); bushTarget[i] = 0f; pendingRegrow[i] = false; var b = runner.Battle; if (b != null) SetBonus(c, b.Bushes.BonusAt(c), true); Retint(i); }
        void RegrowCell(Cell c) { int i = BushGrid.Index(c); bushTarget[i] = 1f; bushes[i].gameObject.SetActive(true); pendingRegrow[i] = false; SetBonus(c, BonusKind.None, false); Retint(i); }

        // ---------- постановка ----------
        void OnApplied(List<CombatEvent> evs)
        {
            var b = runner.Battle; if (b == null) return;
            if (shownFor != b) OnBattleBegan();
            float k = runner.AnimScale, t = Time.unscaledTime + 0.05f; var cfg = runner.Config;
            Ref actor = Ref.None; UnitView av = null; float moveEnd = t;
            foreach (var e in evs)
            {
                switch (e.Type)
                {
                    case EventType.ActionStarted: if (actor.Equals(Ref.None) && e.Action == CommandKind.Clear) { actor = e.Actor; av = View(actor); } break;
                    case EventType.BushCleared:
                        {
                            var cells = BushGrid.Decode(e.Text); bool bot = av != null && av.Unit.Side == 1;
                            for (int i = 0; i < cells.Count; i++) { var c = cells[i]; float at = bot ? t + i * cfg.bushClearSeconds * k : t; timed.Add((at, () => EraseCell(c))); }
                            if (bot) t += cells.Count * cfg.bushClearSeconds * k + 0.2f * k;
                            break;
                        }
                    case EventType.MoveStarted:
                        {
                            var path = BushGrid.Decode(e.Text); var v = View(e.Actor); if (v == null || path.Count == 0) break;
                            float dur = (0.3f + path.Count * cfg.bushStepSeconds) * k; var pts = new List<Vector3>(); foreach (var c in path) pts.Add(CellPos(c));
                            float start = t; timed.Add((start, () => { v.Path = pts; v.From = v.Root.position; v.MoveStart = Time.unscaledTime; v.MoveEnd = Time.unscaledTime + dur; v.Moving = true; }));
                            t += dur; moveEnd = t;
                            break;
                        }
                    case EventType.BonusTaken: { var cells = BushGrid.Decode(e.Text); timed.Add((moveEnd, () => { foreach (var c in cells) SetBonus(c, BonusKind.None, false); })); break; }
                    case EventType.DamageApplied: case EventType.HealApplied:
                        {
                            var tv = View(e.Target); var src = View(e.Actor); float at = moveEnd + 0.35f * k;
                            timed.Add((at, () => { if (tv != null && tv.Unit != null) { tv.ShownHp = tv.Unit.Hp; tv.Punch = 1f; LayoutFigs(tv); } if (src != null) src.Punch = 0.6f; Hit?.Invoke(e); }));
                            t = Mathf.Max(t, at + 0.5f * k);
                            break;
                        }
                    case EventType.SquadDefeated: { var tv = View(e.Target); float at = moveEnd + 0.7f * k; timed.Add((at, () => { if (tv != null) { tv.ShownHp = 0; LayoutFigs(tv); tv.Root.gameObject.SetActive(false); } })); break; }
                    case EventType.BushRegrown: { var cells = BushGrid.Decode(e.Text); float at = t + 0.1f; timed.Add((at, () => { foreach (var c in cells) RegrowCell(c); })); t = at + 0.3f * k; break; }
                    case EventType.BonusRemoved: { var cells = BushGrid.Decode(e.Text); float at = t; timed.Add((at, () => { foreach (var c in cells) SetBonus(c, BonusKind.None, false); })); break; }
                    case EventType.BonusSpawned: { var cells = BushGrid.Decode(e.Text); var bk = (BonusKind)e.Value; float at = t; timed.Add((at, () => { var bb = runner.Battle; foreach (var c in cells) if (bb != null) SetBonus(c, bk, !bb.Bushes.IsBush(c)); })); break; }
                }
            }
            timed.Add((t + 0.05f, () => { var bb = runner.Battle; if (bb != null) { SetPending(PendingRegrow(bb)); foreach (var v in views) if (v.Unit != null && v.Unit.Alive) v.ShownHp = v.Unit.Hp; } }));
        }
        /// Попадание/лечение показано (для всплывающих чисел в HUD).
        public event System.Action<CombatEvent> Hit;

        void Update()
        {
            if (Screen.width != lastW || Screen.height != lastH) ApplyViewport();
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            for (int i = timed.Count - 1; i >= 0; i--) if (timed[i].t <= now) { var a = timed[i].a; timed.RemoveAt(i); a(); }
            for (int i = 0; i < bushScale.Length; i++)
            {
                if (Mathf.Abs(bushScale[i] - bushTarget[i]) < 1e-3f) continue;
                bushScale[i] = Mathf.MoveTowards(bushScale[i], bushTarget[i], dt * 4f);
                bushes[i].localScale = BushScaleVec(i, Mathf.Max(0.001f, bushScale[i]));
                if (bushScale[i] <= 0.001f && bushTarget[i] <= 0f) bushes[i].gameObject.SetActive(false);
            }
            foreach (var v in views)
            {
                if (v.Moving)
                {
                    float u = Mathf.Clamp01((now - v.MoveStart) / Mathf.Max(0.01f, v.MoveEnd - v.MoveStart));
                    v.Root.position = Along(v.From, v.Path, u);
                    float hop = Mathf.Abs(Mathf.Sin(u * v.Path.Count * Mathf.PI)) * 0.08f; v.Root.position += Vector3.up * hop;
                    if (u >= 1f) { v.Moving = false; v.Root.position = v.Path[v.Path.Count - 1]; }
                }
                if (v.Punch > 0f) { v.Punch = Mathf.Max(0f, v.Punch - dt * 3f); float sc = 1f + 0.25f * v.Punch; v.Root.localScale = new Vector3(sc, 1f + 0.4f * v.Punch, sc); }
                else if (v.Root.localScale != Vector3.one) v.Root.localScale = Vector3.one;
            }
            foreach (var bn in bonuses) if (bn.gameObject.activeSelf) bn.localRotation = Quaternion.Euler(0f, now * 90f, 0f);
        }
        static Vector3 Along(Vector3 from, List<Vector3> pts, float u)
        {
            int n = pts.Count; float s = u * n; int i = Mathf.Min(n - 1, Mathf.FloorToInt(s)); float f = s - i;
            var a = i == 0 ? from : pts[i - 1]; var b = pts[i];
            return Vector3.Lerp(a, b, f);
        }

        void OnDestroy() { if (runner != null) { runner.Applied -= OnApplied; runner.BattleBegan -= OnBattleBegan; } }
    }
}
