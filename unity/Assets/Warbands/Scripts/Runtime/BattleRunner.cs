using System.Collections.Generic;
using UnityEngine;
using Warbands.Sim;
using EventType = Warbands.Sim.EventType;

namespace Warbands
{
    /// Цикл §5: Home → Loadout → Formation → Battle → Result. Держит BattleState, таймер стороны (§7.2),
    /// задержку бота, playback-паузы и телеметрию (§19: строки `[SW] ev …` и `[SW] match {json}` в консоль).
    [DefaultExecutionOrder(-10)]
    public sealed class BattleRunner : MonoBehaviour
    {
        public enum Stage { Home, Loadout, Formation, Battle, Result }

        [SerializeField] TuningAsset tuning;
        [SerializeField] bool autoplay;    // игроком тоже управляет бот — CI и просмотр

        public Stage State { get; private set; } = Stage.Home;
        /// Конфиг создаётся лениво: UiRoot.Awake может выполниться раньше Awake раннера (07.09: Loadout строился с профилем 0 → таблица v0.2).
        BattleConfig config;
        public BattleConfig Config { get { if (config == null) config = tuning != null ? tuning.CreateConfig() : BattleConfig.CreateDefault(); return config; } }
        public BattleState Battle { get; private set; }
        public bool Autoplay => autoplay;

        // армия игрока
        public HeroId Hero = HeroId.SerAldren;
        public int UltVariant;                // выбор ульты героя в редакторе колоды (07.09)
        public readonly List<SquadId> Picked = new List<SquadId>();
        /// 4 колоды игрока (автор 07.09): в бой идёт выбранная; Player — синоним текущей.
        public readonly ArmySetup[] Decks = new ArmySetup[4];
        public int DeckIndex { get; private set; }
        public ArmySetup Player { get => Decks[DeckIndex]; private set => Decks[DeckIndex] = value; }
        [System.Serializable] sealed class DeckSave { public ArmySetup[] decks; public int index; }
        public ArmySetup Enemy { get; private set; }
        public Difficulty BotDifficulty = Difficulty.Normal;
        public int BotPresetChoice = -1;      // −1 случайный, −2 зеркало, 0..11 шаблон (по 3 на героя)
        public bool FastAnimations;
        public bool Paused;

        // бой
        public float PlayerTimeLeft { get; private set; }
        public bool Busy => playbackLeft > 0f || Countdown > 0f;
        /// Отсчёт 3-2-1 перед матчем (автор 07.09): секунды до старта, 0 — идёт бой.
        public float Countdown { get; private set; }
        public const float CountdownSeconds = 3.6f;   // 3, 2, 1 по секунде + FIGHT! 0.6 с
        public float MatchSeconds { get; private set; }
        public int Seed { get; private set; }
        public int Battles { get; private set; }
        public List<CombatEvent> LastEvents { get; private set; } = new List<CombatEvent>();
        public bool PlayerTurn => Battle != null && !Battle.Ended && Battle.Current.Side == 0 && !autoplay;

        public event System.Action<List<CombatEvent>> Applied;
        public event System.Action BattleBegan;
        /// Bush Rush: следующий шаблон колоды (без экрана лодаута): PLAY берёт текущий.
        public void NextPreset() { var all = Presets.All; int i = 0; for (int k = 0; k < all.Length; k++) if (Player.Preset == all[k].Id) i = k; UsePreset(all[(i + 1) % all.Length].Id); CommitLoadout(); SaveArmy(); }
        public string PresetName => Player != null && Player.Preset.HasValue ? Presets.Get(Player.Preset.Value).Name : "Custom";

        float playbackLeft, botDelay; Ref timerActor = Ref.None;
        BotBrain[] brains;
        const string ArmyKey = "army", DecksKey = "decks", SettingsKey = "settings";

        public void Setup(TuningAsset t, bool auto) { tuning = t; autoplay = auto; }

        void Awake()
        {
            _ = Config;
            Random.InitState((int)(System.DateTime.Now.Ticks & 0x7fffffff));
            LoadArmy();
            string bot = TuningAsset.UrlParam("bot"); if (bot == "strong") BotDifficulty = Difficulty.Strong;
        }

        void Start()
        {
            if (autoplay || TuningAsset.UrlParam("auto") == "1") { autoplay = true; StartBattle(); }
        }

        // ---------- армия §14.4 ----------

        void LoadArmy()
        {
            // первый запуск: колода i — первый шаблон героя i (§5), в бой идёт Ser Aldren
            for (int i = 0; i < 4; i++) Decks[i] = ArmySetup.FromPreset(Presets.ForHero((HeroId)i)[0].Id);
            DeckIndex = (int)HeroId.SerAldren;
            try
            {
                string s = PlayerPrefs.GetString(DecksKey, "");
                if (!string.IsNullOrEmpty(s))
                {
                    var ds = JsonUtility.FromJson<DeckSave>(s);
                    if (ds != null && ds.decks != null)
                        for (int i = 0; i < 4 && i < ds.decks.Length; i++)
                            if (ds.decks[i] != null && ds.decks[i].ValidatePlacement(Config.heroOffGrid) == null) Decks[i] = ds.decks[i];
                    if (ds != null && ds.index >= 0 && ds.index < 4) DeckIndex = ds.index;
                }
                else
                {
                    string old = PlayerPrefs.GetString(ArmyKey, "");   // армия до колод → колода 0
                    if (!string.IsNullOrEmpty(old)) { var a = JsonUtility.FromJson<ArmySetup>(old); if (a != null && a.ValidatePlacement(Config.heroOffGrid) == null) { Decks[0] = a; DeckIndex = 0; } }
                }
                FastAnimations = PlayerPrefs.GetInt("fast", 0) == 1;
            }
            catch (System.Exception e) { Debug.LogWarning("[SW] army load failed: " + e.Message); }
            foreach (var dk in Decks) foreach (var p in Presets.All) if (IsPreset(dk, p)) dk.Preset = p.Id;   // Preset не сериализуется
            Hero = Player.Hero; UltVariant = Player.UltVariant; Picked.Clear(); Picked.AddRange(Player.Squads);
        }

        void SaveArmy()
        {
            PlayerPrefs.SetString(DecksKey, JsonUtility.ToJson(new DeckSave { decks = Decks, index = DeckIndex }));
            PlayerPrefs.SetInt("fast", FastAnimations ? 1 : 0);
            PlayerPrefs.Save();
        }

        public void GoHome() { CommitLoadout(); SaveArmy(); State = Stage.Home; }
        public void OpenLoadout() { State = Stage.Loadout; }

        /// Переключить колоду (Loadout): текущие правки сохраняются, если армия допустима.
        public void SelectDeck(int i)
        {
            if (i < 0 || i > 3 || i == DeckIndex) return;
            CommitLoadout(); DeckIndex = i;
            Hero = Player.Hero; UltVariant = Player.UltVariant; Picked.Clear(); Picked.AddRange(Player.Squads);
            SaveArmy();
        }

        /// Правки Loadout → текущая колода: расстановка сохраняется, если состав не изменился, иначе AUTO PLACE.
        public bool CommitLoadout()
        {
            if (LoadoutError != null) return false;
            bool same = Player != null && Player.Hero == Hero && Player.Squads.Length == 4;
            if (same) for (int i = 0; i < 4; i++) if (!Picked.Contains(Player.Squads[i])) same = false;
            if (!Heroes.Get(Hero).HasAltUlt) UltVariant = 0;
            if (!same) Player = ArmySetup.AutoPlace(Hero, Picked.ToArray());
            else Player.Hero = Hero;
            Player.UltVariant = UltVariant; Player.Preset = null; foreach (var p in Presets.All) if (IsPreset(Player, p)) Player.Preset = p.Id;
            return true;
        }

        public void UsePreset(PresetId id)
        {
            Player = ArmySetup.FromPreset(id);
            Hero = Player.Hero; UltVariant = Player.UltVariant; Picked.Clear(); Picked.AddRange(Player.Squads);
        }

        public void TogglePick(SquadId id)
        {
            if (Picked.Contains(id)) Picked.Remove(id);
            else if (Picked.Count < 4) Picked.Add(id);
        }

        public string LoadoutError => Picked.Count < 4 ? "Choose 4 different squads." : ArmySetup.Validate(Hero, Picked.ToArray());

        /// Loadout → Formation: сохраняем расстановку, если состав не изменился, иначе AUTO PLACE.
        public bool OpenFormation()
        {
            if (!CommitLoadout()) return false;
            State = Stage.Formation;
            return true;
        }

        /// Допущение прототипа (автор 11.09): из 12 шаблонов бот берёт тот, у которого меньше всего общих отрядов с колодой игрока
        /// (герой тот же — +1 к пересечению); при равенстве — случайный из лучших. Арт у сторон один, иначе на поле каша.
        static PresetId MostDifferentPreset(ArmySetup player, Sim.Rng rng)
        {
            var best = new List<PresetId>(); int bestOverlap = int.MaxValue;
            foreach (var p in Presets.All)
            {
                int overlap = p.Hero == player.Hero ? 1 : 0;
                foreach (var sq in player.Squads) { bool has = false; foreach (var f in p.Front) if (f == sq) has = true; foreach (var b in p.Back) if (b == sq) has = true; if (has) overlap++; }
                if (overlap < bestOverlap) { bestOverlap = overlap; best.Clear(); }
                if (overlap == bestOverlap) best.Add(p.Id);
            }
            return best.Count > 0 ? best[rng.Range(0, best.Count)] : (PresetId)rng.Range(0, Presets.Count);
        }

        static bool IsPreset(ArmySetup a, ArmyPreset p)
        {
            if (a.Hero != p.Hero || a.UltVariant != p.UltVariant) return false;
            foreach (var s in p.Front) if (System.Array.IndexOf(a.Squads, s) < 0) return false;
            foreach (var s in p.Back) if (System.Array.IndexOf(a.Squads, s) < 0) return false;
            return true;
        }

        public void AutoPlace() { var p = Player.Preset; Player = ArmySetup.AutoPlace(Player.Hero, Player.Squads); Player.Preset = p; }

        /// Расстановка §14.6: поставить карту i в слот (row, slot); занятый слот — обмен, если обе карты совместимы.
        public string Place(int card, Row row, int slot)
        {
            var def = Cards.Get(Player.Squads[card]);
            if (def.Row != row) return def.Row == Row.Front ? "Front-row squad required." : "This squad can only fight in the rear row.";
            for (int i = 0; i < 4; i++)
            {
                if (i == card || Player.Slots[i] < 0 || Player.Rows[i] != row || Player.Slots[i] != slot) continue;
                if (Cards.Get(Player.Squads[i]).Row != Player.Rows[card] && Player.Slots[card] >= 0) return "This squad can only fight in the rear row.";
                Player.Rows[i] = Player.Rows[card]; Player.Slots[i] = Player.Slots[card];   // обмен
                break;
            }
            Player.Rows[card] = row; Player.Slots[card] = slot;
            return null;
        }

        public void Unplace(int card) { Player.Slots[card] = -1; }
        public void ClearPlacement() { for (int i = 0; i < 4; i++) Player.Slots[i] = -1; }
        public string FormationError { get { foreach (var s in Player.Slots) if (s < 0) return "Place all 4 squads before battle."; return Player.ValidatePlacement(Config.heroOffGrid); } }

        // ---------- бой ----------

        public void StartBattle()
        {
            if (State == Stage.Loadout) CommitLoadout();
            if (Player == null || Player.ValidatePlacement(Config.heroOffGrid) != null) Player = ArmySetup.FromPreset(PresetId.SustainedFire);
            SaveArmy();
            string seedParam = TuningAsset.UrlParam("seed");
            Seed = seedParam != null && int.TryParse(seedParam, out int sp) ? sp + Battles : Random.Range(1, 1_000_000);
            var rng = new Rng(Seed);
            PresetId enemyPreset = BotPresetChoice >= 0 ? (PresetId)BotPresetChoice
                : BotPresetChoice == -2 && Player.Preset.HasValue ? Player.Preset.Value
                : Config.bushField ? MostDifferentPreset(Player, rng)   // допущение прототипа (автор 11.09): бот берёт максимально другие отряды, чтобы на поле не было каши из одинаковых
                : (PresetId)rng.Range(0, Presets.Count);
            Enemy = BotPresetChoice == -2 && !Player.Preset.HasValue ? Player.Clone() : ArmySetup.FromPreset(enemyPreset);
            Battle = BattleResolver.Create(Player, Enemy, Config, Seed, "Player", "Bot " + BotDifficulty);
            brains = new[] { new BotBrain(BotDifficulty), new BotBrain(BotDifficulty) };
            PlayerTimeLeft = Config.turnTimeSeconds; MatchSeconds = 0f; playbackLeft = 0f; Paused = false; timerActor = Ref.None;
            Countdown = autoplay ? 0f : CountdownSeconds;
            Battles++;
            LastEvents = new List<CombatEvent>(Battle.Log);
            State = Stage.Battle;
            NewBotDelay();
            Debug.Log($"[SW] ev start seed={Seed} player={Player} enemy={Enemy} bot={BotDifficulty}");
            BattleBegan?.Invoke();
            Applied?.Invoke(LastEvents);
        }

        /// Смоук-тест CI (В12, 11.09): бой бот-против-бота из уже поднятой сцены.
        public void StartAutoplay() { autoplay = true; StartBattle(); }
        public void Rematch() => StartBattle();
        public void RestartBattle() => StartBattle();

        void NewBotDelay()
        {
            bool strong = BotDifficulty == Difficulty.Strong;
            botDelay = Random.Range(strong ? Config.botDelayStrongMin : Config.botDelayNormalMin, strong ? Config.botDelayStrongMax : Config.botDelayNormalMax);
        }

        public float AnimScale => FastAnimations ? Config.fastAnimations : 1f;

        void Update()
        {
            if (State != Stage.Battle || Battle == null || Paused) return;
            float dt = Time.unscaledDeltaTime;
            MatchSeconds += dt;
            if (Countdown > 0f) { Countdown -= dt; return; }              // 3-2-1: таймер и бот ждут
            if (playbackLeft > 0f) { playbackLeft -= dt; return; }        // §7.2: таймер стоит во время анимаций
            if (Battle.Ended) { Finish(); return; }
            // Р17: таймер на каждый ход игрока (в GDD §7.2 — общий на раунд, после таймаута остаток раунда уходил в автоатаку)
            if (Battle.Current != timerActor) { timerActor = Battle.Current; PendingClear.Clear(); Stroke.Clear(); liftDeadline = -1f; if (timerActor.Side == 0) { PlayerTimeLeft = Config.turnTimeSeconds; turnDeadline = Time.unscaledTime + Config.turnTimeSeconds; } }
            int side = Battle.Current.Side;
            if (side == 0 && !autoplay)
            {
                if (liftDeadline > 0f) PlayerTimeLeft = Mathf.Min(PlayerTimeLeft, liftDeadline - Time.unscaledTime);   // bush-field: секунда после отпускания пальца
                PlayerTimeLeft -= dt;
                if (PlayerTimeLeft <= 0f)
                {
                    PlayerTimeLeft = 0f;
                    // bush-field (автор 10.09): таймер вышел — расчищенное применяется, отряд бежит сам; это не таймаут
                    var a = Battle.Actor;
                    if (Config.bushField && a != null && !a.IsHero)
                    {
                        // весь росчерк: кусты в нём расчищаются, чистые гексы задают траекторию; недопустимый росчерк не должен ронять ход (исключение → пустая команда → отряд «стоит»)
                        var cells = new List<Cell>(Stroke);
                        if (!BattleResolver.ValidClear(Battle, cells, out var bad)) { Debug.LogWarning($"[SW] stroke rejected: {bad} — only bushes go"); cells = new List<Cell>(PendingClear); if (!BattleResolver.ValidClear(Battle, cells, out _)) cells.Clear(); }
                        PendingClear.Clear(); Stroke.Clear(); Submit(Command.Clear(a.Ref, cells), false);
                    }
                    else Submit(BattleResolver.AutoCommand(Battle), true);
                }
            }
            else
            {
                botDelay -= dt;
                if (botDelay <= 0f) Submit(brains[side].Choose(Battle), false);
            }
        }

        /// bush-field (автор 11.09): отнял палец и секунду не рисует — ход заканчивается; снова коснулся — даёт себе ещё до 1.5 с, но не дольше общего таймера хода.
        float liftDeadline = -1f, turnDeadline;
        public void NotifyDrawStart() { if (!PlayerTurn || Busy || Paused) return; liftDeadline = -1f; PlayerTimeLeft = Mathf.Max(PlayerTimeLeft, Mathf.Min(1.5f, turnDeadline - Time.unscaledTime)); }
        public void NotifyDrawEnd() { if (!PlayerTurn || Busy || Paused || Stroke.Count == 0) return; liftDeadline = Time.unscaledTime + Config.bushLiftEndSeconds; }

        /// bush-field: клетки, которые игрок уже стёр в этот ход (применятся по концу таймера). Кусты в симе ещё стоят — UI прячет их сам.
        public readonly List<Cell> PendingClear = new List<Cell>();   // кусты, которые сотрёт этот ход (для лимита и подсветки)
        public readonly List<Cell> Stroke = new List<Cell>();         // весь росчерк по порядку, включая уже чистые гексы (автор 11.09: зелёные гексы тоже часть линии)
        public int ClearLeft => Config.bushClearBudget > 0 ? Mathf.Max(0, Config.bushClearBudget - PendingClear.Count) : int.MaxValue;
        /// Палец над гексом в свой ход: гекс попадает в росчерк (кроме препятствий и повторов подряд); куст — ещё и в список расчистки.
        /// cleared — куст только что добавлен к расчистке (UI показывает тропу). false с причиной — если нельзя (не свой ход, препятствие, лимит).
        public bool TouchCell(Cell c, out string why, out bool cleared)
        {
            why = null; cleared = false;
            if (!PlayerTurn || Busy || Paused || !Config.bushField) { why = "Not now"; return false; }
            var actor = Battle.Actor; if (actor == null || actor.IsHero) { why = "Not a squad turn"; return false; }
            if (!BushGrid.Inside(c) || Battle.Bushes.IsObstacle(c)) { why = "Blocked"; return false; }
            bool bush = Battle.Bushes.IsBush(c) && !PendingClear.Contains(c);
            if (bush && Config.bushClearBudget > 0 && PendingClear.Count >= Config.bushClearBudget) { why = $"Only {Config.bushClearBudget} bushes per turn"; return false; }
            if (!Stroke.Contains(c)) Stroke.Add(c);   // повторы недопустимы для команды (палец может вернуться на тот же гекс)
            if (bush) { PendingClear.Add(c); cleared = true; }
            return true;
        }
        public bool TryClearCell(Cell c, out string why) { bool ok = TouchCell(c, out why, out bool cleared); return ok && cleared; }

        /// Ход игрока из HUD. false — ход не принят (не его ход, playback, нелегально).
        /// Нарисованный маршрут: цели по порядку касания. false — не ход игрока / недопустимо (причина в out).
        public bool TrySubmitRoute(List<Ref> targets, out string why)
        {
            why = null;
            if (!PlayerTurn || Busy || Paused) { why = "Not now"; return false; }
            var actor = Battle.Actor;
            if (actor == null || actor.IsHero || actor.Squad.Reach == Reach.Ranged || !Config.routeEnabled) { why = "This squad does not run routes"; return false; }
            if (targets == null || targets.Count == 0) { why = "Draw the route through an enemy squad"; return false; }
            if (targets.Count > Config.routeMaxTargets) targets = targets.GetRange(0, Config.routeMaxTargets);
            if (!BattleResolver.ValidRoute(Battle, actor, targets)) { why = Config.routeRearNeedsFront ? "Fight through the front row first" : "Invalid route"; return false; }
            Submit(Command.Route(actor.Ref, targets), false);
            return true;
        }

        public bool TrySubmit(Command cmd)
        {
            if (!PlayerTurn || Busy || Paused) return false;
            foreach (var c in BattleResolver.LegalCommands(Battle))
                if (c.Kind == cmd.Kind && c.Actor == cmd.Actor && c.Target == cmd.Target && c.TargetRow == cmd.TargetRow) { Submit(c, false); return true; }
            return false;
        }

        void Submit(Command cmd, bool timeout)
        {
            var actor = Battle.Get(cmd.Actor);
            var side = Battle.Sides[actor.Side];
            if (timeout) { side.TimeoutActions++; Debug.Log($"[SW] ev timeout r{Battle.Round} {cmd}"); }
            var evs = BattleResolver.Apply(Battle, cmd);
            if (timeout) evs.Insert(0, new CombatEvent { Type = EventType.Timeout, Actor = cmd.Actor, Round = Battle.Round, Text = "TIME OUT" });
            bool meleeRun = !actor.IsHero && cmd.Kind == CommandKind.Attack && actor.Squad.Reach != Reach.Ranged && cmd.TargetRow < 0;
            float secs = cmd.Kind == CommandKind.Clear ? BushSeconds(actor, evs) : cmd.Kind == CommandKind.Ultimate ? Config.ultimateSeconds : cmd.Kind == CommandKind.Route ? Config.routeActionSeconds * (0.7f + 0.3f * cmd.RouteLen) : actor.IsHero ? Config.heroActionSeconds : meleeRun ? Config.meleeActionSeconds : Config.squadActionSeconds;
            foreach (var e in evs) if (e.Type == EventType.SquadDefeated) secs += 0.5f; else if (e.Type == EventType.RoundStarted) secs += 0.4f;
            playbackLeft = secs * AnimScale;
            NewBotDelay();
            LastEvents = evs;
            Debug.Log($"[SW] ev r{Battle.Round} {cmd} → {Summarize(evs)}");
            Applied?.Invoke(evs);
        }

        /// bush-field: постановка хода — расчистка (только у бота видна как анимация), бег по клеткам, удар/выстрел/лечение, отход.
        public const float BushMeleeAnimSeconds = 2.2f;   // короткий рывок в соседнюю клетку + облако драки
        float BushSeconds(Unit actor, List<CombatEvent> evs)
        {
            float secs = 0.25f; bool acted = false;
            foreach (var e in evs)
            {
                if (e.Type == EventType.BushCleared && actor.Side == 1) secs += e.Value * Config.bushClearSeconds + 0.2f;
                else if ((e.Type == EventType.MoveStarted || e.Type == EventType.RetreatStarted) && e.Value > 0) secs += 0.3f + e.Value * Config.bushStepSeconds;
                else if (e.Type == EventType.ActionStarted && e.Actor == actor.Ref) acted = true;
            }
            if (acted) secs += !actor.IsHero && actor.Squad.Reach != Reach.Ranged ? BushMeleeAnimSeconds : Config.squadActionSeconds;
            foreach (var e in evs) if (e.Type == EventType.BushRegrown) { secs += 0.4f; break; }   // трава затягивает тропу после хода
            return secs;
        }

        static string Summarize(List<CombatEvent> evs)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var e in evs)
                if (e.Type == EventType.DamageApplied || e.Type == EventType.HealApplied || e.Type == EventType.RestoreApplied || e.Type == EventType.SquadDefeated || e.Type == EventType.UltimateUsed || e.Type == EventType.BattleEnded)
                    sb.Append(e).Append("; ");
            return sb.ToString();
        }

        void Finish()
        {
            State = Stage.Result;
            Debug.Log("[SW] match " + Headless.MatchJson(Battle, MatchSeconds, Application.version, SystemInfo.deviceModel + " " + Screen.width + "x" + Screen.height));
            if (autoplay) Invoke(nameof(StartBattle), 4f);
        }

        public void ExitToHome() { Battle = null; State = Stage.Home; }
        public void ToggleFast() { FastAnimations = !FastAnimations; PlayerPrefs.SetInt("fast", FastAnimations ? 1 : 0); }
    }
}
