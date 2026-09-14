using TMPro;
using UnityEngine;

namespace Warbands
{
    /// Спрайты и шрифты из Assets/Hyper_Casual_UI (пак автора, 06.09). Заполняет Forge (CI.BuildUiPack), ссылку держит UiRoot.
    [CreateAssetMenu(menuName = "Warbands/UI Pack", fileName = "UiPack")]
    public sealed class UiPackAsset : ScriptableObject
    {
        [Header("Buttons (9-slice)")]
        public Sprite btnPrimary, btnNormal, btnSelected, btnDisabled, btnAttack, btnSkill, btnDanger;
        [Header("Panels (9-slice)")]
        public Sprite panelHud, panelModal, panelMenu, panelDark, panelFrame, pill, cardLight, cardPurple;
        [Header("Icons")]
        public Sprite iconPause, iconSettings, iconHome, iconClose, iconLock, iconTimer, iconCrown, iconSkull, iconCheck, iconHeart, iconRotate, iconSwipe, iconClick, iconBack, iconNext, iconDanger, iconFlag;
        public Sprite toggleOn, toggleOff;
        public Sprite iconBadge, iconStar;   // ульты: Ancestor Guard / Nullstorm
        public Sprite iconSword, iconBow, iconWand, iconHeal;   // классы отрядов (автор 11.09, Assets/icons → gen/, белый силуэт)
        [Header("Fonts")]
        public TMP_FontAsset fontBold, fontHeavy;
    }
}
