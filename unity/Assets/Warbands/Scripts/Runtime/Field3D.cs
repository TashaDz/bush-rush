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
        readonly Transform[] obstacles = new Transform[BushGrid.Cols * BushGrid.Rows], bonuses = new Transform[BushGrid.Cols * BushGrid.Rows];
        readonly float[] bushScale = new float[BushGrid.Cols * BushGrid.Rows], bushTarget = new float[BushGrid.Cols * BushGrid.Rows];
        readonly bool[] pendingRegrow = new bool[BushGrid.Cols * BushGrid.Rows], reachMask = new bool[BushGrid.Cols * BushGrid.Rows];
        Mesh hexMesh, boxMesh, sphereMesh, pawnMesh;   // меши свои (процедурные): встроенные примитивы в WebGL-билд не попадают
        public const float GrassH = 0.45f;   // толщина травы
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
            Debug.Log($"[SW] field: gfx={SystemInfo.graphicsDeviceType} mats={(assets != null && assets.ground != null && assets.ground.shader != null ? assets.ground.shader.name : "NONE")}");
            mpb = new MaterialPropertyBlock();
            hexMesh = BuildHexMesh(); boxMesh = GreyMeshes.Box(); sphereMesh = GreyMeshes.Sphere(14, 10); pawnMesh = GreyMeshes.Cylinder(10);
            // земля
            var ground = Prim("Ground", boxMesh, assets.ground, root);
            ground.localScale = new Vector3(60f, 0.2f, 60f); ground.localPosition = new Vector3(0f, -0.12f, 0f); Tint(ground.GetComponent<MeshRenderer>(), new Color(0.4f, 0.66f, 0.36f));   // земля в цвет травы: край ковра не виден
            tilesRoot = new GameObject("Tiles").transform; tilesRoot.SetParent(root, false);
            unitsRoot = new GameObject("Units").transform; unitsRoot.SetParent(root, false);
            for (int y = 0; y < BushGrid.Rows; y++) for (int x = 0; x < BushGrid.Cols; x++)
            {
                var c = new Cell(x, y); int i = BushGrid.Index(c); var p = CellPos(c);
                var tile = new GameObject($"Tile_{x}_{y}", typeof(MeshFilter), typeof(MeshRenderer)); tile.transform.SetParent(tilesRoot, false); tile.transform.localPosition = p + Vector3.up * 0.01f;
                tile.GetComponent<MeshFilter>().sharedMesh = hexMesh; var mr = tile.GetComponent<MeshRenderer>(); mr.sharedMaterial = assets.sand; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; Tint(mr, assets.sand); tiles[i] = mr;
                bushScale[i] = bushTarget[i] = 1f;   // уровень травы гекса (1 — заросло), из него строится мягкий ковёр (GrassCarpet)
                var o = Prim("Obstacle", boxMesh, assets.obstacle, tilesRoot); o.localPosition = p + Vector3.up * 0.35f; o.localScale = new Vector3(0.9f, 0.7f, 0.9f); o.gameObject.SetActive(false); obstacles[i] = o;
                var bn = Prim("Bonus", sphereMesh, assets.gold, tilesRoot); bn.localPosition = p + Vector3.up * 0.4f; bn.localScale = Vector3.one * 0.38f; bn.gameObject.SetActive(false); bonuses[i] = bn;
            }
            // трава (автор 14.09): мягкий сплошной ковёр с текстурой — сетка высот по уровням гексов, края плавные, свет запечён в вершины
            carpet = new GrassCarpet(root, assets.grassSoft, bushScale, reachMask);
            // прицел хода (из Warbands): кольцо на всё поле сужается на ходящий отряд и остаётся под ним
            reticle = Prim("Reticle", GreyMeshes.Ring(0.34f, 0.46f, 40), assets.decal, root); reticle.gameObject.SetActive(false);   // кольцо внутри гекса (внутренний радиус гекса 0.5), иначе прячется под травой
            PlaceCamera();
        }
        GrassCarpet carpet; Transform reticle; Ref reticleFor = Ref.None; float reticleT = -1f; bool reticleMine;
        const float TurnIn = 0.55f, TurnHold = 0.3f;

        void Start()
        {
            if (runner != null) { runner.Applied += OnApplied; runner.BattleBegan += OnBattleBegan; }
        }

        static float Hash(int x, int y) { unchecked { uint h = (uint)(x * 374761393) ^ (uint)(y * 668265263); h = (h ^ (h >> 13)) * 1274126177u; return ((h ^ (h >> 16)) & 0xFFFF) / 65535f; } }

        Transform Prim(string name, Mesh mesh, Material mat, Transform parent)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh; var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = mat; Tint(mr, mat);
            return go.transform;
        }
        /// Цвет — через MaterialPropertyBlock: с SRP Batcher материалы, созданные forge, рисовались одним цветом (14.09), блок на рендерере надёжен.
        void Tint(MeshRenderer mr, Material mat) { if (mat == null) return; var c = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : mat.color; Tint(mr, c); }
        void Tint(MeshRenderer mr, Color c) { mr.GetPropertyBlock(mpb); mpb.SetColor("_BaseColor", c); mpb.SetColor("_Color", c); mr.SetPropertyBlock(mpb); }

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
            cam.orthographic = false; cam.nearClipPlane = 0.3f; cam.farClipPlane = 60f;
            cam.transform.position = new Vector3(0f, 12.4f, -8.4f);
            cam.transform.LookAt(new Vector3(0f, 0f, 1.15f));   // выше середины: своё поле уходит из-под нижних полосок HUD, герой врага — под верхней
            ApplyViewport();
        }
        int lastW, lastH;
        void ApplyViewport()
        {
            if (cam == null) return;
            float w = Screen.width, h = Screen.height; if (w < 1f || h < 1f) return;
            float aspect = 1080f / 1920f; float colW = h * aspect;
            cam.rect = w > colW ? new Rect((w - colW) / 2f / w, 0f, colW / w, 1f) : new Rect(0f, 0f, 1f, 1f);   // колонка 9:16 по центру, как PortraitFrame
            // горизонтальный угол обзора постоянный (поле 6.5 гексов влезает по ширине), вертикальный — от аспекта колонки
            float colAspect = Mathf.Min(w, colW) / h; const float HFov = 28f;
            cam.fieldOfView = 2f * Mathf.Atan(Mathf.Tan(HFov * 0.5f * Mathf.Deg2Rad) / colAspect) * Mathf.Rad2Deg;
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
                bool bush = b.Bushes.IsBush(c); bushTarget[i] = bushScale[i] = bush ? 1f : 0f;
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
            SetPending(PendingRegrow(b)); carpet.Dirty(); reticleFor = Ref.None; reticle.gameObject.SetActive(false);
        }

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
                var f = Prim("F", u.IsHero ? boxMesh : pawnMesh, u.IsHero ? (u.Side == 0 ? assets.heroBlue : assets.heroRed) : (u.Side == 0 ? assets.blue : assets.red), v.Root);
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
                v.Figs[i].localScale = new Vector3(0.16f, 0.3f, 0.16f);
            }
        }

        void SetBonus(Cell c, BonusKind k, bool visible)
        {
            int i = BushGrid.Index(c); var t = bonuses[i]; bool show = k != BonusKind.None && visible;
            t.gameObject.SetActive(show);
            if (show) { var mr = t.GetComponent<MeshRenderer>(); var m = k == BonusKind.Double ? assets.gold : assets.mint; mr.sharedMaterial = m; Tint(mr, m); }
        }

        void Retint(int i)
        {
            var mr = tiles[i]; if (mr == null) return;
            mr.GetPropertyBlock(mpb);
            Color c = pendingRegrow[i] ? new Color(0.66f, 0.84f, 0.46f) : new Color(0.93f, 0.86f, 0.64f);
            if (reachMask[i]) c *= 0.8f;
            mpb.SetColor("_BaseColor", c); mpb.SetColor("_Color", c);
            mr.SetPropertyBlock(mpb);
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
            carpet?.Dirty();
        }

        /// Игрок стёр куст пальцем: тропа появляется сразу (в симе куст стоит до конца хода).
        public void EraseCell(Cell c) { int i = BushGrid.Index(c); bushTarget[i] = 0f; pendingRegrow[i] = false; var b = runner.Battle; if (b != null) SetBonus(c, b.Bushes.BonusAt(c), true); Retint(i); }
        void RegrowCell(Cell c) { int i = BushGrid.Index(c); bushTarget[i] = 1f; pendingRegrow[i] = false; SetBonus(c, BonusKind.None, false); Retint(i); }

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
            bool grassDirty = false;
            for (int i = 0; i < bushScale.Length; i++)
            {
                if (Mathf.Abs(bushScale[i] - bushTarget[i]) < 1e-3f) continue;
                bushScale[i] = Mathf.MoveTowards(bushScale[i], bushTarget[i], dt * 3f); grassDirty = true;
            }
            if (grassDirty) carpet.Dirty();
            carpet.Tick();
            TickReticle(now, dt);
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
        /// Прицел «чей ход» (из Warbands, автор 14.09: «неясно, кто ходит»): при смене актора кольцо со всего поля сужается на его гекс за 0.55 с,
        /// коротко пульсирует и остаётся под отрядом, пока ход не разыгран; золото — свой, красное — вражеский.
        void TickReticle(float now, float dt)
        {
            var b = runner != null ? runner.Battle : null; var actor = b != null && !b.Ended ? b.Actor : null;
            bool show = actor != null && !actor.IsHero && !runner.Busy && runner.Countdown <= 0f;
            if (!show) { if (reticle.gameObject.activeSelf) reticle.gameObject.SetActive(false); reticleFor = Ref.None; return; }
            if (!actor.Ref.Equals(reticleFor)) { reticleFor = actor.Ref; reticleT = 0f; reticleMine = actor.Side == 0; reticle.gameObject.SetActive(true); }
            var v = View(actor.Ref); if (v == null) return;
            reticleT += dt; float size, alpha;
            if (reticleT < TurnIn) { float k = reticleT / TurnIn; float e = 1f - (1f - k) * (1f - k) * (1f - k); size = Mathf.Lerp(16f, 1f, e); alpha = Mathf.Lerp(0.35f, 1f, e); }
            else if (reticleT < TurnIn + TurnHold) { float k = (reticleT - TurnIn) / TurnHold; size = 1f + 0.1f * Mathf.Sin(k * Mathf.PI); alpha = 1f; }
            else { size = 1f + 0.03f * Mathf.Sin(now * 4f); alpha = 0.85f; }
            float land = Mathf.Clamp01(reticleT / TurnIn); reticle.position = v.Root.position + Vector3.up * Mathf.Lerp(GrassH + 0.08f, 0.06f, land);   // летит над травой, садится на землю у ног
            reticle.localScale = new Vector3(size, 1f, size);
            reticle.localRotation = Quaternion.Euler(0f, (1f - Mathf.Clamp01(reticleT / TurnIn)) * 90f, 0f);
            var col = reticleMine ? new Color(1f, 0.85f, 0.24f, alpha) : new Color(1f, 0.36f, 0.36f, alpha);
            Tint(reticle.GetComponent<MeshRenderer>(), col);
        }

        static Vector3 Along(Vector3 from, List<Vector3> pts, float u)
        {
            int n = pts.Count; float s = u * n; int i = Mathf.Min(n - 1, Mathf.FloorToInt(s)); float f = s - i;
            var a = i == 0 ? from : pts[i - 1]; var b = pts[i];
            return Vector3.Lerp(a, b, f);
        }

        void OnDestroy() { if (runner != null) { runner.Applied -= OnApplied; runner.BattleBegan -= OnBattleBegan; } }
    }

    /// Процедурные меши грейбокса: куб, UV-сфера, цилиндр (пешка). Встроенные примитивы Unity в WebGL-билд без сцены не попадают.
    public static class GreyMeshes
    {
        public static Mesh Box()
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            void Face(Vector3 nrm, Vector3 a, Vector3 b, Vector3 c, Vector3 d) { int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d); for (int k = 0; k < 4; k++) n.Add(nrm); t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 }); }
            float h = 0.5f;
            Face(Vector3.up, new Vector3(-h, h, -h), new Vector3(-h, h, h), new Vector3(h, h, h), new Vector3(h, h, -h));
            Face(Vector3.down, new Vector3(-h, -h, h), new Vector3(-h, -h, -h), new Vector3(h, -h, -h), new Vector3(h, -h, h));
            Face(Vector3.forward, new Vector3(h, -h, h), new Vector3(h, h, h), new Vector3(-h, h, h), new Vector3(-h, -h, h));
            Face(Vector3.back, new Vector3(-h, -h, -h), new Vector3(-h, h, -h), new Vector3(h, h, -h), new Vector3(h, -h, -h));
            Face(Vector3.right, new Vector3(h, -h, -h), new Vector3(h, h, -h), new Vector3(h, h, h), new Vector3(h, -h, h));
            Face(Vector3.left, new Vector3(-h, -h, h), new Vector3(-h, h, h), new Vector3(-h, h, -h), new Vector3(-h, -h, -h));
            var m = new Mesh(); m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }
        /// UV-сфера диаметра 1.
        public static Mesh Sphere(int seg, int rings)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float phi = Mathf.PI * r / rings; float y = Mathf.Cos(phi), rr = Mathf.Sin(phi);
                for (int s2 = 0; s2 <= seg; s2++) { float th = 2f * Mathf.PI * s2 / seg; var p = new Vector3(rr * Mathf.Cos(th), y, rr * Mathf.Sin(th)); v.Add(p * 0.5f); n.Add(p); }
            }
            for (int r = 0; r < rings; r++) for (int s2 = 0; s2 < seg; s2++)
            {
                int a = r * (seg + 1) + s2, b = a + seg + 1;
                t.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
            }
            var m = new Mesh(); m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }
        /// Плоское кольцо в плоскости XZ: внутренний/внешний радиус, нормаль вверх.
        public static Mesh Ring(float rIn, float rOut, int seg)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>(); var c = new List<Color32>();
            for (int s2 = 0; s2 <= seg; s2++)
            {
                float th = 2f * Mathf.PI * s2 / seg; var d = new Vector3(Mathf.Cos(th), 0f, Mathf.Sin(th));
                v.Add(d * rIn); n.Add(Vector3.up); c.Add(new Color32(255, 255, 255, 255)); v.Add(d * rOut); n.Add(Vector3.up); c.Add(new Color32(255, 255, 255, 255));
            }
            for (int s2 = 0; s2 < seg; s2++) { int a = s2 * 2; t.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 }); }
            var m = new Mesh(); m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }
        /// Цилиндр диаметра 1 и высоты 1 (центр в середине).
        public static Mesh Cylinder(int seg)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            for (int s2 = 0; s2 <= seg; s2++)
            {
                float th = 2f * Mathf.PI * s2 / seg; var d = new Vector3(Mathf.Cos(th), 0f, Mathf.Sin(th));
                v.Add(d * 0.5f + Vector3.down * 0.5f); n.Add(d); v.Add(d * 0.5f + Vector3.up * 0.5f); n.Add(d);
            }
            for (int s2 = 0; s2 < seg; s2++) { int a = s2 * 2; t.AddRange(new[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 }); }
            int top = v.Count; v.Add(Vector3.up * 0.5f); n.Add(Vector3.up);
            for (int s2 = 0; s2 <= seg; s2++) { float th = 2f * Mathf.PI * s2 / seg; v.Add(new Vector3(Mathf.Cos(th) * 0.5f, 0.5f, Mathf.Sin(th) * 0.5f)); n.Add(Vector3.up); }
            for (int s2 = 0; s2 < seg; s2++) t.AddRange(new[] { top, top + 1 + s2 + 1, top + 1 + s2 });
            int bot = v.Count; v.Add(Vector3.down * 0.5f); n.Add(Vector3.down);
            for (int s2 = 0; s2 <= seg; s2++) { float th = 2f * Mathf.PI * s2 / seg; v.Add(new Vector3(Mathf.Cos(th) * 0.5f, -0.5f, Mathf.Sin(th) * 0.5f)); n.Add(Vector3.down); }
            for (int s2 = 0; s2 < seg; s2++) t.AddRange(new[] { bot, bot + 1 + s2, bot + 1 + s2 + 1 });
            var m = new Mesh(); m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }
    }

    /// Мягкий сплошной ковёр травы (автор 14.09): сетка высот над полем с шагом 0.1; высота вершины — плавная смесь уровней ближайшего гекса и его
    /// соседей (как маска троп в Warbands) плюс шум, так что края троп органичные, а «толщина» видна по склонам. Свет запекается в вершинный цвет
    /// (Particles/Unlit), текстура — процедурный шум с светлыми кончиками травинок. Затемнение радиуса хода — тоже в вершинах.
    public sealed class GrassCarpet
    {
        const float Step = 0.12f, Margin = 3.2f, FarMargin = 9f, H = Field3D.GrassH;   // поле с запасом (вдаль больше) — за краем кадра ковёр не кончается
        readonly int nx, nz; readonly float x0, z0;
        readonly Mesh mesh; readonly Vector3[] verts; readonly Color32[] cols; readonly Vector3[] normals;
        readonly int[] nearIdx; readonly float[] weights;   // на вершину: ближайший гекс (-1 — вне поля) и 7 весов (сам + 6 соседей)
        readonly short[] nearX, nearY;
        readonly float[] noise, tips;
        readonly float[] level; readonly bool[] reach; bool dirty = true;
        static readonly Vector3 LightDir = new Vector3(-0.35f, 0.85f, -0.4f).normalized;

        public GrassCarpet(Transform parent, Material mat, float[] level, bool[] reach)
        {
            this.level = level; this.reach = reach;
            float w = BushGrid.Cols + 0.5f, d = (BushGrid.Rows - 1) * Field3D.Pitch + Field3D.HexH;
            x0 = -w / 2f - Margin; z0 = -d / 2f - Margin; nx = Mathf.CeilToInt((w + 2f * Margin) / Step) + 1; nz = Mathf.CeilToInt((d + Margin + FarMargin) / Step) + 1;
            int n = nx * nz; verts = new Vector3[n]; cols = new Color32[n]; normals = new Vector3[n]; nearIdx = new int[n]; nearX = new short[n]; nearY = new short[n]; weights = new float[n * 7]; noise = new float[n]; tips = new float[n];
            var uv = new Vector2[n]; var tri = new int[(nx - 1) * (nz - 1) * 6];
            for (int j = 0; j < nz; j++) for (int i = 0; i < nx; i++)
            {
                int k = j * nx + i; float x = x0 + i * Step, z = z0 + j * Step; verts[k] = new Vector3(x, 0f, z); uv[k] = new Vector2(x * 0.7f, z * 0.7f);
                var near = Field3D.CellAt(new Vector3(x, 0f, z)); Cell c = near ?? NearestAny(x, z);
                nearX[k] = (short)c.X; nearY[k] = (short)c.Y; nearIdx[k] = BushGrid.Inside(c) ? BushGrid.Index(c) : -1;
                float sum = 0f; var wk = new float[7];
                for (int q = 0; q < 7; q++)
                {
                    int cx = c.X, cy = c.Y; if (q > 0) { BushGrid.NeighborDelta(c.Y, q - 1, out int dx, out int dy); cx += dx; cy += dy; }
                    var p = Field3D.CellPos(new Cell(cx, cy)); float dist = Mathf.Sqrt((p.x - x) * (p.x - x) + (p.z - z) * (p.z - z));
                    float t = Mathf.Max(0f, 1f - dist / 0.95f); wk[q] = t * t; sum += wk[q];
                }
                if (sum < 1e-3f) { nearX[k] = -100; nearY[k] = -100; wk[0] = 1f; sum = 1f; }   // вдали от всех гексов — сплошная трава (иначе нулевые веса давали «расчищено»)
                for (int q = 0; q < 7; q++) weights[k * 7 + q] = wk[q] / Mathf.Max(sum, 1e-4f);
                noise[k] = Noise(x * 1.7f, z * 1.7f) * 0.6f + Noise(x * 5.1f, z * 5.1f) * 0.4f;
                tips[k] = Hash(i * 7 + 3, j * 13 + 1);
            }
            int t2 = 0;
            for (int j = 0; j < nz - 1; j++) for (int i = 0; i < nx - 1; i++)
            {
                int a = j * nx + i, b = a + 1, cc = a + nx, dd = cc + 1;
                tri[t2++] = a; tri[t2++] = cc; tri[t2++] = b; tri[t2++] = b; tri[t2++] = cc; tri[t2++] = dd;
            }
            mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; mesh.vertices = verts; mesh.uv = uv; mesh.triangles = tri; mesh.colors32 = cols;
            var go = new GameObject("GrassCarpet", typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh; var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var mpb = new MaterialPropertyBlock(); mpb.SetTexture("_BaseMap", BuildTexture()); mpb.SetVector("_BaseMap_ST", new Vector4(1f, 1f, 0f, 0f)); mpb.SetColor("_BaseColor", Color.white); mr.SetPropertyBlock(mpb);
            Rebuild();
        }
        static Cell NearestAny(float x, float z)
        {
            Cell best = default; float bd = float.MaxValue;
            for (int y = 0; y < BushGrid.Rows; y++) for (int cx = 0; cx < BushGrid.Cols; cx++) { var p = Field3D.CellPos(new Cell(cx, y)); float d = (p.x - x) * (p.x - x) + (p.z - z) * (p.z - z); if (d < bd) { bd = d; best = new Cell(cx, y); } }
            return best;
        }
        static float Hash(int x, int y) { unchecked { uint h = (uint)(x * 374761393) ^ (uint)(y * 668265263); h = (h ^ (h >> 13)) * 1274126177u; return ((h ^ (h >> 16)) & 0xFFFF) / 65535f; } }
        /// Value-noise, диапазон [-1, 1].
        static float Noise(float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y); float fx = x - ix, fy = y - iy; fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
            return (Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy) - 0.5f) * 2f;
        }
        /// Текстура травы 256²: шум + светлые кончики травинок; повторяется по миру (UV = xz × 0.7).
        static Texture2D BuildTexture()
        {
            const int N = 256; var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float u = x / (float)N * 8f, v = y / (float)N * 8f;
                float n = Noise(u, v) * 0.5f + Noise(u * 3.1f, v * 3.1f) * 0.3f + Noise(u * 9.7f, v * 9.7f) * 0.2f;   // масштабы кратны периоду — шов не виден
                float k = Mathf.Clamp01(0.9f + n * 0.18f);
                bool tip = Hash(x, y) > 0.965f;
                px[y * N + x] = tip ? new Color32(190, 245, 170, 255) : new Color32((byte)(255 * k), (byte)(255 * Mathf.Clamp01(k + 0.03f)), (byte)(255 * k * 0.92f), 255);
            }
            tex.SetPixels32(px); tex.Apply(true, false); return tex;
        }

        public void Dirty() => dirty = true;
        public void Tick() { if (dirty) { Rebuild(); dirty = false; } }

        void Rebuild()
        {
            int n = verts.Length;
            for (int k = 0; k < n; k++)
            {
                // смесь уровней: вне поля — трава (1)
                float m = 0f; int bx = nearX[k], by = nearY[k];
                for (int q = 0; q < 7; q++)
                {
                    float w = weights[k * 7 + q]; if (w <= 0f) continue;
                    int cx = bx, cy = by; if (q > 0) { BushGrid.NeighborDelta(by, q - 1, out int dx, out int dy); cx += dx; cy += dy; }
                    bool inside = cx >= 0 && cx < BushGrid.Cols && cy >= 0 && cy < BushGrid.Rows;
                    m += w * (inside ? level[cy * BushGrid.Cols + cx] : 1f);
                }
                float edge = Mathf.Clamp01((m + noise[k] * 0.12f - 0.35f) / 0.3f); edge = edge * edge * (3f - 2f * edge);   // мягкий склон у края тропы
                float h = H * edge * (1f + noise[k] * 0.12f);
                verts[k].y = h;
            }
            // нормали по разностям высот, свет и цвет — в вершины
            for (int j = 0; j < nz; j++) for (int i = 0; i < nx; i++)
            {
                int k = j * nx + i;
                float hl = verts[j * nx + Mathf.Max(0, i - 1)].y, hr = verts[j * nx + Mathf.Min(nx - 1, i + 1)].y, hd = verts[Mathf.Max(0, j - 1) * nx + i].y, hu = verts[Mathf.Min(nz - 1, j + 1) * nx + i].y;
                var nrm = new Vector3(hl - hr, 2f * Step, hd - hu).normalized; normals[k] = nrm;
                float light = 0.8f + 0.2f * Mathf.Clamp01(Vector3.Dot(nrm, LightDir));
                float hk = verts[k].y / H; float depth = 0.32f + 0.68f * Mathf.Clamp01(hk * hk * 1.3f);   // верх светлый, склон и низ заметно темнее — «срез» травы читается
                float g = 1f + noise[k] * 0.08f;
                float r = 0.44f * g, gg = 0.8f * g, b = 0.4f * g;
                if (tips[k] > 0.93f && hk > 0.6f) { r += 0.12f; gg += 0.14f; b += 0.08f; }
                int ni = nearIdx[k]; if (ni >= 0 && reach[ni]) { r *= 0.72f; gg *= 0.72f; b *= 0.72f; }
                float f = light * depth;
                cols[k] = new Color32((byte)(255 * Mathf.Clamp01(r * f)), (byte)(255 * Mathf.Clamp01(gg * f)), (byte)(255 * Mathf.Clamp01(b * f)), 255);
            }
            mesh.vertices = verts; mesh.normals = normals; mesh.colors32 = cols; mesh.RecalculateBounds();
        }
    }
}
