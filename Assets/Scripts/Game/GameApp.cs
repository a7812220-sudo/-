using System;
using System.Collections.Generic;
using UnityEngine;
using Volleyball.Core;

namespace Volleyball.Game
{
    /// <summary>
    /// 遊戲主程式：持有目前的連線（NetSession）、3D 場景與介面，每個畫面推進一次。
    /// 畫面種類由連線狀態推得，不另外維護狀態機：沒連線 = 選單；已連線但還沒收到座位表 = 連線中；
    /// 座位表顯示尚未開賽 = 大廳；比賽中 = 比賽。
    /// </summary>
    public sealed class GameApp : MonoBehaviour
    {
        internal enum ScreenKind
        {
            Menu,
            Connecting,
            Lobby,
            Match,
        }

        internal static GameApp Instance { get; private set; }

        internal NetSession Session { get; private set; }
        internal string LastError { get; private set; }
        internal MatchView View { get; private set; }
        internal Camera Cam { get; private set; }
        internal InputReader Reader { get; } = new InputReader();

        GameUi ui;
        readonly List<GameEvent> eventBuf = new List<GameEvent>();

        internal ScreenKind Mode
        {
            get
            {
                if (Session == null) return ScreenKind.Menu;
                GameClient c = Session.Client;
                if (c == null || c.Room == null) return ScreenKind.Connecting;
                return c.Room.InMatch ? ScreenKind.Match : ScreenKind.Lobby;
            }
        }

        void Awake()
        {
            Instance = this;
            Application.runInBackground = true; // 視窗失焦時房主仍要繼續模擬與收發封包

            Transform court = CourtBuilder.Build();
            Cam = CourtBuilder.SetupCameraAndLight();
            View = new MatchView(court, Cam);
            ui = new GameUi(this);

            // 選單／大廳背景：從藍隊底線後方看空球場
            Cam.transform.position = new Vector3(-14.5f, 9.5f, 0f);
            Cam.transform.rotation = Quaternion.LookRotation(new Vector3(-0.5f, 0.9f, 0f) - Cam.transform.position);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;

            GameClient client = Session?.Client;
            bool inMatch = client != null && client.InMatch;
            if (inMatch && Reader.EscapePressed) ui.PauseOpen = !ui.PauseOpen;
            if (!inMatch) ui.PauseOpen = false;
            Reader.Enabled = inMatch && !ui.PauseOpen;
            Reader.Poll();

            if (Session != null)
            {
                Session.Tick(dt, Reader);
                if (Session.Closed)
                {
                    LastError = Session.Error;
                    Session = null;
                    client = null;
                }
            }

            if (client != null && client.InMatch && client.Render.Valid)
            {
                View.Show(true);
                View.Apply(client.Render, client.MySlot, dt);
                eventBuf.Clear();
                client.PopDueEvents(eventBuf);
                foreach (GameEvent e in eventBuf)
                {
                    View.OnEvent(e, client.MySlot);
                    ui.OnEvent(e);
                }
            }
            else
            {
                View.Show(false);
            }
        }

        void OnGUI()
        {
            ui?.Draw();
        }

        void OnApplicationQuit()
        {
            Session?.Close();
        }

        // ---------- 介面呼叫的動作 ----------

        internal void StartSolo(string name) => Begin(() => NetSession.StartSolo(name));

        internal void StartRelayHost(string name) => Begin(() => NetSession.StartRelayHost(name));

        internal void StartRelayJoin(string name, string code) => Begin(() => NetSession.StartRelayJoin(name, code));

        internal void StartDirectHost(string name, ushort port) => Begin(() => NetSession.StartDirectHost(name, port));

        internal void StartDirectJoin(string name, string ip, ushort port) => Begin(() => NetSession.StartDirectJoin(name, ip, port));

        internal void Leave()
        {
            Session?.Close();
            Session = null;
            View.Show(false);
        }

        void Begin(Func<NetSession> create)
        {
            Leave();
            LastError = null;
            ui.PauseOpen = false;
            Session = create();
        }
    }
}
