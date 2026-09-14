using System;
using System.Collections.Generic;

namespace Warbands.Sim
{
    /// Куда идёт отряд в этот ход: вражеский отряд (ближник — к контакту, дальник — в рейндж), союзник (поддержка) или бонус на клетке.
    public struct Goal
    {
        public Unit Unit; public Cell Cell; public bool IsBonus;
        public bool Valid => Unit != null || IsBonus;
        public Cell Target => Unit != null ? Unit.Cell : Cell;
        public static Goal Of(Unit u) => new Goal { Unit = u, Cell = u != null ? u.Cell : default };
        public static Goal Bonus(Cell c) => new Goal { Cell = c, IsBonus = true };
        public static readonly Goal None = new Goal();
    }

    /// Ветка bush-field (автор 10.09): «хомячки» — у каждого отряда своя цель, он сам бежит к ней по расчищенным клеткам;
    /// игрок и бот только расчищают кусты. Здесь — цель (враг / союзник / бонус), путь, бег по нарисованной линии, отход,
    /// кандидаты расчистки и оценка дорог для бота, генерация препятствий и бонусов.
    public static class BushLogic
    {
        public const int BushCost = 3;   // куст «дороже» шага на столько при планировании (сколько шагов готовы обойти ради одного куста)

        public static Unit OccupantAt(BattleState s, Cell c)
        {
            foreach (var sd in s.Sides)
            {
                foreach (var q in sd.Squads) if (q.Alive && q.Cell == c) return q;
                if (s.Cfg.rushMode && sd.Hero != null && sd.Hero.Alive && sd.Hero.Cell == c) return sd.Hero;   // Bush Rush: герой стоит на поле
            }
            return null;
        }
        /// Живой вражеский герой на поле (Bush Rush), иначе null.
        public static Unit EnemyHero(BattleState s, int side) { var h = s.Sides[1 - side].Hero; return s.Cfg.rushMode && h != null && h.Alive ? h : null; }
        /// Все живые враги на поле: отряды и герой (Bush Rush).
        public static IEnumerable<Unit> Enemies(BattleState s, int side)
        {
            foreach (var e in s.Sides[1 - side].Squads) if (e.Alive) yield return e;
            var h = EnemyHero(s, side); if (h != null) yield return h;
        }
        /// Проходима для стороны: внутри, без куста и препятствия, не занята врагом (свои проходимы, но встать на них нельзя).
        public static bool Passable(BattleState s, Cell c, int side, HashSet<Cell> alsoOpen = null)
        {
            if (!BushGrid.Inside(c) || s.Bushes.IsObstacle(c) || (s.Bushes.IsBush(c) && (alsoOpen == null || !alsoOpen.Contains(c)))) return false;
            var o = OccupantAt(s, c); return o == null || (o.Side == side && !o.IsHero);   // свой герой — тоже стена
        }
        public static bool CanStand(BattleState s, Cell c, Unit actor) { var o = OccupantAt(s, c); return o == null || o == actor; }

        // ---------- BFS по расчищенным ----------

        public static Dictionary<Cell, int> Bfs(BattleState s, Unit actor, out Dictionary<Cell, Cell> prev, int maxSteps = int.MaxValue) => BfsFrom(s, actor.Cell, actor.Side, out prev, maxSteps);
        public static Dictionary<Cell, int> BfsFrom(BattleState s, Cell from, int side, out Dictionary<Cell, Cell> prev, int maxSteps = int.MaxValue, HashSet<Cell> alsoOpen = null)
        {
            var dist = new Dictionary<Cell, int>(); prev = new Dictionary<Cell, Cell>(); var q = new Queue<Cell>();
            dist[from] = 0; q.Enqueue(from);
            while (q.Count > 0)
            {
                var c = q.Dequeue(); int d = dist[c]; if (d >= maxSteps) continue;
                foreach (var n in BushGrid.Neighbors(c))
                {
                    if (dist.ContainsKey(n) || !Passable(s, n, side, alsoOpen)) continue;
                    dist[n] = d + 1; prev[n] = c; q.Enqueue(n);
                }
            }
            return dist;
        }
        /// Подсветка хода (автор 11.09, реф HoMM): радиус хода — гексы в пределах скорости, кусты считаются проходимыми (их можно расчистить),
        /// препятствия и вражеские отряды — стены, свои проходимы (и подсвечены). Показывается с начала хода, до рисования.
        public static List<Cell> Reachable(BattleState s, Unit actor)
        {
            var l = new List<Cell>(); if (actor == null || actor.IsHero) return l;
            int speed = BushStats.Speed(actor.Squad.Id);
            var dist = new Dictionary<Cell, int> { [actor.Cell] = 0 }; var q = new Queue<Cell>(); q.Enqueue(actor.Cell);
            while (q.Count > 0)
            {
                var c = q.Dequeue(); int d = dist[c]; if (d >= speed) continue;
                foreach (var n in BushGrid.Neighbors(c))
                {
                    if (dist.ContainsKey(n) || s.Bushes.IsObstacle(n)) continue;
                    var o = OccupantAt(s, n); if (o != null && o.Side != actor.Side) continue;
                    dist[n] = d + 1; q.Enqueue(n);
                }
            }
            foreach (var kv in dist) l.Add(kv.Key);   // и гексы под своими — сквозь них проходят (автор 11.09)
            return l;
        }
        /// Подсветка удара (автор 11.09, реф Civ 5 — контур): гексы в рейндже с текущей позиции, ближник — соседние; свой гекс включён.
        public static List<Cell> InRange(Unit actor)
        {
            var l = new List<Cell>(); if (actor == null || actor.IsHero) return l;
            float range = BushStats.IsMelee(actor.Squad) ? 1f : BushStats.Range(actor.Squad.Id);
            for (int y = 0; y < BushGrid.Rows; y++) for (int x = 0; x < BushGrid.Cols; x++) { var c = new Cell(x, y); if (BushGrid.Dist(actor.Cell, c) <= range + 1e-3f) l.Add(c); }
            return l;
        }
        public static List<Cell> PathTo(Dictionary<Cell, Cell> prev, Cell from, Cell to)
        {
            var p = new List<Cell>(); var cur = to;
            while (cur != from) { p.Add(cur); if (!prev.TryGetValue(cur, out cur)) return null; }
            p.Reverse(); return p;
        }

        // ---------- цель ----------

        /// Предпочтение по архетипам: ближники охотятся на дальников и поддержку, ассасины — на поддержку, дальники — на ближников.
        public static float Preference(Unit a, Unit e)
        {
            bool eSupport = e.Squad.IsSupport, eRanged = e.Squad.Reach == Reach.Ranged && !eSupport, eMelee = e.Squad.Reach != Reach.Ranged;
            switch (a.Squad.Reach)
            {
                case Reach.Assassin: return eSupport ? 4f : eRanged ? 3f : 1f;
                case Reach.Melee: return eRanged || eSupport ? 3f : 1f;
                default: return eMelee ? 3f : 2f;
            }
        }
        /// Цель хода: враг (предпочтение × 2 − дистанция × 0.7, добиваем слабых), для поддержки — союзник; бонус на поле конкурирует с меньшим весом
        /// (×2 — 2.5 − дистанция × 0.7; +HP — столько же, если ранен, иначе 0.5).
        public static Goal PickGoal(BattleState s, Unit actor, Unit except = null)
        {
            if (actor == null || actor.IsHero) return Goal.None;
            if (s.Cfg.rushMode)
            {
                // Bush Rush (автор 14.09): выбора цели нет — все бегут к вражескому герою; усилитель — к союзнику, которому нужен бафф
                if (actor.Squad.IsSupport) { var ally = PickAlly(s, actor); if (ally != null) return Goal.Of(ally); }
                var eh = EnemyHero(s, actor.Side); return eh != null ? Goal.Of(eh) : Goal.None;
            }
            // автор 10.09: считать в момент бега — что реально достижимо по расчищенному в этот ход, весит больше (+4)
            var reach = Bfs(s, actor, out _, BushStats.Speed(actor.Squad.Id));
            Unit bestU = null; float bs = float.NegativeInfinity;
            if (actor.Squad.IsSupport) { bestU = PickAlly(s, actor); bs = bestU != null ? 3f * 2f - BushGrid.Dist(actor.Cell, bestU.Cell) * 0.7f + (CanReach(s, actor, reach, Goal.Of(bestU)) ? 4f : 0f) : float.NegativeInfinity; }
            else foreach (var e in s.Sides[1 - actor.Side].Squads)
            {
                if (!e.Alive || e == except) continue;
                float sc = Preference(actor, e) * 2f - BushGrid.Dist(actor.Cell, e.Cell) * 0.7f - e.AliveRatio * 0.5f + (CanReach(s, actor, reach, Goal.Of(e)) ? 4f : 0f);
                if (sc > bs) { bs = sc; bestU = e; }
            }
            // бонусы — вес ниже атаки (автор 10.09)
            Cell bestB = default; bool anyB = false; float bb = float.NegativeInfinity;
            if (s.Bushes != null) for (int y = 0; y < BushGrid.Rows; y++) for (int x = 0; x < BushGrid.Cols; x++)
            {
                var c = new Cell(x, y); var k = s.Bushes.BonusAt(c); if (k == BonusKind.None || s.Bushes.IsBush(c)) continue;   // под травой бонус спрятан (автор 11.09) — целью не бывает
                float w = k == BonusKind.Heal ? (actor.Hp < actor.MaxHp ? 2.5f : 0.5f) : 2.5f;
                float sc = w - BushGrid.Dist(actor.Cell, c) * 0.7f + (reach.ContainsKey(c) && CanStand(s, c, actor) ? 4f : 0f);
                if (sc > bb) { bb = sc; bestB = c; anyB = true; }
            }
            if (anyB && bb > bs) return Goal.Bonus(bestB);
            return Goal.Of(bestU);
        }
        /// Достижима ли цель в этот ход по расчищенному: ближник — клетка рядом с врагом, дальник/поддержка — клетка в рейндже, бонус — сама клетка.
        static bool CanReach(BattleState s, Unit actor, Dictionary<Cell, int> reach, Goal g)
        {
            if (!g.Valid) return false;
            var ok = GoalOf(s, actor, g);
            foreach (var kv in reach) if (ok(kv.Key)) return true;
            return false;
        }
        /// Вражеская/союзная цель без бонусов.
        public static Unit PickTarget(BattleState s, Unit actor, Unit except = null)
        {
            if (actor == null || actor.IsHero) return null;
            if (actor.Squad.IsSupport) return PickAlly(s, actor);
            Unit best = null; float bs = float.NegativeInfinity;
            foreach (var e in s.Sides[1 - actor.Side].Squads)
            {
                if (!e.Alive || e == except) continue;
                float sc = Preference(actor, e) * 2f - BushGrid.Dist(actor.Cell, e.Cell) * 0.7f - e.AliveRatio * 0.5f;
                if (sc > bs) { bs = sc; best = e; }
            }
            return best;
        }
        /// Кому нужна поддержка: раненый (Heal — живые, Restore — нежить) с наименьшей долей HP; Empower/Link — сильнейший без статуса; иначе передний союзник.
        public static Unit PickAlly(BattleState s, Unit actor)
        {
            var def = actor.Squad; Unit best = null; float bk = float.MaxValue;
            foreach (var q in s.Sides[actor.Side].Squads)
            {
                if (!q.Alive || q == actor) continue;
                bool ok; float key;
                switch (def.Support)
                {
                    case SupportKind.Heal: ok = q.Tag != UnitTag.Undead && q.Hp < q.MaxHp; key = q.AliveRatio; break;
                    case SupportKind.Restore: ok = q.Tag == UnitTag.Undead && q.Hp < q.MaxHp; key = q.AliveRatio; break;
                    case SupportKind.Empower: ok = !q.Has(StatusKind.Empowered); key = 1f - q.Squad.Output / 200f; break;
                    case SupportKind.Link: ok = !q.Has(StatusKind.Linked); key = 1f - q.Squad.Output / 200f; break;
                    default: ok = false; key = 0f; break;
                }
                if (ok && key < bk) { bk = key; best = q; }
            }
            if (best != null) return best;
            float nd = float.MaxValue;
            foreach (var q in s.Sides[actor.Side].Squads)
            {
                if (!q.Alive || q == actor) continue;
                foreach (var e in s.Sides[1 - actor.Side].Squads) if (e.Alive) { float d = BushGrid.Dist(q.Cell, e.Cell); if (d < nd) { nd = d; best = q; } }
            }
            return best;
        }

        // ---------- автоход ----------

        /// Враг, соседний с клеткой: цель в приоритете, иначе с меньшим HP.
        public static Unit AdjacentEnemy(BattleState s, Unit actor, Cell at, Unit target)
        {
            Unit best = null; int bh = int.MaxValue;
            foreach (var e in Enemies(s, actor.Side))
            {
                if (!BushGrid.Adjacent(at, e.Cell)) continue;
                if (e == target || e.IsHero) return e;   // цель хода и герой (Bush Rush) — в приоритете
                if (e.Hp < bh) { bh = e.Hp; best = e; }
            }
            return best;
        }
        static float ThreatDist(BattleState s, Unit actor, Cell c)
        {
            float d = 99f;
            foreach (var e in s.Sides[1 - actor.Side].Squads) if (e.Alive && BushStats.IsMelee(e.Squad)) d = Math.Min(d, BushGrid.Dist(c, e.Cell));
            return d;
        }
        /// Обрезать путь скоростью и занятым концом; ближник — до первого контакта на свободной клетке.
        static List<Cell> Walk(BattleState s, Unit actor, List<Cell> path, int speed, bool melee, Unit target, out Unit contact)
        {
            contact = null; var res = new List<Cell>();
            if (path == null) return res;
            path = WithBonusDetours(s, actor, path, speed, true);   // бонус рядом с дорогой — крюк (автор 11.09)
            if (path.Count > speed) path = path.GetRange(0, speed);
            while (path.Count > 0 && !CanStand(s, path[path.Count - 1], actor)) path.RemoveAt(path.Count - 1);
            if (!melee)
            {
                if (s.Cfg.rushMode && !actor.Squad.IsSupport)
                {
                    // Bush Rush: дальник бежит вперёд, пока кто-то из врагов не окажется в рейндже — там и стреляет
                    float range = BushStats.Range(actor.Squad.Id);
                    for (int i = 0; i < path.Count; i++) if (CanStand(s, path[i], actor) && AnyEnemyInRange(s, actor, path[i], range)) { res.AddRange(path.GetRange(0, i + 1)); return res; }
                }
                res.AddRange(path); return res;
            }
            int stopAt = -1;
            for (int i = 0; i < path.Count; i++)
            {
                if (!CanStand(s, path[i], actor)) continue;
                var e = AdjacentEnemy(s, actor, path[i], target);
                if (e != null) { contact = e; stopAt = i; break; }
            }
            if (stopAt < 0) { stopAt = path.Count - 1; if (stopAt >= 0) contact = AdjacentEnemy(s, actor, path[stopAt], target); }
            // автор 11.09: бонус дальше по пути важнее удара по дороге — идём до последнего стоящего бонуса, бьём того, кто окажется рядом там
            if (stopAt >= 0 && s.Bushes != null)
            {
                int last = -1;
                for (int i = path.Count - 1; i > stopAt; i--) if (WorthBonus(s, actor, path[i]) && CanStand(s, path[i], actor)) { last = i; break; }
                if (last >= 0) { stopAt = last; contact = AdjacentEnemy(s, actor, path[stopAt], target); }
            }
            if (stopAt >= 0) res.AddRange(path.GetRange(0, stopAt + 1));
            return res;
        }
        /// Крюк за бонусом (автор 11.09: «прошёл по соседнему гексу и не взял»): бонус, соседний с двумя подряд гексами пути (или со стартом и первым),
        /// вставляется между ними (+1 шаг; при allowOvershoot конец пути может обрезаться скоростью — бонус важнее); соседний только с одним гексом —
        /// заход и возврат (+2), если есть запас скорости; в конце пути — просто дойти. Один бонус — один раз.
        public static List<Cell> WithBonusDetours(BattleState s, Unit actor, List<Cell> path, int speed, bool allowOvershoot)
        {
            if (s.Bushes == null || path == null || actor == null) return path;
            var res = new List<Cell>(); var used = new HashSet<Cell>(); var prev = actor.Cell;
            for (int i = 0; i <= path.Count; i++)
            {
                bool hasNext = i < path.Count; Cell next = hasNext ? path[i] : default;
                foreach (var x in BushGrid.Neighbors(prev))
                {
                    if (used.Contains(x) || (hasNext && x == next) || path.Contains(x)) continue;
                    if (!WorthBonus(s, actor, x) || !Passable(s, x, actor.Side) || !CanStand(s, x, actor)) continue;
                    int left = path.Count - i;   // сколько гексов пути ещё впереди
                    if (hasNext && BushGrid.Adjacent(x, next))
                    {
                        if (!allowOvershoot && res.Count + left + 1 > speed) continue;
                        res.Add(x); used.Add(x); prev = x; break;
                    }
                    if (!hasNext) { if (allowOvershoot || res.Count + 1 <= speed) { res.Add(x); used.Add(x); prev = x; } break; }
                    if (res.Count + left + 2 <= speed) { res.Add(x); res.Add(prev); used.Add(x); break; }   // тупиковый сосед: зашёл и вернулся
                }
                if (hasNext) { res.Add(next); prev = next; }
            }
            return res;
        }
        /// Стоит ли бонус крюка: ×2 — всегда, +HP — только раненому (полному он ничего не даёт).
        public static bool WorthBonus(BattleState s, Unit actor, Cell c)
        {
            var k = s.Bushes != null ? s.Bushes.BonusAt(c) : BonusKind.None;
            return k == BonusKind.Double || (k == BonusKind.Heal && actor.Hp < actor.MaxHp);
        }

        public static bool AnyEnemyInRange(BattleState s, Unit actor, Cell at, float range) { foreach (var e in Enemies(s, actor.Side)) if (BushGrid.Dist(at, e.Cell) <= range + 1e-3f) return true; return false; }
        /// План хода к цели: путь по расчищенным клеткам (без стартовой), ограниченный скоростью; reached — цель достигнута
        /// (ближник в контакте, дальник/поддержка в рейндже, бонус подобран).
        public static List<Cell> PlanMove(BattleState s, Unit actor, Goal goal, out Unit contact, out bool reached)
        {
            contact = null; reached = false; var res = new List<Cell>();
            if (actor == null || actor.IsHero) return res;
            int speed = BushStats.Speed(actor.Squad.Id); bool melee = BushStats.IsMelee(actor.Squad);
            var target = goal.Unit;
            if (s.Cfg.rushMode && !actor.Squad.IsSupport)
            {
                // Bush Rush (автор 14.09): вперёд к вражескому герою по ближайшему возможному пути — достижимый гекс с наименьшим числом шагов до героя
                // (при равенстве — ближе по дороге); ближник бьёт первого встречного, дальник — останавливается, когда кто-то в рейндже
                var hero = EnemyHero(s, actor.Side); if (hero == null) return res;
                if (melee) { contact = AdjacentEnemy(s, actor, actor.Cell, hero); if (contact != null) { reached = true; return res; } }
                else if (AnyEnemyInRange(s, actor, actor.Cell, BushStats.Range(actor.Squad.Id))) { reached = true; return res; }
                var dist = Bfs(s, actor, out var prev);
                Cell best = actor.Cell; int bd = BushGrid.Steps(actor.Cell, hero.Cell), bl = 0;
                foreach (var kv in dist)
                {
                    if (!CanStand(s, kv.Key, actor)) continue;
                    int d = BushGrid.Steps(kv.Key, hero.Cell);
                    if (d < bd || (d == bd && kv.Value < bl)) { bd = d; bl = kv.Value; best = kv.Key; }
                }
                if (best == actor.Cell) return res;
                res = Walk(s, actor, PathTo(prev, actor.Cell, best), speed, melee, hero, out contact);
                var endCell = res.Count > 0 ? res[res.Count - 1] : actor.Cell;
                reached = melee ? contact != null : AnyEnemyInRange(s, actor, endCell, BushStats.Range(actor.Squad.Id));
                return res;
            }
            if (melee)
            {
                contact = AdjacentEnemy(s, actor, actor.Cell, target);
                if (contact != null) { reached = true; return res; }   // уже в контакте — бьём с места
                if (!goal.Valid) return res;
                var dist = Bfs(s, actor, out var prev);
                var gc = goal.Target;
                Cell best = actor.Cell; float d0 = BushGrid.Dist(actor.Cell, gc), bd = d0; int bl = 0;
                foreach (var kv in dist)
                {
                    if (!CanStand(s, kv.Key, actor)) continue;
                    float d = BushGrid.Dist(kv.Key, gc);
                    if (d < bd - 1e-3f || (Math.Abs(d - bd) < 1e-3f && kv.Value < bl)) { bd = d; bl = kv.Value; best = kv.Key; }
                }
                if (best == actor.Cell || d0 - bd < 0.4f) return res;   // не ёрзать ради мелочи; на гексах шаг к цели даёт 0.5–1 ширины (11.09: было 0.75 — часто держало на месте)
                res = Walk(s, actor, PathTo(prev, actor.Cell, best), speed, true, target, out contact);
                var end = res.Count > 0 ? res[res.Count - 1] : actor.Cell;
                reached = contact != null || (goal.IsBonus && end == gc);
                return res;
            }
            // дальник / поддержка: клетка в пределах скорости по оценке — в рейндже от цели (или на бонусе), подальше от ближников
            float range = BushStats.Range(actor.Squad.Id); bool support = actor.Squad.IsSupport;
            var reach = Bfs(s, actor, out var prev2, speed);
            Cell bestC = actor.Cell; float bs = float.NegativeInfinity;
            foreach (var kv in reach)
            {
                var c = kv.Key; if (!CanStand(s, c, actor)) continue;
                float sc = 0f;
                if (goal.IsBonus) { float d = BushGrid.Dist(c, goal.Cell); sc += c == goal.Cell ? 100f : -d * 10f; }
                else if (target != null)
                {
                    float d = BushGrid.Dist(c, target.Cell);
                    bool inRange = d <= range + 1e-3f && (!support || d >= 1f);
                    sc += inRange ? 100f - d * 2f : -d * 10f;   // в рейндже, но у дальней его кромки
                }
                float threat = ThreatDist(s, actor, c);
                sc += Math.Min(threat, range + 1f) * 8f;
                if (threat <= 1f) sc -= 60f; else if (threat <= 2f) sc -= 25f;   // ближник вплотную — уходим
                sc -= kv.Value * 1f;
                if (sc > bs) { bs = sc; bestC = c; }
            }
            if (goal.IsBonus) reached = bestC == goal.Cell;
            else if (target != null) { float d = BushGrid.Dist(bestC, target.Cell); reached = d <= range + 1e-3f && (!support || d >= 1f); }
            if (bestC == actor.Cell) return WithBonusDetours(s, actor, res, speed, false);
            return WithBonusDetours(s, actor, PathTo(prev2, actor.Cell, bestC) ?? res, speed, false);   // крюк за бонусом, если есть запас скорости
        }

        /// Ход с бонусом (автор 10.09): если в досягаемости есть и бонус, и удар после него — сначала бонус, потом удар одним пробегом;
        /// если после бонуса ударить некого — всё равно идём за бонусом (автор 11.09: «сначала всегда бонус»); +HP полному не нужен — не крюк.
        public static List<Cell> PlanMoveBonusFirst(BattleState s, Unit actor, Goal goal, out Unit contact, out bool reached)
        {
            contact = null; reached = false;
            if (actor == null || actor.IsHero || s.Bushes == null) return PlanMove(s, actor, goal, out contact, out reached);
            int speed = BushStats.Speed(actor.Squad.Id); bool melee = BushStats.IsMelee(actor.Squad); float range = BushStats.Range(actor.Squad.Id);
            if (melee && AdjacentEnemy(s, actor, actor.Cell, goal.Unit) != null) return PlanMove(s, actor, goal, out contact, out reached);   // в контакте — бьём с места
            var reach = Bfs(s, actor, out var prev, speed);
            Unit enemyTarget = goal.Unit != null && goal.Unit.Side != actor.Side ? goal.Unit : null;
            List<Cell> bestLeg1 = null, bestLeg2 = null; Unit bestContact = null; float bestScore = float.NegativeInfinity;
            foreach (var kv in reach)
            {
                var b = kv.Key; if (!WorthBonus(s, actor, b) || !CanStand(s, b, actor)) continue;
                int d1 = kv.Value, left = speed - d1;
                var leg1 = d1 == 0 ? new List<Cell>() : PathTo(prev, actor.Cell, b); if (leg1 == null) continue;
                List<Cell> leg2 = null; Unit c2 = null;
                if (left > 0)
                {
                    var r2 = BfsFrom(s, b, actor.Side, out var prev2, left);
                    if (melee)
                    {
                        int bd = int.MaxValue;
                        foreach (var k2 in r2)
                        {
                            if (!CanStand(s, k2.Key, actor)) continue;
                            var e = AdjacentEnemy(s, actor, k2.Key, enemyTarget); if (e == null) continue;
                            int pri = enemyTarget != null && e == enemyTarget ? 0 : 100;
                            if (k2.Value + pri < bd) { bd = k2.Value + pri; leg2 = k2.Key == b ? new List<Cell>() : PathTo(prev2, b, k2.Key); c2 = e; }
                        }
                    }
                    else if (!actor.Squad.IsSupport)
                    {
                        float bs2 = float.NegativeInfinity;
                        foreach (var k2 in r2)
                        {
                            if (!CanStand(s, k2.Key, actor)) continue;
                            Unit shot = null; float bd2 = float.MaxValue;
                            foreach (var e in Enemies(s, actor.Side)) { float d = BushGrid.Dist(k2.Key, e.Cell); if (d <= range + 1e-3f && (enemyTarget == null || e == enemyTarget || shot == null) && d < bd2) { bd2 = d; shot = e; } }
                            if (shot == null) continue;
                            float sc = 100f - bd2 * 2f + Math.Min(ThreatDist(s, actor, k2.Key), range + 1f) * 8f - k2.Value;
                            if (sc > bs2) { bs2 = sc; leg2 = k2.Key == b ? new List<Cell>() : PathTo(prev2, b, k2.Key); c2 = shot; }
                        }
                    }
                }
                float score = (leg2 != null ? 1000f : 0f) - d1 - (leg2 != null ? leg2.Count : 0);
                if (score > bestScore) { bestScore = score; bestLeg1 = leg1; bestLeg2 = leg2; bestContact = c2; }
            }
            if (bestLeg1 == null) return PlanMove(s, actor, goal, out contact, out reached);
            if (bestLeg2 == null)
            {
                // автор 11.09: «сначала всегда взять бонус» — даже если удар достижим, а бонус с ударом в один пробег не совместить, идём за бонусом
                reached = true; return Walk(s, actor, bestLeg1, speed, melee, enemyTarget, out contact);
            }
            var full = new List<Cell>(bestLeg1); full.AddRange(bestLeg2);
            reached = true;
            var res = Walk(s, actor, full, speed, melee, enemyTarget, out contact);
            if (!melee) contact = null;
            return res;
        }

        /// Бег по нарисованной линии (автор 10.09): когда своей цели не достать, отряд бежит по клеткам линии (в порядке рисования)
        /// максимально далеко — как если бы рисование задавало траекторию. Подходим к первой достижимой клетке линии, дальше — по соседним.
        public static List<Cell> FollowStroke(BattleState s, Unit actor, IList<Cell> stroke, out Unit contact)
        {
            contact = null; var res = new List<Cell>();
            if (actor == null || actor.IsHero || stroke == null || stroke.Count == 0) return res;
            int speed = BushStats.Speed(actor.Squad.Id); bool melee = BushStats.IsMelee(actor.Squad);
            var dist = Bfs(s, actor, out var prev, speed);
            int i0 = -1; int bd = int.MaxValue;
            for (int i = 0; i < stroke.Count; i++) if (dist.TryGetValue(stroke[i], out int d) && d < bd) { bd = d; i0 = i; }
            if (i0 < 0) return res;
            var path = stroke[i0] == actor.Cell ? new List<Cell>() : PathTo(prev, actor.Cell, stroke[i0]) ?? new List<Cell>();
            var last = stroke[i0];
            for (int i = i0 + 1; i < stroke.Count && path.Count < speed; i++)
            {
                var c = stroke[i]; if (c == last || !Passable(s, c, actor.Side)) continue;
                if (BushGrid.Adjacent(last, c)) { path.Add(c); last = c; continue; }
                // пропуск/диагональ в линии — мостим коротким путём по расчищенному в пределах остатка скорости
                var r = BfsFrom(s, last, actor.Side, out var pv, speed - path.Count);
                if (!r.ContainsKey(c)) continue;   // недостижима — пропускаем клетку
                var seg = PathTo(pv, last, c); if (seg == null) continue;
                path.AddRange(seg); last = c;
            }
            return Walk(s, actor, path, speed, melee, null, out contact);
        }

        /// Остаток скорости после линии: если рядом с концом пути есть бонус — дотянуться до него (автор 11.09: «бонусы не берутся»).
        public static List<Cell> ExtendToBonus(BattleState s, Unit actor, List<Cell> path, out Unit contact)
        {
            contact = null; if (actor == null || actor.IsHero || s.Bushes == null) return path;
            int speed = BushStats.Speed(actor.Squad.Id); int left = speed - path.Count; if (left <= 0) return path;
            var end = path.Count > 0 ? path[path.Count - 1] : actor.Cell;
            var r = BfsFrom(s, end, actor.Side, out var prev, left);
            Cell? best = null; int bd = int.MaxValue;
            foreach (var kv in r) if (kv.Value > 0 && WorthBonus(s, actor, kv.Key) && !path.Contains(kv.Key) && CanStand(s, kv.Key, actor) && kv.Value < bd) { bd = kv.Value; best = kv.Key; }   // бонус, уже лежащий на пути, не считаем — он и так будет взят
            if (!best.HasValue) return path;
            var seg = PathTo(prev, end, best.Value); if (seg == null) return path;
            var full = new List<Cell>(path); full.AddRange(seg);
            return Walk(s, actor, full, speed, BushStats.IsMelee(actor.Squad), null, out contact);
        }

        /// Отход после удара (автор 10.09): остаток скорости тратим на клетку подальше от врагов — чтобы не получить следующий удар.
        public static List<Cell> PlanRetreat(BattleState s, Unit actor, Unit target, int stepsLeft)
        {
            var res = new List<Cell>();
            if (actor == null || actor.IsHero || stepsLeft <= 0) return res;
            bool melee = BushStats.IsMelee(actor.Squad); float range = BushStats.Range(actor.Squad.Id);
            float Score(Cell c)
            {
                float near = 99f;
                foreach (var e in s.Sides[1 - actor.Side].Squads) if (e.Alive) near = Math.Min(near, BushGrid.Dist(c, e.Cell));
                if (melee) return Math.Min(near, 4f) * 10f;
                float threat = ThreatDist(s, actor, c);
                float sc = Math.Min(threat, range + 1f) * 8f;
                if (threat <= 1f) sc -= 60f; else if (threat <= 2f) sc -= 25f;
                if (target != null && target.Side != actor.Side && BushGrid.Dist(c, target.Cell) <= range + 1e-3f) sc += 20f;
                return sc;
            }
            var reach = Bfs(s, actor, out var prev, stepsLeft);
            float cur = Score(actor.Cell); Cell best = actor.Cell; float bs = cur; int bl = 0;
            foreach (var kv in reach)
            {
                if (kv.Key == actor.Cell || !CanStand(s, kv.Key, actor)) continue;
                float sc = Score(kv.Key) - kv.Value * 0.5f;
                if (sc > bs + 1e-3f || (Math.Abs(sc - bs) < 1e-3f && kv.Value < bl)) { bs = sc; best = kv.Key; bl = kv.Value; }
            }
            if (best == actor.Cell || bs < cur + 4f) return WithBonusDetours(s, actor, res, stepsLeft, false);   // ради мелочи не бегаем (но бонус рядом — возьмём)
            return WithBonusDetours(s, actor, PathTo(prev, actor.Cell, best) ?? res, stepsLeft, false);
        }

        // ---------- расчистка: планирование «через кусты» ----------

        /// Дешёвый путь от клетки к цели (goal — предикат клетки), кусты дороже шага на BushCost, враги и препятствия — стены.
        public static List<Cell> PathThroughBushes(BattleState s, int side, Cell from, Func<Cell, bool> goal, out int cost)
        {
            cost = int.MaxValue;
            var dist = new Dictionary<Cell, int>(); var prev = new Dictionary<Cell, Cell>();
            var buckets = new SortedDictionary<int, Queue<Cell>>();
            void Push(Cell c, int d) { if (!buckets.TryGetValue(d, out var q)) buckets[d] = q = new Queue<Cell>(); q.Enqueue(c); }
            dist[from] = 0; Push(from, 0);
            Cell? found = null;
            while (buckets.Count > 0 && found == null)
            {
                var e = buckets.GetEnumerator(); e.MoveNext(); var kv = e.Current;
                var q = kv.Value; var c = q.Dequeue(); if (q.Count == 0) buckets.Remove(kv.Key);
                if (dist[c] != kv.Key) continue;
                if (c != from && goal(c)) { found = c; break; }
                foreach (var n in BushGrid.Neighbors(c))
                {
                    if (s.Bushes.IsObstacle(n)) continue;
                    var o = OccupantAt(s, n); if (o != null && o.Side != side) continue;
                    int nd = dist[c] + 1 + (s.Bushes.IsBush(n) ? BushCost : 0);
                    if (dist.TryGetValue(n, out int old) && old <= nd) continue;
                    dist[n] = nd; prev[n] = c; Push(n, nd);
                }
            }
            if (found == null) return null;
            cost = dist[found.Value];
            return PathTo(prev, from, found.Value);
        }

        /// Куда хочет попасть отряд: ближник — к клетке рядом с врагом, дальник/поддержка — в рейндж, к бонусу — на его клетку.
        public static Func<Cell, bool> GoalOf(BattleState s, Unit actor, Goal g)
        {
            if (!g.Valid) return c => false;
            if (g.IsBonus) return c => c == g.Cell;
            var target = g.Unit;
            if (BushStats.IsMelee(actor.Squad)) return c => BushGrid.Adjacent(c, target.Cell) && CanStand(s, c, actor);
            float range = BushStats.Range(actor.Squad.Id); bool support = actor.Squad.IsSupport;
            return c => { float d = BushGrid.Dist(c, target.Cell); return d <= range + 1e-3f && (!support || d >= 1f) && CanStand(s, c, actor); };
        }

        /// Первые budget кустов на дешёвом пути отряда к цели (что расчистить ради него).
        public static List<Cell> ClearsFor(BattleState s, Unit unit, Goal g, int budget)
        {
            var res = new List<Cell>();
            if (unit == null || !g.Valid || budget <= 0) return res;
            var path = PathThroughBushes(s, unit.Side, unit.Cell, GoalOf(s, unit, g), out _);
            if (path == null) return res;
            foreach (var c in path) { if (s.Bushes.IsBush(c)) res.Add(c); if (res.Count >= budget) break; }
            return res;
        }

        /// Кандидаты расчистки для бота: ничего; дорога актору к цели и ко второй цели; дорога каждому союзному ближнику к его цели.
        public static List<Command> ClearCandidates(BattleState s, Unit actor)
        {
            var l = new List<Command>(); if (actor == null || actor.IsHero) return l;
            int budget = s.Cfg.bushClearBudget > 0 ? s.Cfg.bushClearBudget : int.MaxValue; var seen = new HashSet<string>();
            void Add(List<Cell> cells) { var key = BushGrid.Encode(cells); if (seen.Add(key)) l.Add(Command.Clear(actor.Ref, cells)); }
            Add(new List<Cell>());
            var g1 = PickGoal(s, actor); Add(ClearsFor(s, actor, g1, budget));
            if (g1.Unit != null && !actor.Squad.IsSupport) { var g2 = PickGoal(s, actor, g1.Unit); Add(ClearsFor(s, actor, g2, budget)); }
            if (g1.IsBonus) Add(ClearsFor(s, actor, Goal.Of(PickTarget(s, actor)), budget));
            foreach (var ally in s.Sides[actor.Side].Squads)
            {
                if (!ally.Alive || ally == actor || !BushStats.IsMelee(ally.Squad)) continue;
                Add(ClearsFor(s, ally, PickGoal(s, ally), budget));
            }
            return l;
        }

        /// Оценка дорог стороны: сумма дешёвых стоимостей пути каждого отряда к своей цели (меньше — лучше).
        public static float RoadCost(BattleState s, int side)
        {
            float total = 0f;
            foreach (var u in s.Sides[side].Squads)
            {
                if (!u.Alive) continue;
                var g = PickGoal(s, u); if (!g.Valid) continue;
                var goal = GoalOf(s, u, g);
                if (goal(u.Cell)) continue;
                PathThroughBushes(s, side, u.Cell, goal, out int cost);
                total += cost == int.MaxValue ? 60f : Math.Min(cost, 60);
            }
            return total;
        }

        // ---------- генерация поля (автор 10.09): препятствия как в референсе, бонусы ----------

        /// Препятствия — одиночные блоки и стенки 2–3 клетки в строках 2..9; поле должно остаться связным (все отряды достижимы друг для друга без кустов).
        public static void Generate(BattleState s)
        {
            var b = s.Bushes; if (b == null) return;
            int nObs = s.Cfg.bushObstacles, nBon = s.Cfg.bushBonuses;
            var units = new List<Cell>(); foreach (var sd in s.Sides) { foreach (var q in sd.Squads) if (q.Alive) units.Add(q.Cell); if (s.Cfg.rushMode && sd.Hero != null) units.Add(sd.Hero.Cell); }
            for (int attempt = 0; attempt < 40 && nObs > 0; attempt++)
            {
                var obs = new HashSet<Cell>();
                int walls = Math.Min(2, nObs), singles = nObs - walls;
                bool ok = true;
                for (int i = 0; i < singles + walls && ok; i++)
                {
                    bool wall = i >= singles; int len = wall ? 2 + s.Rng.Range(0, 2) : 1; bool horiz = s.Rng.Range(0, 2) == 0;
                    bool placed = false;
                    for (int tries = 0; tries < 20 && !placed; tries++)
                    {
                        int x = s.Rng.Range(0, BushGrid.Cols), y = 2 + s.Rng.Range(0, BushGrid.Rows - 4);
                        var cells = new List<Cell>(); bool fits = true;
                        for (int k = 0; k < len && fits; k++)
                        {
                            var c = new Cell(x + (horiz ? k : 0), y + (horiz ? 0 : k));
                            if (!BushGrid.Inside(c) || c.Y < 2 || c.Y > BushGrid.Rows - 3 || obs.Contains(c) || units.Contains(c)) { fits = false; break; }
                            foreach (var n in BushGrid.Neighbors(c)) if (obs.Contains(n)) { fits = false; break; }   // не слипаются с другими
                            if (fits) cells.Add(c);
                        }
                        if (!fits) continue;
                        foreach (var c in cells) obs.Add(c); placed = true;
                    }
                    if (!placed) ok = false;
                }
                if (!ok || !Connected(units, obs)) continue;
                foreach (var c in obs) b.SetObstacle(c);
                break;
            }
            for (int i = 0; i < nBon; i++) SpawnBonus(s, out _, out _);
        }
        /// Все отряды достижимы друг для друга по клеткам без препятствий (кусты не считаем).
        static bool Connected(List<Cell> units, HashSet<Cell> obs)
        {
            if (units.Count == 0) return true;
            var seen = new HashSet<Cell> { units[0] }; var q = new Queue<Cell>(); q.Enqueue(units[0]);
            while (q.Count > 0) { var c = q.Dequeue(); foreach (var n in BushGrid.Neighbors(c)) if (!seen.Contains(n) && !obs.Contains(n)) { seen.Add(n); q.Enqueue(n); } }
            foreach (var u in units) if (!seen.Contains(u)) return false;
            return true;
        }
        /// Новый бонус на случайной свободной клетке строк 2..9 (не препятствие, не отряд, не бонус). false — мест нет или бонусов уже bushBonusMax.
        public static bool SpawnBonus(BattleState s, out Cell cell, out BonusKind kind)
        {
            cell = default; kind = BonusKind.None; var b = s.Bushes; if (b == null) return false;
            if (b.BonusCount >= s.Cfg.bushBonusMax) return false;
            var free = new List<Cell>();
            for (int y = 2; y <= BushGrid.Rows - 3; y++) for (int x = 0; x < BushGrid.Cols; x++) { var c = new Cell(x, y); if (!b.IsObstacle(c) && b.BonusAt(c) == BonusKind.None && OccupantAt(s, c) == null) free.Add(c); }
            if (free.Count == 0) return false;
            cell = free[s.Rng.Range(0, free.Count)]; kind = s.Round >= s.Cfg.bushDoubleFromRound && s.Rng.Range(0, 2) == 0 ? BonusKind.Double : BonusKind.Heal;   // ×2 — только после 4-го раунда (автор 11.09)
            b.SetBonus(cell, kind);
            return true;
        }
    }
}
