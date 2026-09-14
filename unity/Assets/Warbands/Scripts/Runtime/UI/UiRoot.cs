using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Warbands.UI
{
    /// Корень uGUI Bush Rush (автор 14.09): канвас 1080×1920 в портретной колонке (PortraitFrame), экраны Home / бой (RushHud) / итог;
    /// переключение по BattleRunner.State. Поле — Field3D в мире, HUD поверх.
    public sealed class UiRoot : MonoBehaviour
    {
        [SerializeField] BattleRunner runner;
        [SerializeField] Field3D field;
        public static UiPackAsset Pack => null;   // пака UI в этом проекте нет — Ui/Theme рисуют процедурно
        HomeScreen home; RushHud hud;
        RectTransform frame; PortraitFrame frameFit;
        BattleRunner.Stage shown = (BattleRunner.Stage)(-1);

        public void Setup(BattleRunner r, Field3D f) { runner = r; field = f; }

        void Awake()
        {
            Theme.ResetFonts();
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 0;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(Theme.W, Theme.H);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; scaler.matchWidthOrHeight = 0.5f;
            if (FindFirstObjectByType<EventSystem>() == null) { var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)); es.transform.SetParent(transform, false); }

            var canvasRt = canvasGo.GetComponent<RectTransform>();
            frame = Ui.Node(canvasRt, "PortraitFrame"); frame.gameObject.AddComponent<RectMask2D>();
            frameFit = frame.gameObject.AddComponent<PortraitFrame>();
            var safe = Ui.Node(frame, "SafeArea"); Ui.Stretch(safe); safe.gameObject.AddComponent<SafeArea>();
            home = new HomeScreen(runner, safe);
            hud = new RushHud(runner, safe, field, frame);

            // тёмные полосы по бокам на широком экране — на системном канвасе поверх всего
            var sysGo = new GameObject("Canvas_System", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); sysGo.transform.SetParent(transform, false);
            var sc = sysGo.GetComponent<Canvas>(); sc.renderMode = RenderMode.ScreenSpaceOverlay; sc.sortingOrder = 20;
            var ss = sysGo.GetComponent<CanvasScaler>(); ss.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; ss.referenceResolution = new Vector2(Theme.W, Theme.H); ss.matchWidthOrHeight = 0.5f;
            var sysRt = sysGo.GetComponent<RectTransform>();
            frameFit.BarLeft = SideBar(sysRt, "Bar_Left", 0f); frameFit.BarRight = SideBar(sysRt, "Bar_Right", 1f);
            home.Hide(); hud.Hide();
        }

        static RectTransform SideBar(RectTransform parent, string name, float x)
        {
            var img = Ui.Fill(parent, name, Theme.InkDeep); img.raycastTarget = false;
            var rt = img.rectTransform; rt.anchorMin = new Vector2(x, 0f); rt.anchorMax = new Vector2(x, 1f); rt.pivot = new Vector2(x, 0.5f);
            rt.anchoredPosition = Vector2.zero; rt.sizeDelta = Vector2.zero; img.gameObject.SetActive(false);
            return rt;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            var st = runner.State;
            if (st != shown)
            {
                shown = st;
                home.Hide(); hud.Hide();
                if (st == BattleRunner.Stage.Home || st == BattleRunner.Stage.Loadout || st == BattleRunner.Stage.Formation) home.Show(); else hud.Show();
            }
            if (st == BattleRunner.Stage.Home) home.Tick(dt); else hud.Tick(dt);
        }
    }
}
