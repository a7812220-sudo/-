using UnityEngine;
using Volleyball.Core;

namespace Volleyball.Game
{
    /// <summary>在執行時用基本幾何體組出球場、球網與燈光。座標與 GameConfig 的球場尺寸一致。</summary>
    static class CourtBuilder
    {
        const float LineWidth = 0.08f;

        public static Transform Build()
        {
            Transform root = new GameObject("Court").transform;
            float L = GameConfig.HalfLength, W = GameConfig.HalfWidth;

            // 地面、場邊、兩側半場（用淡藍／淡紅區分隊伍）
            Visuals.Prim(PrimitiveType.Cube, "Ground", root, new Vector3(0f, -0.2f, 0f), new Vector3(70f, 0.1f, 50f), new Color(0.2f, 0.25f, 0.3f));
            Visuals.Prim(PrimitiveType.Cube, "Border", root, new Vector3(0f, -0.08f, 0f),
                new Vector3(2f * (L + 3f), 0.1f, 2f * (W + 3f)), new Color(0.42f, 0.5f, 0.6f));
            Visuals.Prim(PrimitiveType.Cube, "HalfBlue", root, new Vector3(-L * 0.5f, -0.03f, 0f), new Vector3(L, 0.06f, 2f * W), new Color(0.58f, 0.74f, 0.94f));
            Visuals.Prim(PrimitiveType.Cube, "HalfRed", root, new Vector3(L * 0.5f, -0.03f, 0f), new Vector3(L, 0.06f, 2f * W), new Color(0.94f, 0.66f, 0.62f));

            // 界線、中線、進攻線
            Color white = new Color(0.97f, 0.97f, 0.97f);
            Line(root, new Vector3(0f, 0f, W), new Vector3(2f * L, 0.01f, LineWidth), white);
            Line(root, new Vector3(0f, 0f, -W), new Vector3(2f * L, 0.01f, LineWidth), white);
            Line(root, new Vector3(L, 0f, 0f), new Vector3(LineWidth, 0.01f, 2f * W), white);
            Line(root, new Vector3(-L, 0f, 0f), new Vector3(LineWidth, 0.01f, 2f * W), white);
            Line(root, new Vector3(0f, 0f, 0f), new Vector3(LineWidth, 0.01f, 2f * W), white);
            Line(root, new Vector3(2f, 0f, 0f), new Vector3(LineWidth, 0.01f, 2f * W), white);
            Line(root, new Vector3(-2f, 0f, 0f), new Vector3(LineWidth, 0.01f, 2f * W), white);

            BuildNet(root);
            return root;
        }

        static void Line(Transform parent, Vector3 pos, Vector3 scale, Color color)
        {
            Visuals.Prim(PrimitiveType.Cube, "Line", parent, new Vector3(pos.x, 0.004f, pos.z), scale, color, false);
        }

        static void BuildNet(Transform parent)
        {
            Transform net = new GameObject("Net").transform;
            net.SetParent(parent, false);

            float h = GameConfig.NetTop;
            float half = GameConfig.HalfWidth + 0.4f;
            Color post = new Color(0.25f, 0.27f, 0.3f);
            Color cord = new Color(0.95f, 0.95f, 0.95f);

            // 網柱
            foreach (float sign in new[] { -1f, 1f })
                Visuals.Prim(PrimitiveType.Cylinder, "Post", net, new Vector3(0f, (h + 0.15f) * 0.5f, sign * (half + 0.05f)),
                    new Vector3(0.12f, (h + 0.15f) * 0.5f, 0.12f), post);

            // 網目：直線與橫線交織，留出縫隙讓後面的球員看得到。模擬把球網當成一路到地面的牆。
            for (float z = -half; z <= half + 0.001f; z += 0.25f)
                Visuals.Prim(PrimitiveType.Cube, "Cord", net, new Vector3(0f, h * 0.5f, z), new Vector3(0.02f, h, 0.02f), cord, false);
            for (float y = 0.5f; y < h - 0.1f; y += 0.5f)
                Visuals.Prim(PrimitiveType.Cube, "Cord", net, new Vector3(0f, y, 0f), new Vector3(0.02f, 0.02f, 2f * half), cord, false);

            // 網頂白帶
            Visuals.Prim(PrimitiveType.Cube, "Tape", net, new Vector3(0f, h - 0.06f, 0f), new Vector3(0.06f, 0.12f, 2f * half), Color.white);
        }

        /// <summary>建立（或沿用）攝影機與一盞方向光，並關掉場景裡原本的燈。</summary>
        public static Camera SetupCameraAndLight()
        {
            // 預設場景自帶一盞 "Directional Light"，先關掉免得光線疊加。
            // 不用 FindObjectsByType：它帶排序參數的版本在 Unity 6000.4/6000.5 以後被移除，不同版本寫法不一樣。
            GameObject defaultLight = GameObject.Find("Directional Light");
            if (defaultLight != null) defaultLight.SetActive(false);

            GameObject sunGo = new GameObject("Sun");
            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.97f, 0.9f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(52f, -35f, 0f);

            Camera cam = Camera.main;
            if (cam == null)
            {
                GameObject camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
            }

            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.7f, 0.9f);
            cam.fieldOfView = 48f;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 90f;
            return cam;
        }
    }
}
