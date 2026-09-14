using System.IO;
using UnityEngine;
using Warbands.Sim;

namespace Warbands
{
    /// Р2: числа GDD в ассете; в билде поверх — JSON из persistentDataPath/tuning.json, в вебе — `?t={"rounds":4}`.
    [CreateAssetMenu(menuName = "Warbands/Tuning", fileName = "Tuning")]
    public sealed class TuningAsset : ScriptableObject
    {
        [SerializeField] BattleConfig config = BattleConfig.CreateDefault();

        public static string OverridePath => Path.Combine(Application.persistentDataPath, "tuning.json");

        public BattleConfig CreateConfig()
        {
            var copy = JsonUtility.FromJson<BattleConfig>(JsonUtility.ToJson(config));
            int profileBefore = copy.balanceProfile;
            try
            {
                if (File.Exists(OverridePath)) { JsonUtility.FromJsonOverwrite(File.ReadAllText(OverridePath), copy); Debug.Log("[SW] tuning override: " + OverridePath); }
            }
            catch (System.Exception e) { Debug.LogWarning("[SW] tuning override failed: " + e.Message); }
            string json = UrlParam("t");
            if (json != null)
            {
                try { JsonUtility.FromJsonOverwrite(json, copy); Debug.Log("[SW] tuning from url: " + json); }
                catch (System.Exception e) { Debug.LogWarning("[SW] url tuning failed: " + e.Message); }
            }
            // смена профиля через JSON (`?t={"balanceProfile":2}`): применить его числа, затем ещё раз явные поля JSON
            if (copy.balanceProfile != profileBefore)
            {
                copy.ApplyProfile(copy.balanceProfile);
                if (json != null) { try { JsonUtility.FromJsonOverwrite(json, copy); } catch { } }
            }
            return copy;
        }

        /// Параметр адреса веб-билда (`?seed=5&bot=strong`), null если нет.
        public static string UrlParam(string name)
        {
            string url = Application.absoluteURL;
            if (string.IsNullOrEmpty(url)) return null;
            int q = url.IndexOf('?'); if (q < 0) return null;
            foreach (var part in url.Substring(q + 1).Split('&'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0 && part.Substring(0, eq) == name) return System.Uri.UnescapeDataString(part.Substring(eq + 1));
            }
            return null;
        }

        public void ResetToDefaults() => config = BattleConfig.CreateDefault();
        public string ToJson() => JsonUtility.ToJson(config, true);
    }
}
