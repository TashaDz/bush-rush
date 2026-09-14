using UnityEngine;

namespace Warbands
{
    /// Грейбокс-ассеты поля (Bush Rush, автор 14.09): меши примитивов и URP-материалы. Заполняет Forge и кладёт в сцену —
    /// в WebGL-билде нет CreatePrimitive/Shader.Find (ловушка из Assassins), поэтому всё, что рисуем, должно быть ссылкой из сцены.
    public sealed class RushAssets : ScriptableObject
    {
        public Mesh cube, sphere, capsule, cylinder;
        public Material ground, sand, grass, bush, obstacle, blue, red, heroBlue, heroRed, gold, mint, dark;
    }
}
