using System;
using System.Text;

namespace Volleyball.Core
{
    /// <summary>小端序二進位寫入。可重複使用（Reset），避免每個 tick 配置記憶體。</summary>
    public sealed class ByteWriter
    {
        byte[] buf;
        int len;

        public ByteWriter(int capacity = 256)
        {
            buf = new byte[capacity];
        }

        public byte[] Buffer => buf;
        public int Length => len;

        public void Reset() => len = 0;

        void Ensure(int n)
        {
            if (len + n > buf.Length) Array.Resize(ref buf, Math.Max(buf.Length * 2, len + n));
        }

        public void WriteByte(byte v)
        {
            Ensure(1);
            buf[len++] = v;
        }

        public void WriteBool(bool v) => WriteByte(v ? (byte)1 : (byte)0);

        public void WriteUShort(ushort v)
        {
            Ensure(2);
            buf[len++] = (byte)v;
            buf[len++] = (byte)(v >> 8);
        }

        public void WriteShort(short v) => WriteUShort((ushort)v);

        public void WriteUInt(uint v)
        {
            Ensure(4);
            buf[len++] = (byte)v;
            buf[len++] = (byte)(v >> 8);
            buf[len++] = (byte)(v >> 16);
            buf[len++] = (byte)(v >> 24);
        }

        /// <summary>以 scale 為倍率量化成 16 位元整數（例如 scale=100 → 公分精度）。</summary>
        public void WriteQuantized(float v, float scale)
        {
            float x = MathF.Round(v * scale);
            if (x > short.MaxValue) x = short.MaxValue;
            else if (x < short.MinValue) x = short.MinValue;
            WriteShort((short)x);
        }

        public void WriteVec3(Vec3 v)
        {
            WriteQuantized(v.X, 100f);
            WriteQuantized(v.Y, 100f);
            WriteQuantized(v.Z, 100f);
        }

        public void WriteString(string s)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(s ?? string.Empty);
            int n = Math.Min(bytes.Length, 60);
            WriteByte((byte)n);
            Ensure(n);
            Array.Copy(bytes, 0, buf, len, n);
            len += n;
        }

        public void WriteBytes(byte[] src, int count)
        {
            Ensure(count);
            Array.Copy(src, 0, buf, len, count);
            len += count;
        }
    }

    /// <summary>
    /// 小端序二進位讀取。讀過頭不會丟例外，而是把 Failed 設為 true 並回傳 0，
    /// 呼叫端在讀完後檢查 Failed 即可丟棄格式錯誤的封包。
    /// </summary>
    public sealed class ByteReader
    {
        readonly byte[] buf;
        readonly int end;
        int pos;

        public bool Failed { get; private set; }

        public ByteReader(byte[] data, int length)
        {
            buf = data;
            end = Math.Min(length, data.Length);
        }

        bool Need(int n)
        {
            if (pos + n > end)
            {
                Failed = true;
                pos = end;
                return false;
            }

            return true;
        }

        public byte ReadByte() => Need(1) ? buf[pos++] : (byte)0;

        public bool ReadBool() => ReadByte() != 0;

        public ushort ReadUShort()
        {
            if (!Need(2)) return 0;
            ushort v = (ushort)(buf[pos] | (buf[pos + 1] << 8));
            pos += 2;
            return v;
        }

        public short ReadShort() => (short)ReadUShort();

        public uint ReadUInt()
        {
            if (!Need(4)) return 0;
            uint v = (uint)(buf[pos] | (buf[pos + 1] << 8) | (buf[pos + 2] << 16) | (buf[pos + 3] << 24));
            pos += 4;
            return v;
        }

        public float ReadQuantized(float scale) => ReadShort() / scale;

        public Vec3 ReadVec3()
        {
            float x = ReadQuantized(100f), y = ReadQuantized(100f), z = ReadQuantized(100f);
            return new Vec3(x, y, z);
        }

        public string ReadString()
        {
            int n = ReadByte();
            if (!Need(n)) return string.Empty;
            string s = Encoding.UTF8.GetString(buf, pos, n);
            pos += n;
            return s;
        }
    }
}
