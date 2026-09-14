using System;
using System.Collections.Generic;
using System.Text;

namespace Warbands.Sim
{
    /// Бой бот-против-бота без Unity: автоплей, матрица 6×6 (§22 M4), телеметрия §19.
    public static class Headless
    {
        public const int MaxActions = 400;

        public static BattleState Play(ArmySetup a, ArmySetup b, BattleConfig cfg, int seed, Difficulty da = Difficulty.Normal, Difficulty db = Difficulty.Normal)
        {
            var s = BattleResolver.Create(a, b, cfg, seed, "A", "B");
            var brains = new[] { new BotBrain(da), new BotBrain(db) };
            while (!s.Ended && s.Actions < MaxActions)
            {
                var cmd = brains[s.Current.Side].Choose(s);
                BattleResolver.Apply(s, cmd);
            }
            if (!s.Ended) throw new InvalidOperationException("battle did not end in " + MaxActions + " actions");
            return s;
        }

        public sealed class PairStat
        {
            public int Games, WinsA, WinsB, Draws, LimitEnds, ShieldNeverRemovedWins;
            public List<int> RoundsList = new List<int>(); public List<int> UltsA = new List<int>(), UltsB = new List<int>();
            public int Actions;
        }

        /// Матрица пресетов: строка — сторона A (игрок, приоритет в нечётных раундах), столбец — сторона B.
        public static string Matrix(BattleConfig cfg, int seeds, Difficulty d, out string csv)
        {
            int n = Presets.Count;
            var stats = new PairStat[n, n];
            var presetUlts = new List<int>[n]; var presetWins = new int[n]; var presetGames = new int[n]; var presetLimitWins = new int[n]; var presetShieldWins = new int[n];
            for (int i = 0; i < n; i++) presetUlts[i] = new List<int>();
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    var st = new PairStat(); stats[i, j] = st;
                    for (int k = 0; k < seeds; k++)
                    {
                        var s = Play(ArmySetup.FromPreset((PresetId)i), ArmySetup.FromPreset((PresetId)j), cfg, 1000 + k * 17 + i * 7 + j, d, d);
                        st.Games++; st.Actions += s.Actions;
                        if (s.Winner == 0) st.WinsA++; else if (s.Winner == 1) st.WinsB++; else st.Draws++;
                        if (s.EndReason == EndReason.RoundLimit) st.LimitEnds++;
                        if (s.Winner >= 0 && s.EndReason == EndReason.RoundLimit && !s.Sides[s.Winner].ShieldEverRemoved) { st.ShieldNeverRemovedWins++; presetShieldWins[s.Winner == 0 ? i : j]++; }
                        st.RoundsList.Add(s.Round); st.UltsA.Add(s.Sides[0].UltsUsed); st.UltsB.Add(s.Sides[1].UltsUsed);
                        presetUlts[i].Add(s.Sides[0].UltsUsed); presetUlts[j].Add(s.Sides[1].UltsUsed);
                        presetGames[i]++; presetGames[j]++;
                        if (s.Winner == 0) presetWins[i]++; else if (s.Winner == 1) presetWins[j]++;
                        if (s.Winner >= 0 && s.EndReason == EndReason.RoundLimit) presetLimitWins[s.Winner == 0 ? i : j]++;
                    }
                }

            var sb = new StringBuilder(); var cs = new StringBuilder("presetA,presetB,games,winsA,winsB,draws,limitEnds,medianRound,avgUltsA,avgUltsB\n");
            sb.Append("win rate of ROW preset vs COLUMN preset (A side), ").Append(seeds).Append(" seeds per pair, bot ").Append(d).Append('\n');
            sb.Append("              ");
            for (int j = 0; j < n; j++) sb.Append(Presets.All[j].Name.Substring(0, 6).PadRight(8));
            sb.Append('\n');
            int totalLimit = 0, totalGames = 0, totalActions = 0; var allRounds = new List<int>();
            for (int i = 0; i < n; i++)
            {
                sb.Append(Presets.All[i].Name.PadRight(14));
                for (int j = 0; j < n; j++)
                {
                    var st = stats[i, j];
                    sb.Append(((st.WinsA + 0.5f * st.Draws) / st.Games * 100f).ToString("0").PadLeft(3)).Append("%    ");
                    totalLimit += st.LimitEnds; totalGames += st.Games; totalActions += st.Actions; allRounds.AddRange(st.RoundsList);
                    cs.Append(Presets.All[i].Name).Append(',').Append(Presets.All[j].Name).Append(',').Append(st.Games).Append(',').Append(st.WinsA).Append(',').Append(st.WinsB).Append(',').Append(st.Draws).Append(',').Append(st.LimitEnds).Append(',').Append(Median(st.RoundsList)).Append(',').Append(Avg(st.UltsA).ToString("0.00")).Append(',').Append(Avg(st.UltsB).ToString("0.00")).Append('\n');
                }
                sb.Append('\n');
            }
            sb.Append('\n');
            sb.Append("preset          games  win%  ults med/q3/max  limitWins  shieldNeverRemovedWins\n");
            for (int i = 0; i < n; i++)
            {
                var u = presetUlts[i]; u.Sort();
                sb.Append(Presets.All[i].Name.PadRight(16)).Append(presetGames[i].ToString().PadLeft(5)).Append("  ")
                  .Append((100f * presetWins[i] / Math.Max(1, presetGames[i])).ToString("0").PadLeft(3)).Append("%  ")
                  .Append(Median(u).ToString().PadLeft(3)).Append('/').Append(u.Count > 0 ? u[(int)(u.Count * 0.75)] : 0).Append('/').Append(u.Count > 0 ? u[u.Count - 1] : 0).Append("        ")
                  .Append(presetLimitWins[i].ToString().PadLeft(5)).Append("      ").Append(presetShieldWins[i]).Append('\n');
            }
            sb.Append($"\nround-limit endings: {100f * totalLimit / Math.Max(1, totalGames):0}%  median final round: {Median(allRounds)}  avg actions/battle: {(float)totalActions / Math.Max(1, totalGames):0.0}\n");
            csv = cs.ToString();
            return sb.ToString();
        }

        static int Median(List<int> l) { if (l.Count == 0) return 0; var c = new List<int>(l); c.Sort(); return c[c.Count / 2]; }
        static float Avg(List<int> l) { if (l.Count == 0) return 0f; float s = 0f; foreach (var v in l) s += v; return s / l.Count; }

        /// Телеметрия матча §19 в одну JSON-строку (без персональных данных).
        public static string MatchJson(BattleState s, float durationSeconds, string build, string device)
        {
            var sb = new StringBuilder("{");
            sb.Append("\"build\":\"").Append(build).Append("\",\"seed\":").Append(s.Seed).Append(",\"device\":\"").Append(device).Append("\",");
            sb.Append("\"result\":").Append(s.Winner).Append(",\"reason\":\"").Append(s.EndReason).Append("\",\"round\":").Append(s.Round).Append(",\"duration\":").Append(durationSeconds.ToString("0.0")).Append(",\"actions\":").Append(s.Actions).Append(",\"sides\":[");
            for (int i = 0; i < 2; i++)
            {
                var side = s.Sides[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"label\":\"").Append(side.Label).Append("\",\"preset\":\"").Append(side.Preset.HasValue ? side.Preset.Value.ToString() : "custom").Append("\",\"hero\":\"").Append(side.HeroDef.Id).Append("\",\"heroHp\":").Append(side.Hero.Hp)
                  .Append(",\"hpScore\":").Append(side.HpScore).Append(",\"charge\":").Append(side.Charge.ToString("0")).Append(",\"ults\":").Append(side.UltsUsed).Append(",\"wasted\":").Append(side.WastedCharge.ToString("0")).Append(",\"timeouts\":").Append(side.TimeoutActions).Append(",\"shieldRemoved\":").Append(side.ShieldEverRemoved ? 1 : 0).Append(",\"squads\":[");
                for (int q = 0; q < 4; q++)
                {
                    var u = side.Squads[q];
                    if (q > 0) sb.Append(',');
                    sb.Append("{\"id\":\"").Append(u.Squad.Id).Append("\",\"row\":\"").Append(u.Row).Append("\",\"slot\":").Append(u.Slot).Append(",\"hp\":").Append(u.Hp).Append(",\"dmg\":").Append(u.DamageDealt).Append(",\"heal\":").Append(u.HealingDone).Append(",\"restore\":").Append(u.RestoredDone).Append(",\"kills\":").Append(u.Kills).Append(",\"souls\":").Append(u.ChargeGenerated.ToString("0")).Append('}');
                }
                sb.Append("],\"heroDmg\":").Append(side.Hero.DamageDealt).Append(",\"heroHeal\":").Append(side.Hero.HealingDone + side.Hero.RestoredDone).Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }
    }
}
