using System.Runtime.InteropServices;
using UnityEngine;

namespace Warbands.UI
{
    /// Мост к браузеру (автор 07.09): полный экран, режим «на домашнем экране», отступы чёлки. Вне WebGL — заглушки.
    public static class WebHost
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void SwFullscreen();
        [DllImport("__Internal")] static extern int SwCanFullscreen();
        [DllImport("__Internal")] static extern int SwIsIOS();
        [DllImport("__Internal")] static extern int SwIsStandalone();
        [DllImport("__Internal")] static extern void SwSafeInsets(float[] outArr);
        static readonly float[] insets = new float[4];
        public static void RequestFullscreen() { try { SwFullscreen(); } catch (System.Exception e) { Debug.LogWarning("[SW] fullscreen: " + e.Message); } }
        public static bool CanFullscreen { get { try { return SwCanFullscreen() != 0; } catch { return false; } } }
        public static bool IsIOS { get { try { return SwIsIOS() != 0; } catch { return false; } } }
        public static bool IsStandalone { get { try { return SwIsStandalone() != 0; } catch { return false; } } }
        /// left, right, top, bottom в пикселях экрана Unity.
        public static Vector4 SafeInsets { get { try { SwSafeInsets(insets); return new Vector4(insets[0], insets[1], insets[2], insets[3]); } catch { return Vector4.zero; } } }
#else
        public static void RequestFullscreen() { Screen.fullScreen = !Screen.fullScreen; }
        public static bool CanFullscreen => false;
        public static bool IsIOS => false;
        public static bool IsStandalone => false;
        public static Vector4 SafeInsets => Vector4.zero;
#endif
    }
}
