using System.Collections.Generic;

namespace Volleyball.Core
{
    public enum MsgType : byte
    {
        // 客戶端 → 伺服器
        Hello = 1,
        SetTeam = 2,
        Start = 3,
        Input = 4,

        // 伺服器 → 客戶端
        Room = 20,
        Snapshot = 21,
        Events = 22,
        Reject = 23,
    }

    public enum SlotKind : byte
    {
        Empty = 0,
        Human = 1,
        Bot = 2,
    }

    public struct SlotInfo
    {
        public SlotKind Kind;
        public string Name;
    }

    /// <summary>大廳／比賽中的座位表。每位客戶端收到的版本不同（YourSlot / YouAreHost）。</summary>
    public sealed class RoomState
    {
        public bool InMatch;
        public int YourSlot = -1;
        public bool YouAreHost;
        public readonly SlotInfo[] Slots = new SlotInfo[GameConfig.SlotCount];
    }

    public sealed class Snapshot
    {
        public uint Tick;
        public Phase Phase;
        public float PhaseTime;
        public int Score0, Score1;
        public int ServingTeam, ServerSlot, TouchCount, LastTouchTeam;
        public bool ServeInFlight;
        public BallState Ball;
        public readonly PlayerState[] Players = new PlayerState[GameConfig.SlotCount];
        public uint AckSeq; // 伺服器已處理到這位客戶端的第幾個輸入

        public float Time => Tick * GameConfig.TickDt;

        public void CopyFrom(Snapshot o)
        {
            Tick = o.Tick;
            Phase = o.Phase;
            PhaseTime = o.PhaseTime;
            Score0 = o.Score0;
            Score1 = o.Score1;
            ServingTeam = o.ServingTeam;
            ServerSlot = o.ServerSlot;
            TouchCount = o.TouchCount;
            LastTouchTeam = o.LastTouchTeam;
            ServeInFlight = o.ServeInFlight;
            Ball = o.Ball;
            for (int i = 0; i < Players.Length; i++) Players[i] = o.Players[i];
            AckSeq = o.AckSeq;
        }
    }

    public sealed class EventBatch
    {
        public uint Tick;
        public readonly List<GameEvent> Events = new List<GameEvent>();
    }

    /// <summary>
    /// 所有訊息的二進位格式。第一個位元組是 MsgType。
    /// 位置／速度量化為 1/100 公尺（公分），一份快照約 110 位元組。
    /// </summary>
    public static class Protocol
    {
        public const int MaxNameLength = 12;
        public const int InputRedundancy = 6; // 每個輸入封包附帶最近幾個 tick（每個輸入會被送 3 次），抵抗掉包

        // ---------- 客戶端 → 伺服器 ----------

        public static void WriteHello(ByteWriter w, string name)
        {
            w.Reset();
            w.WriteByte((byte)MsgType.Hello);
            w.WriteString(name);
        }

        public static void WriteSetTeam(ByteWriter w, int team)
        {
            w.Reset();
            w.WriteByte((byte)MsgType.SetTeam);
            w.WriteByte((byte)team);
        }

        public static void WriteStart(ByteWriter w)
        {
            w.Reset();
            w.WriteByte((byte)MsgType.Start);
        }

        /// <summary>寫入 frames[first .. first+count)，序號必須連續遞增。</summary>
        public static void WriteInput(ByteWriter w, InputFrame[] frames, int first, int count)
        {
            w.Reset();
            w.WriteByte((byte)MsgType.Input);
            w.WriteByte((byte)count);
            w.WriteUInt(frames[first].Seq);
            for (int i = 0; i < count; i++)
            {
                InputFrame f = frames[first + i];
                w.WriteByte((byte)f.Right);
                w.WriteByte((byte)f.Forward);
                w.WriteByte((byte)((f.Jump ? 1 : 0) | (f.Hit ? 2 : 0)));
            }
        }

        /// <summary>讀取 Input 訊息（已讀掉 MsgType）。</summary>
        public static void ReadInput(ByteReader r, List<InputFrame> frames)
        {
            int count = r.ReadByte();
            uint first = r.ReadUInt();
            for (int i = 0; i < count && !r.Failed; i++)
            {
                var f = new InputFrame { Seq = first + (uint)i };
                f.Right = (sbyte)r.ReadByte();
                f.Forward = (sbyte)r.ReadByte();
                byte flags = r.ReadByte();
                f.Jump = (flags & 1) != 0;
                f.Hit = (flags & 2) != 0;
                if (!r.Failed) frames.Add(f);
            }
        }

        // ---------- 伺服器 → 客戶端 ----------

        public static void WriteRoom(ByteWriter w, RoomState room)
        {
            w.Reset();
            w.WriteByte((byte)MsgType.Room);
            w.WriteBool(room.InMatch);
            w.WriteByte((byte)(room.YourSlot < 0 ? 255 : room.YourSlot));
            w.WriteBool(room.YouAreHost);
            for (int i = 0; i < GameConfig.SlotCount; i++)
            {
                w.WriteByte((byte)room.Slots[i].Kind);
                w.WriteString(room.Slots[i].Name);
            }
        }

        public static RoomState ReadRoom(ByteReader r)
        {
            var room = new RoomState();
            room.InMatch = r.ReadBool();
            int slot = r.ReadByte();
            room.YourSlot = slot >= GameConfig.SlotCount ? -1 : slot;
            room.YouAreHost = r.ReadBool();
            for (int i = 0; i < GameConfig.SlotCount; i++)
            {
                byte kind = r.ReadByte();
                room.Slots[i].Kind = kind > (byte)SlotKind.Bot ? SlotKind.Empty : (SlotKind)kind;
                room.Slots[i].Name = r.ReadString();
            }

            return room;
        }

        public static void WriteReject(ByteWriter w, string reason)
        {
            w.Reset();
            w.WriteByte((byte)MsgType.Reject);
            w.WriteString(reason);
        }

        /// <summary>寫入所有人共用的快照主體（尚未附 Ack）。</summary>
        public static void WriteSnapshotBody(ByteWriter w, VolleyballMatch m)
        {
            w.Reset();
            w.WriteByte((byte)MsgType.Snapshot);
            w.WriteUInt(m.Tick);
            w.WriteByte((byte)m.Phase);
            w.WriteUShort((ushort)Mathx.Clamp(m.PhaseTime * 1000f, 0f, 65535f));
            w.WriteByte((byte)System.Math.Min(m.Score[0], 255));
            w.WriteByte((byte)System.Math.Min(m.Score[1], 255));
            w.WriteByte((byte)m.ServingTeam);
            w.WriteByte((byte)m.ServerSlot);
            w.WriteByte((byte)m.TouchCount);
            w.WriteByte((byte)(m.LastTouchTeam < 0 ? 255 : m.LastTouchTeam));
            w.WriteBool(m.ServeInFlight);
            w.WriteVec3(m.Ball.Pos);
            w.WriteVec3(m.Ball.Vel);
            for (int i = 0; i < GameConfig.SlotCount; i++)
            {
                PlayerState p = m.Players[i];
                w.WriteVec3(p.Pos);
                w.WriteVec3(p.Vel);
                w.WriteByte((byte)Mathx.Clamp(p.SwingTimer * 100f, 0f, 255f));
            }
        }

        /// <summary>在快照主體後面附上這位客戶端專屬的 Ack。</summary>
        public static void AppendAck(ByteWriter w, uint ackSeq) => w.WriteUInt(ackSeq);

        public static bool ReadSnapshot(ByteReader r, Snapshot s)
        {
            s.Tick = r.ReadUInt();
            byte phase = r.ReadByte();
            if (phase < (byte)Phase.Countdown || phase > (byte)Phase.GameOver) return false;
            s.Phase = (Phase)phase;
            s.PhaseTime = r.ReadUShort() / 1000f;
            s.Score0 = r.ReadByte();
            s.Score1 = r.ReadByte();
            s.ServingTeam = r.ReadByte() & 1;
            s.ServerSlot = System.Math.Min((int)r.ReadByte(), GameConfig.SlotCount - 1);
            s.TouchCount = r.ReadByte();
            int last = r.ReadByte();
            s.LastTouchTeam = last > 1 ? -1 : last;
            s.ServeInFlight = r.ReadBool();
            s.Ball.Pos = r.ReadVec3();
            s.Ball.Vel = r.ReadVec3();
            for (int i = 0; i < GameConfig.SlotCount; i++)
            {
                s.Players[i].Pos = r.ReadVec3();
                s.Players[i].Vel = r.ReadVec3();
                s.Players[i].SwingTimer = r.ReadByte() / 100f;
            }

            s.AckSeq = r.ReadUInt();
            return !r.Failed;
        }

        public static void WriteEvents(ByteWriter w, uint tick, List<GameEvent> events)
        {
            w.Reset();
            w.WriteByte((byte)MsgType.Events);
            w.WriteUInt(tick);
            int n = System.Math.Min(events.Count, 32);
            w.WriteByte((byte)n);
            for (int i = 0; i < n; i++)
            {
                GameEvent e = events[i];
                w.WriteByte((byte)e.Kind);
                w.WriteByte(e.Team);
                w.WriteByte((byte)e.Slot);
                w.WriteByte(e.A);
                w.WriteByte(e.B);
                w.WriteVec3(e.Pos);
            }
        }

        public static bool ReadEvents(ByteReader r, EventBatch batch)
        {
            batch.Tick = r.ReadUInt();
            int n = r.ReadByte();
            batch.Events.Clear();
            for (int i = 0; i < n && !r.Failed; i++)
            {
                var e = new GameEvent();
                e.Kind = (EventKind)r.ReadByte();
                e.Team = r.ReadByte();
                e.Slot = (sbyte)r.ReadByte();
                e.A = r.ReadByte();
                e.B = r.ReadByte();
                e.Pos = r.ReadVec3();
                if (!r.Failed) batch.Events.Add(e);
            }

            return !r.Failed;
        }
    }
}
