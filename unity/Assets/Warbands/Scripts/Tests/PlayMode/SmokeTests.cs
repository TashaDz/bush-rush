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
            float t0 = Time.realtimeSinceStartup; int frames = 0;
            while (Time.realtimeSinceStartup - t0 < 6f) { frames++; yield return null; }
            Assert.Greater(frames, 30, "кадры не идут");
            Assert.IsTrue(runner.Battle == null || runner.Battle.TurnIndex >= 1 || runner.Battle.Ended, "за 6 с не начался ни один ход");
        }
    }
}
