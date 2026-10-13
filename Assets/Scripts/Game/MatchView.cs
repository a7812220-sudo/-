using System.Collections.Generic;
using UnityEngine;
using Volleyball.Core;

namespace Volleyball.Game
{
    static class Conv
    {
        public static Vector3 V(Vec3 v) => new Vector3(v.X, v.Y, v.Z);
    }

    sealed class PlayerAvatar
    {
        public readonly Transform Root;
        readonly Transform armPivot;
        readonly Transform shadow;
        readonly Transform meRing;

        public PlayerAvatar(Transform parent, int team, bool isMe)
        {
            Root = new GameObject("Player").transform;
            Root.SetParent(parent, false);
            Root.rotation = Quaternion.LookRotation(team == 0 ? Vector3.right : Vector3.left); // 一律面向球網

            Visuals.Prim(PrimitiveType.Capsule, "Body", Root, new Vector3(0f, 0.8f, 0f), new Vector3(0.7f, 0.8f, 0.7f), Visuals.TeamColor(team));
            Visuals.Prim(PrimitiveType.Sphere, "Head", Root, new Vector3(0f, 1.72f, 0f), Vector3.one * 0.38f, Visuals.Skin);

            armPivot = new GameObject("ArmPivot").transform;
            armPivot.SetParent(Root, false);
            armPivot.localPosition = new Vector3(0.42f, 1.3f, 0f);
            Visuals.Prim(PrimitiveType.Cube, "Arm", armPivot, new Vector3(0f, -0.35f, 0f), new Vector3(0.16f, 0.7f, 0.16f), Visuals.Skin);

            shadow = Visuals.Prim(PrimitiveType.Cylinder, "Shadow", parent, Vector3.zero, new Vector3(0.9f, 0.005f, 0.9f),
                new Color(0.12f, 0.15f, 0.22f), false).transform;
            if (isMe)
                meRing = Visuals.Prim(PrimitiveType.Cylinder, "MeRing", parent, Vector3.zero, new Vector3(1.4f, 0.004f, 1.4f),
                    new Color(1f, 0.85f, 0.2f), false).transform;
        }

        public Vector3 HeadPos => Root.position + Vector3.up * 2.15f;

        public void Apply(RenderPlayer p)
        {
            Vector3 pos = Conv.V(p.Pos);
            Root.position = pos;
            shadow.position = new Vector3(pos.x, 0.007f, pos.z);
            float s = 0.9f - 0.3f * Mathf.Clamp01(pos.y);
            shadow.localScale = new Vector3(s, 0.005f, s);
            if (meRing != null) meRing.position = new Vector3(pos.x, 0.004f, pos.z);
            armPivot.localRotation = Quaternion.Euler(SwingAngle(p.Swing), 0f, 0f);
        }

        /// <summary>揮臂：先稍微往後拉，再快速往前上方揮過頭，最後放下。0 = 手自然下垂。</summary>
        static float SwingAngle(float s)
        {
            if (s <= 0f) return 0f;
            if (s < 0.25f) return Mathf.Lerp(0f, 30f, s / 0.25f);
            if (s < 0.55f) return Mathf.Lerp(30f, -160f, (s - 0.25f) / 0.3f);
            return Mathf.Lerp(-160f, 0f, (s - 0.55f) / 0.45f);
        }

        public void Destroy()
        {
            Object.Destroy(Root.gameObject);
            Object.Destroy(shadow.gameObject);
            if (meRing != null) Object.Destroy(meRing.gameObject);
        }
    }

    struct Popup
    {
        public string Text;
        public Vector3 World;
        public float Born;
        public Color Color;
    }

    /// <summary>比賽中會動的東西：六位球員、球、球的影子、鏡頭，以及擊球／得分的浮動文字。</summary>
    sealed class MatchView
    {
        const float PopupLife = 1.1f;

        readonly Transform root;
        readonly Camera cam;
        readonly Transform ball;
        readonly Transform ballShadow;
        readonly PlayerAvatar[] avatars = new PlayerAvatar[GameConfig.SlotCount];
        readonly List<Popup> popups = new List<Popup>();

        int builtForSlot = -2;
        float pulse;
        Vector3 lastBall;
        bool haveLastBall;

        public MatchView(Transform courtRoot, Camera cam)
        {
            root = new GameObject("MatchView").transform;
            root.SetParent(courtRoot, false);
            this.cam = cam;

            ball = Visuals.Prim(PrimitiveType.Sphere, "Ball", root, new Vector3(0f, 1f, 0f), Vector3.one * (GameConfig.BallRadius * 2f),
                new Color(1f, 0.96f, 0.75f)).transform;
            // 球的深色腰帶：球轉動時看得出旋轉
            GameObject band = Visuals.Prim(PrimitiveType.Cylinder, "Band", ball, Vector3.zero, new Vector3(1.01f, 0.03f, 1.01f), new Color(0.2f, 0.35f, 0.8f), false);
            band.transform.localRotation = Quaternion.Euler(0f, 0f, 35f);
            ballShadow = Visuals.Prim(PrimitiveType.Cylinder, "BallShadow", root, Vector3.zero, new Vector3(0.5f, 0.005f, 0.5f),
                new Color(0.12f, 0.15f, 0.22f), false).transform;
            Show(false);
        }

        public PlayerAvatar Avatar(int slot) => avatars[slot];
        public IReadOnlyList<Popup> Popups => popups;
        public float PopupLifetime => PopupLife;

        public void Show(bool on)
        {
            root.gameObject.SetActive(on);
            if (!on) builtForSlot = -2;
        }

        /// <summary>依畫面狀態擺放所有東西。mySlot 改變（新比賽、中途加入）時重建球員。</summary>
        public void Apply(RenderState rs, int mySlot, float dt)
        {
            if (builtForSlot != mySlot) Rebuild(mySlot);
            for (int i = 0; i < avatars.Length; i++) avatars[i].Apply(rs.Players[i]);

            Vector3 b = Conv.V(rs.BallPos);
            ball.position = b;
            float h = Mathf.Max(0f, b.y - GameConfig.BallRadius);
            float s = Mathf.Lerp(0.5f, 0.2f, Mathf.Clamp01(h / 6f));
            ballShadow.position = new Vector3(b.x, 0.008f, b.z);
            ballShadow.localScale = new Vector3(s, 0.005f, s);

            if (haveLastBall && dt > 0f)
            {
                Vector3 v = (b - lastBall) / dt;
                float speed = v.magnitude;
                if (speed > 0.05f && speed < 40f)
                {
                    Vector3 axis = Vector3.Cross(Vector3.up, v.normalized);
                    ball.Rotate(axis, speed / GameConfig.BallRadius * dt * Mathf.Rad2Deg, Space.World);
                }
            }

            lastBall = b;
            haveLastBall = true;

            pulse = Mathf.Max(0f, pulse - dt * 6f);
            ball.localScale = Vector3.one * (GameConfig.BallRadius * 2f * (1f + 0.5f * pulse));

            PlaceCamera(Court.TeamOf(mySlot));
            for (int i = popups.Count - 1; i >= 0; i--)
                if (Time.time - popups[i].Born > PopupLife)
                    popups.RemoveAt(i);
        }

        void Rebuild(int mySlot)
        {
            foreach (PlayerAvatar a in avatars) a?.Destroy();
            for (int i = 0; i < avatars.Length; i++) avatars[i] = new PlayerAvatar(root, Court.TeamOf(i), i == mySlot);
            builtForSlot = mySlot;
            haveLastBall = false;
        }

        /// <summary>鏡頭固定在自己這隊底線後方偏高處，朝球網看；球場永遠是「我方在下、對手在上」。</summary>
        void PlaceCamera(int team)
        {
            float s = team == 0 ? -1f : 1f;
            Vector3 pos = new Vector3(s * 14.5f, 9.5f, 0f);
            cam.transform.position = pos;
            cam.transform.rotation = Quaternion.LookRotation(new Vector3(s * 0.5f, 0.9f, 0f) - pos);
        }

        public void OnEvent(GameEvent e, int mySlot)
        {
            switch (e.Kind)
            {
                case EventKind.Hit:
                    pulse = 1f;
                    AddPopup(HitText((HitKind)e.A), e.Pos, Visuals.TeamColor(e.Team));
                    break;
                case EventKind.NetTouch:
                    AddPopup("觸網", e.Pos, Color.white);
                    break;
                case EventKind.Landed:
                    AddPopup(e.A == 1 ? "界內" : "出界", e.Pos, e.A == 1 ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 0.6f, 0.4f));
                    break;
                case EventKind.DoubleTouch:
                    if (e.Slot == mySlot && avatars[mySlot] != null)
                        popups.Add(new Popup { Text = "不能連續觸球", World = avatars[mySlot].HeadPos, Born = Time.time, Color = new Color(1f, 0.85f, 0.3f) });
                    break;
            }
        }

        void AddPopup(string text, Vec3 world, Color color)
        {
            popups.Add(new Popup { Text = text, World = Conv.V(world) + Vector3.up * 0.6f, Born = Time.time, Color = color });
        }

        static string HitText(HitKind kind)
        {
            switch (kind)
            {
                case HitKind.Serve: return "發球";
                case HitKind.Bump: return "接球";
                case HitKind.Set: return "托球";
                case HitKind.Spike: return "扣球！";
                default: return "過網";
            }
        }
    }
}
