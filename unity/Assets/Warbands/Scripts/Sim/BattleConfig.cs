using System;

namespace Warbands.Sim
{
    /// Все настраиваемые числа GDD (§7–§11) в одном сериализуемом классе: ассет в редакторе,
    /// поверх — JSON из persistentDataPath/tuning.json или `?t={...}` в адресе веб-билда.
    [Serializable]
    public sealed class BattleConfig
    {
        /// Профиль правил: 2 — baseline v0.2 (GDD §§0–23), 3 — balance v0.3 (GDD §24). Меняет таблицы карт/героев и числа ниже (ApplyProfile).
        public int balanceProfile = 4;
        /// v0.4 (автор 07.09): герой вне сетки и очереди — без HP и щитов, только ульта как свободное действие в ход своей команды;
        /// победа — у врага не осталось отрядов; после последнего раунда счёт по HP отрядов.
        public bool heroOffGrid = true;
        /// Bush Rush (автор 14.09): герои стоят на поле по центру своих краёв, у них HP; отряды не выбирают цель — бегут вперёд к вражескому герою
        /// по ближайшему возможному пути; победа — смерть героя (гибель всех отрядов бой не заканчивает); отхода после удара нет.
        public bool rushMode = true;

        // §7
        public int rounds = 9;                       // решение автора 06.09: 6 раундов + эндшпиль до 9-го
        public float turnTimeSeconds = 5f;    // автор 10.09 (bush-field): 5 с на расчистку (было 15)         // таймер на каждый ход игрока (Р17; в GDD — общий на раунд)
        public float botDelayNormalMin = 0.8f, botDelayNormalMax = 2.2f;     // «думает» случайно (автор 06.09; GDD было 0.35–0.75)
        public float botDelayStrongMin = 0.6f, botDelayStrongMax = 1.8f;

        // Эксперименты по убийству героя (по умолчанию выключены = правила GDD v0.1)
        public int shieldUntilRound = 6;             // эндшпиль: с раунда N+1 щит героя снят, герой — цель по правилам заднего ряда (0 = щит до последнего отряда)
        public int shieldMinSquads = 0;              // щит держится, пока живых отрядов БОЛЬШЕ этого числа (0 = хотя бы один)
        public bool ultsIgnoreShield = false;        // массовые ульты урона бьют и защищённого героя
        public float overkillToHero = 0f;            // доля overkill по отряду, уходящая герою сквозь щит
        public float chipToHero = 0f;                // доля любого HP-урона по отряду, дублируемая герою сквозь щит
        public float roundDamageRamp = 0f;           // урон × (1 + ramp × (раунд − 1))
        public bool shuffleEqualInitiative = false;  // §24.2: равные инициативы перемешиваются общей группой без приоритета стороны

        // §8.2 нелинейная сила
        public float damageScale = 1f;               // глобальный множитель урона для экспериментов баланса
        public float minOutputRatio = 0.20f;
        public float outputExponent = 0.8f;

        // §8.5 души
        public float chargePerFullSquad = 50f;
        public bool soulsPerFighter = true;          // автор 07.09: одна душа за одного потерянного бойца (вместо доли HP пачки); цена ульты — ultimateCostPerFighter
        public float ultimateCostPerFighter = 20f;   // при численности ×2 (07.09) — 30; 11.09 (автор): составы −30 %, цена 20 для всех, кроме некроманта
        public float ultimateCostNecro = 30f;        // Мортейн (Soul Harvest: ×2 душ за нежить) — цена прежняя
        public float chargeMax = 1000f;             // Р13 (автор 06.09): заряды копятся, души не пропадают (было 200 = два применения)
        public float ultimateCost = 100f;
        public int ultimateUnlockRound = 2;
        public float soulHarvestMultiplier = 2f;
        // §8.7 (автор 08.09): разброс, промах и крит обычных атак отрядов и героя (ульты и поддержка — без разброса)
        public float damageVariance = 0.30f;      // урон = база × (1 ± variance), равномерно
        public float missChance = 0.08f;          // промах: урона нет, ход потрачен
        public float critChance = 0.10f;          // крит: ×critMultiplier поверх разброса
        public float critMultiplier = 1.5f;
        // Маршрут (ветка route-drawing, автор 09.09): ближники бегут по нарисованному пути, бьют всех задетых, на обратном пути задетые бьют их
        public bool routeEnabled = true;          // ближний бой только маршрутом (обычной атаки у ближников нет)
        public int routeMaxTargets = 3;
        public float routeSplit1 = 0.85f, routeSplit2 = 0.35f, routeSplit3 = 0.25f;   // подобрано прогонами 09.09 (BALANCE §16)   // доля урона по второй и третьей задетой пачке (первая — 100 %)
        public float routeCounter = 0f;           // автор 10.09: на обратном пути урона не получают         // доля Output задетой пачки ближнего боя, которой она бьёт бегущих на обратном пути
        public float routeCounterRanged = 0f;
        public float routeAssassinTaken = 0.5f;   // ассасины (Veil Knives) получают половину ответов — выскальзывают   // то же для дальников и поддержки (стрелки не дерутся в упор — ассасинам есть куда нырять)
        public bool routeRearNeedsFront = false;  // автор 09.09: задних можно бить сразу (true — только после переднего, пока он жив)
        public float routeActionSeconds = 4.6f;
        // Ветка bush-field (автор 10.09): поле 6×10 гексов, между армиями кусты; за ход расчищаем до bushClearBudget клеток, отряд сам бежит к своей цели
        public bool bushField = true;
        public int bushClearBudget = 0;          // автор 10.09: без ограничения (0 = сколько успеешь за таймер); >0 — лимит клеток за ход
        public int bushRegrowTurns = 2;   // автор 10.09: трава зарастает сразу после хода отряда (в начале следующего), кроме клеток под отрядами; 2 — через ход
        public float bushDamageMult = 1.5f;
        public float bushLiftEndSeconds = 1f;    // автор 11.09: отнял палец и секунду не рисует — ход заканчивается, отряд бежит
        public int bushObstacles = 5;            // автор 10.09: препятствия (3 блока + 2 стенки) в строках 2..9
        public int bushBonuses = 5, bushBonusMax = 6, bushHealBonus = 100, bushDoubleFromRound = 5;   // автор 11.09: бонусов больше (2/2 → 3/4 → 5/6: «совсем не встречаю»)
        public int bushBonusRelocateTurns = 2;    // автор 11.09: раз в 2 хода бонусы исчезают и появляются в других местах; заросло над бонусом — бонус пропал   // автор 11.09: бонусов вдвое меньше (старт 2, не больше 2), ×2 появляется только с 5-го раунда      // автор 10.09: «усилим урон» — множитель обычных ударов и выстрелов в этой ветке (карты не трогаем)          // автор 10.09: куст отрастает через ход — в начале второго хода после расчистки (клетка под отрядом ждёт)
        public float bushClearSeconds = 0.14f;   // постановка: одна клетка расчистки (ход бота)
        public float bushStepSeconds = 0.22f;    // постановка: один шаг бега   // постановка: бег вперёд по маршруту, драки, бег назад с ответами
        public int skillCooldownRounds = 3;          // применил в 1 → снова в 4

        // §9 статусы
        public float linkedShare = 0.30f;
        public float empoweredBonus = 0.25f;         // Blood Chant; §24.5: Spirit Weavers дают 0.40 (weaverEmpower)
        public float weaverEmpower = 0.40f;
        public float exposedBonus = 0.25f;
        public float plaguedHealPenalty = 0.25f;

        // §10 пассивки
        public float shieldWall = 0.15f;
        public float rageMax = 0.25f;
        public float manyBones = 1.25f;
        public float deathlessThreshold = 0.40f, deathless = 0.15f;
        public float executeThreshold = 0.30f, executeBonus = 0.20f;
        public float piercing = 0.50f;
        public float supportHunterBonus = 0.15f;
        public int spellEaterFlat = 25;

        // §11 герои
        public float devotion = 0.10f;
        public float warRhythm = 0.10f;
        public float cleanCut = 0.10f;

        // презентация (не влияет на результат)
        public float fastAnimations = 0.65f;
        public float squadActionSeconds = 1.1f;      // постановка удара: замах → рывок/снаряд → impact → возврат
        public float meleeActionSeconds = 4.3f;      // бег ×2 + облако драки 1.3 с + возврат (автор 07.09: «мощная заруба»)
        public float heroActionSeconds = 1.4f;
        public float ultimateSeconds = 2.0f;

        public static BattleConfig CreateDefault() { var c = new BattleConfig(); c.ApplyProfile(c.balanceProfile); return c; }

        /// v0.3 (§24): 9 раундов, эндшпиль с 7-го (решение автора 07.09), случайная очередь при равной инициативе, кривая 0.35, короткий playback.
        public void ApplyProfile(int profile)
        {
            balanceProfile = profile;
            heroOffGrid = profile >= 4;
            if (profile >= 3)
            {
                rounds = rushMode ? 30 : 9; shieldUntilRound = 6; shuffleEqualInitiative = true; minOutputRatio = 0.35f;   // автор 07.09: эндшпиль вернули на 7–9 (в §24.2 было 5–7)
                squadActionSeconds = 0.8f; heroActionSeconds = 0.8f; ultimateSeconds = 1.4f;
                damageScale = 1f; roundDamageRamp = 0f; chipToHero = 0f; overkillToHero = 0f; ultsIgnoreShield = false; shieldMinSquads = 0;
            }
            else
            {
                rounds = rushMode ? 30 : 9; shieldUntilRound = 6; shuffleEqualInitiative = false; minOutputRatio = 0.20f;
                squadActionSeconds = 1.1f; heroActionSeconds = 1.4f; ultimateSeconds = 2.0f;
            }
        }
        public bool V03 => balanceProfile >= 3;
        /// Действующая цена ульты с учётом режима душ.
        public float UltCost => soulsPerFighter ? ultimateCostPerFighter : ultimateCost;
        /// Цена ульты героя: у некроманта (Soul Harvest) своя.
        public float UltCostFor(HeroDef h) => soulsPerFighter ? (h != null && h.Passive == HeroPassive.SoulHarvest ? ultimateCostNecro : ultimateCostPerFighter) : ultimateCost;
    }
}
