using System.Collections.Generic;

namespace Volleyball.Core
{
    /// <summary>
    /// 房間與比賽的權威伺服器邏輯，與傳輸層無關：
    /// 外部把連線事件與收到的位元組餵進來，並提供 send 回呼負責送出。
    /// 真人不足的座位在開賽時由電腦補滿；比賽中有人離線由電腦接手、有人加入則取代一個電腦。
    /// </summary>
    public sealed class GameServer
    {
        public delegate void SendFn(int clientId, byte[] data, int length, bool reliable);

        const int MaxInputBacklog = 6; // 超過就一次吃兩幀追上進度
        const int MaxQueuedInputs = 30;
        const int StarvedTicksBeforeStop = 15; // 這麼久都沒收到輸入，就讓角色停下來

        sealed class Client
        {
            public int Id;
            public string Name;
            public int Slot = -1; // -1 = 尚未入座
            public readonly Queue<InputFrame> Inputs = new Queue<InputFrame>();
            public bool HaveSeq;
            public uint LastQueuedSeq;
            public uint LastAppliedSeq; // 隨快照回傳給客戶端當 Ack
            public InputFrame Held; // 佇列空時沿用上一幀的移動（邊緣動作已清除）
            public int StarvedTicks;
        }

        readonly SendFn send;
        readonly int hostClientId;
        readonly Dictionary<int, Client> clients = new Dictionary<int, Client>();
        readonly SlotInfo[] slots = new SlotInfo[GameConfig.SlotCount];
        readonly int[] slotClient = new int[GameConfig.SlotCount];
        readonly BotBrain[] bots = new BotBrain[GameConfig.SlotCount];
        readonly Rng rng;
        readonly ByteWriter writer = new ByteWriter(512);
        readonly ByteWriter body = new ByteWriter(512);
        readonly List<InputFrame> parsed = new List<InputFrame>(8);
        readonly InputFrame[] tickInputs = new InputFrame[GameConfig.SlotCount];

        VolleyballMatch match;
        float accumulator;
        float resultsTimer;
        int botCounter;

        public GameServer(SendFn send, int hostClientId, uint seed)
        {
            this.send = send;
            this.hostClientId = hostClientId;
            rng = new Rng(seed);
            for (int i = 0; i < slotClient.Length; i++) slotClient[i] = -1;
        }

        public bool InMatch => match != null;
        public VolleyballMatch Match => match;

        // ---------- 連線事件 ----------

        public void OnClientConnected(int id)
        {
            GetOrAddClient(id);
        }

        public void OnClientDisconnected(int id)
        {
            if (!clients.TryGetValue(id, out Client c)) return;
            clients.Remove(id);
            if (c.Slot < 0) return;

            int s = c.Slot;
            slotClient[s] = -1;
            if (match != null) MakeBot(s);
            else slots[s] = default;

            if (match != null && HumanCount() == 0) ReturnToLobby();
            else BroadcastRoom();
        }

        public void OnMessage(int id, byte[] data, int length)
        {
            if (length <= 0) return;
            Client c = GetOrAddClient(id);
            var r = new ByteReader(data, length);
            switch ((MsgType)r.ReadByte())
            {
                case MsgType.Hello:
                    HandleHello(c, r.ReadString());
                    break;
                case MsgType.SetTeam:
                    HandleSetTeam(c, r.ReadByte());
                    break;
                case MsgType.Start:
                    HandleStart(c);
                    break;
                case MsgType.Input:
                    HandleInput(c, r);
                    break;
            }
        }

        // ---------- 訊息處理 ----------

        void HandleHello(Client c, string name)
        {
            if (c.Slot >= 0) return;

            c.Name = SanitizeName(name, c.Id);
            int slot = FindSeat(c);
            if (slot < 0)
            {
                Protocol.WriteReject(writer, "房間已滿");
                send(c.Id, writer.Buffer, writer.Length, true);
                return;
            }

            slots[slot] = new SlotInfo { Kind = SlotKind.Human, Name = c.Name };
            slotClient[slot] = c.Id;
            bots[slot] = null;
            c.Slot = slot;
            c.Inputs.Clear();
            c.HaveSeq = false;
            c.Held = default;
            BroadcastRoom();
        }

        void HandleSetTeam(Client c, int team)
        {
            if (match != null || c.Slot < 0 || team < 0 || team > 1) return;
            if (Court.TeamOf(c.Slot) == team) return;

            int target = FirstSlot(team, SlotKind.Empty);
            if (target < 0) return;

            slots[target] = slots[c.Slot];
            slotClient[target] = c.Id;
            slots[c.Slot] = default;
            slotClient[c.Slot] = -1;
            c.Slot = target;
            BroadcastRoom();
        }

        void HandleStart(Client c)
        {
            if (match != null || c.Id != hostClientId || c.Slot < 0) return;

            botCounter = 0;
            for (int s = 0; s < GameConfig.SlotCount; s++)
                if (slots[s].Kind == SlotKind.Empty)
                    MakeBot(s);
            foreach (Client other in clients.Values)
            {
                other.Inputs.Clear();
                other.HaveSeq = false;
                other.Held = default;
            }

            match = new VolleyballMatch(rng.NextUInt(), (int)(rng.NextUInt() & 1u));
            accumulator = 0f;
            resultsTimer = 0f;
            BroadcastRoom();
        }

        void HandleInput(Client c, ByteReader r)
        {
            if (c.Slot < 0) return;
            parsed.Clear();
            Protocol.ReadInput(r, parsed);
            foreach (InputFrame f in parsed)
            {
                if (!c.HaveSeq)
                {
                    c.HaveSeq = true;
                    c.LastQueuedSeq = f.Seq - 1;
                }

                if (f.Seq <= c.LastQueuedSeq) continue; // 重複（冗餘傳送）或舊的
                c.LastQueuedSeq = f.Seq;
                if (c.Inputs.Count >= MaxQueuedInputs) c.Inputs.Dequeue();
                c.Inputs.Enqueue(f);
            }
        }

        // ---------- 座位 ----------

        int FindSeat(Client c)
        {
            int humans0 = HumansOnTeam(0), humans1 = HumansOnTeam(1);
            int preferred = c.Id == hostClientId ? 0 : (humans1 < humans0 ? 1 : 0);
            SlotKind want = SlotKind.Empty;
            if (match != null)
            {
                // 比賽中加入：取代電腦。真人較少的一隊優先；一樣多就選電腦較多的一隊
                want = SlotKind.Bot;
                if (humans0 == humans1) preferred = BotsOnTeam(1) > BotsOnTeam(0) ? 1 : 0;
            }

            int slot = FirstSlot(preferred, want);
            return slot >= 0 ? slot : FirstSlot(1 - preferred, want);
        }

        int FirstSlot(int team, SlotKind kind)
        {
            for (int s = team * GameConfig.PlayersPerTeam; s < (team + 1) * GameConfig.PlayersPerTeam; s++)
                if (slots[s].Kind == kind)
                    return s;
            return -1;
        }

        int HumansOnTeam(int team) => CountOnTeam(team, SlotKind.Human);

        int BotsOnTeam(int team) => CountOnTeam(team, SlotKind.Bot);

        int CountOnTeam(int team, SlotKind kind)
        {
            int n = 0;
            for (int s = team * GameConfig.PlayersPerTeam; s < (team + 1) * GameConfig.PlayersPerTeam; s++)
                if (slots[s].Kind == kind)
                    n++;
            return n;
        }

        int HumanCount() => HumansOnTeam(0) + HumansOnTeam(1);

        void MakeBot(int slot)
        {
            slots[slot] = new SlotInfo { Kind = SlotKind.Bot, Name = "電腦" + (++botCounter) };
            bots[slot] = new BotBrain(slot, rng.NextUInt());
        }

        Client GetOrAddClient(int id)
        {
            if (!clients.TryGetValue(id, out Client c))
            {
                c = new Client { Id = id };
                clients[id] = c;
            }

            return c;
        }

        static string SanitizeName(string name, int id)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char ch in name ?? string.Empty)
                if (ch >= ' ' && ch != '\u007f')
                    sb.Append(ch);
            string s = sb.ToString().Trim();
            if (s.Length > Protocol.MaxNameLength) s = s.Substring(0, Protocol.MaxNameLength);
            return s.Length == 0 ? "玩家" + id : s;
        }

        // ---------- 模擬迴圈 ----------

        public void Update(float dt)
        {
            if (match == null) return;

            accumulator += dt;
            if (accumulator > 0.25f) accumulator = 0.25f;
            while (accumulator >= GameConfig.TickDt && match != null)
            {
                accumulator -= GameConfig.TickDt;
                Tick();
            }
        }

        void Tick()
        {
            for (int s = 0; s < GameConfig.SlotCount; s++)
            {
                if (slots[s].Kind == SlotKind.Human) tickInputs[s] = NextInput(clients[slotClient[s]]);
                else if (slots[s].Kind == SlotKind.Bot) tickInputs[s] = bots[s].Think(match, GameConfig.TickDt);
                else tickInputs[s] = default;
            }

            match.Step(tickInputs, GameConfig.TickDt);

            if (match.Events.Count > 0)
            {
                Protocol.WriteEvents(writer, match.Tick, match.Events);
                SendToSeated(writer, true);
            }

            if (match.Tick % GameConfig.SnapshotInterval == 0) SendSnapshots();

            if (match.Phase == Phase.GameOver)
            {
                resultsTimer += GameConfig.TickDt;
                if (resultsTimer >= GameConfig.ResultsTime) ReturnToLobby();
            }
        }

        InputFrame NextInput(Client c)
        {
            // 積壓太多（客戶端時鐘比較快或封包擠在一起）：一次吃兩幀，邊緣動作合併
            if (c.Inputs.Count > MaxInputBacklog)
            {
                InputFrame skipped = c.Inputs.Dequeue();
                InputFrame next = c.Inputs.Dequeue();
                next.Jump |= skipped.Jump;
                next.Hit |= skipped.Hit;
                return Apply(c, next);
            }

            if (c.Inputs.Count > 0) return Apply(c, c.Inputs.Dequeue());

            if (++c.StarvedTicks > StarvedTicksBeforeStop)
            {
                c.Held.Right = 0;
                c.Held.Forward = 0;
            }

            return c.Held;
        }

        static InputFrame Apply(Client c, InputFrame f)
        {
            c.StarvedTicks = 0;
            c.LastAppliedSeq = f.Seq;
            c.Held = f;
            c.Held.Jump = false;
            c.Held.Hit = false;
            return f;
        }

        void SendSnapshots()
        {
            Protocol.WriteSnapshotBody(body, match);
            foreach (Client c in clients.Values)
            {
                if (c.Slot < 0) continue;
                writer.Reset();
                writer.WriteBytes(body.Buffer, body.Length);
                Protocol.AppendAck(writer, c.LastAppliedSeq);
                send(c.Id, writer.Buffer, writer.Length, false);
            }
        }

        void SendToSeated(ByteWriter w, bool reliable)
        {
            foreach (Client c in clients.Values)
                if (c.Slot >= 0)
                    send(c.Id, w.Buffer, w.Length, reliable);
        }

        void ReturnToLobby()
        {
            match = null;
            for (int s = 0; s < GameConfig.SlotCount; s++)
            {
                bots[s] = null;
                if (slots[s].Kind == SlotKind.Bot) slots[s] = default;
            }

            foreach (Client c in clients.Values)
            {
                c.Inputs.Clear();
                c.HaveSeq = false;
            }

            BroadcastRoom();
        }

        void BroadcastRoom()
        {
            var room = new RoomState { InMatch = match != null };
            for (int s = 0; s < GameConfig.SlotCount; s++) room.Slots[s] = slots[s];
            foreach (Client c in clients.Values)
            {
                if (c.Slot < 0) continue;
                room.YourSlot = c.Slot;
                room.YouAreHost = c.Id == hostClientId;
                Protocol.WriteRoom(writer, room);
                send(c.Id, writer.Buffer, writer.Length, true);
            }
        }
    }
}
