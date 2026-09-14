# BALANCE — Bush Rush

Матрица 12×12 шаблонов, бот против бота, `SEEDS=20 tools/build.sh autoplay` → `unity/CI/autoplay.txt` (копии в `balance/`). Правила — GDD.md.

## 1. Первый прогон (14.09) — `balance/matrix-12-presets-rush-v1.txt`

Правила v0.1 (GDD): поле 6×10, герои на поле с HP из карт (Aldren 600, Varka 440, Ilyra 380, Morthane 320), скорости 5/6/3, лекарей нет, победа — смерть героя. 20 сидов, бот Normal. Диапазон **18–80** (Sustained Fire 80, Execution Chain 65, Storm Wall 63, Lasting Light 55, Rage Link 54, Arcane Siege 54, Magic Eaters 52, Bone Tide 42, Radiant Line 42, Thunder Volley 41, Plague March 35, Grave Cycle 18). Лимитов **0 %**, медиана 5 раундов, 26 действий на бой.

Читается прямо: колоды Aldren (600 HP героя) сверху, колоды Мортейна (320 HP) снизу — при победе «смерть героя» HP героя стал главным числом. Предложение — выровнять HP героев (например, 500 всем) или дать Мортейну компенсацию; вопрос автору В3 в ROADMAP.

## 2. Бойцы «как вода» (14.09) — `balance/matrix-12-presets-rush-v2-flow.txt`

Правила v0.2: бойцы по гексам (до 8 в гексе), каждый бежит к герою своим путём, удар — доля бьющих бойцов. 20 сидов, бот Normal. Диапазон **16–85** (Sustained Fire 85, Execution Chain 75, Storm Wall 69, Magic Eaters 59, Arcane Siege 58, Bone Tide 51, Rage Link 49, Lasting Light 45, Plague March 36, Radiant Line 28, Thunder Volley 28, Grave Cycle 16). Лимитов **0 %**, медиана 6 раундов, 29 действий на бой. Разброс тот же, что в §1, и по той же причине — HP героя (В3); поток бойцов сам по себе баланс не сдвинул, бои на раунд длиннее.

