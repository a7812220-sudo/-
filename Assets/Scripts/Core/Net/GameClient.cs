using System;
using System.Collections.Generic;

namespace Volleyball.Core
{
    /// <summary>每個 tick 取樣一次輸入。Jump / Hit 是「自上次取樣以來是否按過」（邊緣觸發，不能漏掉）。</summary>
    public interface IInputSource
    {
        InputFrame Sample();
    }

    public struct RenderPlayer
    {
        public Vec3 Pos;
        public float Swing; // 0 = 沒在揮臂；(0, 1] = 揮臂進度
    }

    /// <summary>給畫面層讀取的「此刻該畫什麼」。</summary>
    public sealed class RenderState
    {
        public bool Valid;
        public Phase Phase;
        public float PhaseTime;
        public int Score0, Score1;
        public int ServingTeam, ServerSlot, TouchCount, LastTouchTeam;
        public bool ServeInFlight;
        public Vec3 BallPos;
        public readonly RenderPlayer[] Players = new RenderPlayer[GameConfig.SlotCount];
    }

    /// <summary>
    /// 客戶端邏輯，與傳輸層無關。
    /// 本機球員：用與伺服器相同的 PlayerMotion 立刻預測，收到快照後重播尚未確認的輸入來校正。
    /// 其他球員與球：在快照之間做插值，刻意落後約 0.1 秒以換取平滑。
    /// </summary>
    public sealed class GameClient
    {
        public delegate void SendFn(byte[] data, int length, bool reliable);

        public const float InterpDelay = 0.1f;
        const float SnapDistance = 2f; // 預測與伺服器差超過這麼多就直接瞬移
        const int RingSize = 24;

        readonly SendFn send;
        readonly string name;
        readonly ByteWriter writer = new ByteWriter(128);

        // 快照環形緩衝（預先配置，收到時直接讀進去）
        readonly Snapshot[] ring = new Snapshot[RingSize];
        int ringStart, ringCount;
        readonly Snapshot scratch = new Snapshot();
        Snapshot latest;

        // 本機預測
        readonly List<InputFrame> pending = new List<InputFrame>(128);
        readonly InputFrame[] sendBuf = new InputFrame[Protocol.InputRedundancy];
        PlayerState predicted;
        bool predictedValid;
        Vec3 visualOffset;
        uint seq;
        int sendCounter;
        float tickAcc;
        float localSwing;

        // 渲染時間（落後最新快照 InterpDelay）
        float renderTime;
        bool haveRenderTime;

        readonly Queue<EventBatch> eventQueue = new Queue<EventBatch>();

        public RoomState Room { get; private set; }
        public string RejectReason { get; private set; }
        public readonly RenderState Render = new RenderState();

        public GameClient(SendFn send, string name)
        {
            this.send = send;
            this.name = name;
            for (int i = 0; i < RingSize; i++) ring[i] = new Snapshot();
        }

        public bool InMatch => Room != null && Room.InMatch;
        public int MySlot => Room != null ? Room.YourSlot : -1;
        public int MyTeam => MySlot < 0 ? 0 : Court.TeamOf(MySlot);

        // ---------- 送出 ----------

        /// <summary>傳輸層連上後呼叫，向伺服器報到。</summary>
        public void OnConnected()
        {
            Protocol.WriteHello(writer, name);
            send(writer.Buffer, writer.Length, true);
        }

        public void SetTeam(int team)
        {
            Protocol.WriteSetTeam(writer, team);
            send(writer.Buffer, writer.Length, true);
        }

        public void RequestStart()
        {
            Protocol.WriteStart(writer);
            send(writer.Buffer, writer.Length, true);
        }

        // ---------- 收到 ----------

        public void OnMessage(byte[] data, int length)
        {
            if (length <= 0) return;
            var r = new ByteReader(data, length);
            switch ((MsgType)r.ReadByte())
            {
                case MsgType.Room:
                    OnRoom(Protocol.ReadRoom(r), r.Failed);
                    break;
                case MsgType.Snapshot:
                    OnSnapshot(r);
                    break;
                case MsgType.Events:
                    var batch = new EventBatch();
                    if (Protocol.ReadEvents(r, batch)) eventQueue.Enqueue(batch);
                    break;
                case MsgType.Reject:
                    RejectReason = r.ReadString();
                    break;
            }
        }

        void OnRoom(RoomState room, bool failed)
        {
            if (failed) return;
            bool wasInMatch = InMatch;
            Room = room;
            RejectReason = null;
            if (room.InMatch != wasInMatch) ResetMatchState();
        }

        void ResetMatchState()
        {
            ringStart = ringCount = 0;
            latest = null;
            pending.Clear();
            predictedValid = false;
            visualOffset = default;
            haveRenderTime = false;
            tickAcc = 0f;
            sendCounter = 0;
            localSwing = 0f;
            eventQueue.Clear();
            Render.Valid = false;
        }

        void OnSnapshot(ByteReader r)
        {
            if (!InMatch || MySlot < 0) return;

            // 先讀進暫存區，確認格式正確且不是亂序／重複，才放進環形緩衝
            if (!Protocol.ReadSnapshot(r, scratch)) return;
            if (latest != null && scratch.Tick <= latest.Tick) return;

            if (ringCount == RingSize)
            {
                ringStart = (ringStart + 1) % RingSize;
                ringCount--;
            }

            Snapshot snap = ring[(ringStart + ringCount) % RingSize];
            snap.CopyFrom(scratch);
            ringCount++;
            latest = snap;
            Reconcile(snap);
        }

        /// <summary>以伺服器狀態為準，重播尚未被確認的輸入，得到修正後的預測。</summary>
        void Reconcile(Snapshot snap)
        {
            int drop = 0;
            while (drop < pending.Count && pending[drop].Seq <= snap.AckSeq) drop++;
            pending.RemoveRange(0, drop);

            PlayerState replay = snap.Players[MySlot];
            replay.SwingTimer = 0f;
            for (int i = 0; i < pending.Count; i++) PlayerMotion.Step(ref replay, MyTeam, pending[i], GameConfig.TickDt);

            if (!predictedValid)
            {
                predicted = replay;
                visualOffset = default;
                predictedValid = true;
                return;
            }

            // 畫面位置保持連續：把「舊畫面位置 − 新預測位置」記成偏移，之後讓它慢慢消掉
            Vec3 oldDisplay = predicted.Pos + visualOffset;
            predicted = replay;
            visualOffset = oldDisplay - predicted.Pos;
            if (Vec3.DistanceXZ(oldDisplay, predicted.Pos) > SnapDistance) visualOffset = default;
        }

        // ---------- 每個畫面更新 ----------

        public void Update(float dt, IInputSource input)
        {
            if (!InMatch || MySlot < 0)
            {
                tickAcc = 0f;
                return;
            }

            tickAcc += dt;
            if (tickAcc > 0.25f) tickAcc = 0.25f;
            while (tickAcc >= GameConfig.TickDt)
            {
                tickAcc -= GameConfig.TickDt;
                ClientTick(input);
            }

            if (localSwing > 0f) localSwing -= dt;
            visualOffset = visualOffset * MathF.Exp(-dt * 10f);
            AdvanceRenderTime(dt);
            BuildRender();
        }

        void ClientTick(IInputSource input)
        {
            InputFrame f = input.Sample();
            f.Seq = ++seq;
            pending.Add(f);
            if (pending.Count > 180) pending.RemoveAt(0);

            if (predictedValid) PlayerMotion.Step(ref predicted, MyTeam, f, GameConfig.TickDt);
            if (f.Hit && localSwing <= 0f) localSwing = GameConfig.SwingDuration;

            if (++sendCounter >= GameConfig.SnapshotInterval)
            {
                sendCounter = 0;
                SendInputs();
            }
        }

        void SendInputs()
        {
            int n = Math.Min(pending.Count, Protocol.InputRedundancy);
            for (int i = 0; i < n; i++) sendBuf[i] = pending[pending.Count - n + i];
            Protocol.WriteInput(writer, sendBuf, 0, n);
            send(writer.Buffer, writer.Length, false);
        }

        void AdvanceRenderTime(float dt)
        {
            if (latest == null) return;
            float target = latest.Time - InterpDelay;
            if (!haveRenderTime)
            {
                renderTime = target;
                haveRenderTime = true;
                return;
            }

            renderTime += dt;
            float err = target - renderTime;
            if (MathF.Abs(err) > 0.5f) renderTime = target;
            else renderTime += err * Mathx.Clamp01(dt * 5f);
        }

        /// <summary>取出「依渲染時間已經該發生」的事件（與畫面同步，不會比球的動作早）。</summary>
        public void PopDueEvents(List<GameEvent> into)
        {
            if (!haveRenderTime) return;
            while (eventQueue.Count > 0 && eventQueue.Peek().Tick * GameConfig.TickDt <= renderTime)
                into.AddRange(eventQueue.Dequeue().Events);
        }

        void BuildRender()
        {
            if (latest == null) return;

            Render.Valid = true;
            Render.Phase = latest.Phase;
            Render.PhaseTime = latest.PhaseTime;
            Render.Score0 = latest.Score0;
            Render.Score1 = latest.Score1;
            Render.ServingTeam = latest.ServingTeam;
            Render.ServerSlot = latest.ServerSlot;
            Render.TouchCount = latest.TouchCount;
            Render.LastTouchTeam = latest.LastTouchTeam;
            Render.ServeInFlight = latest.ServeInFlight;

            Interpolate(renderTime);
            for (int i = 0; i < GameConfig.SlotCount; i++)
            {
                float swing = latest.Players[i].SwingTimer;
                Render.Players[i].Swing = swing > 0f ? 1f - swing / GameConfig.SwingDuration : 0f;
            }

            if (predictedValid)
            {
                Render.Players[MySlot].Pos = predicted.Pos + visualOffset;
                Render.Players[MySlot].Swing = localSwing > 0f ? 1f - localSwing / GameConfig.SwingDuration : 0f;
            }
        }

        void Interpolate(float t)
        {
            Snapshot a = null, b = null;
            for (int i = ringCount - 1; i >= 0; i--)
            {
                Snapshot s = ring[(ringStart + i) % RingSize];
                if (s.Time <= t)
                {
                    a = s;
                    break;
                }

                b = s;
            }

            if (a == null)
            {
                a = ring[ringStart]; // 比最舊的還早：停在最舊那張
                b = null;
            }

            if (b == null)
            {
                // 比最新的還新（封包延遲）：用速度外插一小段
                float ahead = Mathx.Clamp(t - a.Time, 0f, 0.1f);
                float g = GameConfig.BallGravity;
                Render.BallPos = new Vec3(
                    a.Ball.Pos.X + a.Ball.Vel.X * ahead,
                    MathF.Max(GameConfig.BallRadius, a.Ball.Pos.Y + a.Ball.Vel.Y * ahead - 0.5f * g * ahead * ahead),
                    a.Ball.Pos.Z + a.Ball.Vel.Z * ahead);
                for (int i = 0; i < GameConfig.SlotCount; i++)
                {
                    PlayerState p = a.Players[i];
                    Render.Players[i].Pos = new Vec3(
                        p.Pos.X + p.Vel.X * ahead,
                        MathF.Max(0f, p.Pos.Y + p.Vel.Y * ahead),
                        p.Pos.Z + p.Vel.Z * ahead);
                }

                return;
            }

            float span = b.Time - a.Time;
            float alpha = span > 0f ? Mathx.Clamp01((t - a.Time) / span) : 1f;
            Render.BallPos = Vec3.Lerp(a.Ball.Pos, b.Ball.Pos, alpha);
            for (int i = 0; i < GameConfig.SlotCount; i++)
                Render.Players[i].Pos = Vec3.Lerp(a.Players[i].Pos, b.Players[i].Pos, alpha);
        }
    }
}
