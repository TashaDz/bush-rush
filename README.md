# Bush Rush

Прототип на Unity 6000.3.21f1 (URP, WebGL, портрет). Форк правил «поля кустов» из Soulbound Warbands, но в 3D-грейбоксе и с другой целью боя:
герои стоят на поле по центру своих краёв, отряды не выбирают цель — бегут вперёд к вражескому герою по ближайшему возможному пути,
игрок и бот только расчищают кусты пальцем. Победа — смерть героя. Лекарей в пачках пока нет.

Рабочее название «Bush Rush» — временное, автор не утверждала.

- Правила: `GDD.md`. Состояние и как продолжать: `HANDOFF.md`. Журнал решений: `ROADMAP.md`. Прогоны баланса: `BALANCE.md`.
- Сборка: `tools/build.sh compile | forge | tests | smoke | autoplay | webgl | all`, выкладка: `tools/deploy.sh` → https://tashadz.github.io/bush-rush-web/
- Код сима — `unity/Assets/Warbands/Scripts/Sim` (общий с Soulbound Warbands, режим `rushMode`), 3D-поле — `Runtime/Field3D.cs`, HUD — `Runtime/UI/RushHud.cs`.
