using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Warbands.PlayTests
{
    /// Смоук-прогон сцены боя (В12, автор 11.09): сцена поднимается, UiRoot строит все экраны (в т.ч. BattleScreen — 11.09 падение на null
    /// прошло мимо compile/tests/autoplay, потому что ни один шаг не строил UI), автоплей бот-против-бота крутится несколько секунд.
    /// Любая ошибка или исключение в логе валит тест (LogAssert по умолчанию).
    public class SmokeTests
    {
        [UnityTest]
        public IEnumerator BattleSceneBootsAndAutoplayRuns()
        {
            SceneManager.LoadScene("Battle");
            yield return null; yield return null;
            var runner = Object.FindFirstObjectByType<BattleRunner>();
            Assert.IsNotNull(runner, "в сцене нет BattleRunner");
            Assert.IsNotNull(Object.FindFirstObjectByType<Warbands.UI.UiRoot>(), "в сцене нет UiRoot");
            runner.StartAutoplay();
            yield return null;
            Assert.IsNotNull(runner.Battle, "бой не стартовал");
            Assert.AreEqual(BattleRunner.Stage.Battle, runner.State);
            float t0 = Time.realtimeSinceStartup; int frames = 0; bool shot = false;
            while (Time.realtimeSinceStartup - t0 < 6f)
            {
                frames++; yield return null;
                // SHOT=1 (без -nographics): снимок камеры в CI/smoke.png — визуальный смоук 3D-поля
                if (!shot && Time.realtimeSinceStartup - t0 > 2.5f && System.Environment.GetEnvironmentVariable("SHOT") == "1" && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                {
                    shot = true; var cam = Camera.main; if (cam != null)
                    {
                        var rt = new RenderTexture(540, 960, 24); var old = cam.targetTexture; var oldRect = cam.rect; cam.rect = new Rect(0, 0, 1, 1); cam.targetTexture = rt; cam.Render();
                        RenderTexture.active = rt; var tex = new Texture2D(540, 960, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 540, 960), 0, 0); tex.Apply(); RenderTexture.active = null;
                        cam.targetTexture = old; cam.rect = oldRect;
                        System.IO.File.WriteAllBytes(System.IO.Path.GetFullPath("CI/smoke.png"), tex.EncodeToPNG()); Debug.Log("[SW] smoke shot CI/smoke.png");
                    }
                }
            }
            Assert.Greater(frames, 30, "кадры не идут");
            Assert.IsTrue(runner.Battle == null || runner.Battle.TurnIndex >= 1 || runner.Battle.Ended, "за 6 с не начался ни один ход");
        }
    }
}
