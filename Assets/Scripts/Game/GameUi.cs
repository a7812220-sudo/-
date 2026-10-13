using UnityEngine;
using Volleyball.Core;

namespace Volleyball.Game
{
    /// <summary>
    /// 全部介面用 IMGUI（OnGUI）畫：不依賴 uGUI／UI Toolkit／任何素材。
    /// 以 1280×720 為基準自動縮放。中文字用系統字型（Windows 正黑體、macOS 蘋方、Linux Noto CJK）。
    /// </summary>
    sealed class GameUi
    {
        const string NameKey = "volleyball.name";

        readonly GameApp app;

        GUIStyle label, small, title, huge, button, field, toggle, panel, center, centerSmall;
        bool stylesReady;
        float scale = 1f;

        string nameField;
        string codeField = "";
        string ipField = "127.0.0.1";
        string portField = "7777";
        bool showAdvanced;

        string pointText = "";
        float pointTime = -100f;

        public bool PauseOpen;

        public GameUi(GameApp app)
        {
            this.app = app;
            nameField = PlayerPrefs.GetString(NameKey, "玩家" + Random.Range(100, 1000));
        }

        // ---------- 事件 ----------

        public void OnEvent(GameEvent e)
        {
            if (e.Kind != EventKind.Point) return;
            string why;
            switch ((PointReason)e.A)
            {
                case PointReason.Out: why = "出界"; break;
                case PointReason.FourTouches: why = "第四次觸球"; break;
                default: why = "落地"; break;
            }

            pointText = "<color=" + (e.Team == 0 ? "#6FA8FF" : "#FF7A7A") + ">" + Visuals.TeamName(e.Team) + "得分</color>　" + why;
            pointTime = Time.time;
        }

        // ---------- 入口 ----------

        public void Draw()
        {
            EnsureStyles();
            scale = Screen.height / 720f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale;

            if (app.Reader.Error != null)
            {
                Fill(new Rect(0, 0, w, 90), new Color(0.6f, 0.1f, 0.1f, 0.95f));
                GUI.Label(new Rect(20, 6, w - 40, 80), app.Reader.Error, small);
            }

            switch (app.Mode)
            {
                case GameApp.ScreenKind.Menu: DrawMenu(w); break;
                case GameApp.ScreenKind.Connecting: DrawConnecting(w); break;
                case GameApp.ScreenKind.Lobby: DrawLobby(w); break;
                default: DrawMatch(w); break;
            }
        }

        // ---------- 選單 ----------

        string PlayerName()
        {
            string n = nameField.Trim();
            return n.Length == 0 ? "玩家" : n;
        }

        void DrawMenu(float w)
        {
            // IMGUI 的 Layout 與後續事件必須畫出相同數量的控制項：
            // 按鈕會在同一回合裡改變這些狀態（例如清掉 LastError），所以先拷貝一份當作本回合的依據。
            string error = app.LastError;
            bool advanced = showAdvanced;

            GUILayout.BeginArea(new Rect((w - 580) / 2, 40, 580, 640), panel);
            GUILayout.Label("3v3 排球", huge);
            GUILayout.Label("真人不足的位子，由電腦補滿", small);
            GUILayout.Space(14);

            GUILayout.BeginHorizontal();
            GUILayout.Label("暱稱", label, GUILayout.Width(80));
            string edited = GUILayout.TextField(nameField, 12, field, GUILayout.Height(40));
            if (edited != nameField)
            {
                nameField = edited;
                PlayerPrefs.SetString(NameKey, nameField);
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(14);

            if (GUILayout.Button("單人練習（5 位電腦）", button, GUILayout.Height(52))) app.StartSolo(PlayerName());
            GUILayout.Space(8);
            if (GUILayout.Button("建立房間（Unity Relay）", button, GUILayout.Height(52))) app.StartRelayHost(PlayerName());
            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            codeField = GUILayout.TextField(codeField, 12, field, GUILayout.Height(48));
            GUI.enabled = codeField.Trim().Length > 0;
            if (GUILayout.Button("輸入代碼加入", button, GUILayout.Width(200), GUILayout.Height(48)))
                app.StartRelayJoin(PlayerName(), codeField);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label("朋友建立房間後會得到一組代碼，貼在這裡就能加入", small);

            GUILayout.Space(10);
            showAdvanced = GUILayout.Toggle(advanced, " 進階：區網直連（不需要 Unity 帳號）", toggle);
            if (advanced)
            {
                if (GUILayout.Button("建立區網主機", button, GUILayout.Height(44)))
                    app.StartDirectHost(PlayerName(), ParsePort());
                GUILayout.BeginHorizontal();
                ipField = GUILayout.TextField(ipField, 40, field, GUILayout.Height(40));
                GUILayout.Label(":", label, GUILayout.Width(14));
                portField = GUILayout.TextField(portField, 5, field, GUILayout.Width(90), GUILayout.Height(40));
                if (GUILayout.Button("連線", button, GUILayout.Width(110), GUILayout.Height(40)))
                    app.StartDirectJoin(PlayerName(), ipField, ParsePort());
                GUILayout.EndHorizontal();
            }

            if (!string.IsNullOrEmpty(error))
            {
                GUILayout.Space(10);
                GUILayout.Label("<color=#FF8A7A>" + error + "</color>", small);
            }

            GUILayout.EndArea();
        }

        ushort ParsePort()
        {
            return ushort.TryParse(portField, out ushort p) && p > 0 ? p : (ushort)7777;
        }

        void DrawConnecting(float w)
        {
            GUILayout.BeginArea(new Rect((w - 520) / 2, 220, 520, 220), panel);
            string status = app.Session.Status;
            GUILayout.Label(string.IsNullOrEmpty(status) ? "連線中…" : status, center);
            GUILayout.Space(24);
            if (GUILayout.Button("取消", button, GUILayout.Height(46))) app.Leave();
            GUILayout.EndArea();
        }

        // ---------- 大廳 ----------

        void DrawLobby(float w)
        {
            NetSession session = app.Session;
            GameClient client = session.Client;
            RoomState room = client.Room;

            GUILayout.BeginArea(new Rect((w - 800) / 2, 30, 800, 660), panel);
            GUILayout.Label("房間", title);

            if (!string.IsNullOrEmpty(session.JoinCode))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("房間代碼", label, GUILayout.Width(120));
                GUILayout.Label("<b>" + session.JoinCode + "</b>", huge);
                if (GUILayout.Button("複製", button, GUILayout.Width(100), GUILayout.Height(44))) GUIUtility.systemCopyBuffer = session.JoinCode;
                GUILayout.EndHorizontal();
                GUILayout.Label("把代碼傳給朋友，他們在選單輸入代碼就能加入", small);
            }
            else if (!string.IsNullOrEmpty(session.DirectAddress))
            {
                GUILayout.Label("區網位址：<b>" + session.DirectAddress + "</b>", label);
                GUILayout.Label("朋友在選單的「進階：區網直連」輸入這個位址（需同一個區網或 VPN）", small);
            }

            GUILayout.Space(12);
            GUILayout.BeginHorizontal();
            for (int team = 0; team < 2; team++)
            {
                GUILayout.BeginVertical(GUILayout.Width(360));
                string teamColor = team == 0 ? "#6FA8FF" : "#FF7A7A";
                GUILayout.Label("<color=" + teamColor + "><b>" + Visuals.TeamName(team) + "</b></color>", title);
                bool hasEmpty = false;
                for (int i = 0; i < GameConfig.PlayersPerTeam; i++)
                {
                    int slot = team * GameConfig.PlayersPerTeam + i;
                    SlotInfo s = room.Slots[slot];
                    string text;
                    if (s.Kind == SlotKind.Empty)
                    {
                        hasEmpty = true;
                        text = "<color=#8893A0>（空位 — 開賽時由電腦補上）</color>";
                    }
                    else
                    {
                        text = s.Name + (slot == room.YourSlot ? "　<color=#FFD54A>← 你</color>" : "");
                    }

                    GUILayout.Label(text, label, GUILayout.Height(40));
                }

                if (Court.TeamOf(room.YourSlot) != team && hasEmpty)
                {
                    if (GUILayout.Button("換到" + Visuals.TeamName(team), button, GUILayout.Height(40))) client.SetTeam(team);
                }

                GUILayout.EndVertical();
                GUILayout.Space(20);
            }

            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            if (room.YouAreHost)
            {
                if (GUILayout.Button("開始比賽（空位由電腦補滿）", button, GUILayout.Height(56))) client.RequestStart();
            }
            else
            {
                GUILayout.Label("等待房主開始比賽…", center);
            }

            if (GUILayout.Button("離開房間", button, GUILayout.Height(44))) app.Leave();
            GUILayout.EndArea();
        }

        // ---------- 比賽 ----------

        void DrawMatch(float w)
        {
            GameClient client = app.Session.Client;
            RenderState rs = client.Render;
            RoomState room = client.Room;
            if (!rs.Valid)
            {
                Label(new Rect(0, 300, w, 60), "載入比賽…", center);
                return;
            }

            DrawNames(room);
            DrawPopups();
            DrawScoreboard(w, rs);
            DrawStatusText(w, rs, room);

            Label(new Rect(16, 686, 900, 28),
                "<color=#CBD5E1>WASD 移動　空白鍵 跳　J／滑鼠左鍵 擊球（按住方向鍵決定球路）　Esc 選單</color>", small);
            if (!string.IsNullOrEmpty(app.Session.JoinCode))
                Label(new Rect(w - 260, 686, 244, 28), "<color=#CBD5E1>房間代碼 " + app.Session.JoinCode + "</color>", new GUIStyle(small) { alignment = TextAnchor.MiddleRight });

            if (PauseOpen) DrawPause(w);
        }

        void DrawNames(RoomState room)
        {
            for (int slot = 0; slot < GameConfig.SlotCount; slot++)
            {
                PlayerAvatar a = app.View.Avatar(slot);
                if (a == null) continue;
                Vector3 sp = app.Cam.WorldToScreenPoint(a.HeadPos);
                if (sp.z <= 0f) continue;
                float x = sp.x / scale, y = (Screen.height - sp.y) / scale;
                string text = (slot == room.YourSlot ? "▼ " : "") + room.Slots[slot].Name;
                Outlined(new Rect(x - 100, y - 26, 200, 28), text, centerSmall, Visuals.TeamColor(Court.TeamOf(slot)));
            }
        }

        void DrawPopups()
        {
            float life = app.View.PopupLifetime;
            foreach (Popup p in app.View.Popups)
            {
                float age = Time.time - p.Born;
                if (age < 0f || age > life) continue;
                Vector3 sp = app.Cam.WorldToScreenPoint(p.World);
                if (sp.z <= 0f) continue;
                float x = sp.x / scale, y = (Screen.height - sp.y) / scale - 40f * age;
                Color c = p.Color;
                c.a = 1f - age / life;
                Outlined(new Rect(x - 120, y - 20, 240, 40), p.Text, center, c);
            }
        }

        void DrawScoreboard(float w, RenderState rs)
        {
            Rect r = new Rect((w - 440) / 2, 12, 440, 64);
            Fill(r, new Color(0f, 0f, 0f, 0.55f));
            Fill(new Rect(r.x, r.y, 6, r.height), Visuals.Blue);
            Fill(new Rect(r.xMax - 6, r.y, 6, r.height), Visuals.Red);

            string serve0 = rs.ServingTeam == 0 ? " ●" : "";
            string serve1 = rs.ServingTeam == 1 ? "● " : "";
            Label(new Rect(r.x + 14, r.y, 140, r.height), "<color=#6FA8FF>藍隊</color>" + serve0, label);
            Label(new Rect(r.xMax - 154, r.y, 140, r.height), serve1 + "<color=#FF7A7A>紅隊</color>", new GUIStyle(label) { alignment = TextAnchor.MiddleRight });
            Label(new Rect(r.x + 150, r.y, 140, r.height), rs.Score0 + "  :  " + rs.Score1, new GUIStyle(huge) { alignment = TextAnchor.MiddleCenter });

            if (rs.Phase == Phase.Rally && rs.LastTouchTeam >= 0 && !rs.ServeInFlight)
            {
                string dots = "";
                for (int i = 1; i <= 3; i++) dots += i <= rs.TouchCount ? "●" : "○";
                string color = rs.LastTouchTeam == 0 ? "#6FA8FF" : "#FF7A7A";
                Label(new Rect(r.x, r.yMax + 2, r.width, 30), "<color=" + color + ">" + Visuals.TeamName(rs.LastTouchTeam) + "觸球 " + dots + "</color>", centerSmall);
            }
        }

        void DrawStatusText(float w, RenderState rs, RoomState room)
        {
            string text = null;
            GUIStyle style = center;
            switch (rs.Phase)
            {
                case Phase.Countdown:
                    text = Mathf.Max(1, Mathf.CeilToInt(GameConfig.CountdownTime - rs.PhaseTime)).ToString();
                    style = huge;
                    break;
                case Phase.ServeWait:
                    text = rs.ServerSlot == room.YourSlot
                        ? "輪到你發球！按 J 或滑鼠左鍵（方向鍵決定落點）"
                        : room.Slots[rs.ServerSlot].Name + " 準備發球";
                    break;
                case Phase.PointPause:
                    if (Time.time - pointTime < GameConfig.PointPauseTime + 0.5f) text = pointText;
                    break;
                case Phase.GameOver:
                    int winner = rs.Score0 > rs.Score1 ? 0 : 1;
                    float left = Mathf.Max(0f, GameConfig.ResultsTime - rs.PhaseTime);
                    text = "<color=" + (winner == 0 ? "#6FA8FF" : "#FF7A7A") + "><b>" + Visuals.TeamName(winner) + "獲勝！</b></color>　"
                           + rs.Score0 + " : " + rs.Score1 + "\n<size=22>" + Mathf.CeilToInt(left) + " 秒後回到房間</size>";
                    style = title;
                    break;
            }

            if (text == null) return;
            Rect r = new Rect(0, 210, w, 150);
            Fill(new Rect(w * 0.5f - 420, r.y + 20, 840, r.height - 40), new Color(0f, 0f, 0f, 0.35f));
            Label(r, text, style);
        }

        void DrawPause(float w)
        {
            Fill(new Rect(0, 0, w, 720), new Color(0f, 0f, 0f, 0.6f));
            GUILayout.BeginArea(new Rect((w - 420) / 2, 220, 420, 280), panel);
            GUILayout.Label("選單", title);
            GUILayout.Space(16);
            if (GUILayout.Button("繼續比賽", button, GUILayout.Height(52))) PauseOpen = false;
            GUILayout.Space(10);
            if (GUILayout.Button("離開比賽", button, GUILayout.Height(52)))
            {
                PauseOpen = false;
                app.Leave();
            }

            GUILayout.EndArea();
        }

        // ---------- 繪製小工具 ----------

        static void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        static void Label(Rect r, string text, GUIStyle style) => GUI.Label(r, text, style);

        /// <summary>有黑色描邊的文字，在 3D 場景上才讀得清楚。</summary>
        static void Outlined(Rect r, string text, GUIStyle style, Color color)
        {
            Color old = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, color.a);
            foreach (Vector2 d in new[] { new Vector2(-1.5f, 0), new Vector2(1.5f, 0), new Vector2(0, -1.5f), new Vector2(0, 1.5f) })
                GUI.Label(new Rect(r.x + d.x, r.y + d.y, r.width, r.height), text, style);
            style.normal.textColor = color;
            GUI.Label(r, text, style);
            style.normal.textColor = old;
        }

        // ---------- 樣式 ----------

        static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        static GUIStyle Make(GUIStyle basis, Font font, int size, TextAnchor anchor)
        {
            var s = new GUIStyle(basis)
            {
                font = font,
                fontSize = size,
                alignment = anchor,
                richText = true,
                wordWrap = true,
            };
            s.normal.textColor = Color.white;
            s.hover.textColor = Color.white;
            s.active.textColor = Color.white;
            s.focused.textColor = Color.white;
            return s;
        }

        void EnsureStyles()
        {
            if (stylesReady) return;

            Font font = Font.CreateDynamicFontFromOSFont(new[]
            {
                "Microsoft JhengHei UI", "Microsoft JhengHei", "PingFang TC", "Heiti TC", "Noto Sans CJK TC", "Noto Sans TC",
                "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", "WenQuanYi Zen Hei", "Arial Unicode MS", "Arial",
            }, 24);

            label = Make(GUI.skin.label, font, 22, TextAnchor.MiddleLeft);
            small = Make(GUI.skin.label, font, 17, TextAnchor.MiddleLeft);
            title = Make(GUI.skin.label, font, 30, TextAnchor.MiddleLeft);
            huge = Make(GUI.skin.label, font, 46, TextAnchor.MiddleLeft);
            center = Make(GUI.skin.label, font, 30, TextAnchor.MiddleCenter);
            centerSmall = Make(GUI.skin.label, font, 18, TextAnchor.MiddleCenter);
            button = Make(GUI.skin.button, font, 24, TextAnchor.MiddleCenter);
            field = Make(GUI.skin.textField, font, 22, TextAnchor.MiddleLeft);
            toggle = Make(GUI.skin.toggle, font, 19, TextAnchor.MiddleLeft);

            panel = new GUIStyle(GUI.skin.box) { padding = new RectOffset(32, 32, 24, 24) };
            panel.normal.background = Tex(new Color(0.07f, 0.09f, 0.13f, 0.93f));
            stylesReady = true;
        }
    }
}
