using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Warbands.Sim;

namespace Warbands.EditorTools
{
    /// Headless-цикл Bush Rush (tools/build.sh): compile → forge (сцена, ассеты, настройки) → tests/smoke → autoplay → webgl.
    public static class CI
    {
        const string Root = "Assets/Warbands";
        const string ScenePath = Root + "/Scenes/Battle.unity";
        const string TuningPath = Root + "/Data/Tuning.asset";
        const string AssetsPath = Root + "/Data/RushAssets.asset";

        public static void Compile() { Debug.Log("[SW] compile ok"); EditorApplication.Exit(0); }

        /// Настройки плеера, тюнинг, грейбокс-ассеты, сцена боя.
        public static void Forge()
        {
            Configure();
            Directory.CreateDirectory(Root + "/Data");
            var tuning = AssetDatabase.LoadAssetAtPath<TuningAsset>(TuningPath);
            if (tuning == null) { tuning = ScriptableObject.CreateInstance<TuningAsset>(); AssetDatabase.CreateAsset(tuning, TuningPath); }
            tuning.ResetToDefaults(); EditorUtility.SetDirty(tuning);
            File.WriteAllText(Root + "/Data/tuning.default.json", tuning.ToJson());
            bool auto = System.Environment.GetEnvironmentVariable("AUTOPLAY") == "1";
            BuildScene(tuning, BuildRushAssets(), auto);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("[SW] forge ok" + (auto ? " (autoplay scene)" : ""));
            EditorApplication.Exit(0);
        }

        static void Configure()
        {
            PlayerSettings.companyName = "TashaDz";
            PlayerSettings.productName = "Bush Rush";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false; PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.allowedAutorotateToPortrait = true; PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            foreach (var t in new[] { NamedBuildTarget.Standalone, NamedBuildTarget.Android, NamedBuildTarget.iOS, NamedBuildTarget.WebGL })
                PlayerSettings.SetApplicationIdentifier(t, "com.TashaDz.BushRush");
            var webgl = NamedBuildTarget.WebGL;
            PlayerSettings.SetScriptingBackend(webgl, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(webgl, ManagedStrippingLevel.Medium);
            PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = false;
            PlayerSettings.WebGL.template = "PROJECT:Warbands";
        }

        /// Меши примитивов и URP-материалы как ассеты — в WebGL нет CreatePrimitive/Shader.Find, всё должно быть ссылкой из сцены.
        static RushAssets BuildRushAssets()
        {
            var a = AssetDatabase.LoadAssetAtPath<RushAssets>(AssetsPath);
            if (a == null) { a = ScriptableObject.CreateInstance<RushAssets>(); AssetDatabase.CreateAsset(a, AssetsPath); }
            a.cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx"); a.sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            a.capsule = Resources.GetBuiltinResource<Mesh>("Capsule.fbx"); a.cylinder = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
            a.ground = Mat("Ground", new Color(0.22f, 0.45f, 0.24f)); a.sand = Mat("Sand", new Color(0.93f, 0.86f, 0.64f)); a.grass = Mat("Grass", new Color(0.36f, 0.68f, 0.36f));
            a.bush = Mat("Bush", new Color(0.36f, 0.68f, 0.36f)); a.obstacle = Mat("Obstacle", new Color(0.62f, 0.64f, 0.68f));
            a.blue = Mat("Blue", new Color(0.25f, 0.45f, 1f)); a.red = Mat("Red", new Color(1f, 0.32f, 0.3f));
            a.heroBlue = Mat("HeroBlue", new Color(0.12f, 0.25f, 0.85f)); a.heroRed = Mat("HeroRed", new Color(0.8f, 0.12f, 0.12f));
            a.gold = Mat("Gold", new Color(1f, 0.85f, 0.24f)); a.mint = Mat("Mint", new Color(0.49f, 1f, 0.56f)); a.dark = Mat("Dark", new Color(0.1f, 0.1f, 0.12f));
            a.grassSoft = VertexMat("GrassSoft", false); a.decal = VertexMat("Decal", true);
            EditorUtility.SetDirty(a);
            return a;
        }
        static Material Mat(string name, Color c)
        {
            string path = Root + "/Data/Mat_" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit"); if (sh == null) sh = Shader.Find("Universal Render Pipeline/Simple Lit");
                m = new Material(sh); AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", c); m.color = c; if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// Particles/Unlit URP: цвет = вершинный × _BaseColor (свет запекаем в вершины сами); transparent — для декалей с альфой.
        static Material VertexMat(string name, bool transparent)
        {
            string path = Root + "/Data/Mat_" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit"); m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", Color.white); m.color = Color.white; m.SetFloat("_ColorMode", 0f);
            if (transparent)
            {
                m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", 2f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = 3000; m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.EnableKeyword("_ALPHAPREMULTIPLY_ON"); m.DisableKeyword("_ALPHATEST_ON");
                m.SetShaderPassEnabled("ShadowCaster", false);
            }
            else { m.SetFloat("_Surface", 0f); m.SetFloat("_ZWrite", 1f); m.renderQueue = 2000; m.SetOverrideTag("RenderType", "Opaque"); }
            EditorUtility.SetDirty(m);
            return m;
        }

        static void BuildScene(TuningAsset tuning, RushAssets assets, bool autoplay)
        {
            Directory.CreateDirectory(Root + "/Scenes");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null; RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.55f, 0.6f, 0.65f);
            if (AssetDatabase.LoadAssetAtPath<Object>("Assets/TextMesh Pro/Resources/TMP Settings.asset") == null)
            { Debug.LogError("[SW] TMP Essential Resources missing — run tools/build.sh forge (extracts the package)"); EditorApplication.Exit(1); }

            var camGo = new GameObject("Main Camera", typeof(Camera)); camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.36f, 0.6f, 0.33f);   // в цвет травы: горизонт за ковром не бросается в глаза
            camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            var lightGo = new GameObject("Sun", typeof(Light)); var light = lightGo.GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.1f; light.color = new Color(1f, 0.97f, 0.9f);
            lightGo.transform.rotation = Quaternion.Euler(55f, -30f, 0f); light.shadows = LightShadows.Soft;

            var game = new GameObject("Game");
            var runner = game.AddComponent<BattleRunner>(); runner.Setup(tuning, autoplay);
            var field = game.AddComponent<Field3D>(); field.Setup(runner, assets, cam);
            var ui = game.AddComponent<Warbands.UI.UiRoot>(); ui.Setup(runner, field);

            EditorSceneManager.SaveScene(scene, ScenePath);
            string text = File.ReadAllText(ScenePath);
            if (text.Contains("!u!115")) { Debug.LogError("[SW] embedded MonoScript in scene — class/file name mismatch"); EditorApplication.Exit(1); }
        }

        /// Автоплей: матрица шаблонов бот-против-бота → CI/autoplay.txt + CI/matrix.csv (SEEDS=, BOT=strong, TUNE='{json}').
        public static void Autoplay()
        {
            var cfg = BattleConfig.CreateDefault();
            string tune = System.Environment.GetEnvironmentVariable("TUNE");
            if (!string.IsNullOrEmpty(tune)) { JsonUtility.FromJsonOverwrite(tune, cfg); Debug.Log("[SW] tune " + tune); }
            int seeds = int.TryParse(System.Environment.GetEnvironmentVariable("SEEDS"), out int n) ? n : 20;
            var diff = System.Environment.GetEnvironmentVariable("BOT") == "strong" ? Difficulty.Strong : Difficulty.Normal;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string report = Headless.Matrix(cfg, seeds, diff, out string csv);
            Directory.CreateDirectory("CI");
            File.WriteAllText("CI/matrix.csv", csv); File.WriteAllText("CI/autoplay.txt", report);
            foreach (var line in report.Split('\n')) Debug.Log("[SW] " + line);
            Debug.Log($"[SW] autoplay ok {sw.Elapsed.TotalSeconds:0.0}s");
            EditorApplication.Exit(0);
        }

        /// WebGL-билд: DEV=1 — development без сжатия; в релизе Brotli.
        public static void BuildWebGL()
        {
            bool dev = System.Environment.GetEnvironmentVariable("DEV") == "1";
            string path = "Builds/WebGL";
            PlayerSettings.WebGL.compressionFormat = dev ? WebGLCompressionFormat.Disabled : WebGLCompressionFormat.Brotli;
            var opts = new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = path, target = BuildTarget.WebGL, options = dev ? BuildOptions.Development : BuildOptions.None };
            var report = BuildPipeline.BuildPlayer(opts);
            bool ok = report.summary.result == BuildResult.Succeeded;
            string index = Path.Combine(path, "index.html");
            if (ok && File.Exists(index))
            {
                string stamp = System.DateTime.Now.ToString("yyyyMMddHHmm");
                string html = File.ReadAllText(index).Replace(".unityweb\"", ".unityweb?v=" + stamp + "\"").Replace(".loader.js\"", ".loader.js?v=" + stamp + "\"")
                    .Replace("<title>Bush Rush</title>", "<title>Bush Rush " + stamp + "</title>");
                File.WriteAllText(index, html);
                Debug.Log("[SW] webgl stamp " + stamp);
            }
            Debug.Log($"[SW] webgl {(ok ? "ok" : "FAILED")} {(dev ? "dev" : "release")} {report.summary.totalSize / 1048576f:0.0} MB → {path}");
            EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
