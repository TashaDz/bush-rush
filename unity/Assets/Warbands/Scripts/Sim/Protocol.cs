using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Warbands.Sim
{
    /// Протокол сравнения GDD §24.9: 6×6 упорядоченных пар × N сидов (0..N−1), Normal с обеих сторон,
    /// W/L/D и причины по ячейкам, агрегаты по пресетам без зеркал, ранний burst, поддержка до первого хода,
    /// экономика душ, статистика карт. Результат — Markdown + CSV.
    public static class Protocol
    {
        sealed class Cell { public int W, L, D, Limit; public readonly List<int> Rounds = new List<int>(), Actions = new List<int>(); }
        sealed class CardStat { public int Instances, Died, DiedBeforeAct, Actions, Damage, Absorbed, Overkill, Heal, Restore, HealExcess, Kills, BuffsGiven, BuffsUsed, BuffsExpired, BuffExtra; public int ActionsBeforeDeath; public int DeathsCounted; }
        sealed class PresetStat
        {
            public int Games, W, L, D, Limit; public readonly List<int> Ults = new List<int>(); public float Generated, Spent, Reserve, ReLost; public int HeroDeadUltReady, BonecallersAlive, BonecallersR4, BonecallersInstances;
            public readonly int[] UltRoundHist = new int[12]; public long DmgR1, DmgR2, KillsR1, KillsR2; public int SquadsInst;
        }

        public static string Run(BattleConfig cfg, int seeds, Difficulty diff, out string csv, string title)
        {
            int n = Presets.Count; var cells = new Cell[n, n];
            var presets = new PresetStat[n]; for (int i = 0; i < n; i++) presets[i] = new PresetStat();
            var cards = new Dictionary<string, CardStat>();
            int total = 0, totalLimit = 0, aWins = 0, bWins = 0, draws = 0, mirrorA = 0, mirrorB = 0, mirrorD = 0, armyKills = 0;
            var allRounds = new List<int>(); var allActions = new List<int>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    var cell = new Cell(); cells[i, j] = cell;
                    for (int seed = 0; seed < seeds; seed++)
                    {
                        var s = Headless.Play(ArmySetup.FromPreset((PresetId)i), ArmySetup.FromPreset((PresetId)j), cfg, seed, diff, diff);
                        total++;
                        if (s.Winner == 0) { cell.W++; aWins++; } else if (s.Winner == 1) { cell.L++; bWins++; } else { cell.D++; draws++; }
                        if (s.EndReason == EndReason.RoundLimit) { cell.Limit++; totalLimit++; }
                        if (s.EndReason == EndReason.ArmyDestroyed) armyKills++;
                        if (i == j) { if (s.Winner == 0) mirrorA++; else if (s.Winner == 1) mirrorB++; else mirrorD++; }
                        cell.Rounds.Add(s.Round); cell.Actions.Add(s.Actions); allRounds.Add(s.Round); allActions.Add(s.Actions);
                        for (int side = 0; side < 2; side++)
                        {
                            int pi = side == 0 ? i : j; var ps = presets[pi]; var ss = s.Sides[side];
                            bool mirror = i == j;
                            if (!mirror)
                            {
                                ps.Games++;
                                if (s.Winner == side) ps.W++; else if (s.Winner < 0) ps.D++; else ps.L++;
                                if (s.EndReason == EndReason.RoundLimit) ps.Limit++;
                            }
                            ps.Ults.Add(ss.UltsUsed); float gen = 0f; foreach (var q in ss.Squads) gen += q.ChargeGenerated;
                            ps.Generated += gen; ps.Spent += ss.ChargeSpent; ps.Reserve += ss.Charge;
                            foreach (var r in ss.UltRounds) if (r < ps.UltRoundHist.Length) ps.UltRoundHist[r]++;
                            if (ss.HeroDiedWithUltReady) ps.HeroDeadUltReady++;
                            ps.DmgR1 += ss.DamageR1; ps.DmgR2 += ss.DamageR2; ps.KillsR1 += ss.KillsR1; ps.KillsR2 += ss.KillsR2;
                            foreach (var q in ss.Squads)
                            {
                                ps.ReLost += q.ReLostHp; ps.SquadsInst++;
                                if (q.Squad.Id == SquadId.Bonecallers) { ps.BonecallersInstances++; if (q.DiedRound == 0 || q.DiedRound >= 4) ps.BonecallersR4++; if (q.Alive) ps.BonecallersAlive++; }
                                var cs = Card(cards, q.Squad.Name);
                                cs.Instances++; cs.Actions += q.Actions; cs.Damage += q.DamageDealt; cs.Absorbed += q.AbsorbedDealt; cs.Overkill += q.OverkillDealt;
                                cs.Heal += q.HealingDone; cs.Restore += q.RestoredDone; cs.HealExcess += q.HealExcess; cs.Kills += q.Kills;
                                cs.BuffsGiven += q.BuffsGiven; cs.BuffsUsed += q.BuffsUsed; cs.BuffsExpired += q.BuffsExpired; cs.BuffExtra += q.BuffExtraDamage;
                                if (!q.Alive) { cs.Died++; cs.ActionsBeforeDeath += q.Actions; cs.DeathsCounted++; if (q.FirstActionRound == 0) cs.DiedBeforeAct++; }
                            }
                            var h = ss.Hero; var hs = Card(cards, "Hero " + h.Hero.Name);
                            hs.Instances++; hs.Actions += h.Actions; hs.Damage += h.DamageDealt; hs.Absorbed += h.AbsorbedDealt; hs.Overkill += h.OverkillDealt; hs.Heal += h.HealingDone; hs.Restore += h.RestoredDone; hs.HealExcess += h.HealExcess; hs.Kills += h.Kills;
                            hs.BuffsGiven += h.BuffsGiven; hs.BuffsUsed += h.BuffsUsed; hs.BuffsExpired += h.BuffsExpired; hs.BuffExtra += h.BuffExtraDamage;
                            if (!h.Alive) hs.Died++;
                        }
                    }
                }

            var md = new StringBuilder(); var cs2 = new StringBuilder("presetA,presetB,W,L,D,limit,medianRound,q3Round,medianActions\n");
            md.Append("# ").Append(title).Append('\n').Append($"\nПрофиль balance v0.{cfg.balanceProfile}, бот {diff} с обеих сторон, сиды 0–{seeds - 1} на ячейку, {total} боёв, {sw.Elapsed.TotalSeconds:0.0} с. ")
              .Append($"Раундов {cfg.rounds}, {(cfg.heroOffGrid ? "герой вне сетки (v0.4): победа при уничтожении всех отрядов, ульта — свободное действие" : "эндшпиль с " + (cfg.shieldUntilRound + 1))}, minOutputRatio {cfg.minOutputRatio}, души {(cfg.soulsPerFighter ? "за бойца, цена " + cfg.UltCost : "долей HP, цена " + cfg.UltCost)}, очередь при равной инициативе: {(cfg.shuffleEqualInitiative ? "случайная (§24.2)" : "приоритет стороны (v0.2)")}.\n");
            md.Append($"\n**Итого:** по лимиту {100f * totalLimit / total:0.0} % боёв{(cfg.heroOffGrid ? $", армия уничтожена в {100f * armyKills / total:0.0} %" : "")}, ничьих {100f * draws / total:0.0} %, медиана финального раунда {Median(allRounds)} (Q3 {Q3(allRounds)}), действий {Median(allActions)} (Q3 {Q3(allActions)}). ")
              .Append($"Преимущество стороны A по всем боям: A {100f * aWins / total:0.0} % / B {100f * bWins / total:0.0} % / D {100f * draws / total:0.0} %; по зеркалам: A {mirrorA} / B {mirrorB} / D {mirrorD} из {n * seeds}.\n");

            md.Append("\n## Матрица: win rate стороны A = W / (W+L+D), в скобках L и D\n\n| A \\\\ B |");
            for (int j = 0; j < n; j++) md.Append(' ').Append(Presets.All[j].Name).Append(" |");
            md.Append("\n|---|"); for (int j = 0; j < n; j++) md.Append("---:|");
            md.Append('\n');
            for (int i = 0; i < n; i++)
            {
                md.Append("| ").Append(Presets.All[i].Name).Append(" |");
                for (int j = 0; j < n; j++)
                {
                    var c = cells[i, j];
                    md.Append($" {100f * c.W / seeds:0}% ({c.L}/{c.D}) |");
                    cs2.Append(Presets.All[i].Name).Append(',').Append(Presets.All[j].Name).Append(',').Append(c.W).Append(',').Append(c.L).Append(',').Append(c.D).Append(',').Append(c.Limit).Append(',').Append(Median(c.Rounds)).Append(',').Append(Q3(c.Rounds)).Append(',').Append(Median(c.Actions)).Append('\n');
                }
                md.Append('\n');
            }
            md.Append("\n## Лимит раундов и длина по ячейкам: доля по лимиту / медиана раунда / медиана действий\n\n| A \\\\ B |");
            for (int j = 0; j < n; j++) md.Append(' ').Append(Presets.All[j].Name.Split(' ')[0]).Append(" |");
            md.Append("\n|---|"); for (int j = 0; j < n; j++) md.Append("---:|");
            md.Append('\n');
            for (int i = 0; i < n; i++)
            {
                md.Append("| ").Append(Presets.All[i].Name).Append(" |");
                for (int j = 0; j < n; j++) { var c = cells[i, j]; md.Append($" {100f * c.Limit / seeds:0}% / {Median(c.Rounds)} / {Median(c.Actions)} |"); }
                md.Append('\n');
            }

            md.Append("\n## Пресеты: обе стороны против пяти других (без зеркал)\n\n| Пресет | N | Win % | Draw % | Decisive % | Лимит % | Ульты мед/Q3/макс | Generated | Spent | Reserve | Повторно потеряно HP | Герой погиб с готовой ультой | Bonecallers живы к R4 |\n|---|---:|---:|---:|---:|---:|---|---:|---:|---:|---:|---:|---:|\n");
            for (int i = 0; i < n; i++)
            {
                var p = presets[i]; int obs = Math.Max(1, p.Games); int games2 = Math.Max(1, p.Ults.Count);
                string bc = p.BonecallersInstances > 0 ? $"{100f * p.BonecallersR4 / p.BonecallersInstances:0}%" : "—";
                md.Append($"| {Presets.All[i].Name} | {p.Games} | {100f * p.W / obs:0.0}% | {100f * p.D / obs:0.0}% | {(p.W + p.L > 0 ? (100f * p.W / (p.W + p.L)).ToString("0.0") + "%" : "N/A")} | {100f * p.Limit / obs:0.0}% | {Median(p.Ults)}/{Q3(p.Ults)}/{p.Ults.Max()} | {p.Generated / games2:0} | {p.Spent / games2:0} | {p.Reserve / games2:0} | {p.ReLost / games2:0} | {p.HeroDeadUltReady} | {bc} |\n");
            }
            md.Append("\nПроверка экономики: generated = spent + reserve по каждой строке (средние на бой; расхождение — ошибка учёта).\n");

            md.Append("\n## Ранний burst (средние на бой на сторону) и раунды применения ульт\n\n| Пресет | Урон R1 | Убито отрядов R1 | Урон к концу R2 | Убито к концу R2 | Ульты по раундам 2..7+ |\n|---|---:|---:|---:|---:|---|\n");
            for (int i = 0; i < n; i++)
            {
                var p = presets[i]; int g = Math.Max(1, p.Ults.Count);
                var hist = new StringBuilder(); for (int r = 2; r < p.UltRoundHist.Length; r++) if (p.UltRoundHist[r] > 0 || r <= cfg.rounds) hist.Append($"R{r}:{p.UltRoundHist[r]} ");
                md.Append($"| {Presets.All[i].Name} | {(float)p.DmgR1 / g:0} | {(float)p.KillsR1 / g:0.00} | {(float)p.DmgR2 / g:0} | {(float)p.KillsR2 / g:0.00} | {hist} |\n");
            }

            md.Append("\n## Карты и герои (сумма по всем боям, средние на экземпляр)\n\n| Карта | Экз. | Действий/экз. | Урон/экз. | Поглощено щитом | Overkill | Heal | Restore | Избыток лечения | Убийств/экз. | Погиб % | Погиб до 1-го хода | Действий до гибели | Баффы дано/использовано/истекло | Доп. урон от баффа |\n|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|\n");
            foreach (var kv in cards.OrderBy(k => k.Key.StartsWith("Hero") ? 1 : 0).ThenBy(k => k.Key))
            {
                var c = kv.Value; int inst = Math.Max(1, c.Instances);
                md.Append($"| {kv.Key} | {c.Instances} | {(float)c.Actions / inst:0.0} | {(float)c.Damage / inst:0} | {(float)c.Absorbed / inst:0} | {(float)c.Overkill / inst:0} | {(float)c.Heal / inst:0} | {(float)c.Restore / inst:0} | {(float)c.HealExcess / inst:0} | {(float)c.Kills / inst:0.00} | {100f * c.Died / inst:0}% | {(c.Died > 0 ? (100f * c.DiedBeforeAct / c.Died).ToString("0") + "%" : "—")} | {(c.DeathsCounted > 0 ? ((float)c.ActionsBeforeDeath / c.DeathsCounted).ToString("0.0") : "—")} | {c.BuffsGiven}/{c.BuffsUsed}/{c.BuffsExpired} | {c.BuffExtra} |\n");
            }
            csv = cs2.ToString();
            return md.ToString();
        }

        static CardStat Card(Dictionary<string, CardStat> d, string k) { if (!d.TryGetValue(k, out var c)) { c = new CardStat(); d[k] = c; } return c; }
        static int Median(List<int> l) { if (l.Count == 0) return 0; var c = new List<int>(l); c.Sort(); return c[c.Count / 2]; }
        static int Q3(List<int> l) { if (l.Count == 0) return 0; var c = new List<int>(l); c.Sort(); return c[Math.Min(c.Count - 1, (int)(c.Count * 0.75))]; }
    }
}
