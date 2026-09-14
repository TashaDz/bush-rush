using System;
using System.Collections.Generic;

namespace Warbands.Sim
{
    public enum Row { Front = 0, Back = 1 }
    public enum UnitTag { Living, Undead, Arcane }
    public enum Reach { Melee, Ranged, Assassin }
    public enum DamageType { Physical, Magic, Pure }
    public enum SupportKind { None, Heal, Restore, Link, Empower }
    public enum Passive { None, ShieldWall, Rage, ManyBones, DeathlessStand, ExecuteVolley, PiercingBolts, SupportHunter, Plague, Dispel, SteadyOutput }

    public enum SquadId
    {
        IronWardens, BloodboundReavers, BoneCohort, Graveguard, RoyalArbalists, ArmorbreakGunners,
        VeilKnives, PlagueCabal, DawnClerics, Bonecallers, SpiritWeavers, SpellEaters
    }
    public enum HeroId { Morthane, SerAldren, Varka, Ilyra }
    public enum HeroSkill { RaiseTheFallen, HolyLight, BloodChant, Fracture }
    public enum HeroUlt { GraveTempest, DawnRenewal, AncestorGuard, Nullstorm, RaiseTheFallen }
    public enum HeroPassive { SoulHarvest, Devotion, WarRhythm, CleanCut, None }
    public enum PresetId { GraveCycle, SustainedFire, RageLink, ExecutionChain, MagicEaters, LastingLight, BoneTide, PlagueMarch, RadiantLine, StormWall, ThunderVolley, ArcaneSiege }

    /// §10: карта отряда. Числа — из таблицы GDD v0.1, уровни зафиксированы на 1.
    public sealed class SquadDef
    {
        public SquadId Id; public string Name, Short, Role, Rule;
        public Row Row; public UnitTag Tag; public Reach Reach;
        public int BaseCount, HpPerFighter, Output, Initiative;
        public DamageType DamageType;
        public bool RowAttack;                 // Plague Cabal v0.2: урон каждому отряду выбранного ряда
        public bool AoeAll;                    // Plague Cabal v0.3 (§24.2): урон каждому вражескому отряду в обоих рядах одним действием
        public SupportKind Support; public int SupportAmount;
        public Passive Passive; public int VisualCap;
        public int MaxHp => BaseCount * HpPerFighter;
        public bool IsSupport => Support != SupportKind.None;
    }

    /// §11: герой.
    public sealed class HeroDef
    {
        public HeroId Id; public string Name, Short, Archetype;
        public bool Melee; public int Hp, Initiative, Attack; public DamageType AttackType;
        public HeroSkill Skill; public int SkillAmount; public string SkillName;
        public HeroUlt Ult; public int UltAmount; public string UltName;
        public HeroPassive Passive; public string PassiveName, AttackName;
        public string UltUsesHint;
        /// Вторая ульта на выбор в колоде (автор 07.09): Morthane — Raise the Fallen вместо Grave Tempest.
        public HeroUlt? AltUlt; public int AltUltAmount; public string AltUltName;
        public bool HasAltUlt => AltUlt.HasValue;
        /// Копия определения с выбранной ультой: variant 1 — альтернативная.
        public HeroDef WithVariant(int variant)
        {
            if (variant != 1 || !AltUlt.HasValue) return this;
            var c = (HeroDef)MemberwiseClone(); c.Ult = AltUlt.Value; c.UltAmount = AltUltAmount; c.UltName = AltUltName; return c;
        }
        public string UltNameOf(int variant) => variant == 1 && AltUlt.HasValue ? AltUltName : UltName;
    }

    /// §12: готовый состав.
    public sealed class ArmyPreset
    {
        public PresetId Id; public string Name, Badge, GamePlan, GamePlanV03; public int UltVariant;   // 1 — альтернативная ульта героя (Raise the Fallen)
        public HeroId Hero; public SquadId[] Front, Back;
    }

    public static class Cards
    {
        static SquadDef S(SquadId id, string name, string shortName, string role, Row row, UnitTag tag, Reach reach,
            int count, int hp, int output, DamageType dt, int init, Passive passive, string rule,
            SupportKind support = SupportKind.None, int supportAmount = 0, bool rowAttack = false, bool aoeAll = false)
            => new SquadDef
            {
                Id = id, Name = name, Short = shortName, Role = role, Row = row, Tag = tag, Reach = reach,
                BaseCount = count, HpPerFighter = hp, Output = output, DamageType = dt, Initiative = init,
                Passive = passive, Rule = rule, Support = support, SupportAmount = supportAmount, RowAttack = rowAttack, AoeAll = aoeAll,
                VisualCap = count >= 20 ? 6 : count >= 8 ? 5 : 4
            };

        public static readonly SquadDef[] All =
        {
            S(SquadId.IronWardens, "Iron Wardens", "WARDENS", "Tank", Row.Front, UnitTag.Living, Reach.Melee, 10, 50, 75, DamageType.Physical, 35, Passive.ShieldWall, "Shield Wall: -15% incoming Physical damage"),
            S(SquadId.BloodboundReavers, "Bloodbound Reavers", "REAVERS", "Berserker", Row.Front, UnitTag.Living, Reach.Melee, 8, 50, 110, DamageType.Physical, 65, Passive.Rage, "Rage: up to +25% damage as the squad thins out"),
            S(SquadId.BoneCohort, "Bone Cohort", "COHORT", "Expendable", Row.Front, UnitTag.Undead, Reach.Melee, 20, 15, 80, DamageType.Physical, 40, Passive.ManyBones, "Many Bones: restores to this squad x1.25"),
            S(SquadId.Graveguard, "Graveguard", "GRAVEGUARD", "Attrition tank", Row.Front, UnitTag.Undead, Reach.Melee, 5, 100, 70, DamageType.Physical, 25, Passive.DeathlessStand, "Deathless Stand: -15% damage at 40% HP or less"),
            S(SquadId.RoyalArbalists, "Royal Arbalists", "ARBALISTS", "Finisher", Row.Back, UnitTag.Living, Reach.Ranged, 10, 30, 115, DamageType.Physical, 55, Passive.ExecuteVolley, "Execute Volley: +20% damage vs squads at 30% HP or less"),
            S(SquadId.ArmorbreakGunners, "Armorbreak Gunners", "GUNNERS", "Steady DPS", Row.Back, UnitTag.Living, Reach.Ranged, 6, 50, 100, DamageType.Physical, 45, Passive.PiercingBolts, "Piercing Bolts: ignores 50% of Physical damage reduction"),
            S(SquadId.VeilKnives, "Veil Knives", "KNIVES", "Assassin", Row.Front, UnitTag.Living, Reach.Assassin, 6, 50, 100, DamageType.Physical, 75, Passive.SupportHunter, "Can strike the back row; +15% damage vs Support squads"),
            S(SquadId.PlagueCabal, "Plague Cabal", "CABAL", "AoE control", Row.Back, UnitTag.Undead, Reach.Ranged, 6, 40, 40, DamageType.Magic, 30, Passive.Plague, "Hits every squad in a row; applies Plagued (-25% healing)", rowAttack: true),
            S(SquadId.DawnClerics, "Dawn Clerics", "CLERICS", "Healer", Row.Back, UnitTag.Living, Reach.Ranged, 8, 35, 45, DamageType.Magic, 50, Passive.None, "Heal 100 to a Living ally, or 45 Magic", SupportKind.Heal, 100),
            S(SquadId.Bonecallers, "Bonecallers", "CALLERS", "Restorer", Row.Back, UnitTag.Undead, Reach.Ranged, 6, 40, 50, DamageType.Magic, 50, Passive.None, "Restore 100 to a living Undead squad, or 50 Magic", SupportKind.Restore, 100),
            S(SquadId.SpiritWeavers, "Spirit Weavers", "WEAVERS", "Protector", Row.Back, UnitTag.Living, Reach.Ranged, 6, 50, 55, DamageType.Magic, 70, Passive.None, "Link: takes 30% of an ally squad's HP damage, or 55 Magic", SupportKind.Link, 0),
            S(SquadId.SpellEaters, "Spell Eaters", "EATERS", "Dispeller", Row.Front, UnitTag.Arcane, Reach.Melee, 4, 100, 80, DamageType.Magic, 60, Passive.Dispel, "Attack strips one buff and grants +25 to the next attack"),
        };

        /// balance v0.3 (§24.3): единственное свойство на карту, инициатива 1–10, без старых пассивок.
        /// 07.09 (автор): численность ×2 при тех же MaxHP (Bone Cohort ×1.5, Clerics 14×20) — толпа гуще, динамика та же.
        /// Bush Rush 14.09: составы как в Warbands после −30 % (10×50 … 6×67); удвоение «лавины» откачено — бойцы крупные, до 4 в гексе.
        public static readonly SquadDef[] V03 =
        {
            S(SquadId.IronWardens, "Iron Wardens", "WARDENS", "Tank", Row.Front, UnitTag.Living, Reach.Melee, 10, 50, 100, DamageType.Physical, 3, Passive.None, "Sturdy front line"),
            S(SquadId.BloodboundReavers, "Bloodbound Reavers", "REAVERS", "Berserker", Row.Front, UnitTag.Living, Reach.Melee, 11, 36, 130, DamageType.Physical, 6, Passive.SteadyOutput, "Attack never weakens from losses while alive"),
            S(SquadId.BoneCohort, "Bone Cohort", "COHORT", "Expendable", Row.Front, UnitTag.Undead, Reach.Melee, 21, 14, 100, DamageType.Physical, 4, Passive.None, "Expendable undead; restore at normal rate"),
            S(SquadId.Graveguard, "Graveguard", "GRAVEGUARD", "Undead tank", Row.Front, UnitTag.Undead, Reach.Melee, 7, 71, 90, DamageType.Physical, 2, Passive.None, "Sturdy undead"),
            S(SquadId.RoyalArbalists, "Royal Arbalists", "ARBALISTS", "Archer", Row.Back, UnitTag.Living, Reach.Ranged, 10, 30, 140, DamageType.Physical, 6, Passive.None, "Ranged: hits any enemy squad"),
            S(SquadId.ArmorbreakGunners, "Armorbreak Gunners", "GUNNERS", "Heavy gunner", Row.Back, UnitTag.Living, Reach.Ranged, 8, 37, 170, DamageType.Physical, 2, Passive.None, "Slow, heavy ranged hit"),
            S(SquadId.VeilKnives, "Veil Knives", "KNIVES", "Assassin", Row.Front, UnitTag.Living, Reach.Assassin, 8, 37, 110, DamageType.Physical, 9, Passive.None, "Strikes through the front line"),
            S(SquadId.PlagueCabal, "Plague Cabal", "CABAL", "Area attacker", Row.Back, UnitTag.Undead, Reach.Ranged, 8, 30, 55, DamageType.Magic, 3, Passive.None, "55 Magic to every enemy squad in both rows, or 55 to an exposed hero", aoeAll: true),
            S(SquadId.DawnClerics, "Dawn Clerics", "CLERICS", "Healer", Row.Back, UnitTag.Living, Reach.Ranged, 7, 40, 50, DamageType.Magic, 5, Passive.None, "Heal 80 to a Living/Arcane ally or exposed hero, or 50 Magic", SupportKind.Heal, 80),
            S(SquadId.Bonecallers, "Bonecallers", "CALLERS", "Restorer", Row.Back, UnitTag.Undead, Reach.Ranged, 8, 30, 50, DamageType.Magic, 5, Passive.None, "Restore 100 to a living Undead squad, or 50 Magic", SupportKind.Restore, 100),
            S(SquadId.SpiritWeavers, "Spirit Weavers", "WEAVERS", "Attack booster", Row.Back, UnitTag.Living, Reach.Ranged, 8, 37, 50, DamageType.Magic, 7, Passive.None, "+40% to an ally squad's next attack, or 50 Magic", SupportKind.Empower, 40),
            S(SquadId.SpellEaters, "Spell Eaters", "EATERS", "Fighter", Row.Front, UnitTag.Arcane, Reach.Melee, 6, 67, 140, DamageType.Magic, 4, Passive.None, "Sturdy fighter, 140 Magic"),
        };

        /// Bush Rush (автор 14.09): лекари временно убраны из пачек — в колоды и на выбор не идут.
        public static bool Available(SquadId id) { var d = Get(id, 4); return d.Support != SupportKind.Heal && d.Support != SupportKind.Restore; }
        public static SquadDef Get(SquadId id) => All[(int)id];
        public static SquadDef Get(SquadId id, int profile) => profile >= 3 ? V03[(int)id] : All[(int)id];
        public static SquadDef[] Table(int profile) => profile >= 3 ? V03 : All;
        public static int Count => All.Length;
        public static bool IsSupportCard(SquadId id) => id == SquadId.DawnClerics || id == SquadId.Bonecallers || id == SquadId.SpiritWeavers;
    }

    public static class Heroes
    {
        public static readonly HeroDef[] All =
        {
            new HeroDef { Id = HeroId.Morthane, Name = "Morthane", Short = "MORTHANE", Archetype = "Necromancer", Melee = false, Hp = 320, Initiative = 60,
                Attack = 70, AttackType = DamageType.Magic, AttackName = "Soul Bolt",
                Skill = HeroSkill.RaiseTheFallen, SkillAmount = 100, SkillName = "Raise the Fallen",
                Ult = HeroUlt.GraveTempest, UltAmount = 85, UltName = "Grave Tempest", AltUlt = HeroUlt.RaiseTheFallen, AltUltAmount = 200, AltUltName = "Raise the Fallen",
                Passive = HeroPassive.SoulHarvest, PassiveName = "Soul Harvest: x2 souls from Undead losses", UltUsesHint = "4-5" },
            new HeroDef { Id = HeroId.SerAldren, Name = "Ser Aldren", Short = "SER ALDREN", Archetype = "Paladin", Melee = true, Hp = 600, Initiative = 60,
                Attack = 65, AttackType = DamageType.Physical, AttackName = "Radiant Strike",
                Skill = HeroSkill.HolyLight, SkillAmount = 130, SkillName = "Holy Light",
                Ult = HeroUlt.DawnRenewal, UltAmount = 100, UltName = "Dawn Renewal",
                Passive = HeroPassive.Devotion, PassiveName = "Devotion: allies take -10% Physical damage", UltUsesHint = "2-3" },
            new HeroDef { Id = HeroId.Varka, Name = "Varka Stormspeaker", Short = "VARKA", Archetype = "Shaman", Melee = true, Hp = 440, Initiative = 80,
                Attack = 75, AttackType = DamageType.Physical, AttackName = "Storm Cleaver",
                Skill = HeroSkill.BloodChant, SkillAmount = 0, SkillName = "Blood Chant",
                Ult = HeroUlt.AncestorGuard, UltAmount = 80, UltName = "Ancestor Guard",
                Passive = HeroPassive.WarRhythm, PassiveName = "War Rhythm: +10% damage while all 4 squads live", UltUsesHint = "1-2" },
            new HeroDef { Id = HeroId.Ilyra, Name = "Ilyra the Unbound", Short = "ILYRA", Archetype = "Battle mage", Melee = false, Hp = 380, Initiative = 85,
                Attack = 90, AttackType = DamageType.Magic, AttackName = "Arc Lance",
                Skill = HeroSkill.Fracture, SkillAmount = 0, SkillName = "Fracture",
                Ult = HeroUlt.Nullstorm, UltAmount = 65, UltName = "Nullstorm",
                Passive = HeroPassive.CleanCut, PassiveName = "Clean Cut: +10% attack vs targets without buffs", UltUsesHint = "1-2" },
        };
        /// balance v0.3 (§24.5): те же числа действий, инициатива 1–10, пассивки удалены (кроме Soul Harvest).
        public static readonly HeroDef[] V03 =
        {
            new HeroDef { Id = HeroId.Morthane, Name = "Morthane", Short = "MORTHANE", Archetype = "Necromancer", Melee = false, Hp = 320, Initiative = 6,
                Attack = 70, AttackType = DamageType.Magic, AttackName = "Soul Bolt", Skill = HeroSkill.RaiseTheFallen, SkillAmount = 100, SkillName = "Raise the Fallen",
                Ult = HeroUlt.GraveTempest, UltAmount = 85, UltName = "Grave Tempest", AltUlt = HeroUlt.RaiseTheFallen, AltUltAmount = 200, AltUltName = "Raise the Fallen", Passive = HeroPassive.SoulHarvest, PassiveName = "Soul Harvest: x2 souls from Undead losses", UltUsesHint = "4-5" },
            new HeroDef { Id = HeroId.SerAldren, Name = "Ser Aldren", Short = "SER ALDREN", Archetype = "Paladin", Melee = true, Hp = 600, Initiative = 6,
                Attack = 65, AttackType = DamageType.Physical, AttackName = "Radiant Strike", Skill = HeroSkill.HolyLight, SkillAmount = 130, SkillName = "Holy Light",
                Ult = HeroUlt.DawnRenewal, UltAmount = 100, UltName = "Dawn Renewal", Passive = HeroPassive.None, PassiveName = "No passive", UltUsesHint = "2-3" },
            new HeroDef { Id = HeroId.Varka, Name = "Varka Stormspeaker", Short = "VARKA", Archetype = "Shaman", Melee = true, Hp = 440, Initiative = 8,
                Attack = 75, AttackType = DamageType.Physical, AttackName = "Storm Cleaver", Skill = HeroSkill.BloodChant, SkillAmount = 25, SkillName = "Blood Chant",
                Ult = HeroUlt.AncestorGuard, UltAmount = 80, UltName = "Ancestor Guard", Passive = HeroPassive.None, PassiveName = "No passive", UltUsesHint = "1-2" },
            new HeroDef { Id = HeroId.Ilyra, Name = "Ilyra the Unbound", Short = "ILYRA", Archetype = "Battle mage", Melee = false, Hp = 380, Initiative = 9,
                Attack = 90, AttackType = DamageType.Magic, AttackName = "Arc Lance", Skill = HeroSkill.Fracture, SkillAmount = 25, SkillName = "Fracture",
                Ult = HeroUlt.Nullstorm, UltAmount = 65, UltName = "Nullstorm", Passive = HeroPassive.None, PassiveName = "No passive", UltUsesHint = "1-2" },
        };
        public static HeroDef Get(HeroId id) => All[(int)id];
        public static HeroDef Get(HeroId id, int profile) => profile >= 3 ? V03[(int)id] : All[(int)id];
    }

    public static class Presets
    {
        public static readonly ArmyPreset[] All =
        {
            new ArmyPreset { Id = PresetId.GraveCycle, GamePlanV03 = "Restore surviving undead: keep the Bonecallers alive, rebuild the Bone Cohort, lose it again for souls", Name = "Grave Cycle", Badge = "ATTRITION", Hero = HeroId.Morthane,
                Front = new[] { SquadId.BoneCohort, SquadId.Graveguard }, Back = new[] { SquadId.SpiritWeavers, SquadId.PlagueCabal },   // Bush Rush: без лекарей (было Bonecallers)
                GamePlan = "Lose and restore undead HP again and again, charge Grave Tempest" },
            new ArmyPreset { Id = PresetId.SustainedFire, GamePlanV03 = "Heal your archers: focus one target, keep the Arbalists healthy", Name = "Sustained Fire", Badge = "SUSTAIN", Hero = HeroId.SerAldren,
                Front = new[] { SquadId.IronWardens, SquadId.SpellEaters }, Back = new[] { SquadId.RoyalArbalists, SquadId.ArmorbreakGunners },   // Bush Rush: без лекарей (было DawnClerics)
                GamePlan = "Keep the Arbalists at full strength and finish wounded targets" },
            new ArmyPreset { Id = PresetId.RageLink, GamePlanV03 = "Boost heavy attacks: empower the next big hit", Name = "Rage Link", Badge = "CONTROL", Hero = HeroId.Varka,
                Front = new[] { SquadId.BloodboundReavers, SquadId.IronWardens }, Back = new[] { SquadId.SpiritWeavers, SquadId.ArmorbreakGunners },
                GamePlan = "Push the Reavers into Rage, then hold them there with Link and Empower" },
            new ArmyPreset { Id = PresetId.ExecutionChain, GamePlanV03 = "Focus and finish: pick a target you can kill this round, Fracture first", Name = "Execution Chain", Badge = "BURST", Hero = HeroId.Ilyra,
                Front = new[] { SquadId.VeilKnives, SquadId.BloodboundReavers }, Back = new[] { SquadId.RoyalArbalists, SquadId.ArmorbreakGunners },
                GamePlan = "Fracture, assassin, finisher: kill before the enemy healer acts" },
            new ArmyPreset { Id = PresetId.MagicEaters, GamePlanV03 = "Area damage and heavy hits: AoE the rows, finish with big hits", Name = "Magic Eaters", Badge = "ANTI-BUFF", Hero = HeroId.Ilyra,
                Front = new[] { SquadId.SpellEaters, SquadId.IronWardens }, Back = new[] { SquadId.PlagueCabal, SquadId.ArmorbreakGunners },
                GamePlan = "Strip buffs, block healing and punish buff armies" },
            new ArmyPreset { Id = PresetId.LastingLight, GamePlanV03 = "Keep your army alive: heal, preserve HP, watch the hero from Round 5", Name = "Lasting Light", Badge = "SUSTAIN", Hero = HeroId.SerAldren,
                Front = new[] { SquadId.IronWardens, SquadId.BloodboundReavers, SquadId.SpellEaters }, Back = new[] { SquadId.SpiritWeavers },   // Bush Rush: без лекарей (было DawnClerics)   // автор 07.09: без нежити
                GamePlan = "Keep the highest total HP until the end of Round 6" },
            // 07.09 (автор): по 3 шаблона на героя — боты берут из тех же 12
            new ArmyPreset { Id = PresetId.BoneTide, UltVariant = 1, GamePlanV03 = "Wall of bones: three undead fronts soak hits, Bonecallers rebuild, Raise the Fallen restores every undead squad at once", Name = "Bone Tide", Badge = "SWARM", Hero = HeroId.Morthane,
                Front = new[] { SquadId.BoneCohort, SquadId.Graveguard, SquadId.SpellEaters }, Back = new[] { SquadId.PlagueCabal },   // Bush Rush: без лекарей (было Bonecallers)
                GamePlan = "Three fronts, one restorer: outlast and cast Raise the Fallen twice" },
            new ArmyPreset { Id = PresetId.PlagueMarch, GamePlanV03 = "Grind them down: Cabal hits every squad, Gunners finish the weakest, Knives reach the healer", Name = "Plague March", Badge = "AOE", Hero = HeroId.Morthane,
                Front = new[] { SquadId.BoneCohort, SquadId.VeilKnives }, Back = new[] { SquadId.PlagueCabal, SquadId.ArmorbreakGunners },
                GamePlan = "Cabal every turn, Gunners on the lowest squad, Knives on the support" },
            new ArmyPreset { Id = PresetId.RadiantLine, GamePlanV03 = "Tempo behind a wall: Wardens hold, Knives reach the support, Weavers empower the Arbalists, Dawn Renewal resets the fight", Name = "Radiant Line", Badge = "TEMPO", Hero = HeroId.SerAldren,
                Front = new[] { SquadId.IronWardens, SquadId.VeilKnives }, Back = new[] { SquadId.RoyalArbalists, SquadId.SpiritWeavers },
                GamePlan = "Empower the Arbalists, Knives on the support, cast Dawn Renewal at half HP" },
            new ArmyPreset { Id = PresetId.StormWall, GamePlanV03 = "Three fronts and a cannon: Wardens and Graveguard hold, Reavers bite, Gunners delete one squad a round", Name = "Storm Wall", Badge = "BULWARK", Hero = HeroId.Varka,
                Front = new[] { SquadId.IronWardens, SquadId.Graveguard, SquadId.BloodboundReavers }, Back = new[] { SquadId.ArmorbreakGunners },
                GamePlan = "Hold the line, focus the Gunners, Ancestor Guard before the enemy burst" },
            new ArmyPreset { Id = PresetId.ThunderVolley, GamePlanV03 = "Strike first: Knives and Reavers act early, Weavers boost the Arbalists, finish before healers answer", Name = "Thunder Volley", Badge = "BURST", Hero = HeroId.Varka,
                Front = new[] { SquadId.BloodboundReavers, SquadId.VeilKnives }, Back = new[] { SquadId.RoyalArbalists, SquadId.SpiritWeavers },
                GamePlan = "High initiative: kill a squad per round, Ancestor Guard to survive the counter" },
            new ArmyPreset { Id = PresetId.ArcaneSiege, GamePlanV03 = "Area then execute: Cabal softens every squad, Arbalists and Eaters finish, Nullstorm strips their buffs", Name = "Arcane Siege", Badge = "SIEGE", Hero = HeroId.Ilyra,
                Front = new[] { SquadId.SpellEaters, SquadId.Graveguard }, Back = new[] { SquadId.PlagueCabal, SquadId.RoyalArbalists },
                GamePlan = "AoE first, then pick off the lowest squad; Nullstorm when they stack buffs" },
        };
        public static ArmyPreset Get(PresetId id) => All[(int)id];
        public static int Count => All.Length;
        /// Шаблоны героя в порядке таблицы (3 на героя).
        public static List<ArmyPreset> ForHero(HeroId h) { var l = new List<ArmyPreset>(); foreach (var p in All) if (p.Hero == h) l.Add(p); return l; }
    }

    /// Собранная армия с расстановкой (§6.1–6.2): герой в задней клетке 0, отряды в своих слотах.
    [Serializable]
    public sealed class ArmySetup
    {
        public HeroId Hero;
        public SquadId[] Squads = new SquadId[4];
        public Row[] Rows = new Row[4];
        public int[] Slots = new int[4];     // 0..2 в переднем ряду, 1..2 в заднем (0 — герой)
        public PresetId? Preset;
        public int UltVariant;               // 0 — основная ульта героя, 1 — альтернативная (Morthane: Raise the Fallen), автор 07.09

        public static ArmySetup FromPreset(PresetId id)
        {
            var p = Presets.Get(id);
            var squads = new List<SquadId>(); squads.AddRange(p.Front); squads.AddRange(p.Back);
            var a = AutoPlace(p.Hero, squads.ToArray());
            a.Preset = id; a.UltVariant = p.UltVariant;
            return a;
        }

        /// AUTO PLACE (§14.6): передние карты в FRONT 1.., задние в REAR 2..
        public static ArmySetup AutoPlace(HeroId hero, SquadId[] squads)
        {
            var a = new ArmySetup { Hero = hero, Squads = (SquadId[])squads.Clone() };
            int f = 0, b = 1;
            for (int i = 0; i < a.Squads.Length; i++)
            {
                var def = Cards.Get(a.Squads[i]);
                a.Rows[i] = def.Row;
                a.Slots[i] = def.Row == Row.Front ? f++ : b++;
            }
            return a;
        }

        /// Ошибки §14.4/§14.6; null — армия допустима.
        public static string Validate(HeroId hero, SquadId[] squads)
        {
            if (squads == null || squads.Length != 4) return "Choose 4 different squads.";
            for (int i = 0; i < 4; i++) for (int j = i + 1; j < 4; j++) if (squads[i] == squads[j]) return "Choose 4 different squads.";
            int front = 0; foreach (var s in squads) if (Cards.Get(s).Row == Row.Front) front++;
            if (front < 2) return "Front-row squad required.";       // допустимы только 2F+2B и 3F+1B
            if (front > 3) return "Too many front-row squads.";
            return null;
        }

        public string ValidatePlacement(bool heroOffGrid = false)
        {
            string e = Validate(Hero, Squads); if (e != null) return e;
            var used = new HashSet<int>();
            for (int i = 0; i < 4; i++)
            {
                var def = Cards.Get(Squads[i]);
                if (Rows[i] != def.Row) return def.Row == Row.Front ? "Front-row squad required." : "This squad can only fight in the rear row.";
                int key = (int)Rows[i] * 10 + Slots[i];
                if (Rows[i] == Row.Back && (Slots[i] < (heroOffGrid ? 0 : 1) || Slots[i] > 2)) return "Place all 4 squads before battle.";
                if (Rows[i] == Row.Front && (Slots[i] < 0 || Slots[i] > 2)) return "Place all 4 squads before battle.";
                if (!used.Add(key)) return "Place all 4 squads before battle.";
            }
            return null;
        }

        public ArmySetup Clone() => new ArmySetup { Hero = Hero, Squads = (SquadId[])Squads.Clone(), Rows = (Row[])Rows.Clone(), Slots = (int[])Slots.Clone(), Preset = Preset, UltVariant = UltVariant };

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder(Heroes.Get(Hero).Name).Append(UltVariant == 1 ? "[Raise]" : "").Append(':');
            for (int i = 0; i < Squads.Length; i++) sb.Append(' ').Append(Cards.Get(Squads[i]).Short).Append('@').Append(Rows[i] == Row.Front ? 'F' : 'B').Append(Slots[i] + 1);
            return sb.ToString();
        }
    }
}
