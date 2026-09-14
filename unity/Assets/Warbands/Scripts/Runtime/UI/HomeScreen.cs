using TMPro;
using UnityEngine;
using Warbands.Sim;

namespace Warbands.UI
{
    /// Главный экран Bush Rush: название, PLAY, переключение шаблона колоды и сложности бота. Экранов лодаута/расстановки в прототипе нет.
    public sealed class HomeScreen : UiScreen
    {
        readonly Ui.Btn deckBtn, botBtn; readonly TextMeshProUGUI deckInfo;

        public HomeScreen(BattleRunner r, Transform parent) : base(r, parent, "UI_Home", false)
        {
            var scrim = Ui.Fill(Root, "Scrim", Theme.A(Theme.InkDeep, 0.35f)); Ui.Stretch(scrim.rectTransform); scrim.raycastTarget = true;
            var title = Ui.Text(Root, "Title", "BUSH RUSH", 96, Theme.GoldLight, TextAlignmentOptions.Center, FontStyles.Bold, 6f, true); Ui.Place(title.rectTransform, Ui.Anchor.TopCenter, 0, 260, 1000, 120); Theme.Halo(title, 0.35f);
            var sub = Ui.Text(Root, "Sub", "clear the bushes · rush the hero", 30, Theme.TextDim, TextAlignmentOptions.Center); Ui.Place(sub.rectTransform, Ui.Anchor.TopCenter, 0, 380, 1000, 50);
            var play = Ui.Primary(Root, "Btn_Play", "PLAY", 48, () => runner.StartBattle()); Ui.Place(play.Rt, Ui.Anchor.Center, 0, -60, 520, 120);
            deckBtn = Ui.Normal(Root, "Btn_Deck", "", () => { runner.NextPreset(); Refresh(); }, 28); Ui.Place(deckBtn.Rt, Ui.Anchor.Center, 0, 80, 620, 84);
            deckInfo = Ui.Text(Root, "DeckInfo", "", 22, Theme.TextMuted, TextAlignmentOptions.Center); Ui.Place(deckInfo.rectTransform, Ui.Anchor.Center, 0, 150, 900, 40);
            botBtn = Ui.Normal(Root, "Btn_Bot", "", () => { runner.BotDifficulty = runner.BotDifficulty == Difficulty.Strong ? Difficulty.Normal : Difficulty.Strong; Refresh(); }, 24); Ui.Place(botBtn.Rt, Ui.Anchor.Center, 0, 230, 420, 70);
            var ver = Ui.Text(Root, "Ver", "prototype greybox · " + Application.version, 18, Theme.TextCaption, TextAlignmentOptions.Center); Ui.Place(ver.rectTransform, Ui.Anchor.BottomCenter, 0, 40, 800, 30);
            BuildToast();
        }

        public override void Refresh()
        {
            deckBtn.SetText("DECK: " + runner.PresetName.ToUpperInvariant());
            var p = runner.Player; var sb = new System.Text.StringBuilder(Heroes.Get(p.Hero).Name + " · ");
            for (int i = 0; i < p.Squads.Length; i++) { if (i > 0) sb.Append(" · "); sb.Append(Cards.Get(p.Squads[i]).Short); }
            deckInfo.text = sb.ToString();
            botBtn.SetText("BOT: " + runner.BotDifficulty.ToString().ToUpperInvariant());
        }
    }
}
