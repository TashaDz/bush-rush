# HANDOFF — Bush Rush

Актуально на 14.09.2026. Правила — `GDD.md`, журнал — `ROADMAP.md`.

## 1. Что это
Новый проект от Soulbound Warbands (ветка `feature/bush-field`, 11.09): поле кустов в 3D-грейбоксе, герои на поле, все бегут к вражескому герою, победа — смерть героя. Unity 6000.3.21f1, URP, WebGL, портрет. Git: `TashaDz/bush-rush`; билд: `TashaDz/bush-rush-web` → https://tashadz.github.io/bush-rush-web/

## 2. Структура
```
bush-rush/
├── GDD.md / HANDOFF.md / ROADMAP.md / BALANCE.md ; balance/ (отчёты прогонов)
├── tools/build.sh   compile | forge (AUTOPLAY=1 — игрок тоже бот) | tests | smoke (PlayMode: сцена + 6 с автоплея; SHOT=1 — с графикой и снимком камеры в unity/CI/smoke.png) | autoplay (SEEDS=, BOT=strong, TUNE='{json}') | webgl (DEV=1) | all
├── tools/deploy.sh  → deploy/ (клон bush-rush-web, orphan-коммит, push --force)
└── unity/Assets/Warbands/Scripts
    ├── Sim/        общий сим Warbands + Bush Rush: BattleConfig.rushMode (герои на поле, конец — смерть героя), FlowLogic + BattleResolverFlow (бойцы «как вода»: Unit.Fighters по гексам, ход отряда = бег каждого бойца к вражескому герою, удар по встречным), BushField.HeroCell/Home
    ├── Runtime/    BattleRunner (таймер, росчерк, бот — из Warbands), Field3D (3D-поле и постановка по событиям), RushAssets (меши/материалы из forge), TuningAsset
    ├── Runtime/UI/ UiRoot (канвас, PortraitFrame, экраны), HomeScreen, RushHud (HUD, палец, плашки, итог), Ui/Theme/UiSprites/PortraitFrame/SafeArea/PointerDrag/UiScreen — из Warbands
    ├── Editor/CI.cs   Forge: PlayerSettings, Tuning, RushAssets (меши примитивов + URP-материалы как ассеты — в WebGL нет CreatePrimitive/Shader.Find), сцена Battle (камера, свет, Game: BattleRunner + Field3D + UiRoot)
    └── Tests/      RushTests (EditMode, 6), PlayMode/SmokeTests
```

## 3. Как продолжать
1. `tools/build.sh all` — компиляция, forge, тесты, смоук, матрица, WebGL (~15 мин). Unity batch не параллелится (Temp/UnityLockfile).
2. Локально: сервер `bush-rush-webgl` (порт 5189) в `../.claude/launch.json` → http://localhost:5189 (`?auto=1&seed=N` — бот против бота).
3. Выкладка: `tools/deploy.sh`.
4. Крутилки: `BattleConfig` (`rushMode`, `turnTimeSeconds`, `bushRegrowTurns`, `bushBonuses/Max`, `ultimateCostPerFighter/Necro`, `rounds`), `BushStats.Speed/Range`, `BushGrid.HeroCell/Home`, камера — `Field3D.PlaceCamera`.

## 4. Открытое
- Название проекта временное. Ёмкость гекса 8 бойцов (`FlowLogic.Cap`) — число предложено. Кисть (зона под пальцем шире линии, правило 40 %) из Warbands сюда не перенесена — линия по гексам между касаниями. Красный контур рейнджа удара не рисуется, только затемнение радиуса хода.
- Баланс на новых правилах — первый прогон в BALANCE §1; скорости 5/6/3 и HP героев не трогались по воле автора.
