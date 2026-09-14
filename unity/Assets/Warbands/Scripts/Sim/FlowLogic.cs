using System;
using System.Collections.Generic;

namespace Warbands.Sim
{
    /// Bush Rush «как вода» (автор 14.09): правила «один отряд — один гекс» нет. Отряд остаётся единицей HP, урона и хода в очереди,
    /// но каждый его боец стоит на своём гексе (`Unit.Fighters`), в гексе до Cap бойцов одной стороны. На ходу отряда каждый боец
    /// бежит к вражескому герою своим кратчайшим путём по расчищенному (могут разойтись по разным дорогам), ближник останавливается
    /// у первого встречного врага, дальник — когда кто-то в рейндже; бьют того, кого встретили (герой в приоритете).
    /// У каждого бойца своё HP (FighterHp), урон снимается с ближайших к атакующему; вражеские бойцы не стена — дерутся вперемешку в одном гексе.
    public static class FlowLogic
    {
        public const int Cap = 8;   // бойцов одной стороны в гексе

        public static void InitFighters(BattleState s)
        {
            foreach (var sd in s.Sides) foreach (var q in sd.Squads)
            {
                q.Fighters = new List<Cell>(); q.FighterHp = new List<int>(); int left = q.Hp, hpf = q.Squad.HpPerFighter;
                while (left > 0) { q.Fighters.Add(q.Cell); q.FighterHp.Add(Math.Min(hpf, left)); left -= hpf; }
            }
        }

        /// Сколько своих бойцов стоит в гексе.
        public static int OwnAt(BattleState s, Cell c, int side)
        {
            int n = 0; foreach (var q in s.Sides[side].Squads) if (q.Alive && q.Fighters != null) foreach (var f in q.Fighters) if (f == c) n++;
            return n;
        }
        /// Враг в гексе: герой или отряд, чей боец там стоит.
        public static Unit EnemyAt(BattleState s, Cell c, int side)
        {
            var h = s.Sides[1 - side].Hero; if (h != null && h.Alive && h.Cell == c) return h;
            foreach (var q in s.Sides[1 - side].Squads) if (q.Alive && q.Fighters != null && q.Fighters.Contains(c)) return q;
            return null;
        }
        /// Любой отряд/герой, чей боец стоит в гексе (для отрастания, бонусов, препятствий).
        public static Unit AnyAt(BattleState s, Cell c)
        {
            foreach (var sd in s.Sides)
            {
                if (sd.Hero != null && sd.Hero.Alive && sd.Hero.Cell == c) return sd.Hero;
                foreach (var q in sd.Squads) if (q.Alive && q.Fighters != null && q.Fighters.Contains(c)) return q;
            }
            return null;
        }
        /// Проходим для бойца стороны: внутри, без куста (или в alsoOpen) и препятствия, без врагов, не гекс своего героя.
        public static bool Passable(BattleState s, Cell c, int side, HashSet<Cell> alsoOpen = null)
        {
            if (!BushGrid.Inside(c) || s.Bushes.IsObstacle(c) || (s.Bushes.IsBush(c) && (alsoOpen == null || !alsoOpen.Contains(c)))) return false;
            var eh = s.Sides[1 - side].Hero; if (eh != null && eh.Alive && eh.Cell == c) return false;   // гекс героя — стена; вражеские бойцы — нет: дерёмся вперемешку (автор 14.09)
            var mh = s.Sides[side].Hero; return mh == null || !mh.Alive || mh.Cell != c;
        }
        /// Враг рядом с гексом: герой в приоритете, иначе отряд с наименьшим HP среди соседних.
        public static Unit AdjacentEnemy(BattleState s, int side, Cell at)
        {
            var h = s.Sides[1 - side].Hero; if (h != null && h.Alive && BushGrid.Adjacent(at, h.Cell)) return h;
            Unit best = null; bool bestSame = false;
            foreach (var q in s.Sides[1 - side].Squads)
            {
                if (!q.Alive || q.Fighters == null) continue;
                foreach (var f in q.Fighters)
                {
                    bool same = f == at; if (!same && !BushGrid.Adjacent(at, f)) continue;
                    if (best == null || (same && !bestSame) || (same == bestSame && q.Hp < best.Hp)) { best = q; bestSame = same; }   // в своём гексе — первым делом
                    if (same) break;
                }
            }
            return best;
        }
        /// Ближайший враг в рейндже от гекса (герой в приоритете), null — никого.
        public static Unit EnemyInRange(BattleState s, int side, Cell at, float range)
        {
            var h = s.Sides[1 - side].Hero; if (h != null && h.Alive && BushGrid.Dist(at, h.Cell) <= range + 1e-3f) return h;
            Unit best = null; float bd = float.MaxValue;
            foreach (var q in s.Sides[1 - side].Squads)
            {
                if (!q.Alive || q.Fighters == null) continue;
                foreach (var f in q.Fighters) { float d = BushGrid.Dist(at, f); if (d <= range + 1e-3f && d < bd) { bd = d; best = q; } }
            }
            return best;
        }
        /// Все враги (отряды и герой), у кого хоть один боец/гекс в рейндже (Cabal бьёт всех).
        public static List<Unit> EnemiesInRange(BattleState s, int side, Cell at, float range)
        {
            var l = new List<Unit>(); var h = s.Sides[1 - side].Hero; if (h != null && h.Alive && BushGrid.Dist(at, h.Cell) <= range + 1e-3f) l.Add(h);
            foreach (var q in s.Sides[1 - side].Squads)
            {
                if (!q.Alive || q.Fighters == null) continue;
                foreach (var f in q.Fighters) if (BushGrid.Dist(at, f) <= range + 1e-3f) { l.Add(q); break; }
            }
            return l;
        }
        /// BFS по проходимым от гекса, не дальше maxSteps.
        public static Dictionary<Cell, int> Bfs(BattleState s, Cell from, int side, int maxSteps, out Dictionary<Cell, Cell> prev)
        {
            var dist = new Dictionary<Cell, int>(); prev = new Dictionary<Cell, Cell>(); var q = new Queue<Cell>(); dist[from] = 0; q.Enqueue(from);
            while (q.Count > 0)
            {
                var c = q.Dequeue(); int d = dist[c]; if (d >= maxSteps) continue;
                foreach (var n in BushGrid.Neighbors(c)) { if (dist.ContainsKey(n) || !Passable(s, n, side)) continue; dist[n] = d + 1; prev[n] = c; q.Enqueue(n); }
            }
            return dist;
        }
        /// Передний боец отряда — ближайший к вражескому герою (это и есть `Unit.Cell` для бота и HUD).
        public static Cell Front(BattleState s, Unit u)
        {
            if (u.Fighters == null || u.Fighters.Count == 0) return u.Cell;
            var h = s.Sides[1 - u.Side].Hero; Cell best = u.Fighters[0]; int bd = int.MaxValue;
            foreach (var f in u.Fighters) { int d = h != null ? BushGrid.Steps(f, h.Cell) : 0; if (d < bd) { bd = d; best = f; } }
            return best;
        }
        /// HP бойцов ↔ HP отряда (автор 14.09: у каждого бойца своё HP). Потери: урон снимается с бойцов, ближайших к from (там контакт),
        /// боец с нулём умирает; лечение — сначала раненым, остаток — новыми бойцами рядом со своими. Число бойцов = Count (ceil(Hp/HpPerFighter)).
        public static void Sync(BattleState s, Unit u, Cell? from = null)
        {
            if (u == null || u.IsHero || u.Fighters == null) return;
            if (u.FighterHp == null || u.FighterHp.Count != u.Fighters.Count) { u.FighterHp = new List<int>(); int hpf0 = u.Squad.HpPerFighter; for (int i = 0; i < u.Fighters.Count; i++) u.FighterHp.Add(hpf0); }
            if (!u.Alive) { u.Fighters.Clear(); u.FighterHp.Clear(); return; }
            int hpf = u.Squad.HpPerFighter; var anchor = from ?? (s.Sides[1 - u.Side].Hero != null ? s.Sides[1 - u.Side].Hero.Cell : u.Cell);
            int sum = 0; foreach (var h in u.FighterHp) sum += h;
            int loss = sum - u.Hp;
            while (loss > 0 && u.Fighters.Count > 0)
            {
                int bi = 0, bd = int.MaxValue;   // ближайший к атакующему — первым
                for (int i = 0; i < u.Fighters.Count; i++) { int d = BushGrid.Steps(u.Fighters[i], anchor); if (d < bd) { bd = d; bi = i; } }
                int dmg = Math.Min(u.FighterHp[bi], loss); u.FighterHp[bi] -= dmg; loss -= dmg;
                if (u.FighterHp[bi] <= 0) { u.Fighters.RemoveAt(bi); u.FighterHp.RemoveAt(bi); }
            }
            int gain = -loss;
            if (gain > 0)
            {
                // лечение: раненым по возрастанию HP, остаток — новые бойцы рядом
                for (int pass = 0; pass < 64 && gain > 0; pass++)
                {
                    int wi = -1, wh = hpf;
                    for (int i = 0; i < u.FighterHp.Count; i++) if (u.FighterHp[i] < wh) { wh = u.FighterHp[i]; wi = i; }
                    if (wi < 0) break;
                    int add = Math.Min(hpf - u.FighterHp[wi], gain); u.FighterHp[wi] += add; gain -= add;
                }
                int k = 0;
                while (gain > 0) { var at = u.Fighters.Count > 0 ? u.Fighters[k++ % u.Fighters.Count] : u.Cell; int add = Math.Min(hpf, gain); u.Fighters.Add(at); u.FighterHp.Add(add); gain -= add; }
            }
            // ровно Count бойцов: лишние (несколько неполных) сливаются в ближайшего неполного
            while (u.Fighters.Count > u.Count && u.Fighters.Count > 1)
            {
                int li = 0; for (int i = 1; i < u.FighterHp.Count; i++) if (u.FighterHp[i] < u.FighterHp[li]) li = i;
                int hp = u.FighterHp[li]; u.Fighters.RemoveAt(li); u.FighterHp.RemoveAt(li);
                int ri = 0; for (int i = 1; i < u.FighterHp.Count; i++) if (u.FighterHp[i] < u.FighterHp[ri]) ri = i;
                u.FighterHp[ri] = Math.Min(hpf, u.FighterHp[ri] + hp);
            }
            u.Cell = Front(s, u);
        }
        public static void SyncAll(BattleState s) { foreach (var sd in s.Sides) foreach (var q in sd.Squads) Sync(s, q); }

        /// Кодирование путей бойцов для UI: «sx,sy:x,y;x,y|…» — по бойцу, стоявший на месте — «sx,sy:».
        public static string EncodePaths(List<Cell> starts, List<List<Cell>> paths)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < starts.Count; i++)
            {
                if (i > 0) sb.Append('|'); sb.Append(starts[i].X).Append(',').Append(starts[i].Y).Append(':');
                var p = paths[i]; for (int j = 0; j < p.Count; j++) { if (j > 0) sb.Append(';'); sb.Append(p[j].X).Append(',').Append(p[j].Y); }
            }
            return sb.ToString();
        }
        public static List<(Cell start, List<Cell> path)> DecodePaths(string t)
        {
            var l = new List<(Cell, List<Cell>)>(); if (string.IsNullOrEmpty(t)) return l;
            foreach (var part in t.Split('|'))
            {
                var sp = part.Split(':'); var sc = sp[0].Split(','); var start = new Cell(int.Parse(sc[0]), int.Parse(sc[1]));
                var path = sp.Length > 1 && sp[1].Length > 0 ? BushGrid.Decode(sp[1]) : new List<Cell>();
                l.Add((start, path));
            }
            return l;
        }
    }
}
