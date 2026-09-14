using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Warbands.UI
{
    /// Общее для экранов вне боя: корень, показ/скрытие, toast (ui-spec/14.5), шапка шагов Loadout/Formation.
    public abstract class UiScreen
    {
        protected readonly BattleRunner runner;
        public readonly RectTransform Root, Outer;
        Image toastBg; TextMeshProUGUI toastText; float toastT;

        /// fit=true: содержимое строится в коробке 1920×1080, которая ужимается под низкий канвас телефона (FitBox), автор 07.09.
        protected UiScreen(BattleRunner r, Transform parent, string name, bool fit = true)
        {
            runner = r;
            Outer = Ui.Node(parent, name); Ui.Stretch(Outer);
            if (fit) { Root = Ui.Node(Outer, "Fit"); Ui.Place(Root, Ui.Anchor.Center, 0, 0, Theme.W, Theme.H); Root.gameObject.AddComponent<FitBox>(); }
            else Root = Outer;
        }

        protected void BuildToast()
        {
            toastBg = Ui.Panel(Root, "Toast", 16, Theme.A(Theme.InkDeep, 0.88f), Theme.Bronze, 3);
            Ui.Place(toastBg.rectTransform, Ui.Anchor.TopCenter, 0, 116, 760, 56);
            toastText = Ui.Text(toastBg.transform, "T", "", 24, Theme.GoldLight, TextAlignmentOptions.Center, FontStyles.Bold, 1.2f);
            Ui.Stretch(toastText.rectTransform, 16, 0, 16, 0);
            toastBg.gameObject.SetActive(false);
            toastBg.transform.SetAsLastSibling();
        }

        public void Toast(string s, float secs = 1.5f) { if (toastBg == null) return; toastText.text = s; toastT = secs; toastBg.transform.SetAsLastSibling(); }

        public virtual void Show() { Outer.gameObject.SetActive(true); Refresh(); }
        public virtual void Hide() { Outer.gameObject.SetActive(false); }
        public virtual void Refresh() { }
        public virtual void Tick(float dt)
        {
            if (toastBg == null) return;
            toastT -= dt; if (toastBg.gameObject.activeSelf != toastT > 0f) toastBg.gameObject.SetActive(toastT > 0f);
        }

        /// Шапка «1 LOADOUT — 2 FORMATION» + подсказка справа.
        protected TextMeshProUGUI BuildHeader(int activeStep, out TextMeshProUGUI hint)
        {
            var header = Ui.Node(Root, "Header"); Ui.Place(header, Ui.Anchor.TopCenter, 0, 0, 0, 92); header.anchorMin = new Vector2(0, 1); header.anchorMax = new Vector2(1, 1); header.offsetMin = new Vector2(0, -92); header.offsetMax = Vector2.zero;
            var row = Ui.Node(header, "Steps"); Ui.Place(row, Ui.Anchor.MiddleLeft, 24, 0, 700, 44); Ui.HLayout(row, 14, 0, 0, TextAnchor.MiddleLeft, false, false);
            TextMeshProUGUI first = null;
            for (int i = 1; i <= 2; i++)
            {
                bool on = i == activeStep;
                var num = Ui.Panel(row, "Step_" + i, 12, on ? Theme.Gold : Theme.Stroke, Theme.Ink, 0); Ui.LE(num.rectTransform, prefW: 40, prefH: 40);
                var n = Ui.Text(num.transform, "N", i.ToString(), 22, on ? Theme.TextOnGold : Theme.TextDim); Ui.Stretch(n.rectTransform);
                var lbl = Ui.Text(row, "Label_" + i, i == 1 ? "LOADOUT" : "FORMATION", 26, on ? Theme.Text : Theme.A(Theme.Text, 0.45f), TextAlignmentOptions.Left, FontStyles.Bold, 2.6f, true); Ui.LE(lbl.rectTransform, prefW: 200, prefH: 40);
                if (first == null) first = lbl;
                if (i == 1) { var div = Ui.Fill(row, "Divider", Theme.Stroke); Ui.LE(div.rectTransform, prefW: 60, prefH: 3); }
            }
            hint = Ui.Text(header, "Hint", "", 22, Theme.TargetAlly, TextAlignmentOptions.Right, FontStyles.Bold); Ui.Place(hint.rectTransform, Ui.Anchor.MiddleRight, 24, 0, 800, 44);
            return first;
        }

        protected static string RowName(Sim.SquadDef d) => d.Row == Sim.Row.Front ? "FRONT" : "BACK";
        protected static Color RowColor(Sim.SquadDef d) => d.Row == Sim.Row.Front ? Theme.TeamEnemy : Theme.Cyan;
        protected static Color TagColor(Sim.UnitTag t) => t == Sim.UnitTag.Undead ? Theme.Undead : t == Sim.UnitTag.Arcane ? Theme.Arcane : Theme.Living;
        protected static string TypeName(Sim.SquadDef d) => d.IsSupport ? "SUP" : d.DamageType == Sim.DamageType.Magic ? "MAG" : "PHY";
        protected static Color TypeColor(Sim.SquadDef d) => d.IsSupport ? Theme.TargetAlly : d.DamageType == Sim.DamageType.Magic ? Theme.Magic : Theme.Physical;

        /// Маленький бейдж-пилюля с текстом.
        protected static Image Badge(Transform parent, string name, string text, Color bg, Color fg, float w, float h, float size = 15)
        {
            var b = Ui.Panel(parent, name, 7, bg, Theme.A(Theme.Ink, 0f), 0); b.rectTransform.sizeDelta = new Vector2(w, h);
            var t = Ui.Text(b.transform, "T", text, size, fg, TextAlignmentOptions.Center, FontStyles.Bold, 0.5f, true); Ui.Stretch(t.rectTransform);
            return b;
        }
    }
}
