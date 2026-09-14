using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Warbands.Sim;
using EventType = Warbands.Sim.EventType;

namespace Warbands.UI
{
    /// HUD боя Bush Rush (автор 14.09): полоски HP героев сверху и снизу, таймер, подсказка, кнопка ульты, плашки HP отрядов над толпами
    /// (мир → экран), всплывающие числа урона, итог боя. Ловит палец на всём экране и стирает кусты через Field3D/BattleRunner.
    public sealed class RushHud : UiScreen
    {
        readonly Field3D field; readonly RectTransform frame;
        readonly Ui.Bar heroTop, heroBottom; readonly TextMeshProUGUI nameTop, nameBottom, timer, prompt, roundText, resultTitle, resultSub;
        readonly Ui.Btn ultBtn, homeBtn, againBtn, resultHomeBtn; readonly RectTransform resultPanel, chips, floaters;
        readonly Dictionary<Ref, (RectTransform rt, Image fill, TextMeshProUGUI txt)> chipMap = new Dictionary<Ref, (RectTransform, Image, TextMeshProUGUI)>();
        readonly List<(TextMeshProUGUI t, float life, Vector2 pos)> floats = new List<(TextMeshProUGUI, float, Vector2)>();
        readonly List<TextMeshProUGUI> floatPool = new List<TextMeshProUGUI>();
        Cell? strokeLast; string reachKey;

        public RushHud(BattleRunner r, Transform parent, Field3D f, RectTransform frame) : base(r, parent, "UI_Rush", false)
        {
            field = f; this.frame = frame;
            // ловец пальца — под всем HUD
            var catcher = Ui.Fill(Root, "Catcher", new Color(0, 0, 0, 0)); Ui.Stretch(catcher.rectTransform); catcher.raycastTarget = true;
            var pd = catcher.gameObject.AddComponent<PointerDrag>(); pd.Down = DrawDown; pd.Move = DrawMove; pd.Up = DrawUp;
            chips = Ui.Node(Root, "Chips"); Ui.Stretch(chips);
            floaters = Ui.Node(Root, "Floaters"); Ui.Stretch(floaters);
            // герои
            nameTop = Ui.Text(Root, "NameTop", "", 26, Theme.TextDim, TextAlignmentOptions.Left, FontStyles.Bold, 2f, true); Ui.Place(nameTop.rectTransform, Ui.Anchor.TopLeft, 40, 36, 700, 36);
            heroTop = Ui.HpBar(Root, "HpTop", 14, 3, Theme.Ink2, Theme.Red2, 24); Ui.Place(heroTop.Rt, Ui.Anchor.TopLeft, 40, 76, 1000, 44);
            nameBottom = Ui.Text(Root, "NameBottom", "", 26, Theme.TextDim, TextAlignmentOptions.Left, FontStyles.Bold, 2f, true); Ui.Place(nameBottom.rectTransform, Ui.Anchor.BottomLeft, 40, 176, 700, 36);
            heroBottom = Ui.HpBar(Root, "HpBottom", 14, 3, Theme.Ink2, Theme.Green, 24); Ui.Place(heroBottom.Rt, Ui.Anchor.BottomLeft, 40, 128, 1000, 44);
            roundText = Ui.Text(Root, "Round", "", 24, Theme.TextMuted, TextAlignmentOptions.Right, FontStyles.Bold, 2f, true); Ui.Place(roundText.rectTransform, Ui.Anchor.TopRight, 130, 36, 400, 36);
            homeBtn = Ui.Normal(Root, "Btn_Home", "II", () => runner.ExitToHome(), 26); Ui.Place(homeBtn.Rt, Ui.Anchor.TopRight, 30, 28, 80, 56);
            timer = Ui.Text(Root, "Timer", "", 64, Theme.GoldLight, TextAlignmentOptions.Center, FontStyles.Bold, 0f, true); Ui.Place(timer.rectTransform, Ui.Anchor.TopRight, 40, 130, 160, 80); Theme.Halo(timer, 0.3f);
            prompt = Ui.Text(Root, "Prompt", "", 30, Theme.GoldLight, TextAlignmentOptions.Center, FontStyles.Bold, 3f, true); Ui.Place(prompt.rectTransform, Ui.Anchor.BottomCenter, 0, 216, 900, 44); Theme.Halo(prompt, 0.3f);
            ultBtn = Ui.Primary(Root, "Btn_Ult", "", 26, CastUlt); Ui.Place(ultBtn.Rt, Ui.Anchor.BottomCenter, 0, 30, 640, 84);
            // итог
            resultPanel = Ui.Node(Root, "Result"); Ui.Stretch(resultPanel);
            var scrim = Ui.Fill(resultPanel, "Scrim", Theme.A(Theme.InkDeep, 0.7f)); Ui.Stretch(scrim.rectTransform); scrim.raycastTarget = true;
            resultTitle = Ui.Text(resultPanel, "Title", "", 96, Theme.GoldLight, TextAlignmentOptions.Center, FontStyles.Bold, 6f, true); Ui.Place(resultTitle.rectTransform, Ui.Anchor.Center, 0, -200, 1000, 120); Theme.Halo(resultTitle, 0.35f);
            resultSub = Ui.Text(resultPanel, "Sub", "", 28, Theme.TextDim, TextAlignmentOptions.Center); Ui.Place(resultSub.rectTransform, Ui.Anchor.Center, 0, -110, 1000, 44);
            againBtn = Ui.Primary(resultPanel, "Btn_Again", "PLAY AGAIN", 40, () => runner.Rematch()); Ui.Place(againBtn.Rt, Ui.Anchor.Center, 0, 20, 520, 110);
            resultHomeBtn = Ui.Normal(resultPanel, "Btn_Home", "HOME", () => runner.ExitToHome(), 28); Ui.Place(resultHomeBtn.Rt, Ui.Anchor.Center, 0, 150, 420, 80);
            resultPanel.gameObject.SetActive(false);
            BuildToast();
            if (field != null) field.Hit += OnHit;
        }

        void CastUlt()
        {
            var b = runner.Battle; if (b == null || b.Ended) return;
            var hero = b.Sides[0].Hero;
            if (!runner.TrySubmit(Command.Make(CommandKind.Ultimate, hero.Ref, Ref.None))) Toast(b.Sides[0].Charge >= b.Cfg.UltCostFor(b.Sides[0].HeroDef) ? "Ultimate unlocks in Round " + b.Cfg.ultimateUnlockRound : $"Need {b.Cfg.UltCostFor(b.Sides[0].HeroDef):0} souls");
        }

        // ---------- рисование ----------
        void DrawDown(Vector2 sp) { strokeLast = null; runner.NotifyDrawStart(); EraseAt(sp); }
        void DrawMove(Vector2 sp) { EraseAt(sp); }
        void DrawUp(Vector2 sp) { strokeLast = null; runner.NotifyDrawEnd(); }
        void EraseAt(Vector2 sp)
        {
            if (field == null || !runner.PlayerTurn || runner.Busy) return;
            if (!field.GroundPoint(sp, out var p)) return;
            var cell = Field3D.CellAt(p); if (!cell.HasValue) return;
            var actor = runner.Battle?.Actor; if (actor == null || actor.IsHero) return;
            if (strokeLast.HasValue && strokeLast.Value == cell.Value) return;
            var from = strokeLast ?? actor.Cell;
            foreach (var c in BushGrid.Line(from, cell.Value)) if (runner.TouchCell(c, out _, out bool cleared) && cleared) field.EraseCell(c);
            strokeLast = cell.Value;
        }

        // ---------- показ ----------
        public override void Show() { base.Show(); reachKey = null; }
        public override void Tick(float dt)
        {
            base.Tick(dt);
            var b = runner.Battle; if (b == null) return;
            var top = b.Sides[1].Hero; var bottom = b.Sides[0].Hero;
            int topHp = Shown(top), bottomHp = Shown(bottom);
            nameTop.text = b.Sides[1].HeroDef.Name.ToUpperInvariant() + " (BOT)"; nameBottom.text = b.Sides[0].HeroDef.Name.ToUpperInvariant();
            heroTop.Set(top.MaxHp > 0 ? (float)topHp / top.MaxHp : 0f, $"{topHp} / {top.MaxHp}");
            heroBottom.Set(bottom.MaxHp > 0 ? (float)bottomHp / bottom.MaxHp : 0f, $"{bottomHp} / {bottom.MaxHp}");
            roundText.text = $"ROUND {b.Round} / {b.Cfg.rounds}";
            bool mine = runner.PlayerTurn && !runner.Busy && !b.Ended;
            timer.text = runner.Countdown > 0f ? Mathf.CeilToInt(runner.Countdown).ToString() : mine ? Mathf.CeilToInt(runner.PlayerTimeLeft).ToString() : "";
            prompt.text = runner.Countdown > 0f ? "GET READY" : b.Ended ? "" : mine ? "CLEAR THE WAY" : runner.Busy ? "" : "ENEMY TURN";
            var side = b.Sides[0]; float cost = b.Cfg.UltCostFor(side.HeroDef);
            bool ultOk = !b.Ended && !runner.Busy && runner.PlayerTurn && BattleResolver.UltimateAvailable(b, 0) && !side.UltUsedThisTurn;
            ultBtn.SetText($"{side.HeroDef.UltName.ToUpperInvariant()}  ·  {side.Charge:0}/{cost:0}"); ultBtn.SetEnabled(ultOk);
            // подсветка радиуса хода ходящего отряда
            var actor = b.Ended ? null : b.Actor;
            string key = actor != null && !actor.IsHero && !runner.Busy ? $"{actor.Side}:{actor.Index}:{b.TurnIndex}" : "";
            if (key != reachKey) { reachKey = key; field?.SetReach(key == "" ? null : BushLogic.Reachable(b, actor)); }
            TickChips(b, dt);
            // итог — когда постановка доиграна
            bool showResult = runner.State == BattleRunner.Stage.Result;
            if (resultPanel.gameObject.activeSelf != showResult)
            {
                resultPanel.gameObject.SetActive(showResult);
                if (showResult) { bool win = b.Winner == 0; resultTitle.text = win ? "VICTORY" : b.Winner == 1 ? "DEFEAT" : "DRAW"; resultTitle.color = win ? Theme.GoldLight : Theme.Red2; resultSub.text = b.EndReason == EndReason.HeroDefeated ? (win ? "Enemy hero has fallen" : "Your hero has fallen") : "Round limit — score by HP"; }
            }
        }
        int Shown(Unit u) { var v = field?.View(u.Ref); return v != null ? v.ShownHp : u.Hp; }

        const bool ShowSquadChips = false;   // автор 14.09: HP-бары бойцов/отрядов не показываем (у каждого бойца своё HP в симе)
        void TickChips(BattleState b, float dt)
        {
            if (field == null) return;
            if (!ShowSquadChips) { foreach (var kv in chipMap) if (kv.Value.rt.gameObject.activeSelf) kv.Value.rt.gameObject.SetActive(false); TickFloats(dt); return; }
            var seen = new HashSet<Ref>();
            foreach (var v in field.Views)
            {
                var u = v.Unit; if (u == null || u.IsHero) continue;
                bool alive = v.ShownHp > 0 && v.Root.gameObject.activeSelf;
                if (!chipMap.TryGetValue(u.Ref, out var chip))
                {
                    var rt = Ui.Node(chips, "Chip_" + u.Name); rt.sizeDelta = new Vector2(150, 30);
                    var bg = Ui.Flat(rt, "Bg", 8, Theme.A(Theme.Ink2, 0.85f)); Ui.Stretch(bg.rectTransform);
                    var fill = Ui.Flat(rt, "Fill", 6, Theme.Hp2(u.Side)); fill.rectTransform.anchorMin = new Vector2(0, 0); fill.rectTransform.anchorMax = new Vector2(1, 1); fill.rectTransform.offsetMin = new Vector2(4, 4); fill.rectTransform.offsetMax = new Vector2(-4, -4);
                    var t = Ui.Text(rt, "T", "", 18, Color.white, TextAlignmentOptions.Center, FontStyles.Bold, 0.5f, true); Ui.Stretch(t.rectTransform);
                    chip = (rt, fill, t); chipMap[u.Ref] = chip;
                }
                seen.Add(u.Ref);
                if (chip.rt.gameObject.activeSelf != alive) chip.rt.gameObject.SetActive(alive);
                if (!alive) continue;
                if (WorldToFrame(v.HeadPos, out var local)) chip.rt.anchoredPosition = local + new Vector2(0f, 10f);
                float k = u.MaxHp > 0 ? (float)v.ShownHp / u.MaxHp : 0f;
                chip.fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(k), 1f); chip.txt.text = $"{v.ShownHp}";
            }
            foreach (var kv in chipMap) if (!seen.Contains(kv.Key) && kv.Value.rt.gameObject.activeSelf) kv.Value.rt.gameObject.SetActive(false);
            TickFloats(dt);
        }
        void TickFloats(float dt)
        {
            for (int i = floats.Count - 1; i >= 0; i--)
            {
                var f = floats[i]; f.life -= dt; if (f.life <= 0f) { f.t.gameObject.SetActive(false); floats.RemoveAt(i); continue; }
                f.t.rectTransform.anchoredPosition = f.pos + new Vector2(0f, (1.1f - f.life) * 90f); f.t.alpha = Mathf.Clamp01(f.life / 0.4f); floats[i] = f;
            }
        }
        /// Точка мира → координаты внутри портретной рамки (якорь в центре).
        bool WorldToFrame(Vector3 world, out Vector2 local)
        {
            local = Vector2.zero; var cam = field.Cam; if (cam == null) return false;
            var sp = cam.WorldToScreenPoint(world); if (sp.z < 0f) return false;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(chips, new Vector2(sp.x, sp.y), null, out local);
        }

        void OnHit(CombatEvent e)
        {
            var v = field.View(e.Target); if (v == null) return;
            if (!WorldToFrame(v.HeadPos, out var pos)) return;
            TextMeshProUGUI t = null; foreach (var p in floatPool) if (!p.gameObject.activeSelf) { t = p; break; }
            if (t == null) { t = Ui.Text(floaters, "Float", "", 34, Color.white, TextAlignmentOptions.Center, FontStyles.Bold, 0.5f, true); t.rectTransform.sizeDelta = new Vector2(300, 50); Theme.Halo(t, 0.3f); floatPool.Add(t); }
            bool heal = e.Type == EventType.HealApplied;
            t.gameObject.SetActive(true); t.text = (heal ? "+" : "-") + e.Value; t.color = heal ? Theme.Mint : Theme.Red2; t.alpha = 1f;
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f); t.rectTransform.anchoredPosition = pos + new Vector2(0f, 40f);
            floats.Add((t, 1.1f, pos + new Vector2(0f, 40f)));
        }
    }
}
