using System;
using System.Collections.Generic;

namespace Warbands.Sim
{
    /// Ссылка на сущность: сторона 0 (игрок) / 1 (бот); Index −1 — герой, 0..3 — отряд.
    public struct Ref : IEquatable<Ref>
    {
        public int Side, Index;
        public bool IsHero => Index < 0;
        public bool IsNone => Side < 0;
        public static Ref Hero(int side) => new Ref { Side = side, Index = -1 };
        public static Ref Squad(int side, int index) => new Ref { Side = side, Index = index };
        public static readonly Ref None = new Ref { Side = -1, Index = -1 };
        public bool Equals(Ref o) => Side == o.Side && Index == o.Index;
        public override bool Equals(object o) => o is Ref r && Equals(r);
        public override int GetHashCode() => Side * 16 + Index + 2;
        public static bool operator ==(Ref a, Ref b) => a.Equals(b);
        public static bool operator !=(Ref a, Ref b) => !a.Equals(b);
        public override string ToString() => IsNone ? "none" : (Side == 0 ? "P" : "E") + (IsHero ? "hero" : Index.ToString());
    }

    public enum StatusKind { Shield, Linked, Empowered, Exposed, Plagued, AntiMagic }

    /// §9. Value — остаток щита; Source — индекс связующего для Linked; ExpiresAfterRound — снимается в конце этого раунда.
    public sealed class Status
    {
        public StatusKind Kind; public int Value; public int Source = -1; public int ExpiresAfterRound;
        public bool SourceIsHero;
        public Status Clone() => (Status)MemberwiseClone();
        public static bool IsPositive(StatusKind k) => k == StatusKind.Shield || k == StatusKind.Linked || k == StatusKind.Empowered || k == StatusKind.AntiMagic;
    }

    public sealed class Unit
    {
        public int Side, Index;
        public SquadDef Squad; public HeroDef Hero;
        public string Name;
        public int MaxHp, Hp, Initiative; public Row Row; public int Slot;
        public Cell Cell; public Ref TargetRef = Ref.None; public Cell GoalCell; public bool GoalIsBonus;   // ветка bush-field: клетка и цель хода (враг/союзник или бонус на клетке)
        public List<Status> Statuses = new List<Status>();
        public int FlatBonus;              // Spell Eaters: +25 к следующей атаке
        public int LinkTarget = -1;        // Spirit Weavers: кого связали
        // телеметрия §19 / §24.9
        public int DamageDealt, HealingDone, RestoredDone, Kills; public float ChargeGenerated, HpLost;
        public int AbsorbedDealt, OverkillDealt, HealExcess, Actions, FirstActionRound, DiedRound;
        public int HealedPool, ReLostHp;                 // восстановленные HP, которые потом потеряны снова
        public int BuffsGiven, BuffsUsed, BuffsExpired, BuffExtraDamage;   // U11 / Blood Chant

        public bool IsHero => Hero != null;
        public bool Alive => Hp > 0;
        public float AliveRatio => MaxHp > 0 ? (float)Hp / MaxHp : 0f;
        public int Count => Squad == null || Hp <= 0 ? 0 : (Hp + Squad.HpPerFighter - 1) / Squad.HpPerFighter;
        public Ref Ref => IsHero ? Ref.Hero(Side) : Ref.Squad(Side, Index);
        public UnitTag Tag => Squad != null ? Squad.Tag : UnitTag.Living;

        public Status Get(StatusKind k) { foreach (var s in Statuses) if (s.Kind == k) return s; return null; }
        public bool Has(StatusKind k) => Get(k) != null;
        public int ShieldValue { get { var s = Get(StatusKind.Shield); return s != null ? s.Value : 0; } }
        public bool HasPositiveStatus { get { foreach (var s in Statuses) if (Status.IsPositive(s.Kind)) return true; return false; } }
        public bool Remove(StatusKind k) { for (int i = 0; i < Statuses.Count; i++) if (Statuses[i].Kind == k) { Statuses.RemoveAt(i); return true; } return false; }

        public Unit Clone()
        {
            var u = (Unit)MemberwiseClone();
            u.Statuses = new List<Status>(Statuses.Count);
            foreach (var s in Statuses) u.Statuses.Add(s.Clone());
            return u;
        }
    }

    public sealed class SideState
    {
        public int Index; public HeroDef HeroDef; public Unit Hero; public Unit[] Squads = new Unit[4];
        public float Charge; public int SkillReadyRound = 1; public int UltsUsed; public float WastedCharge;
        public int TimeoutActions, Turns; public bool ShieldEverRemoved;
        public List<int> UltRounds = new List<int>(); public float ChargeSpent; public bool HeroDiedWithUltReady;
        public int DamageR1, DamageR2, KillsR1, KillsR2;   // ранний burst (§24.9 п.6): нанесённый этой стороной
        public bool ShieldExpired; public int ShieldMinSquads;   // эксперименты: щит снят по раунду / порог живых отрядов
        public PresetId? Preset; public string Label;

        public bool HeroOffGrid;   // v0.4: герой не цель и не в очереди
        public bool UltUsedThisTurn;
        public bool HeroShielded => HeroOffGrid || (!ShieldExpired && AliveSquads > ShieldMinSquads);
        public int AliveSquads { get { int n = 0; foreach (var s in Squads) if (s.Alive) n++; return n; } }
        public int HpScore { get { int n = HeroOffGrid ? 0 : Hero.Hp; foreach (var s in Squads) n += s.Hp; return n; } }
        public int MaxScore { get { int n = HeroOffGrid ? 0 : Hero.MaxHp; foreach (var s in Squads) n += s.MaxHp; return n; } }

        public SideState Clone()
        {
            var c = (SideState)MemberwiseClone();
            c.UltRounds = new List<int>(UltRounds);
            c.Hero = Hero.Clone();
            c.Squads = new Unit[Squads.Length];
            for (int i = 0; i < Squads.Length; i++) c.Squads[i] = Squads[i].Clone();
            return c;
        }
    }

    public enum CommandKind { Attack, Support, Skill, Ultimate, Route, Clear }

    /// Ход: действие + цель. TargetRow ≥ 0 — атака по ряду (Plague Cabal); массовые ульты — без цели.
    public struct Command
    {
        public CommandKind Kind; public Ref Actor; public Ref Target; public int TargetRow;
        /// Маршрут (ветка route-drawing, автор 09.09): до трёх задетых по пути вражеских отрядов по порядку; Target — первый.
        public Ref Target2, Target3; public int RouteLen;
        /// Ветка bush-field: клетки кустов, которые расчищаем в этот ход (0..бюджет); отряд после этого бежит сам.
        public Cell[] Cells;
        public static Command Clear(Ref actor, IList<Cell> cells) { var a = new Cell[cells?.Count ?? 0]; for (int i = 0; i < a.Length; i++) a[i] = cells[i]; return new Command { Kind = CommandKind.Clear, Actor = actor, Target = Ref.None, Target2 = Ref.None, Target3 = Ref.None, TargetRow = -1, Cells = a }; }
        public bool SameClear(Command o)
        {
            if (o.Kind != CommandKind.Clear || Kind != CommandKind.Clear || o.Actor != Actor) return false;
            int n = Cells?.Length ?? 0, m = o.Cells?.Length ?? 0; if (n != m) return false;
            for (int i = 0; i < n; i++) if (Cells[i] != o.Cells[i]) return false;
            return true;
        }
        public static Command Make(CommandKind k, Ref actor, Ref target) => new Command { Kind = k, Actor = actor, Target = target, Target2 = Ref.None, Target3 = Ref.None, TargetRow = -1 };
        public static Command Route(Ref actor, System.Collections.Generic.IList<Ref> targets)
        {
            var c = new Command { Kind = CommandKind.Route, Actor = actor, Target = Ref.None, Target2 = Ref.None, Target3 = Ref.None, TargetRow = -1, RouteLen = System.Math.Min(3, targets.Count) };
            if (targets.Count > 0) c.Target = targets[0]; if (targets.Count > 1) c.Target2 = targets[1]; if (targets.Count > 2) c.Target3 = targets[2];
            return c;
        }
        public Ref RouteTarget(int i) => i == 0 ? Target : i == 1 ? Target2 : Target3;
        public bool SameRoute(Command o) => o.Kind == CommandKind.Route && o.Actor == Actor && o.RouteLen == RouteLen && o.Target == Target && o.Target2 == Target2 && o.Target3 == Target3;
        public static Command RowAttack(Ref actor, int enemySide, Row row) => new Command { Kind = CommandKind.Attack, Actor = actor, Target = Ref.None, TargetRow = (int)row + enemySide * 10 };
        /// §24.2: AoE по всем вражеским отрядам обоих рядов (TargetRow = 100 + сторона).
        public static Command AoeAll(Ref actor, int enemySide) => new Command { Kind = CommandKind.Attack, Actor = actor, Target = Ref.None, TargetRow = 100 + enemySide };
        public bool IsAoeAll => TargetRow >= 100;
        public int RowSide => IsAoeAll ? TargetRow - 100 : TargetRow / 10; public Row RowValue => IsAoeAll ? Row.Front : (Row)(TargetRow % 10);
        public override string ToString() => Kind == CommandKind.Clear ? $"Clear {Actor} [{BushGrid.Encode(Cells)}]" : Kind == CommandKind.Route ? $"Route {Actor}->{Target}{(RouteLen > 1 ? "," + Target2 : "")}{(RouteLen > 2 ? "," + Target3 : "")}"
            : $"{Kind} {Actor}->{(TargetRow >= 0 ? (IsAoeAll ? "all squads" : RowValue == Row.Front ? "front row" : "back row") : Target.ToString())}";
    }

    public enum EventType
    {
        BattleStarted, RoundStarted, TurnStarted, ActionStarted, DamageApplied, ShieldAbsorbed, ShieldBroken, LinkTransfer,
        HealApplied, RestoreApplied, StatusApplied, StatusRemoved, Dispelled, ChargeGained, ChargeWasted, UltimateReady,
        SquadDefeated, HeroShieldRemoved, SkillUsed, UltimateUsed, RoundEnded, BattleEnded, Timeout, Missed, RouteStarted, RouteReturn, BushCleared, MoveStarted, BushRegrown, RetreatStarted, BonusSpawned, BonusTaken, BonusRemoved
    }

    public sealed class CombatEvent
    {
        public EventType Type; public Ref Actor = Ref.None, Target = Ref.None;
        public int Value, Extra; public DamageType DamageType; public StatusKind Status; public string Text; public int Round;
        public CommandKind Action;
        public override string ToString() => $"{Type} {Actor}->{Target} {Value}{(Extra != 0 ? "/" + Extra : "")}{(Text != null ? " " + Text : "")}";
    }

    public enum EndReason { None, HeroDefeated, RoundLimit, ArmyDestroyed }

    /// xorshift32 — одинаковый сид даёт одинаковый бой (§18.3, §21).
    public sealed class Rng
    {
        uint s;
        public Rng(int seed) { s = (uint)seed * 2654435761u + 0x9E3779B9u; if (s == 0) s = 1; }
        public uint Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s; }
        public float NextFloat() => (Next() & 0xFFFFFF) / 16777216f;
        public int Range(int minInclusive, int maxExclusive) => minInclusive + (int)(Next() % (uint)(maxExclusive - minInclusive));
        public float Range(float min, float max) => min + NextFloat() * (max - min);
        public Rng Clone() => new Rng(0) { s = s };
    }

    public sealed class BattleState
    {
        public BattleConfig Cfg; public SideState[] Sides = new SideState[2];
        public int Round; public List<Ref> Queue = new List<Ref>(); public int QueuePos;
        public bool Ended; public int Winner = -1; public EndReason EndReason;
        public int Seed; public Rng Rng; public List<CombatEvent> Log = new List<CombatEvent>();
        public Ref[] LastAttackTarget = { Ref.None, Ref.None };
        public BushState Bushes; public int TurnIndex;   // ветка bush-field: кусты и сквозной номер хода (для отрастания)
        public int[] SoulHarvestShownRound = { 0, 0 };
        public int Actions;

        public Ref Current => QueuePos < Queue.Count ? Queue[QueuePos] : Ref.None;
        public Unit Actor => Current.IsNone ? null : Get(Current);
        public Unit Get(Ref r) => r.IsNone ? null : (r.IsHero ? Sides[r.Side].Hero : Sides[r.Side].Squads[r.Index]);
        public SideState Side(int i) => Sides[i];

        public IEnumerable<Unit> Units(int side) { yield return Sides[side].Hero; foreach (var s in Sides[side].Squads) yield return s; }

        /// Оставшаяся очередь текущего раунда (живые), начиная с текущего.
        public List<Ref> RemainingQueue()
        {
            var l = new List<Ref>();
            for (int i = QueuePos; i < Queue.Count; i++) if (Get(Queue[i]).Alive) l.Add(Queue[i]);
            return l;
        }

        /// Клон для прогноза бота: атаки считаются по среднему, без бросков (иначе бот «видел бы» будущие кубики).
        public bool Evaluating;
        public int LastCrits;   // критов бойцов в последней атаке (для пометки события)

        public BattleState Clone()
        {
            var c = (BattleState)MemberwiseClone();
            c.Sides = new[] { Sides[0].Clone(), Sides[1].Clone() };
            c.Queue = new List<Ref>(Queue);
            c.Log = new List<CombatEvent>();            // клон для прогноза — журнал не копируем
            c.Rng = Rng.Clone();
            c.Bushes = Bushes?.Clone();
            c.LastAttackTarget = (Ref[])LastAttackTarget.Clone();
            c.SoulHarvestShownRound = (int[])SoulHarvestShownRound.Clone();
            return c;
        }
    }
}
