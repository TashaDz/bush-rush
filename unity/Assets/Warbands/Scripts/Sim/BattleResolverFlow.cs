using System;
using System.Collections.Generic;

namespace Warbands.Sim
{
    /// Ход отряда «как вода» (Bush Rush, автор 14.09): после расчистки каждый боец бежит своим путём к вражескому герою,
    /// потом все, кто в одном гексе или рядом с врагом (дальник — в рейндже), бьют — урон отряда делится по бойцам и суммируется по целям;
    /// вражеские бойцы не преграда: ближники врезаются в толпу и дерутся вперемешку (автор 14.09).
    public static partial class BattleResolver
    {
        static void DoFlowTurn(BattleState s, Unit actor, List<CombatEvent> evs)
        {
            var def = actor.Squad; int side = actor.Side;
            var hero = BushLogic.EnemyHero(s, side); if (hero == null) return;
            int speed = BushStats.Speed(def.Id); bool melee = BushStats.IsMelee(def); float range = BushStats.Range(def.Id);
            FlowLogic.Sync(s, actor);
            // занятость своей стороны: чужие свои отряды + ещё не сходившие бойцы этого
            var occ = new Dictionary<Cell, int>();
            foreach (var q in s.Sides[side].Squads) if (q.Alive && q.Fighters != null) foreach (var f in q.Fighters) occ[f] = occ.TryGetValue(f, out int n0) ? n0 + 1 : 1;
            // передние бойцы ходят первыми — занимают лучшие гексы
            var order = new List<Cell>(actor.Fighters); order.Sort((a, b) => BushGrid.Steps(a, hero.Cell).CompareTo(BushGrid.Steps(b, hero.Cell)));
            var starts = new List<Cell>(); var paths = new List<List<Cell>>(); var newPos = new List<Cell>(); var visited = new List<Cell>(); int maxLen = 0;
            foreach (var f in order)
            {
                occ[f] = occ[f] - 1;
                var path = new List<Cell>();
                bool engaged = melee ? FlowLogic.AdjacentEnemy(s, side, f) != null : FlowLogic.EnemyInRange(s, side, f, range) != null;
                if (!engaged)
                {
                    var dist = FlowLogic.Bfs(s, f, side, speed, out var prev);
                    Cell best = f; int bd = BushGrid.Steps(f, hero.Cell), bl = 0;
                    foreach (var kv in dist)
                    {
                        if (kv.Key != f && occ.TryGetValue(kv.Key, out int n) && n >= FlowLogic.Cap) continue;
                        int d = BushGrid.Steps(kv.Key, hero.Cell);
                        if (d < bd || (d == bd && kv.Value < bl)) { bd = d; bl = kv.Value; best = kv.Key; }
                    }
                    if (best != f)
                    {
                        var full = BushLogic.PathTo(prev, f, best) ?? new List<Cell>();
                        // стоп у первого врага (ближник) / первого гекса с врагом в рейндже (дальник), если там есть место; иначе — последний гекс с местом
                        int stop = -1;
                        for (int i = 0; i < full.Count; i++)
                        {
                            bool hasRoom = !occ.TryGetValue(full[i], out int n) || n < FlowLogic.Cap;
                            bool meet = melee ? FlowLogic.AdjacentEnemy(s, side, full[i]) != null : FlowLogic.EnemyInRange(s, side, full[i], range) != null;
                            if (meet && hasRoom) { stop = i; break; }
                        }
                        if (stop < 0) for (int i = full.Count - 1; i >= 0; i--) { if (!occ.TryGetValue(full[i], out int n) || n < FlowLogic.Cap) { stop = i; break; } }
                        if (stop >= 0) path = full.GetRange(0, stop + 1);
                    }
                }
                var end = path.Count > 0 ? path[path.Count - 1] : f;
                occ[end] = occ.TryGetValue(end, out int n1) ? n1 + 1 : 1;
                starts.Add(f); paths.Add(path); newPos.Add(end); foreach (var c in path) if (!visited.Contains(c)) visited.Add(c);
                if (path.Count > maxLen) maxLen = path.Count;
            }
            actor.Fighters = newPos; actor.Cell = FlowLogic.Front(s, actor);
            Emit(s, evs, EventType.MoveStarted, actor.Ref, Ref.None, maxLen).Text = FlowLogic.EncodePaths(starts, paths);
            CollectBonuses(s, actor, visited, evs);
            FlowLogic.Sync(s, actor);   // ×2 добавил бойцов — поставить рядом
            // удары: каждый боец бьёт того, кого встретил; урон отряда делится по бойцам, по целям суммируется
            var byTarget = new Dictionary<Unit, int>();
            foreach (var f in actor.Fighters)
            {
                if (melee) { var t = FlowLogic.AdjacentEnemy(s, side, f); if (t != null) byTarget[t] = byTarget.TryGetValue(t, out int n) ? n + 1 : 1; }
                else if (def.AoeAll) { foreach (var t in FlowLogic.EnemiesInRange(s, side, f, range)) byTarget[t] = byTarget.TryGetValue(t, out int n) ? n + 1 : 1; }
                else { var t = FlowLogic.EnemyInRange(s, side, f, range); if (t != null) byTarget[t] = byTarget.TryGetValue(t, out int n) ? n + 1 : 1; }
            }
            if (byTarget.Count == 0 || def.Output <= 0) return;
            var emp = actor.Get(StatusKind.Empowered); float empMult = emp != null ? 1f + (emp.Value > 0 ? emp.Value / 100f : s.Cfg.empoweredBonus) : 1f;
            int alive = Math.Max(1, actor.Count);
            foreach (var kv in byTarget)
            {
                if (s.Ended) break; var t = kv.Key; if (!t.Alive) continue;
                int baseDmg = RoundInt(def.Output * kv.Value / (double)alive);   // доля бьющих бойцов; множитель потерь (OutputMultiplier) — в DamageMultiplier
                Emit(s, evs, EventType.ActionStarted, actor.Ref, t.Ref, -1).Action = CommandKind.Attack;
                if (!RollAttack(s, actor, t, out float roll, out bool crit, evs)) continue;
                DealDamage(s, actor, t, baseDmg, def.DamageType, false, empMult * roll * s.Cfg.bushDamageMult, evs);
                if (crit) MarkCrit(s, evs, t.Ref);
                if (t.Alive && def.Passive == Passive.Plague) ApplyStatus(s, actor, t, StatusKind.Plagued, 0, evs);
                s.LastAttackTarget[side] = t.Ref;
                if (!t.IsHero) FlowLogic.Sync(s, t, actor.Cell);   // павшие — ближайшие к атакующему
            }
        }
    }
}
