# BALANCE — Bush Rush

Матрица 12×12 шаблонов, бот против бота, `SEEDS=20 tools/build.sh autoplay` → `unity/CI/autoplay.txt` (копии в `balance/`). Правила — GDD.md.

## 1. Первый прогон (14.09) — `balance/matrix-12-presets-rush-v1.txt`

Правила v0.1 (GDD): поле 6×10, герои на поле с HP из карт (Aldren 600, Varka 440, Ilyra 380, Morthane 320), скорости 5/6/3, лекарей нет, победа — смерть героя. 20 сидов, бот Normal. Диапазон **18–80** (Sustained Fire 80, Execution Chain 65, Storm Wall 63, Lasting Light 55, Rage Link 54, Arcane Siege 54, Magic Eaters 52, Bone Tide 42, Radiant Line 42, Thunder Volley 41, Plague March 35, Grave Cycle 18). Лимитов **0 %**, медиана 5 раундов, 26 действий на бой.

Читается прямо: колоды Aldren (600 HP героя) сверху, колоды Мортейна (320 HP) снизу — при победе «смерть героя» HP героя стал главным числом. Предложение — выровнять HP героев (например, 500 всем) или дать Мортейну компенсацию; вопрос автору В3 в ROADMAP.

## 2. Бойцы «как вода» (14.09) — `balance/matrix-12-presets-rush-v2-flow.txt`

Правила v0.2: бойцы по гексам (до 8 в гексе), каждый бежит к герою своим путём, удар — доля бьющих бойцов. 20 сидов, бот Normal. Диапазон **16–85** (Sustained Fire 85, Execution Chain 75, Storm Wall 69, Magic Eaters 59, Arcane Siege 58, Bone Tide 51, Rage Link 49, Lasting Light 45, Plague March 36, Radiant Line 28, Thunder Volley 28, Grave Cycle 16). Лимитов **0 %**, медиана 6 раундов, 29 действий на бой. Разброс тот же, что в §1, и по той же причине — HP героя (В3); поток бойцов сам по себе баланс не сдвинул, бои на раунд длиннее.

## 3. Лавина: составы ×2, HP бойца ÷2, ёмкость гекса 16, ульта 40/60 (14.09) — `balance/matrix-12-presets-rush-v3-avalanche.txt`

20 сидов, бот Normal. Диапазон **13–86** (Sustained Fire 86, Execution Chain 73, Storm Wall 71, Magic Eaters 64, Arcane Siege 56, Rage Link 49, Bone Tide 49, Lasting Light 48, Plague March 38, Thunder Volley 27, Radiant Line 26, Grave Cycle 13). Лимитов **0 %**, медиана 6 раундов, 29 действий. Картина §2 без сдвигов: удвоение бойцов при тех же HP пачек динамику не меняет; разброс задаёт HP героя (В3).

## 4. До 4 бойцов в гексе, бойцы ×4, составы прежние (14.09) — `balance/matrix-12-presets-rush-v4-cap4.txt`

20 сидов, бот Normal. Составы как в Warbands (10×50 … 6×67), ёмкость гекса 4, лагерь на старте, ульта 20/30. Диапазон **20–86** (Sustained Fire 86, Storm Wall 69, Magic Eaters 68, Arcane Siege 68, Bone Tide 64, Execution Chain 54, Rage Link 48, Lasting Light 46, Plague March 31, Radiant Line 26, Grave Cycle 20, Thunder Volley 20). Лимитов **0 %**, медиана 7 раундов, 35 действий. Ульт у Bone Tide до 11 за бой (некромант при 30 душах на дешёвой нежити) — стоит посмотреть отдельно. Картина та же, что в §2–3: разброс задаёт HP героя (В3).

## 5. Красный герой за краем + драка боец-на-бойца (14.09) — `balance/matrix-12-presets-rush-v5-hero-edge.txt`

20 сидов, бот Normal, правила §4, вражеский герой на (2,−1). Диапазон **17–85** (Sustained Fire 85, Storm Wall 70, Arcane Siege 70, Magic Eaters 68, Bone Tide 68, Execution Chain 55, Rage Link 49, Lasting Light 43, Plague March 29, Radiant Line 28, Thunder Volley 19, Grave Cycle 17). Лимитов **0 %**, медиана 7 раундов, 36 действий — как §4, лишний ряд до героя врага картину не сдвинул. Bone Tide до 14 ульт за бой (см. §4).

