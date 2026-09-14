using System;
using NUnit.Framework;
using Warbands.Sim;

namespace Warbands.Tests
{
    /// Правила Bush Rush (автор 14.09): герои на поле, бойцы «как вода» — каждый на своём гексе бежит к вражескому герою,
    /// бьют встречных, победа — смерть героя, лекарей нет.
    public class RushTests
    {
        static BattleConfig Cfg() { var c = BattleConfig.CreateDefault(); c.damageVariance = 0f; c.missChance = 0f; c.critChance = 0f; c.bushRegrowTurns = 0; c.bushObstacles = 0; c.bushBonuses = 0; return c; }
        static BattleState New(int seed = 5, BattleConfig c = null) => BattleResolver.Create(ArmySetup.FromPreset(PresetId.SustainedFire), ArmySetup.FromPreset(PresetId.ExecutionChain), c ?? Cfg(), seed);
        static void Force(BattleState s, Ref r) { for (int i = 0; i < s.Queue.Count; i++) if (s.Queue[i] == r) { s.QueuePos = i; return; } Assert.Fail("not in queue: " + r); }
        static void ClearAll(BattleState s) { for (int y = 0; y < BushGrid.Rows; y++) for (int x = 0; x < BushGrid.Cols; x++) s.Bushes.Clear(new Cell(x, y)); }

        [Test]
        public void HeroesOnEdgesFightersOnHomeHexesNoHealers()
        {
            var s = New();
            Assert.AreEqual(BushGrid.HeroCell(0), s.Sides[0].Hero.Cell); Assert.AreEqual(BushGrid.HeroCell(1), s.Sides[1].Hero.Cell);
            Assert.AreEqual(BushGrid.Rows - 1, s.Sides[0].Hero.Cell.Y); Assert.AreEqual(0, s.Sides[1].Hero.Cell.Y);
            foreach (var sd in s.Sides) foreach (var q in sd.Squads)
            {
                Assert.AreEqual(q.Count, q.Fighters.Count, "по бойцу на живого: " + q.Name);
                var home = BushGrid.Home(sd.Index, q.Row, q.Slot);
                foreach (var f in q.Fighters)
                {
                    Assert.LessOrEqual(BushGrid.Steps(f, home), 2, "лагерь — рядом с домашним гексом: " + q.Name + " " + f);
                    Assert.LessOrEqual(FlowLogic.OwnAt(s, f, sd.Index), FlowLogic.Cap, "не больше Cap бойцов стороны в гексе");
                    Assert.IsFalse(s.Bushes.IsBush(f), "под лагерем расчищено");
                    Assert.AreNotEqual(sd.Hero.Cell, f); Assert.AreNotEqual(s.Sides[1 - sd.Index].Hero.Cell, f);
                }
                Assert.Greater(new System.Collections.Generic.HashSet<Cell>(q.Fighters).Count, 1, "отряд крупнее Cap стоит на нескольких гексах: " + q.Name);
            }
            Assert.AreSame(s.Sides[1].Hero, BushLogic.OccupantAt(s, s.Sides[1].Hero.Cell), "герой занимает гекс");
            Assert.IsFalse(BushLogic.Passable(s, s.Sides[0].Hero.Cell, 0), "свой герой — стена");
            Assert.IsTrue(BushLogic.Passable(s, s.Sides[1].Squads[0].Cell, 0), "гекс с вражескими бойцами проходим — дерёмся вперемешку");
            foreach (var sd in s.Sides) foreach (var q in sd.Squads) { int sum = 0; foreach (var h in q.FighterHp) sum += h; Assert.AreEqual(q.Hp, sum, "HP бойцов в сумме = HP отряда: " + q.Name); foreach (var h in q.FighterHp) Assert.LessOrEqual(h, q.Squad.HpPerFighter); }
            foreach (var p in Presets.All) { foreach (var id in p.Front) Assert.IsTrue(Cards.Available(id)); foreach (var id in p.Back) Assert.IsTrue(Cards.Available(id), "лекарей в колодах нет: " + id); }
        }

        [Test]
        public void FightersSplitAlongDifferentRoadsTowardHero()
        {
            // от гекса Wardens две дороги вверх: слева и справа; бойцов больше, чем влезает в один гекс — растекаются по обеим
            var s = New(); var w = s.Sides[0].Squads[0]; var hero = s.Sides[1].Hero; int cx = w.Cell.X, cy = w.Cell.Y;
            foreach (var q in s.Sides[1].Squads) { q.Cell = new Cell(5, 1); q.Fighters = new System.Collections.Generic.List<Cell>(); for (int i = 0; i < q.Count; i++) q.Fighters.Add(q.Cell); }   // враг в стороне
            var left = new Cell(cx, cy - 1); var right = new Cell(cx + 1, cy - 1);   // оба соседи стартового гекса (чётная строка 8? проверим)
            Assert.IsTrue(BushGrid.Adjacent(w.Cell, left)); Assert.IsTrue(BushGrid.Adjacent(w.Cell, right) || BushGrid.Adjacent(w.Cell, new Cell(cx - 1, cy - 1)));
            if (!BushGrid.Adjacent(w.Cell, right)) right = new Cell(cx - 1, cy - 1);
            s.Bushes.Clear(left); s.Bushes.Clear(right);
            Force(s, w.Ref);
            var evs = BattleResolver.Apply(s, Command.Clear(w.Ref, null));
            Assert.IsTrue(evs.Exists(e => e.Type == EventType.MoveStarted && e.Actor == w.Ref && e.Value > 0), "побежали");
            var cells = new System.Collections.Generic.HashSet<Cell>(w.Fighters);
            Assert.GreaterOrEqual(cells.Count, 2, "бойцы разошлись по разным гексам: " + string.Join(" ", cells));
            foreach (var f in w.Fighters) Assert.LessOrEqual(FlowLogic.OwnAt(s, f, 0), FlowLogic.Cap, "не больше Cap в гексе");
            Assert.AreEqual(w.Count, w.Fighters.Count);
            Assert.AreEqual(FlowLogic.Front(s, w), w.Cell, "Cell — передний боец");
            Assert.Less(BushGrid.Steps(w.Cell, hero.Cell), BushGrid.Steps(new Cell(cx, cy), hero.Cell), "фронт стал ближе к герою");
            Assert.AreEqual(FlowLogic.Cap, 4, "автор 14.09: до 4 бойцов в гексе");
        }

        [Test]
        public void MeleeFightersStrikeWhatTheyMeetAndTrimFromContact()
        {
            var s = New(); ClearAll(s); var w = s.Sides[0].Squads[0]; var enemy = s.Sides[1].Squads[1]; var hero = s.Sides[1].Hero;
            // враг стоит поперёк дороги в двух гексах перед Wardens
            enemy.Cell = new Cell(w.Cell.X, w.Cell.Y - 2); enemy.Fighters = new System.Collections.Generic.List<Cell>(); for (int i = 0; i < enemy.Count; i++) enemy.Fighters.Add(enemy.Cell);
            foreach (var q in s.Sides[1].Squads) if (q != enemy) { q.Cell = new Cell(5, 1); q.Fighters = new System.Collections.Generic.List<Cell>(); for (int i = 0; i < q.Count; i++) q.Fighters.Add(q.Cell); }
            int hp0 = enemy.Hp; Force(s, w.Ref);
            var evs = BattleResolver.Apply(s, Command.Clear(w.Ref, null));
            Assert.IsTrue(evs.Exists(e => e.Type == EventType.DamageApplied && e.Actor == w.Ref && e.Target == enemy.Ref), "встретили врага по дороге — ударили");
            Assert.Less(enemy.Hp, hp0); Assert.AreEqual(enemy.Count, enemy.Fighters.Count, "павшие бойцы сняты");
            int sum2 = 0; foreach (var h in enemy.FighterHp) sum2 += h; Assert.AreEqual(enemy.Hp, sum2, "HP бойцов после урона = HP отряда");
            int wounded = 0; foreach (var h in enemy.FighterHp) if (h < enemy.Squad.HpPerFighter) wounded++; Assert.LessOrEqual(wounded, 1, "урон снимался с бойцов по очереди, неполный — не больше одного");
            Assert.IsFalse(evs.Exists(e => e.Type == EventType.RetreatStarted), "отхода нет");
        }

        [Test]
        public void RangedStopInRangeAndPreferHero()
        {
            var s = New(); ClearAll(s); var a = s.Sides[0].Squads[2]; Assert.AreEqual(Reach.Ranged, a.Squad.Reach); var hero = s.Sides[1].Hero;
            foreach (var q in s.Sides[1].Squads) { q.Cell = new Cell(5, 1); q.Fighters = new System.Collections.Generic.List<Cell>(); for (int i = 0; i < q.Count; i++) q.Fighters.Add(q.Cell); }
            Force(s, a.Ref);
            var evs = BattleResolver.Apply(s, Command.Clear(a.Ref, null));
            Assert.IsTrue(evs.Exists(e => e.Type == EventType.MoveStarted && e.Actor == a.Ref && e.Value > 0), "побежали вперёд");
            int guard = 0;
            while (!evs.Exists(e => e.Type == EventType.DamageApplied && e.Actor == a.Ref) && guard++ < 4) { Force(s, a.Ref); evs = BattleResolver.Apply(s, Command.Clear(a.Ref, null)); }
            Assert.IsTrue(evs.Exists(e => e.Type == EventType.DamageApplied && e.Actor == a.Ref && e.Target == hero.Ref), "герой в рейндже — стреляют в героя");
            float range = BushStats.Range(a.Squad.Id); foreach (var f in a.Fighters) Assert.IsNotNull(FlowLogic.EnemyInRange(s, 0, f, range), "каждый боец остановился, когда кто-то в рейндже");
        }

        [Test]
        public void HeroDeathEndsBattleButArmyLossDoesNot()
        {
            var s = New(); ClearAll(s); var hero = s.Sides[1].Hero; var w = s.Sides[0].Squads[0];
            foreach (var q in s.Sides[1].Squads) { q.Hp = 0; FlowLogic.Sync(s, q); }
            Assert.IsFalse(s.Ended);
            hero.Hp = 10; w.Cell = new Cell(hero.Cell.X, 1); w.Fighters = new System.Collections.Generic.List<Cell>(); for (int i = 0; i < w.Count; i++) w.Fighters.Add(w.Cell);
            Force(s, w.Ref);
            var evs = BattleResolver.Apply(s, Command.Clear(w.Ref, null));
            Assert.IsTrue(evs.Exists(e => e.Type == EventType.DamageApplied && e.Target == hero.Ref));
            Assert.IsTrue(s.Ended); Assert.AreEqual(0, s.Winner); Assert.AreEqual(EndReason.HeroDefeated, s.EndReason);
        }

        [Test]
        public void BotClearsRoadAndHeadlessMatchesFinishByHeroDeath()
        {
            var s = New(); var e = s.Sides[1].Squads[0]; Force(s, e.Ref);
            var cands = BattleResolver.LegalCommands(s).FindAll(c => c.Kind == CommandKind.Clear && c.Cells != null && c.Cells.Length > 0);
            if (cands.Count == 0)
            {
                var g = BushLogic.PickGoal(s, e); var path = BushLogic.PathThroughBushes(s, 1, e.Cell, BushLogic.GoalOf(s, e, g), out int cost);
                var sb = new System.Text.StringBuilder(); sb.Append("actor ").Append(e.Name).Append(" cell ").Append(e.Cell).Append(" fighters ").Append(string.Join(" ", e.Fighters)).Append(" goal valid ").Append(g.Valid).Append(" unit ").Append(g.Unit != null ? g.Unit.Name : "-").Append(" hero0 ").Append(s.Sides[0].Hero.Cell).Append(" path ");
                if (path == null) sb.Append("null"); else foreach (var c in path) sb.Append(c).Append(s.Bushes.IsBush(c) ? "B " : "_ ");
                sb.Append(" legal: "); foreach (var c in BattleResolver.LegalCommands(s)) sb.Append(c.Kind).Append('/').Append(c.Cells == null ? -1 : c.Cells.Length).Append(' ');
                Assert.Fail(sb.ToString());
            }
            var evs = BattleResolver.Apply(s, new BotBrain(Difficulty.Normal).Choose(s));
            Assert.IsTrue(evs.Exists(ev => ev.Type == EventType.MoveStarted && ev.Actor == e.Ref && ev.Value > 0), "бот побежал к герою игрока");
            var cfg = BattleConfig.CreateDefault(); int heroEnds = 0;
            for (int seed = 1; seed <= 6; seed++)
            {
                var m = Headless.Play(ArmySetup.FromPreset(PresetId.StormWall), ArmySetup.FromPreset(PresetId.PlagueMarch), cfg, seed);
                Assert.IsTrue(m.Ended); Assert.AreNotEqual(EndReason.ArmyDestroyed, m.EndReason, "гибель армии бой не заканчивает");
                if (m.EndReason == EndReason.HeroDefeated) heroEnds++;
                foreach (var sd in m.Sides) foreach (var q in sd.Squads) { Assert.AreEqual(q.Count, q.Fighters.Count, "бойцы = живые: " + q.Name); int sm = 0; foreach (var h in q.FighterHp) sm += h; Assert.AreEqual(Math.Max(0, q.Hp), sm, "HP бойцов = HP отряда: " + q.Name); }
            }
            Assert.Greater(heroEnds, 0, "хоть один бой из шести закончился смертью героя");
        }
    }
}
