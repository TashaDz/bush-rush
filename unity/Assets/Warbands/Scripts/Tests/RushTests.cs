using NUnit.Framework;
using Warbands.Sim;

namespace Warbands.Tests
{
    /// Правила Bush Rush (автор 14.09): герои на поле, все бегут к вражескому герою, победа — смерть героя, лекарей нет.
    public class RushTests
    {
        static BattleConfig Cfg() { var c = BattleConfig.CreateDefault(); c.damageVariance = 0f; c.missChance = 0f; c.critChance = 0f; c.bushRegrowTurns = 0; c.bushObstacles = 0; c.bushBonuses = 0; return c; }
        static BattleState New(int seed = 5, BattleConfig c = null) => BattleResolver.Create(ArmySetup.FromPreset(PresetId.SustainedFire), ArmySetup.FromPreset(PresetId.ExecutionChain), c ?? Cfg(), seed);
        static void Force(BattleState s, Ref r) { for (int i = 0; i < s.Queue.Count; i++) if (s.Queue[i] == r) { s.QueuePos = i; return; } Assert.Fail("not in queue: " + r); }

        [Test]
        public void HeroesStandOnTheirEdgesAndArmiesInFront()
        {
            var s = New();
            Assert.AreEqual(BushGrid.HeroCell(0), s.Sides[0].Hero.Cell); Assert.AreEqual(BushGrid.HeroCell(1), s.Sides[1].Hero.Cell);
            Assert.AreEqual(BushGrid.Rows - 1, s.Sides[0].Hero.Cell.Y); Assert.AreEqual(0, s.Sides[1].Hero.Cell.Y);
            foreach (var q in s.Sides[0].Squads) Assert.IsTrue(q.Cell.Y == BushGrid.Rows - 2 || q.Cell.Y == BushGrid.Rows - 3, "игрок в строках Rows-3/Rows-2");
            foreach (var q in s.Sides[1].Squads) Assert.IsTrue(q.Cell.Y == 1 || q.Cell.Y == 2, "враг в строках 1/2");
            Assert.IsFalse(s.Bushes.IsBush(s.Sides[0].Hero.Cell)); Assert.IsFalse(s.Bushes.IsBush(s.Sides[1].Hero.Cell));
            Assert.AreSame(s.Sides[1].Hero, BushLogic.OccupantAt(s, s.Sides[1].Hero.Cell), "герой занимает гекс");
            Assert.IsFalse(BushLogic.Passable(s, s.Sides[0].Hero.Cell, 0), "свой герой — стена");
            foreach (var p in Presets.All) foreach (var id in p.Front) Assert.IsTrue(Cards.Available(id));
            foreach (var p in Presets.All) foreach (var id in p.Back) Assert.IsTrue(Cards.Available(id), "лекарей в колодах нет: " + id);
        }

        [Test]
        public void MeleeRunsTowardEnemyHeroAlongClearedRoadAndStrikesFirstContact()
        {
            var s = New(); var w = s.Sides[0].Squads[0]; int cx = w.Cell.X; var hero = s.Sides[1].Hero;
            Assert.AreEqual(hero, BushLogic.PickGoal(s, w).Unit, "цель — вражеский герой, без выбора");
            // дорога — гексы отрезка от отряда до героя (без гекса героя): кончается рядом с ним; врагов с дороги убираем
            var stroke = BushGrid.Line(w.Cell, hero.Cell); stroke.RemoveAt(stroke.Count - 1);
            foreach (var q in s.Sides[1].Squads) if (stroke.Contains(q.Cell)) q.Cell = new Cell(5, q.Cell.Y);
            if (stroke.Contains(new Cell(5, 1)) || stroke.Contains(new Cell(5, 2))) foreach (var q in s.Sides[1].Squads) if (stroke.Contains(q.Cell)) q.Cell = new Cell(0, q.Cell.Y);
            Force(s, w.Ref);
            var evs = BattleResolver.Apply(s, Command.Clear(w.Ref, stroke));
            Assert.IsTrue(evs.Exists(e => e.Type == EventType.MoveStarted && e.Actor == w.Ref && e.Value > 0), "побежал");
            Assert.Less(BushGrid.Steps(w.Cell, hero.Cell), BushGrid.Steps(BushGrid.Home(0, w.Row, w.Slot), hero.Cell), "стал ближе к герою");
            Assert.IsFalse(evs.Exists(e => e.Type == EventType.RetreatStarted), "отхода в Bush Rush нет");
            // следующие ходы — дойдёт до героя и ударит его
            int guard = 0;
            while (!evs.Exists(e => e.Type == EventType.DamageApplied && e.Target == hero.Ref) && guard++ < 6) { Force(s, w.Ref); evs = BattleResolver.Apply(s, Command.Clear(w.Ref, stroke)); }
            Assert.IsTrue(evs.Exists(e => e.Type == EventType.DamageApplied && e.Actor == w.Ref && e.Target == hero.Ref), "ударил героя");
            Assert.Less(hero.Hp, hero.MaxHp);
        }

        [Test]
        public void RangedStopsWhenSomethingIsInRangeAndShootsHeroFirst()
        {
            var s = New(); var a = s.Sides[0].Squads[2]; Assert.AreEqual(Reach.Ranged, a.Squad.Reach); var hero = s.Sides[1].Hero;
            // вертикальная дорога от арбалетчиков до строки 1; враги убраны с колонки
            int cx = a.Cell.X; foreach (var q in s.Sides[1].Squads) if (q.Cell.X == cx) q.Cell = new Cell(q.Cell.X == 5 ? 4 : q.Cell.X + 1, q.Cell.Y);
            for (int y = a.Cell.Y - 1; y >= 1; y--) s.Bushes.Clear(new Cell(cx, y));
            Force(s, a.Ref);
            var evs = BattleResolver.Apply(s, Command.Clear(a.Ref, null));
            Assert.IsTrue(evs.Exists(e => e.Type == EventType.MoveStarted && e.Actor == a.Ref && e.Value > 0), "побежал вперёд");
            Assert.IsTrue(BushLogic.AnyEnemyInRange(s, a, a.Cell, BushStats.Range(a.Squad.Id)), "остановился, когда кто-то в рейндже");
            Assert.IsTrue(evs.Exists(e => e.Type == EventType.DamageApplied && e.Actor == a.Ref), "выстрелил");
            // герой в рейндже — стреляет в героя
            var s2 = New(); var a2 = s2.Sides[0].Squads[2]; var h2 = s2.Sides[1].Hero; a2.Cell = new Cell(h2.Cell.X, 2); s2.Bushes.Clear(a2.Cell);
            Force(s2, a2.Ref); var evs2 = BattleResolver.Apply(s2, Command.Clear(a2.Ref, null));
            Assert.IsTrue(evs2.Exists(e => e.Type == EventType.DamageApplied && e.Actor == a2.Ref && e.Target == h2.Ref), "герой в рейндже — цель герой");
        }

        [Test]
        public void HeroDeathEndsBattleButArmyLossDoesNot()
        {
            var s = New(); var hero = s.Sides[1].Hero; var w = s.Sides[0].Squads[0];
            foreach (var q in s.Sides[1].Squads) q.Hp = 0;   // вся вражеская армия мертва — бой идёт
            Assert.IsFalse(s.Ended);
            hero.Hp = 10; w.Cell = new Cell(hero.Cell.X, 1); s.Bushes.Clear(w.Cell);
            Assert.IsTrue(BushGrid.Adjacent(w.Cell, hero.Cell));
            Force(s, w.Ref);
            var evs = BattleResolver.Apply(s, Command.Clear(w.Ref, null));
            Assert.IsTrue(evs.Exists(e => e.Type == EventType.DamageApplied && e.Target == hero.Ref));
            Assert.IsTrue(s.Ended); Assert.AreEqual(0, s.Winner); Assert.AreEqual(EndReason.HeroDefeated, s.EndReason);
        }

        [Test]
        public void BotClearsRoadTowardPlayerHero()
        {
            var s = New(); var e = s.Sides[1].Squads[0]; Force(s, e.Ref);
            var cands = BattleResolver.LegalCommands(s).FindAll(c => c.Kind == CommandKind.Clear && c.Cells != null && c.Cells.Length > 0);
            Assert.Greater(cands.Count, 0, "у бота есть дорога к герою игрока");
            var brain = new BotBrain(Difficulty.Normal); var cmd = brain.Choose(s);
            var evs = BattleResolver.Apply(s, cmd);
            Assert.IsTrue(evs.Exists(ev => ev.Type == EventType.MoveStarted && ev.Actor == e.Ref && ev.Value > 0), "бот побежал к герою игрока");
            Assert.Less(BushGrid.Steps(e.Cell, s.Sides[0].Hero.Cell), BushGrid.Steps(BushGrid.Home(1, e.Row, e.Slot), s.Sides[0].Hero.Cell));
        }

        [Test]
        public void HeadlessMatchFinishesByHeroDeath()
        {
            var cfg = BattleConfig.CreateDefault();
            int heroEnds = 0, limits = 0;
            for (int seed = 1; seed <= 6; seed++)
            {
                var s = Headless.Play(ArmySetup.FromPreset(PresetId.StormWall), ArmySetup.FromPreset(PresetId.PlagueMarch), cfg, seed);
                Assert.IsTrue(s.Ended);
                if (s.EndReason == EndReason.HeroDefeated) heroEnds++; else if (s.EndReason == EndReason.RoundLimit) limits++;
                Assert.AreNotEqual(EndReason.ArmyDestroyed, s.EndReason, "гибель армии бой не заканчивает");
            }
            Assert.Greater(heroEnds, 0, "хоть один бой из шести закончился смертью героя");
        }
    }
}
