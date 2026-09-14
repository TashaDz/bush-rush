using System;
using System.Collections.Generic;

namespace Warbands.Sim
{
    /// §18.2: Command → BattleResolver → CombatEvent[]. Детерминированный, без ссылок на Unity.
    /// Один и тот же резолвер обслуживает игрока, бота, автоплей и тесты.
    public static partial class BattleResolver
    {
        // ---------- создание ----------

        public static BattleState Create(ArmySetup a, ArmySetup b, BattleConfig cfg, int seed, string labelA = "Player", string labelB = "Bot")
        {
            string ea = a.ValidatePlacement(cfg.heroOffGrid); if (ea != null) throw new ArgumentException("side A: " + ea);
            string eb = b.ValidatePlacement(cfg.heroOffGrid); if (eb != null) throw new ArgumentException("side B: " + eb);
            var s = new BattleState { Cfg = cfg, Seed = seed, Rng = new Rng(seed) };
            if (cfg.bushField) s.Bushes = BushState.Fresh();   // ветка bush-field: кусты на всём поле
            s.Sides[0] = MakeSide(0, a, labelA, cfg);
            s.Sides[1] = MakeSide(1, b, labelB, cfg);
            if (cfg.bushField)
            {
                foreach (var sd in s.Sides) foreach (var q in sd.Squads) s.Bushes.Clear(q.Cell, -1000);   // под отрядами чисто
                if (cfg.rushMode) { foreach (var sd in s.Sides) { sd.Hero.Cell = BushGrid.HeroCell(sd.Index); s.Bushes.Clear(sd.Hero.Cell, -1000); } FlowLogic.InitFighters(s); }   // Bush Rush: герои на поле по центру своих краёв; бойцы — по гексам
                BushLogic.Generate(s);   // препятствия и бонусы
            }
            var evs = new List<CombatEvent>();
            Emit(s, evs, EventType.BattleStarted);
            StartRound(s, 1, evs);
            return s;
        }

        static SideState MakeSide(int index, ArmySetup setup, string label, BattleConfig cfg)
        {
            var hd = Heroes.Get(setup.Hero, cfg.balanceProfile).WithVariant(setup.UltVariant);
            var side = new SideState { Index = index, HeroDef = hd, Preset = setup.Preset, Label = label, ShieldMinSquads = cfg.shieldMinSquads, HeroOffGrid = cfg.heroOffGrid };
            side.Hero = new Unit { Side = index, Index = -1, Hero = hd, Name = hd.Name, MaxHp = hd.Hp, Hp = hd.Hp, Initiative = hd.Initiative, Row = Row.Back, Slot = 0 };
            for (int i = 0; i < 4; i++)
            {
                var def = Cards.Get(setup.Squads[i], cfg.balanceProfile);
                side.Squads[i] = new Unit { Side = index, Index = i, Squad = def, Name = def.Name, MaxHp = def.MaxHp, Hp = def.MaxHp, Initiative = def.Initiative, Row = setup.Rows[i], Slot = setup.Slots[i] };
                if (cfg.bushField) side.Squads[i].Cell = BushGrid.Home(index, setup.Rows[i], setup.Slots[i]);
            }
            return side;
        }

        static CombatEvent Emit(BattleState s, List<CombatEvent> evs, EventType t, Ref? actor = null, Ref? target = null, int value = 0, int extra = 0, string text = null)
        {
            var e = new CombatEvent { Type = t, Actor = actor ?? Ref.None, Target = target ?? Ref.None, Value = value, Extra = extra, Text = text, Round = s.Round };
            evs.Add(e); s.Log.Add(e);
            return e;
        }

        // ---------- раунды и очередь §7.1 ----------

        static void StartRound(BattleState s, int round, List<CombatEvent> evs)
        {
            s.Round = round;
            BuildQueue(s);
            s.QueuePos = 0;
            Emit(s, evs, EventType.RoundStarted, Ref.None, Ref.None, round);
            // bush-field: бонусы перегенерируются раз в N ходов (BeginTurn), а не по раундам (автор 11.09)
            if (s.Cfg.shieldUntilRound > 0 && round > s.Cfg.shieldUntilRound && !s.Cfg.heroOffGrid)
                for (int side = 0; side < 2; side++)
                {
                    var ss = s.Sides[side];
                    if (ss.ShieldExpired) continue;
                    bool was = ss.HeroShielded; ss.ShieldExpired = true;
                    if (was) { ss.ShieldEverRemoved = true; Emit(s, evs, EventType.HeroShieldRemoved, Ref.None, ss.Hero.Ref, 0, 0, "VULNERABLE"); }
                }
            BeginTurn(s, evs);
        }

        public static void BuildQueue(BattleState s)
        {
            var q = new List<Ref>();
            for (int side = 0; side < 2; side++) foreach (var u in s.Units(side)) if (u.Alive && !(u.IsHero && s.Cfg.heroOffGrid)) q.Add(u.Ref);
            int prioritySide = s.Round % 2 == 1 ? 0 : 1;
            // §24.2: равные инициативы — общая перемешанная группа; отдельный детерминированный поток RNG от сида и раунда
            var tie = new Dictionary<Ref, uint>();
            if (s.Cfg.shuffleEqualInitiative) { var rng = new Rng(s.Seed * 7919 + s.Round * 104729 + 17); foreach (var r in q) tie[r] = rng.Next(); }
            q.Sort((x, y) =>
            {
                var a = s.Get(x); var b = s.Get(y);
                if (a.Initiative != b.Initiative) return b.Initiative.CompareTo(a.Initiative);
                if (s.Cfg.shuffleEqualInitiative) { int c = tie[x].CompareTo(tie[y]); if (c != 0) return c; }
                int pa = a.Side == prioritySide ? 0 : 1, pb = b.Side == prioritySide ? 0 : 1;
                if (pa != pb) return pa.CompareTo(pb);
                if (a.IsHero != b.IsHero) return a.IsHero ? -1 : 1;
                if (a.Row != b.Row) return a.Row.CompareTo(b.Row);
                return a.Slot.CompareTo(b.Slot);
            });
            s.Queue = q;
        }

        /// Пропускает погибших, снимает связь связующего перед его ходом, закрывает раунд, когда очередь кончилась.
        static void BeginTurn(BattleState s, List<CombatEvent> evs)
        {
            while (!s.Ended)
            {
                if (s.QueuePos >= s.Queue.Count) { EndRound(s, evs); return; }
                var actor = s.Actor;
                if (!actor.Alive) { s.QueuePos++; continue; }
                if (actor.LinkTarget >= 0) BreakLink(s, actor, evs);
                s.Sides[0].UltUsedThisTurn = false; s.Sides[1].UltUsedThisTurn = false;
                s.Sides[actor.Side].Turns++;
                s.TurnIndex++;
                if (s.Cfg.bushField && s.Bushes != null && s.Cfg.bushRegrowTurns > 0)
                {
                    // отрастание: срок вышел и клетка свободна
                    var back = new List<Cell>(); var gone = new List<Cell>();
                    foreach (var c in s.Bushes.Due(s.TurnIndex, s.Cfg.bushRegrowTurns)) if (BushLogic.OccupantAt(s, c) == null)
                    {
                        s.Bushes.Regrow(c); back.Add(c);
                        if (s.Bushes.BonusAt(c) != BonusKind.None) { s.Bushes.SetBonus(c, BonusKind.None); gone.Add(c); }   // заросло над бонусом — бонус пропал (автор 11.09)
                    }
                    if (back.Count > 0) Emit(s, evs, EventType.BushRegrown, Ref.None, Ref.None, back.Count).Text = BushGrid.Encode(back);
                    // раз в bushBonusRelocateTurns ходов бонусы исчезают и появляются в других местах
                    if (s.Cfg.bushBonusRelocateTurns > 0 && s.TurnIndex % s.Cfg.bushBonusRelocateTurns == 0)
                    {
                        int n = 0;
                        for (int y = 0; y < BushGrid.Rows; y++) for (int x = 0; x < BushGrid.Cols; x++) { var c = new Cell(x, y); if (s.Bushes.BonusAt(c) != BonusKind.None) { s.Bushes.SetBonus(c, BonusKind.None); gone.Add(c); n++; } }
                        if (gone.Count > 0) Emit(s, evs, EventType.BonusRemoved, Ref.None, Ref.None, gone.Count).Text = BushGrid.Encode(gone);
                        gone = new List<Cell>();
                        for (int i = 0; i < n; i++) if (BushLogic.SpawnBonus(s, out var bc2, out var bk2)) Emit(s, evs, EventType.BonusSpawned, Ref.None, Ref.None, (int)bk2).Text = BushGrid.Encode(new[] { bc2 });
                    }
                    if (gone.Count > 0) Emit(s, evs, EventType.BonusRemoved, Ref.None, Ref.None, gone.Count).Text = BushGrid.Encode(gone);
                }
                if (s.Cfg.bushField && !actor.IsHero) { var g = BushLogic.PickGoal(s, actor); actor.TargetRef = g.Unit != null ? g.Unit.Ref : Ref.None; actor.GoalCell = g.Cell; actor.GoalIsBonus = g.IsBonus; }   // bush-field: у каждого своя цель (враг / союзник / бонус)
                Emit(s, evs, EventType.TurnStarted, actor.Ref, Ref.None, actor.Initiative);
                return;
            }
        }

        static void EndRound(BattleState s, List<CombatEvent> evs)
        {
            for (int side = 0; side < 2; side++)
                foreach (var u in s.Units(side))
                    for (int i = u.Statuses.Count - 1; i >= 0; i--)
                    {
                        var st = u.Statuses[i];
                        if (st.Kind == StatusKind.Linked) continue;               // живёт до хода связующего
                        if (st.ExpiresAfterRound <= s.Round)
                        {
                            if (st.Kind == StatusKind.Empowered) { var bs = BuffSource(s, u.Side, st); if (bs != null) bs.BuffsExpired++; }
                            u.Statuses.RemoveAt(i);
                            Emit(s, evs, EventType.StatusRemoved, Ref.None, u.Ref, st.Value, 0, st.Kind.ToString()).Status = st.Kind;
                        }
                    }
            Emit(s, evs, EventType.RoundEnded, Ref.None, Ref.None, s.Round);
            if (s.Round >= s.Cfg.rounds) { EndByScore(s, evs); return; }
            StartRound(s, s.Round + 1, evs);
        }

        static Unit BuffSource(BattleState s, int side, Status st) => st.SourceIsHero ? s.Sides[side].Hero : st.Source < 0 ? null : s.Sides[side].Squads[st.Source];

        static void EndByScore(BattleState s, List<CombatEvent> evs)
        {
            int a = s.Sides[0].HpScore, b = s.Sides[1].HpScore;
            s.Ended = true; s.EndReason = EndReason.RoundLimit;
            s.Winner = a > b ? 0 : b > a ? 1 : -1;
            Emit(s, evs, EventType.BattleEnded, Ref.None, Ref.None, a, b, s.Winner < 0 ? "DRAW" : "Higher remaining HP after Round " + s.Cfg.rounds);
        }

        // ---------- доступ к целям §6.3 ----------

        public static bool CanAttack(BattleState s, Unit attacker, Unit target)
        {
            if (target == null || !target.Alive || target.Side == attacker.Side) return false;
            var enemy = s.Sides[target.Side];
            if (target.IsHero && (enemy.HeroShielded || s.Cfg.heroOffGrid)) return false;
            if (attacker.IsHero) return true;
            switch (attacker.Squad.Reach)
            {
                case Reach.Melee:
                    // герой без щита — цель по правилам заднего ряда (эндшпиль, решение автора 06.09)
                    bool frontAlive = false;
                    foreach (var q in enemy.Squads) if (q.Alive && q.Row == Row.Front) { frontAlive = true; break; }
                    return frontAlive ? target.Row == Row.Front && !target.IsHero : true;
                default: return true;
            }
        }

        static List<Ref> AttackTargets(BattleState s, Unit attacker)
        {
            var l = new List<Ref>();
            foreach (var u in s.Units(1 - attacker.Side)) if (CanAttack(s, attacker, u)) l.Add(u.Ref);
            return l;
        }

        /// Кандидаты маршрутов для ближника: список списков целей. Рантайм подставляет геометрию поля (прямые линии через пачки);
        /// по умолчанию (тесты, headless без геометрии) — по одной цели из AttackTargets без учёта переднего ряда: задний ряд закрыт, пока жив передний.
        public static Func<BattleState, Unit, List<List<Ref>>> RouteResolver;
        public static List<List<Ref>> RouteCandidates(BattleState s, Unit actor)
        {
            var res = new List<List<Ref>>();
            if (RouteResolver != null)
            {
                foreach (var cand in RouteResolver(s, actor)) if (ValidRoute(s, actor, cand)) res.Add(cand);
                if (res.Count > 0) return res;
            }
            foreach (var t in AttackTargets(s, actor)) { var u = s.Get(t); if (!u.IsHero) { var one = new List<Ref> { t }; if (ValidRoute(s, actor, one)) res.Add(one); } }
            return res;
        }
        /// Маршрут допустим: 1–N живых вражеских отрядов без повторов; ближник (не ассасин) задевает задний ряд только после переднего, пока передний жив.
        public static bool ValidRoute(BattleState s, Unit actor, IList<Ref> targets)
        {
            if (targets == null || targets.Count == 0 || targets.Count > s.Cfg.routeMaxTargets) return false;
            int enemy = 1 - actor.Side; bool frontAlive = false;
            foreach (var q in s.Sides[enemy].Squads) if (q.Alive && q.Row == Row.Front) frontAlive = true;
            bool assassin = actor.Squad != null && actor.Squad.Reach == Reach.Assassin;
            bool passedFront = false;
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i]; if (t.Side != enemy || t.IsHero) return false;
                var u = s.Get(t); if (u == null || !u.Alive) return false;
                for (int j = 0; j < i; j++) if (targets[j] == t) return false;
                if (u.Row == Row.Front) passedFront = true;
                else if (s.Cfg.routeRearNeedsFront && frontAlive && !assassin && !passedFront) return false;
            }
            return true;
        }

        public static List<Command> LegalCommands(BattleState s)
        {
            var l = new List<Command>();
            if (s.Ended) return l;
            var actor = s.Actor; if (actor == null) return l;
            int side = actor.Side, enemy = 1 - side;
            var mine = s.Sides[side]; var theirs = s.Sides[enemy];

            if (!actor.IsHero && s.Cfg.bushField)
            {
                // bush-field: ход отряда — только расчистка (0..бюджет клеток), бег и удар автоматически; ульта героя — свободное действие
                foreach (var c in BushLogic.ClearCandidates(s, actor)) l.Add(c);
                if (s.Cfg.heroOffGrid && !mine.UltUsedThisTurn && UltimateAvailable(s, side)) l.Add(Command.Make(CommandKind.Ultimate, mine.Hero.Ref, Ref.None));
                return l;
            }
            if (!actor.IsHero)
            {
                var def = actor.Squad;
                if (def.AoeAll)
                {
                    // §24.2: один AoE по всем вражеским отрядам; одиночный удар только по уязвимому герою
                    if (theirs.AliveSquads > 0) l.Add(Command.AoeAll(actor.Ref, enemy));
                    if (CanAttack(s, actor, theirs.Hero)) l.Add(Command.Make(CommandKind.Attack, actor.Ref, theirs.Hero.Ref));
                }
                else if (def.RowAttack)
                {
                    bool f = false, b = false;
                    foreach (var q in theirs.Squads) if (q.Alive) { if (q.Row == Row.Front) f = true; else b = true; }
                    if (f) l.Add(Command.RowAttack(actor.Ref, enemy, Row.Front));
                    if (b) l.Add(Command.RowAttack(actor.Ref, enemy, Row.Back));
                    if (CanAttack(s, actor, theirs.Hero)) l.Add(Command.Make(CommandKind.Attack, actor.Ref, theirs.Hero.Ref));   // герой не часть ряда — одиночный удар
                }
                else if (s.Cfg.routeEnabled && def.Reach != Reach.Ranged && !def.IsSupport)
                {
                    // маршрут: варианты от резолвера (геометрия поля в рантайме; по умолчанию — по одной цели)
                    foreach (var route in RouteCandidates(s, actor)) l.Add(Command.Route(actor.Ref, route));
                    if (CanAttack(s, actor, theirs.Hero)) l.Add(Command.Make(CommandKind.Attack, actor.Ref, theirs.Hero.Ref));
                }
                else foreach (var t in AttackTargets(s, actor)) l.Add(Command.Make(CommandKind.Attack, actor.Ref, t));

                switch (def.Support)
                {
                    case SupportKind.Heal:
                        foreach (var q in mine.Squads) if (q.Alive && q.Tag != UnitTag.Undead && q.Hp < q.MaxHp) l.Add(Command.Make(CommandKind.Support, actor.Ref, q.Ref));
                        if (!mine.HeroShielded && mine.Hero.Hp < mine.Hero.MaxHp) l.Add(Command.Make(CommandKind.Support, actor.Ref, mine.Hero.Ref));
                        break;
                    case SupportKind.Restore:
                        foreach (var q in mine.Squads) if (q.Alive && q.Tag == UnitTag.Undead && q.Hp < q.MaxHp) l.Add(Command.Make(CommandKind.Support, actor.Ref, q.Ref));
                        break;
                    case SupportKind.Link:
                        foreach (var q in mine.Squads) if (q.Alive && q.Index != actor.Index) l.Add(Command.Make(CommandKind.Support, actor.Ref, q.Ref));
                        break;
                    case SupportKind.Empower:   // §24.3: себе или другому живому союзному отряду, не герою
                        foreach (var q in mine.Squads) if (q.Alive) l.Add(Command.Make(CommandKind.Support, actor.Ref, q.Ref));
                        break;
                }
                // v0.4: ульта героя — свободное действие в ход своей команды, одна за ход
                if (s.Cfg.heroOffGrid && !mine.UltUsedThisTurn && UltimateAvailable(s, side)) l.Add(Command.Make(CommandKind.Ultimate, mine.Hero.Ref, Ref.None));
                return l;
            }

            foreach (var t in AttackTargets(s, actor)) l.Add(Command.Make(CommandKind.Attack, actor.Ref, t));
            if (s.Round >= mine.SkillReadyRound)
            {
                switch (mine.HeroDef.Skill)
                {
                    case HeroSkill.RaiseTheFallen:
                        foreach (var q in mine.Squads) if (q.Alive && q.Tag == UnitTag.Undead && q.Hp < q.MaxHp) l.Add(Command.Make(CommandKind.Skill, actor.Ref, q.Ref));
                        break;
                    case HeroSkill.HolyLight:
                        foreach (var q in mine.Squads) if (q.Alive && q.Tag != UnitTag.Undead && q.Hp < q.MaxHp) l.Add(Command.Make(CommandKind.Skill, actor.Ref, q.Ref));
                        if (!mine.HeroShielded && actor.Hp < actor.MaxHp) l.Add(Command.Make(CommandKind.Skill, actor.Ref, actor.Ref));
                        break;
                    case HeroSkill.BloodChant:
                        foreach (var q in mine.Squads) if (q.Alive) l.Add(Command.Make(CommandKind.Skill, actor.Ref, q.Ref));
                        break;
                    case HeroSkill.Fracture:
                        foreach (var q in theirs.Squads) if (q.Alive) l.Add(Command.Make(CommandKind.Skill, actor.Ref, q.Ref));
                        break;
                }
            }
            if (UltimateAvailable(s, side)) l.Add(Command.Make(CommandKind.Ultimate, actor.Ref, Ref.None));
            return l;
        }

        public static bool UltimateAvailable(BattleState s, int side) => s.Sides[side].Charge >= s.Cfg.UltCostFor(s.Sides[side].HeroDef) - 1e-4f && s.Round >= s.Cfg.ultimateUnlockRound;
        public static int SkillCooldownTurns(BattleState s, int side) => Math.Max(0, s.Sides[side].SkillReadyRound - s.Round);

        /// Почему цель недопустима (§6.3 подсказки).
        public static string WhyNotTarget(BattleState s, Unit target)
        {
            if (target == null || !target.Alive) return "Defeated";
            if (target.IsHero && s.Cfg.heroOffGrid) return "Heroes stay off the field";
            if (target.IsHero && s.Sides[target.Side].HeroShielded) return "Hero shielded";
            var actor = s.Actor;
            if (actor != null && !actor.IsHero && actor.Squad.Reach == Reach.Melee && (target.Row == Row.Back || target.IsHero) && target.Side != actor.Side) return "Protected by the frontline";
            return "Not a valid target";
        }

        /// §7.2: при истечении таймера — базовая атака по допустимой цели с наименьшим HP.
        public static Command AutoCommand(BattleState s)
        {
            var legal = LegalCommands(s);
            if (s.Cfg.bushField && s.Actor != null && !s.Actor.IsHero) return Command.Clear(s.Actor.Ref, null);   // bush-field: таймер вышел — бежит как есть
            Command best = default; int bestHp = int.MaxValue; bool found = false;
            foreach (var c in legal)
            {
                if (c.Kind != CommandKind.Attack && c.Kind != CommandKind.Route) continue;
                int hp;
                if (c.Kind == CommandKind.Route) hp = s.Get(c.Target).Hp - 1;   // маршрут в одну цель ≈ атака; предпочитаем короткие
                else if (c.TargetRow >= 0)
                {
                    hp = int.MaxValue;
                    foreach (var q in s.Sides[c.RowSide].Squads) if (q.Alive && (c.IsAoeAll || q.Row == c.RowValue)) hp = Math.Min(hp, q.Hp);
                }
                else hp = s.Get(c.Target).Hp;
                if (hp < bestHp) { bestHp = hp; best = c; found = true; }
            }
            if (!found && legal.Count > 0) best = legal[0];
            return best;
        }

        /// Для тестов: сделать текущим ходом указанную сущность (вставка в очередь).
        public static void ForceCurrent(BattleState s, Ref r)
        {
            s.Queue.Insert(s.QueuePos, r);
        }

        // ---------- применение хода ----------

        public static List<CombatEvent> Apply(BattleState s, Command cmd)
        {
            var evs = new List<CombatEvent>();
            if (s.Ended) return evs;
            bool legal = false;
            foreach (var c in LegalCommands(s)) if (cmd.Kind == CommandKind.Route ? c.SameRoute(cmd) : (c.Kind == cmd.Kind && c.Actor == cmd.Actor && c.Target == cmd.Target && c.TargetRow == cmd.TargetRow)) { legal = true; break; }
            if (cmd.Kind == CommandKind.Clear) legal = s.Cfg.bushField && s.Actor != null && s.Actor.Ref == cmd.Actor && !s.Actor.IsHero && ValidClear(s, cmd.Cells, out _);   // клетки игрока проверяем напрямую
            if (!legal && cmd.Kind == CommandKind.Route && s.Actor != null && s.Actor.Ref == cmd.Actor && !s.Actor.IsHero && s.Actor.Squad.Reach != Reach.Ranged) { var rt = new List<Ref>(); for (int i = 0; i < cmd.RouteLen; i++) rt.Add(cmd.RouteTarget(i)); legal = ValidRoute(s, s.Actor, rt); }   // нарисованный игроком маршрут
            if (!legal) throw new InvalidOperationException("illegal command " + cmd + " for " + s.Current);

            var actor = s.Get(cmd.Actor);
            var side = s.Sides[actor.Side];
            Emit(s, evs, EventType.ActionStarted, actor.Ref, cmd.Target, cmd.TargetRow).Action = cmd.Kind;
            s.Actions++; actor.Actions++; if (actor.FirstActionRound == 0) actor.FirstActionRound = s.Round;

            switch (cmd.Kind)
            {
                case CommandKind.Attack: DoAttack(s, actor, cmd, evs); break;
                case CommandKind.Route: DoRoute(s, actor, cmd, evs); break;
                case CommandKind.Clear: DoBushTurn(s, actor, cmd, evs); break;
                case CommandKind.Support: DoSupport(s, actor, s.Get(cmd.Target), evs); break;
                case CommandKind.Skill:
                    side.SkillReadyRound = s.Round + s.Cfg.skillCooldownRounds;
                    Emit(s, evs, EventType.SkillUsed, actor.Ref, cmd.Target, 0, 0, side.HeroDef.SkillName);
                    DoSkill(s, actor, s.Get(cmd.Target), evs);
                    break;
                case CommandKind.Ultimate:
                    { float uc = s.Cfg.UltCostFor(side.HeroDef); side.Charge -= uc; side.UltsUsed++; side.ChargeSpent += uc; } side.UltRounds.Add(s.Round);
                    Emit(s, evs, EventType.UltimateUsed, actor.Ref, Ref.None, (int)Math.Round(side.Charge), 0, side.HeroDef.UltName);
                    DoUltimate(s, actor, evs);
                    if (s.Cfg.heroOffGrid) { side.UltUsedThisTurn = true; return evs; }   // свободное действие: ход отряда продолжается
                    break;
            }

            if (s.Cfg.rushMode) FlowLogic.SyncAll(s);   // ульты/лечение меняют число бойцов — расставить
            if (!s.Ended) { s.QueuePos++; BeginTurn(s, evs); }
            return evs;
        }

        static void DoAttack(BattleState s, Unit actor, Command cmd, List<CombatEvent> evs)
        {
            var emp = actor.Get(StatusKind.Empowered);
            bool empowered = emp != null;
            float empMult = empowered ? 1f + (emp.Value > 0 ? emp.Value / 100f : s.Cfg.empoweredBonus) : 1f;
            int dealtBefore = actor.DamageDealt + actor.AbsorbedDealt;
            if (cmd.TargetRow >= 0)
            {
                var targets = new List<Unit>();
                foreach (var q in s.Sides[cmd.RowSide].Squads) if (q.Alive && (cmd.IsAoeAll || q.Row == cmd.RowValue)) targets.Add(q);
                foreach (var t in targets)
                {
                    if (s.Ended) break;
                    if (!RollAttack(s, actor, t, out float roll, out bool crit, evs)) continue;   // промах
                    DealDamage(s, actor, t, actor.Squad.Output, actor.Squad.DamageType, false, empMult * roll, evs);
                    if (crit) MarkCrit(s, evs, t.Ref);
                    if (t.Alive && actor.Squad.Passive == Passive.Plague) ApplyStatus(s, actor, t, StatusKind.Plagued, 0, evs);
                }
            }
            else
            {
                var target = s.Get(cmd.Target);
                int flat = actor.FlatBonus; actor.FlatBonus = 0;      // +25 от прошлого снятия — на эту атаку
                if (!actor.IsHero && actor.Squad.Passive == Passive.Dispel && Dispel(s, actor, target, evs)) actor.FlatBonus = s.Cfg.spellEaterFlat;   // а это — на следующую
                int baseDmg = actor.IsHero ? actor.Hero.Attack : actor.Squad.Output;
                var type = actor.IsHero ? actor.Hero.AttackType : actor.Squad.DamageType;
                if (RollAttack(s, actor, target, out float roll, out bool crit, evs))
                {
                    DealDamage(s, actor, target, baseDmg, type, actor.IsHero, empMult * roll, evs, flat);
                    if (crit) MarkCrit(s, evs, target.Ref);
                    if (!actor.IsHero && actor.Squad.Passive == Passive.Plague && target.Alive) ApplyStatus(s, actor, target, StatusKind.Plagued, 0, evs);
                }
                s.LastAttackTarget[actor.Side] = cmd.Target;
            }
            if (empowered)
            {
                // §24.5: бафф расходуется один раз после всего действия (и AoE); телеметрия — источнику баффа
                var src = BuffSource(s, actor.Side, emp);
                if (src != null) { src.BuffsUsed++; int dealt = actor.DamageDealt + actor.AbsorbedDealt - dealtBefore; src.BuffExtraDamage += dealt - RoundInt(dealt / empMult); }
                actor.Remove(StatusKind.Empowered); Emit(s, evs, EventType.StatusRemoved, Ref.None, actor.Ref, 0, 0, "Empowered").Status = StatusKind.Empowered;
            }
        }

        static void DoSupport(BattleState s, Unit actor, Unit target, List<CombatEvent> evs)
        {
            switch (actor.Squad.Support)
            {
                case SupportKind.Heal: Heal(s, actor, target, actor.Squad.SupportAmount, false, evs); break;
                case SupportKind.Restore: Heal(s, actor, target, actor.Squad.SupportAmount, true, evs); break;
                case SupportKind.Link: ApplyStatus(s, actor, target, StatusKind.Linked, 0, evs); break;
                case SupportKind.Empower: ApplyStatus(s, actor, target, StatusKind.Empowered, actor.Squad.SupportAmount > 0 ? actor.Squad.SupportAmount : RoundInt(s.Cfg.weaverEmpower * 100), evs, s.Round + 1); break;
            }
        }

        static void DoSkill(BattleState s, Unit hero, Unit target, List<CombatEvent> evs)
        {
            switch (hero.Hero.Skill)
            {
                case HeroSkill.RaiseTheFallen: Heal(s, hero, target, hero.Hero.SkillAmount, true, evs); break;
                case HeroSkill.HolyLight: Heal(s, hero, target, hero.Hero.SkillAmount, false, evs); break;
                case HeroSkill.BloodChant: ApplyStatus(s, hero, target, StatusKind.Empowered, RoundInt(s.Cfg.empoweredBonus * 100), evs, s.Round); break;
                case HeroSkill.Fracture: ApplyStatus(s, hero, target, StatusKind.Exposed, 0, evs); break;
            }
        }

        /// Маршрут: удары по задетым по порядку (100 / split2 / split3 от Output, с бросками), затем обратный путь —
        /// каждая выжившая задетая пачка бьёт бегущих на routeCounter от своего Output (тоже с бросками). Урон бегущим не может убить их ниже… может — это риск маршрута.
        static void DoRoute(BattleState s, Unit actor, Command cmd, List<CombatEvent> evs)
        {
            var emp = actor.Get(StatusKind.Empowered);
            float empMult = emp != null ? 1f + (emp.Value > 0 ? emp.Value / 100f : s.Cfg.empoweredBonus) : 1f;
            var touched = new List<Unit>();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < cmd.RouteLen; i++) { var t = s.Get(cmd.RouteTarget(i)); if (t != null) { touched.Add(t); if (sb.Length > 0) sb.Append(','); sb.Append(t.Index); } }
            Emit(s, evs, EventType.RouteStarted, actor.Ref, cmd.Target, cmd.RouteLen).Text = sb.ToString();
            for (int i = 0; i < touched.Count; i++)
            {
                if (s.Ended) return;
                var t = touched[i]; if (!t.Alive) continue;
                float split = i == 0 ? s.Cfg.routeSplit1 : i == 1 ? s.Cfg.routeSplit2 : s.Cfg.routeSplit3;
                if (!RollAttack(s, actor, t, out float roll, out bool crit, evs)) continue;
                DealDamage(s, actor, t, actor.Squad.Output, actor.Squad.DamageType, false, empMult * roll * split, evs);
                if (crit) MarkCrit(s, evs, t.Ref);
                if (!actor.IsHero && actor.Squad.Passive == Passive.Plague && t.Alive) ApplyStatus(s, actor, t, StatusKind.Plagued, 0, evs);
            }
            s.LastAttackTarget[actor.Side] = cmd.Target;
            // обратный путь: задетые бьют бегущих (в обратном порядке маршрута)
            Emit(s, evs, EventType.RouteReturn, actor.Ref, Ref.None, touched.Count);
            if (s.Cfg.routeCounter <= 0f && s.Cfg.routeCounterRanged <= 0f) return;   // автор 10.09: обратный путь без ответов
            for (int i = touched.Count - 1; i >= 0; i--)
            {
                if (s.Ended || !actor.Alive) return;
                var t = touched[i]; if (!t.Alive || t.Squad == null) continue;
                if (!RollAttack(s, t, actor, out float roll, out bool crit, evs)) continue;
                float counter = (t.Squad.Reach == Reach.Ranged ? s.Cfg.routeCounterRanged : s.Cfg.routeCounter) * (actor.Squad.Reach == Reach.Assassin ? s.Cfg.routeAssassinTaken : 1f);
                DealDamage(s, t, actor, t.Squad.Output, t.Squad.DamageType, false, roll * counter, evs);
                if (crit) MarkCrit(s, evs, actor.Ref);
            }
        }

        /// bush-field: расчистка допустима — до бюджета уникальных клеток-кустов.
        public static bool ValidClear(BattleState s, IList<Cell> cells, out string why)
        {
            why = null; if (cells == null) return true;
            int bushes = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                // линия может идти и по уже чистым гексам (автор 11.09) — они задают траекторию; препятствия и повторы нельзя
                if (!BushGrid.Inside(cells[i]) || s.Bushes.IsObstacle(cells[i])) { why = "Blocked"; return false; }
                if (s.Bushes.IsBush(cells[i])) bushes++;
                for (int j = 0; j < i; j++) if (cells[j] == cells[i]) { why = "Cell repeated"; return false; }
            }
            if (s.Cfg.bushClearBudget > 0 && bushes > s.Cfg.bushClearBudget) { why = $"Only {s.Cfg.bushClearBudget} bushes per turn"; return false; }
            return true;
        }

        /// bush-field: расчищаем клетки, затем отряд сам бежит к своей цели по расчищенному и действует:
        /// ближник бьёт контакт; дальник стреляет в цель в рейндже (иначе в ближайшего); поддержка лечит/усиливает союзника в рейндже; Cabal — всех в рейндже.
        static void DoBushTurn(BattleState s, Unit actor, Command cmd, List<CombatEvent> evs)
        {
            if (cmd.Cells != null && cmd.Cells.Length > 0)
            {
                var cleared = new List<Cell>(); foreach (var c in cmd.Cells) if (s.Bushes.IsBush(c)) { s.Bushes.Clear(c, s.TurnIndex); cleared.Add(c); }   // в линии могут быть и чистые гексы — только траектория
                if (cleared.Count > 0) Emit(s, evs, EventType.BushCleared, actor.Ref, Ref.None, cleared.Count).Text = BushGrid.Encode(cleared);
            }
            if (s.Cfg.rushMode) { DoFlowTurn(s, actor, evs); return; }   // Bush Rush «как вода»: бойцы по гексам, каждый сам бежит к герою
            // цель хода считается сейчас, в момент бега — после расчистки, с учётом того, что достижимо (автор 10.09)
            Goal goal = BushLogic.PickGoal(s, actor); actor.TargetRef = goal.Unit != null ? goal.Unit.Ref : Ref.None; actor.GoalCell = goal.Cell; actor.GoalIsBonus = goal.IsBonus;
            var target = goal.Unit;
            // автор 10.09: свежая линия — приоритет: если нарисовано и линия достижима, бежим по ней (даже если к цели есть старая дорога);
            // исключение — ближник уже в контакте: бьёт с места. Иначе — своим путём к цели.
            List<Cell> path = null; Unit contact = null; bool reached = false;
            bool inContact = BushStats.IsMelee(actor.Squad) && BushLogic.AdjacentEnemy(s, actor, actor.Cell, target) != null;
            if (!inContact && cmd.Cells != null && cmd.Cells.Length > 0) { var alt = BushLogic.FollowStroke(s, actor, cmd.Cells, out var c2); if (alt.Count > 0) { path = alt; contact = c2; if (contact == null) path = BushLogic.ExtendToBonus(s, actor, path, out contact); } }   // линия кончилась рядом с бонусом — дотягиваемся остатком скорости
            if (path == null) path = BushLogic.PlanMoveBonusFirst(s, actor, goal, out contact, out reached);   // сначала бонус, потом удар
            if (path.Count > 0) actor.Cell = path[path.Count - 1];
            Emit(s, evs, EventType.MoveStarted, actor.Ref, contact != null ? contact.Ref : Ref.None, path.Count).Text = BushGrid.Encode(path);
            CollectBonuses(s, actor, path, evs);
            bool acted = DoBushAct(s, actor, target, contact, evs);
            // автор 10.09: после удара отряд старается отойти остатком скорости, чтобы не получить следующий удар
            if (acted && !s.Ended && actor.Alive && !s.Cfg.rushMode)   // Bush Rush: отхода нет — давим вперёд
            {
                var back = BushLogic.PlanRetreat(s, actor, target, BushStats.Speed(actor.Squad.Id) - path.Count);
                if (back.Count > 0) { actor.Cell = back[back.Count - 1]; Emit(s, evs, EventType.RetreatStarted, actor.Ref, Ref.None, back.Count).Text = BushGrid.Encode(back); CollectBonuses(s, actor, back, evs); }
            }
        }

        /// Бонусы на пройденных клетках (автор 10.09): ×2 — удвоить текущих бойцов (потолок растёт), +HP — вылечить bushHealBonus.
        static void CollectBonuses(BattleState s, Unit actor, List<Cell> cells, List<CombatEvent> evs)
        {
            foreach (var c in cells)
            {
                var k = s.Bushes.BonusAt(c); if (k == BonusKind.None) continue;
                s.Bushes.SetBonus(c, BonusKind.None);
                if (k == BonusKind.Double) { actor.Hp *= 2; if (actor.Hp > actor.MaxHp) actor.MaxHp = actor.Hp; }
                else { int amt = Math.Max(0, Math.Min(s.Cfg.bushHealBonus, actor.MaxHp - actor.Hp)); actor.Hp += amt; actor.HealedPool += amt; if (amt > 0) Emit(s, evs, EventType.HealApplied, actor.Ref, actor.Ref, amt); }
                Emit(s, evs, EventType.BonusTaken, actor.Ref, Ref.None, (int)k).Text = BushGrid.Encode(new[] { c });
            }
        }

        /// Действие после бега: ближник бьёт контакт; дальник стреляет; поддержка лечит/усиливает. true — действие было.
        static bool DoBushAct(BattleState s, Unit actor, Unit target, Unit contact, List<CombatEvent> evs)
        {
            var def = actor.Squad; bool melee = BushStats.IsMelee(def);
            var emp = actor.Get(StatusKind.Empowered);
            float empMult = emp != null ? 1f + (emp.Value > 0 ? emp.Value / 100f : s.Cfg.empoweredBonus) : 1f;
            if (melee)
            {
                if (contact == null || !contact.Alive) return false;
                Emit(s, evs, EventType.ActionStarted, actor.Ref, contact.Ref, -1).Action = CommandKind.Attack;
                if (!RollAttack(s, actor, contact, out float roll, out bool crit, evs)) return true;
                DealDamage(s, actor, contact, def.Output, def.DamageType, false, empMult * roll * s.Cfg.bushDamageMult, evs);
                if (crit) MarkCrit(s, evs, contact.Ref);
                s.LastAttackTarget[actor.Side] = contact.Ref;
                return true;
            }
            float range = BushStats.Range(def.Id);
            if (def.IsSupport)
            {
                // кого поддержать: цель, если она союзник в рейндже и ей нужно; иначе любой подходящий союзник в рейндже (самый раненый) — автор 11.09: хил ходил и не лечил, когда цель была бонусом
                bool Needs(Unit q) => q != null && q != actor && q.Alive && q.Side == actor.Side && BushGrid.Dist(actor.Cell, q.Cell) <= range + 1e-3f &&
                    (def.Support == SupportKind.Heal ? q.Tag != UnitTag.Undead && q.Hp < q.MaxHp
                    : def.Support == SupportKind.Restore ? q.Tag == UnitTag.Undead && q.Hp < q.MaxHp
                    : def.Support == SupportKind.Empower ? !q.Has(StatusKind.Empowered)
                    : def.Support == SupportKind.Link ? !q.Has(StatusKind.Linked) : false);
                Unit ally = Needs(target) ? target : null;
                if (ally == null) { float worst = float.MaxValue; foreach (var q in s.Sides[actor.Side].Squads) if (Needs(q)) { float key = def.Support == SupportKind.Empower || def.Support == SupportKind.Link ? 1f - q.Squad.Output / 200f : q.AliveRatio; if (key < worst) { worst = key; ally = q; } } }
                if (ally != null) { Emit(s, evs, EventType.ActionStarted, actor.Ref, ally.Ref, -1).Action = CommandKind.Support; DoSupport(s, actor, ally, evs); return true; }
                // некого поддержать — стреляем как дальник (если умеем)
                if (def.Output <= 0) return false;
            }
            var inRange = new List<Unit>();
            foreach (var q in BushLogic.Enemies(s, actor.Side)) if (BushGrid.Dist(actor.Cell, q.Cell) <= range + 1e-3f) inRange.Add(q);   // Bush Rush: герой тоже цель
            if (inRange.Count == 0) return false;
            if (def.AoeAll)
            {
                Emit(s, evs, EventType.ActionStarted, actor.Ref, Ref.None, -1).Action = CommandKind.Attack;
                foreach (var t in inRange) { if (s.Ended) break; if (!RollAttack(s, actor, t, out float roll, out bool crit, evs)) continue; DealDamage(s, actor, t, def.Output, def.DamageType, false, empMult * roll * s.Cfg.bushDamageMult, evs); if (crit) MarkCrit(s, evs, t.Ref); if (t.Alive && def.Passive == Passive.Plague) ApplyStatus(s, actor, t, StatusKind.Plagued, 0, evs); }
                return true;
            }
            Unit shot = target != null && target.Side != actor.Side && inRange.Contains(target) ? target : null;
            if (shot == null && s.Cfg.rushMode) foreach (var q in inRange) if (q.IsHero) shot = q;   // Bush Rush: герой в рейндже — стреляем в героя
            if (shot == null) { float bd = float.MaxValue; int bh = int.MaxValue; foreach (var q in inRange) { float d = BushGrid.Dist(actor.Cell, q.Cell); if (d < bd - 1e-3f || (Math.Abs(d - bd) < 1e-3f && q.Hp < bh)) { bd = d; bh = q.Hp; shot = q; } } }
            Emit(s, evs, EventType.ActionStarted, actor.Ref, shot.Ref, -1).Action = CommandKind.Attack;
            if (!RollAttack(s, actor, shot, out float r2, out bool c2, evs)) return true;
            DealDamage(s, actor, shot, def.Output, def.DamageType, false, empMult * r2 * s.Cfg.bushDamageMult, evs);
            if (c2) MarkCrit(s, evs, shot.Ref);
            s.LastAttackTarget[actor.Side] = shot.Ref;
            return true;
        }

        static void DoUltimate(BattleState s, Unit hero, List<CombatEvent> evs)
        {
            var mine = s.Sides[hero.Side]; var theirs = s.Sides[1 - hero.Side];
            switch (hero.Hero.Ult)
            {
                case HeroUlt.GraveTempest:
                    foreach (var q in theirs.Squads) { if (s.Ended) break; if (q.Alive) DealDamage(s, hero, q, hero.Hero.UltAmount, DamageType.Magic, false, 1f, evs); }
                    if (s.Cfg.ultsIgnoreShield && !s.Ended && theirs.HeroShielded) DealDamage(s, hero, theirs.Hero, hero.Hero.UltAmount, DamageType.Magic, false, 1f, evs);
                    break;
                case HeroUlt.DawnRenewal:
                    foreach (var q in mine.Squads) if (q.Alive && q.Tag != UnitTag.Undead) Heal(s, hero, q, hero.Hero.UltAmount, false, evs);
                    if (!mine.HeroShielded && !s.Cfg.heroOffGrid) Heal(s, hero, hero, hero.Hero.UltAmount, false, evs);
                    break;
                case HeroUlt.AncestorGuard:
                    foreach (var q in mine.Squads) if (q.Alive) ApplyStatus(s, hero, q, StatusKind.Shield, hero.Hero.UltAmount, evs);
                    break;
                case HeroUlt.RaiseTheFallen:   // автор 07.09: восстановление всем живым Undead-пачкам (вторая ульта Morthane)
                    foreach (var q in mine.Squads) if (q.Alive && q.Tag == UnitTag.Undead && q.Hp < q.MaxHp) Heal(s, hero, q, hero.Hero.UltAmount, true, evs);
                    break;
                case HeroUlt.Nullstorm:
                    var hit = new List<Unit>();
                    foreach (var q in theirs.Squads) { if (s.Ended) break; if (q.Alive) { DealDamage(s, hero, q, hero.Hero.UltAmount, DamageType.Magic, false, 1f, evs); hit.Add(q); } }
                    foreach (var q in hit) if (q.Alive) Dispel(s, hero, q, evs);
                    if (s.Cfg.ultsIgnoreShield && !s.Ended && theirs.HeroShielded) DealDamage(s, hero, theirs.Hero, hero.Hero.UltAmount, DamageType.Magic, false, 1f, evs);
                    break;
            }
        }

        // ---------- математика §8 ----------

        public static float OutputMultiplier(BattleConfig cfg, Unit u)
        {
            if (u.Squad == null) return 1f;
            float r = Math.Max(0f, u.AliveRatio);
            return cfg.minOutputRatio + (1f - cfg.minOutputRatio) * (float)Math.Pow(r, cfg.outputExponent);
        }

        public static float RageBonus(BattleConfig cfg, Unit u)
        {
            if (u.Squad == null || u.Squad.Passive != Passive.Rage) return 1f;
            float t = (1f - u.AliveRatio) / 0.8f; if (t < 0f) t = 0f; if (t > 1f) t = 1f;
            return 1f + cfg.rageMax * t;
        }

        static int RoundInt(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

        /// Ожидаемый урон без учёта щитов цели — для бота и подсказок на кнопках.
        public static int ExpectedDamage(BattleState s, Unit attacker, Unit target, bool basicAttack = true)
        {
            int baseDmg = attacker.IsHero ? attacker.Hero.Attack : attacker.Squad.Output;
            var type = attacker.IsHero ? attacker.Hero.AttackType : attacker.Squad.DamageType;
            var emp = attacker.Get(StatusKind.Empowered);
            float empMult = emp == null ? 1f : 1f + (emp.Value > 0 ? emp.Value / 100f : s.Cfg.empoweredBonus);
            return RoundInt(baseDmg * DamageMultiplier(s, attacker, target, type, basicAttack, empMult));
        }

        static float DamageMultiplier(BattleState s, Unit attacker, Unit target, DamageType type, bool basicAttack, float empMult)
        {
            var cfg = s.Cfg;
            float mult = empMult * cfg.damageScale * (1f + cfg.roundDamageRamp * (s.Round - 1));
            if (attacker.Squad != null)
            {
                mult *= (attacker.Squad.Passive == Passive.SteadyOutput ? 1f : OutputMultiplier(cfg, attacker)) * RageBonus(cfg, attacker);
                var mine = s.Sides[attacker.Side];
                if (mine.HeroDef.Passive == HeroPassive.WarRhythm && mine.AliveSquads == 4) mult *= 1f + cfg.warRhythm;
                if (attacker.Squad.Passive == Passive.ExecuteVolley && target.Squad != null && target.AliveRatio <= cfg.executeThreshold) mult *= 1f + cfg.executeBonus;
                if (attacker.Squad.Passive == Passive.SupportHunter && target.Squad != null && Cards.IsSupportCard(target.Squad.Id)) mult *= 1f + cfg.supportHunterBonus;
            }
            else if (attacker.Hero.Passive == HeroPassive.CleanCut && basicAttack && !target.HasPositiveStatus) mult *= 1f + cfg.cleanCut;
            if (target.Has(StatusKind.Exposed)) mult *= 1f + cfg.exposedBonus;

            if (type == DamageType.Physical)
            {
                float pierce = attacker.Squad != null && attacker.Squad.Passive == Passive.PiercingBolts ? cfg.piercing : 0f;
                if (target.Squad != null && target.Squad.Passive == Passive.ShieldWall) mult *= 1f - cfg.shieldWall * (1f - pierce);
                if (target.Squad != null && s.Sides[target.Side].HeroDef.Passive == HeroPassive.Devotion) mult *= 1f - cfg.devotion * (1f - pierce);
            }
            if (type != DamageType.Pure && target.Squad != null && target.Squad.Passive == Passive.DeathlessStand && target.AliveRatio <= cfg.deathlessThreshold) mult *= 1f - cfg.deathless;
            return mult;
        }

        /// §8.4a (автор 08.09): бросок обычной атаки — **по каждому бойцу пачки**: боец промахивается (0), иначе даёт (1 ± variance) и с шансом крита ×critMultiplier;
        /// множитель урона пачки — среднее по бойцам. Все промахнулись → Missed, урона нет. Часть промахнулась → Missed с числом (для всплывашки) и урон.
        /// Герой — один боец. В прогнозе бота (Evaluating) — матожидание без бросков.
        static bool RollAttack(BattleState s, Unit attacker, Unit target, out float roll, out bool crit, List<CombatEvent> evs)
        {
            crit = false; s.LastCrits = 0;
            var cfg = s.Cfg;
            if (s.Evaluating) { roll = (1f - cfg.missChance) * (1f + cfg.critChance * (cfg.critMultiplier - 1f)); return true; }
            int n = attacker.IsHero ? 1 : Math.Max(1, attacker.Count);
            float sum = 0f; int misses = 0, crits = 0;
            for (int i = 0; i < n; i++)
            {
                if (cfg.missChance > 0f && s.Rng.NextFloat() < cfg.missChance) { misses++; continue; }
                float f = cfg.damageVariance > 0f ? 1f - cfg.damageVariance + 2f * cfg.damageVariance * s.Rng.NextFloat() : 1f;
                if (cfg.critChance > 0f && s.Rng.NextFloat() < cfg.critChance) { crits++; f *= cfg.critMultiplier; }
                sum += f;
            }
            roll = sum / n; crit = crits > 0; s.LastCrits = crits;
            if (misses == n) { Emit(s, evs, EventType.Missed, attacker.Ref, target.Ref, misses).Text = "MISS"; return false; }
            if (misses > 0) Emit(s, evs, EventType.Missed, attacker.Ref, target.Ref, misses).Text = "MISS x" + misses;
            return true;
        }
        static void MarkCrit(BattleState s, List<CombatEvent> evs, Ref target)
        {
            for (int i = evs.Count - 1; i >= 0; i--) if (evs[i].Type == EventType.DamageApplied && evs[i].Target == target) { evs[i].Text = s.LastCrits > 1 ? "CRIT x" + s.LastCrits : "CRIT"; return; }
        }

        static void DealDamage(BattleState s, Unit attacker, Unit target, int baseDmg, DamageType type, bool basicAttack, float empMult, List<CombatEvent> evs, int flat = 0)
        {
            var cfg = s.Cfg;
            int dmg = RoundInt(baseDmg * DamageMultiplier(s, attacker, target, type, basicAttack, empMult)) + flat;
            if (dmg < 0) dmg = 0;

            // щит §8.1
            var shield = target.Get(StatusKind.Shield);
            if (shield != null && dmg > 0)
            {
                int absorbed = Math.Min(shield.Value, dmg);
                shield.Value -= absorbed; dmg -= absorbed; attacker.AbsorbedDealt += absorbed;
                Emit(s, evs, EventType.ShieldAbsorbed, attacker.Ref, target.Ref, absorbed, shield.Value);
                if (shield.Value <= 0) { target.Remove(StatusKind.Shield); Emit(s, evs, EventType.ShieldBroken, attacker.Ref, target.Ref).Status = StatusKind.Shield; }
            }

            // связь §9: 30 % HP-урона уходит связующему, не смертельно, без цепочек
            var linked = target.Get(StatusKind.Linked);
            if (linked != null && dmg > 0 && target.Squad != null)
            {
                var linker = s.Sides[target.Side].Squads[linked.Source];
                if (linker.Alive)
                {
                    int transfer = RoundInt(dmg * cfg.linkedShare);
                    int cap = linker.Hp - 1;
                    int actual = Math.Min(transfer, Math.Max(0, cap));
                    if (actual > 0)
                    {
                        linker.Hp -= actual; linker.HpLost += actual; attacker.DamageDealt += actual;
                        Emit(s, evs, EventType.LinkTransfer, target.Ref, linker.Ref, actual);
                        AddCharge(s, linker, actual, evs);
                        dmg -= actual;
                    }
                    if (transfer > cap) BreakLink(s, linker, evs);
                }
            }

            int hpLoss = Math.Min(target.Hp, dmg);
            int overkill = dmg - hpLoss;
            target.Hp -= hpLoss; target.HpLost += hpLoss; attacker.DamageDealt += hpLoss; attacker.OverkillDealt += overkill;
            if (target.HealedPool > 0) { int re = Math.Min(hpLoss, target.HealedPool); target.HealedPool -= re; target.ReLostHp += re; }
            var atkSide = s.Sides[attacker.Side];
            if (s.Round == 1) atkSide.DamageR1 += hpLoss; if (s.Round <= 2) atkSide.DamageR2 += hpLoss;
            var e = Emit(s, evs, EventType.DamageApplied, attacker.Ref, target.Ref, hpLoss, overkill);
            e.DamageType = type;
            if (target.Squad != null) AddCharge(s, target, hpLoss, evs);
            if (target.Squad != null && !s.Ended)
            {
                int leak = RoundInt(overkill * cfg.overkillToHero + hpLoss * cfg.chipToHero);
                var hero = s.Sides[target.Side].Hero;
                if (leak > 0 && hero.Alive)
                {
                    int hl = Math.Min(hero.Hp, leak);
                    hero.Hp -= hl; hero.HpLost += hl; attacker.DamageDealt += hl;
                    Emit(s, evs, EventType.DamageApplied, attacker.Ref, hero.Ref, hl, 0, "LEAK").DamageType = type;
                    if (!hero.Alive)
                    {
                        s.Ended = true; s.Winner = attacker.Side; s.EndReason = EndReason.HeroDefeated;
                        Emit(s, evs, EventType.BattleEnded, attacker.Ref, hero.Ref, s.Sides[0].HpScore, s.Sides[1].HpScore, "Hero defeated");
                    }
                }
            }

            if (!target.Alive)
            {
                if (target.IsHero)
                {
                    var ts = s.Sides[target.Side]; ts.HeroDiedWithUltReady = ts.Charge >= cfg.UltCostFor(ts.HeroDef); target.DiedRound = s.Round;
                    s.Ended = true; s.Winner = attacker.Side; s.EndReason = EndReason.HeroDefeated;
                    Emit(s, evs, EventType.BattleEnded, attacker.Ref, target.Ref, s.Sides[0].HpScore, s.Sides[1].HpScore, "Hero defeated");
                }
                else
                {
                    if (s.Round == 1) atkSide.KillsR1++; if (s.Round <= 2) atkSide.KillsR2++;
                    OnSquadDefeated(s, attacker, target, evs);
                }
            }
        }

        static void OnSquadDefeated(BattleState s, Unit attacker, Unit squad, List<CombatEvent> evs)
        {
            attacker.Kills++; squad.DiedRound = s.Round;
            if (squad.LinkTarget >= 0) BreakLink(s, squad, evs);
            squad.Statuses.Clear(); squad.FlatBonus = 0;
            var side = s.Sides[squad.Side];
            foreach (var q in side.Squads) { var l = q.Get(StatusKind.Linked); if (l != null && l.Source == squad.Index) q.Remove(StatusKind.Linked); }
            Emit(s, evs, EventType.SquadDefeated, attacker.Ref, squad.Ref, 0, 0, "SQUAD DEFEATED");
            if (s.Cfg.heroOffGrid)
            {
                if (side.AliveSquads == 0 && !s.Cfg.rushMode)   // Bush Rush: бой кончается только смертью героя
                {
                    s.Ended = true; s.Winner = attacker.Side; s.EndReason = EndReason.ArmyDestroyed;
                    Emit(s, evs, EventType.BattleEnded, attacker.Ref, squad.Ref, s.Sides[0].HpScore, s.Sides[1].HpScore, "Army destroyed");
                }
                return;
            }
            if (!side.HeroShielded)
            {
                side.ShieldEverRemoved = true;
                Emit(s, evs, EventType.HeroShieldRemoved, Ref.None, side.Hero.Ref, 0, 0, "VULNERABLE");
            }
        }

        static int CountAt(Unit u, int hp) => hp <= 0 ? 0 : (hp + u.Squad.HpPerFighter - 1) / u.Squad.HpPerFighter;

        /// §8.5: заряд владельцу потерявшего HP отряда.
        static void AddCharge(BattleState s, Unit squad, int hpLost, List<CombatEvent> evs)
        {
            if (hpLost <= 0 || squad.Squad == null) return;
            var cfg = s.Cfg; var side = s.Sides[squad.Side];
            float mult = 1f; string text = null;
            if (side.HeroDef.Passive == HeroPassive.SoulHarvest && squad.Tag == UnitTag.Undead)
            {
                mult = cfg.soulHarvestMultiplier;
                if (s.SoulHarvestShownRound[squad.Side] != s.Round) { s.SoulHarvestShownRound[squad.Side] = s.Round; text = "SOUL HARVEST x2"; }
            }
            float gain = cfg.soulsPerFighter
                ? Math.Max(0, CountAt(squad, squad.Hp + hpLost) - squad.Count) * mult      // 1 душа за бойца (автор 07.09)
                : (float)hpLost / squad.MaxHp * cfg.chargePerFullSquad * mult;
            if (gain <= 0f) return;
            float before = side.Charge;
            side.Charge = Math.Min(cfg.chargeMax, side.Charge + gain);
            float actual = side.Charge - before, wasted = gain - actual;
            squad.ChargeGenerated += gain; side.WastedCharge += wasted;
            Emit(s, evs, EventType.ChargeGained, squad.Ref, side.Hero.Ref, RoundInt(actual), RoundInt(gain), text);
            if (wasted > 0.5f) Emit(s, evs, EventType.ChargeWasted, squad.Ref, side.Hero.Ref, RoundInt(wasted), 0, "FULL");
            if (before < cfg.UltCostFor(side.HeroDef) && side.Charge >= cfg.UltCostFor(side.HeroDef))
                Emit(s, evs, EventType.UltimateReady, side.Hero.Ref, Ref.None, 0, 0, s.Round < cfg.ultimateUnlockRound ? "UNLOCKS ROUND " + cfg.ultimateUnlockRound : "ULTIMATE READY");
        }

        static void Heal(BattleState s, Unit caster, Unit target, int amount, bool restore, List<CombatEvent> evs)
        {
            var cfg = s.Cfg;
            float mult = OutputMultiplier(cfg, caster);
            if (restore && target.Squad != null && target.Squad.Passive == Passive.ManyBones) mult *= cfg.manyBones;
            if (target.Has(StatusKind.Plagued)) mult *= 1f - cfg.plaguedHealPenalty;
            int amt = RoundInt(amount * mult);
            int eff = Math.Min(amt, target.MaxHp - target.Hp);
            if (eff < 0) eff = 0;
            target.Hp += eff; target.HealedPool += eff; caster.HealExcess += amt - eff;
            if (restore) caster.RestoredDone += eff; else caster.HealingDone += eff;
            Emit(s, evs, restore ? EventType.RestoreApplied : EventType.HealApplied, caster.Ref, target.Ref, eff, amt - eff);
        }

        static void ApplyStatus(BattleState s, Unit source, Unit target, StatusKind kind, int value, List<CombatEvent> evs, int expiresAfterRound = -1)
        {
            bool negative = !Status.IsPositive(kind);
            if (negative && source.Side != target.Side && target.Has(StatusKind.AntiMagic))
            {
                target.Remove(StatusKind.AntiMagic);
                Emit(s, evs, EventType.StatusRemoved, source.Ref, target.Ref, 0, 0, "Anti-Magic absorbed " + kind).Status = StatusKind.AntiMagic;
                return;
            }
            var existing = target.Get(kind);
            switch (kind)
            {
                case StatusKind.Shield:
                    if (existing != null) { if (value > existing.Value) existing.Value = value; existing.ExpiresAfterRound = s.Round + 1; }
                    else target.Statuses.Add(new Status { Kind = kind, Value = value, ExpiresAfterRound = s.Round + 1 });
                    break;
                case StatusKind.Empowered:
                {
                    // §24.5: один слот — сильнее заменяет слабый со своим сроком, слабый не трогает сильный, равный обновляет срок
                    int exp = expiresAfterRound >= 0 ? expiresAfterRound : s.Round;
                    if (existing != null && existing.Value > value) { Emit(s, evs, EventType.StatusApplied, source.Ref, target.Ref, 0, 0, "Empowered kept").Status = kind; return; }
                    if (existing != null) { existing.Value = value; existing.ExpiresAfterRound = exp; existing.Source = source.Index; existing.SourceIsHero = source.IsHero; }
                    else target.Statuses.Add(new Status { Kind = kind, Value = value, ExpiresAfterRound = exp, Source = source.Index, SourceIsHero = source.IsHero });
                    source.BuffsGiven++;
                    break;
                }
                case StatusKind.Exposed:
                case StatusKind.Plagued:
                    if (existing == null) target.Statuses.Add(new Status { Kind = kind, ExpiresAfterRound = s.Round });
                    else existing.ExpiresAfterRound = s.Round;
                    break;
                case StatusKind.AntiMagic:
                    if (existing == null) target.Statuses.Add(new Status { Kind = kind, ExpiresAfterRound = s.Round + 1 });
                    else existing.ExpiresAfterRound = s.Round + 1;
                    break;
                case StatusKind.Linked:
                    if (source.LinkTarget >= 0) BreakLink(s, source, evs);
                    target.Remove(StatusKind.Linked);
                    target.Statuses.Add(new Status { Kind = kind, Source = source.Index, ExpiresAfterRound = int.MaxValue });
                    source.LinkTarget = target.Index;
                    break;
            }
            Emit(s, evs, EventType.StatusApplied, source.Ref, target.Ref, value, 0, kind.ToString()).Status = kind;
        }

        static void BreakLink(BattleState s, Unit linker, List<CombatEvent> evs)
        {
            if (linker.LinkTarget < 0) return;
            var t = s.Sides[linker.Side].Squads[linker.LinkTarget];
            linker.LinkTarget = -1;
            if (t.Remove(StatusKind.Linked)) Emit(s, evs, EventType.StatusRemoved, linker.Ref, t.Ref, 0, 0, "Linked").Status = StatusKind.Linked;
        }

        static readonly StatusKind[] DispelOrder = { StatusKind.Linked, StatusKind.Shield, StatusKind.Empowered, StatusKind.AntiMagic };
        static readonly StatusKind[] DispelOrderV03 = { StatusKind.Shield, StatusKind.Empowered };   // §24.5 Nullstorm

        /// §10 U12 / §11 Nullstorm: снять один положительный статус по приоритету Linked → Shield → Empowered → Anti-Magic.
        static bool Dispel(BattleState s, Unit actor, Unit target, List<CombatEvent> evs)
        {
            foreach (var k in s.Cfg.V03 ? DispelOrderV03 : DispelOrder)
            {
                var st = target.Get(k); if (st == null) continue;
                if (k == StatusKind.Linked)
                {
                    var linker = s.Sides[target.Side].Squads[st.Source];
                    linker.LinkTarget = -1;
                }
                target.Remove(k);
                Emit(s, evs, EventType.Dispelled, actor.Ref, target.Ref, st.Value, 0, k.ToString()).Status = k;
                return true;
            }
            return false;
        }
    }
}
