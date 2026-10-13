using System;

namespace Volleyball.Core
{
    /// <summary>左手座標系（與 Unity 相同）：+X 右、+Y 上、+Z 前。</summary>
    public struct Vec3
    {
        public float X, Y, Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);

        public static float DistanceXZ(Vec3 a, Vec3 b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        public static Vec3 Lerp(Vec3 a, Vec3 b, float t) => a + (b - a) * t;

        public override string ToString() => "(" + X.ToString("0.00") + ", " + Y.ToString("0.00") + ", " + Z.ToString("0.00") + ")";
    }

    public static class Mathx
    {
        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }

    /// <summary>xorshift32：不同執行環境（.NET / Mono）結果一致，測試可重現。</summary>
    public sealed class Rng
    {
        uint state;

        public Rng(uint seed)
        {
            state = seed == 0 ? 0x9E3779B9u : seed;
        }

        public uint NextUInt()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        /// <summary>[0, 1)</summary>
        public float Value01() => (NextUInt() & 0xFFFFFFu) / 16777216f;

        public float Range(float min, float max) => min + (max - min) * Value01();
    }
}
