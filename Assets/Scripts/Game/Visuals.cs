using System.Collections.Generic;
using UnityEngine;

namespace Volleyball.Game
{
    /// <summary>
    /// 用 Unity 內建基本幾何體組場景的小工具。材質以「預設材質」為範本複製，
    /// 所以 Built-in 與 URP 專案都能正確顯示，不需要任何自訂 Shader 或素材。
    /// </summary>
    static class Visuals
    {
        static readonly Dictionary<int, Material> materials = new Dictionary<int, Material>();
        static Material template;

        public static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Color color, bool castShadow = true)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Collider col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);

            Transform t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localScale = scale;

            Renderer r = go.GetComponent<Renderer>();
            if (template == null) template = r.sharedMaterial;
            r.sharedMaterial = MaterialFor(color);
            if (!castShadow) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        static Material MaterialFor(Color c)
        {
            Color32 c32 = c;
            int key = (c32.r << 24) | (c32.g << 16) | (c32.b << 8) | c32.a;
            if (!materials.TryGetValue(key, out Material m))
            {
                m = new Material(template);
                m.color = c;
                materials[key] = m;
            }

            return m;
        }

        public static readonly Color Blue = new Color(0.2f, 0.45f, 0.95f);
        public static readonly Color Red = new Color(0.95f, 0.3f, 0.3f);
        public static readonly Color Skin = new Color(0.96f, 0.8f, 0.66f);

        public static Color TeamColor(int team) => team == 0 ? Blue : Red;

        public static string TeamName(int team) => team == 0 ? "藍隊" : "紅隊";
    }
}
