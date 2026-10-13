using System.Collections.Generic;
using Volleyball.Core;

namespace Volleyball.Tests
{
    /// <summary>模擬一位玩家的鍵盤：持續按住的方向，加上「按一下」的跳躍／擊球。</summary>
    sealed class TestClient : IInputSource
    {
        public readonly int Id;
        public GameClient Client;
        public float Right, Forward;
        bool jump, hit;

        public TestClient(int id) => Id = id;

        public void PressJump() => jump = true;
        public void PressHit() => hit = true;

        public InputFrame Sample()
        {
            var f = new InputFrame
            {
                Right = InputFrame.Quantize(Right),
                Forward = InputFrame.Quantize(Forward),
                Jump = jump,
                Hit = hit,
            };
            jump = hit = false;
            return f;
        }
    }

    /// <summary>
    /// 把 GameServer 與多個 GameClient 用記憶體串起來，可設定單程延遲、抖動、非可靠封包掉包率。
    /// 可靠封包保證順序（像 TCP / NGO 的 ReliableSequenced）。
    /// </summary>
    sealed class LoopbackNetwork
    {
        public const int ServerId = -1;

        struct Packet
        {
            public float At;
            public long Order;
            public int From, To;
            public byte[] Data;
        }

        readonly List<Packet> queue = new List<Packet>();
        readonly List<Packet> ready = new List<Packet>();
        readonly Dictionary<int, TestClient> clients = new Dictionary<int, TestClient>();
        readonly Dictionary<int, float> latency = new Dictionary<int, float>();
        readonly Dictionary<long, float> lastReliableAt = new Dictionary<long, float>();
        readonly Rng rng = new Rng(77);
        float now;
        long order;

        public float DefaultLatency = 0.03f; // 單程，秒
        public float Jitter; // 0..Jitter 的額外隨機延遲
        public float Loss; // 非可靠封包掉包率
        public readonly GameServer Server;

        public LoopbackNetwork(int hostId = 0)
        {
            Server = new GameServer((id, data, len, reliable) => Enqueue(ServerId, id, data, len, reliable), hostId, 1234);
        }

        public TestClient Connect(int id, string name, float? oneWayLatency = null)
        {
            var tc = new TestClient(id);
            tc.Client = new GameClient((data, len, reliable) => Enqueue(id, ServerId, data, len, reliable), name);
            clients[id] = tc;
            latency[id] = oneWayLatency ?? DefaultLatency;
            Server.OnClientConnected(id);
            tc.Client.OnConnected();
            return tc;
        }

        public void Disconnect(int id)
        {
            clients.Remove(id);
            queue.RemoveAll(p => p.From == id || p.To == id);
            Server.OnClientDisconnected(id);
        }

        void Enqueue(int from, int to, byte[] data, int len, bool reliable)
        {
            if (!reliable && Loss > 0f && rng.Value01() < Loss) return;

            int clientId = from == ServerId ? to : from;
            float lat = latency.TryGetValue(clientId, out float l) ? l : DefaultLatency;
            float at = now + lat + (Jitter > 0f ? rng.Range(0f, Jitter) : 0f);
            if (reliable)
            {
                long key = ((long)from << 32) ^ (uint)to;
                if (lastReliableAt.TryGetValue(key, out float last) && at < last) at = last;
                lastReliableAt[key] = at;
            }

            var copy = new byte[len];
            System.Array.Copy(data, copy, len);
            queue.Add(new Packet { At = at, Order = order++, From = from, To = to, Data = copy });
        }

        public void Advance(float seconds, float frameDt = 1f / 60f)
        {
            int frames = (int)(seconds / frameDt + 0.5f);
            for (int i = 0; i < frames; i++)
            {
                now += frameDt;
                Deliver();
                Server.Update(frameDt);
                foreach (TestClient tc in new List<TestClient>(clients.Values)) tc.Client.Update(frameDt, tc);
            }
        }

        void Deliver()
        {
            ready.Clear();
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                if (queue[i].At > now) continue;
                ready.Add(queue[i]);
                queue.RemoveAt(i);
            }

            ready.Sort((a, b) => a.At != b.At ? a.At.CompareTo(b.At) : a.Order.CompareTo(b.Order));
            foreach (Packet p in ready)
            {
                if (p.To == ServerId) Server.OnMessage(p.From, p.Data, p.Data.Length);
                else if (clients.TryGetValue(p.To, out TestClient tc)) tc.Client.OnMessage(p.Data, p.Data.Length);
            }
        }
    }
}
