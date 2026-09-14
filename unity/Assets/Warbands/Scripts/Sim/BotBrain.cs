using System;
using System.Collections.Generic;

namespace Warbands.Sim
{
    public enum Difficulty { Normal, Strong }

    /// §13: детерминированный utility AI. Каждый легальный ход применяется к копии состояния,
    /// оценка — по разнице состояний и событиям (веса §13.1). Strong — one-ply: минус лучший ответ противника.
    public sealed class BotBrain
    {
        public Difficulty Difficulty;
        public const float TieBand = 0.03f;

        public BotBrain(Difficulty d) { Difficulty = d; }

        public Command Choose(BattleState s)
        {
            var legal = BattleResolver.LegalCommands(s);
            if (legal.Count == 0) return default;
            if (legal.Count == 1) return legal[0];
            int side = s.Current.Side;
            var scores = new float[legal.Count];
            float best = float.NegativeInfinity;
            for (int i = 0; i < legal.Count; i++)
            {
                var after = s.Clone(); after.Evaluating = true;
                var evs = BattleResolver.Apply(after, legal[i]);
                float sc = Score(s, after, legal[i], evs, side);
                if (Difficulty == Difficulty.Strong && !after.Ended && after.Current.Side != side)
                    sc -= 0.7f * BestReply(after);
                scores[i] = sc;
                if (sc > best) best = sc;
            }
            // случайность только между вариантами в пределах 3 % (§13.1); свой генератор — очередь боя не трогаем
            float band = Math.Max(1f, Math.Abs(best)) * TieBand;
            var cands = new List<int>();
            for (int i = 0; i < legal.Count; i++) if (scores[i] >= best - band) cands.Add(i);
            var rng = new Rng(s.Seed * 31 + s.Actions * 7 + side);
            return legal[cands[rng.Range(0, cands.Count)]];
        }

        /// Лучший ответ противника по оценке Normal (без рекурсии).
        static float BestReply(BattleState s)
        {
            var legal = BattleResolver.LegalCommands(s);
            int side = s.Current.Side; float best = float.NegativeInfinity;
            foreach (var c in legal)
            {
                var after = s.Clone(); after.Evaluating = true;
                var evs = BattleResolver.Apply(after, c);
                float sc = Score(s, after, c, evs, side);
                if (sc > best) best = sc;
            }
            return float.IsNegativeInfinity(best) ? 0f : best;
        }

        // ---------- оценка ----------

        public static float Score(BattleState before, BattleState after, Command cmd, List<CombatEvent> evs, int side)
        {
            int enemy = 1 - side; var cfg = before.Cfg;
            float sc = 0f;
            bool enemyKill = false;
            if (cmd.Kind == CommandKind.Clear) sc += BushScore(after, side);

            // реальные HP
            foreach (var ub in before.Units(enemy))
            {
                var ua = after.Get(ub.Ref);
                int lost = ub.Hp - ua.Hp;
                if (ub.IsHero) { sc += lost * 1.5f; if (!ua.Alive) sc += 100000f; }
                else
                {
                    sc += lost;
                    if (ub.Alive && !ua.Alive) { enemyKill = true; sc += 120f; if (ub.Squad.IsSupport) sc += 60f; }
                }
            }
            foreach (var ub in before.Units(side))
            {
                var ua = after.Get(ub.Ref);
                int delta = ua.Hp - ub.Hp;
                sc += delta;                                             // лечение/восстановление +, перенос связи −
                if (ub.IsHero && !ua.Alive) sc -= 100000f;
                if (!ub.IsHero && ub.Alive && !ua.Alive) sc -= 200f;
                if (!ub.IsHero && ua.Alive)
                {
                    // RestoredOutputValue: на сколько выросла отдача на ближайшие ходы
                    float dm = BattleResolver.OutputMultiplier(cfg, ua) - BattleResolver.OutputMultiplier(cfg, ub);
                    if (dm > 0f) sc += dm * ub.Squad.Output * 1.5f;
                    // PreventedDeathValue: умер бы до следующего хода, теперь нет
                    float threat = Threat(before, ub);
                    bool wasDead = ub.Hp + ub.ShieldValue <= threat, nowDead = ua.Hp + ua.ShieldValue <= threat;
                    if (wasDead && !nowDead && threat > 0f) sc += 140f;
                }
            }

            // заряд врага без добивания, overkill, лишнее лечение
            float enemyCharge = after.Sides[enemy].Charge - before.Sides[enemy].Charge;
            if (!enemyKill && enemyCharge > 0f) sc -= 0.35f * enemyCharge;

            foreach (var e in evs)
            {
                switch (e.Type)
                {
                    case EventType.DamageApplied: sc -= 0.5f * e.Extra; break;
                    case EventType.HealApplied:
                    case EventType.RestoreApplied: sc -= 0.5f * e.Extra; break;
                    case EventType.Dispelled:
                        if (e.Target.Side == enemy) sc += StatusValueRemoved(e.Status, e.Value);
                        break;
                    case EventType.StatusApplied:
                        if (e.Actor.Side != side) break;
                        var tb = before.Get(e.Target);
                        switch (e.Status)
                        {
                            case StatusKind.Exposed: sc += cfg.exposedBonus * FollowUp(before, tb, side) + 10f; break;
                            case StatusKind.Plagued: sc += EnemyCanHeal(before, enemy) ? 25f : 5f; break;
                            case StatusKind.Empowered:
                            {
                                if (e.Value <= 0) break;   // «Empowered kept»: слабый бафф поверх сильного — пользы нет
                                float pct = e.Value / 100f;
                                sc += pct * Expected(before, tb) + 5f;
                                if (HasTurnLeft(before, tb)) sc += 10f;   // §24.7: предпочесть получателя с оставшимся ходом в раунде
                                break;
                            }
                            case StatusKind.Shield: sc += 0.6f * Math.Min(e.Value, Math.Max(20f, Threat(before, tb))); break;
                            case StatusKind.Linked: sc += 0.3f * Math.Min(Threat(before, tb), tb.Hp) + 5f; break;
                        }
                        break;
                }
            }

            // фокус: та же цель, что и прошлая атака стороны
            if (cmd.Kind == CommandKind.Attack && !cmd.Target.IsNone && cmd.Target == before.LastAttackTarget[side] && after.Get(cmd.Target).Alive) sc += 15f;
            // цена ульты и способности
            if (cmd.Kind == CommandKind.Ultimate && before.Round < cfg.rounds - 1) sc -= 25f;
            if (cmd.Kind == CommandKind.Skill) sc -= 10f;
            // концовка §13.2: в раундах 5–6 важен итоговый счёт HP (уже учтён через HP; добавим вес)
            if (before.Round >= cfg.rounds - 1)
            {
                int db = before.Sides[side].HpScore - before.Sides[enemy].HpScore;
                int da = after.Sides[side].HpScore - after.Sides[enemy].HpScore;
                sc += 0.3f * (da - db);
            }
            return sc;
        }

        static float StatusValueRemoved(StatusKind k, int value)
        {
            switch (k)
            {
                case StatusKind.Linked: return 90f;
                case StatusKind.Shield: return value * 0.8f;
                case StatusKind.Empowered: return 70f;
                default: return 30f;
            }
        }

        /// bush-field: дороги — свои короче (к целям), чужие длиннее; расчищенное открыто обеим сторонам, поэтому чужая выгода вычитается.
        public static float BushRoadWeight = 4f;
        static float BushScore(BattleState after, int side)
        {
            float mine = BushLogic.RoadCost(after, side), theirs = BushLogic.RoadCost(after, 1 - side);
            return (-mine + 0.6f * theirs) * BushRoadWeight;
        }

        static bool EnemyCanHeal(BattleState s, int side)
        {
            var ss = s.Sides[side];
            if (ss.HeroDef.Skill == HeroSkill.HolyLight || ss.HeroDef.Skill == HeroSkill.RaiseTheFallen || ss.HeroDef.Ult == HeroUlt.DawnRenewal || ss.HeroDef.Ult == HeroUlt.RaiseTheFallen) return true;
            foreach (var q in ss.Squads) if (q.Alive && (q.Squad.Support == SupportKind.Heal || q.Squad.Support == SupportKind.Restore)) return true;
            return false;
        }

        public static float Expected(BattleState s, Unit u)
        {
            if (u.IsHero) return u.Hero.Attack;
            return u.Squad.Output * BattleResolver.OutputMultiplier(s.Cfg, u) * BattleResolver.RageBonus(s.Cfg, u);
        }

        /// Ожидаемый входящий урон по цели от оставшихся в этом раунде врагов.
        public static float Threat(BattleState s, Unit target)
        {
            float t = 0f;
            for (int i = s.QueuePos + 1; i < s.Queue.Count; i++)
            {
                var u = s.Get(s.Queue[i]);
                if (!u.Alive || u.Side == target.Side) continue;
                if (!u.IsHero && (u.Squad.RowAttack || u.Squad.AoeAll)) { if (!target.IsHero) t += u.Squad.Output * BattleResolver.OutputMultiplier(s.Cfg, u); continue; }
                if (BattleResolver.CanAttack(s, u, target)) t += Expected(s, u);
            }
            return t;
        }

        static bool HasTurnLeft(BattleState s, Unit u) { for (int i = s.QueuePos + 1; i < s.Queue.Count; i++) if (s.Queue[i] == u.Ref) return true; return false; }

        /// Normal §13.2: фокус максимум двух следующих союзных атак по цели.
        public static float FollowUp(BattleState s, Unit target, int side)
        {
            float t = 0f; int n = 0;
            for (int i = s.QueuePos + 1; i < s.Queue.Count && n < 2; i++)
            {
                var u = s.Get(s.Queue[i]);
                if (!u.Alive || u.Side != side) continue;
                if (BattleResolver.CanAttack(s, u, target)) { t += Expected(s, u); n++; }
            }
            return t;
        }
    }
}
