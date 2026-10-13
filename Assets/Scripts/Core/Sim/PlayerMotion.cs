using System;

namespace Volleyball.Core
{
    /// <summary>
    /// 球員移動與跳躍。伺服器與客戶端（本機預測）共用同一份程式，所以結果一致。
    /// </summary>
    public static class PlayerMotion
    {
        public static void Step(ref PlayerState p, int team, InputFrame input, float dt)
        {
            float ax = input.AxisRight, ay = input.AxisForward;
            float len = MathF.Sqrt(ax * ax + ay * ay);
            if (len > 1f)
            {
                ax /= len;
                ay /= len;
            }

            Vec3 right = Court.Right(team), fwd = Court.Forward(team);
            float wishX = (right.X * ax + fwd.X * ay) * GameConfig.MoveSpeed;
            float wishZ = (right.Z * ax + fwd.Z * ay) * GameConfig.MoveSpeed;

            bool grounded = p.Pos.Y <= 0f;
            float maxDv = (grounded ? GameConfig.GroundAccel : GameConfig.AirAccel) * dt;
            float dvx = wishX - p.Vel.X, dvz = wishZ - p.Vel.Z;
            float dv = MathF.Sqrt(dvx * dvx + dvz * dvz);
            if (dv <= maxDv)
            {
                p.Vel.X = wishX;
                p.Vel.Z = wishZ;
            }
            else
            {
                p.Vel.X += dvx / dv * maxDv;
                p.Vel.Z += dvz / dv * maxDv;
            }

            if (grounded && input.Jump)
            {
                p.Vel.Y = GameConfig.JumpSpeed;
                grounded = false;
            }

            if (!grounded)
            {
                p.Pos.Y += p.Vel.Y * dt - 0.5f * GameConfig.PlayerGravity * dt * dt;
                p.Vel.Y -= GameConfig.PlayerGravity * dt;
                if (p.Pos.Y <= 0f)
                {
                    p.Pos.Y = 0f;
                    p.Vel.Y = 0f;
                }
            }

            p.Pos.X += p.Vel.X * dt;
            p.Pos.Z += p.Vel.Z * dt;
            ClampToArea(ref p, team);
        }

        /// <summary>留在己方半場（可跑到界外一段距離，但不能碰到網）。</summary>
        static void ClampToArea(ref PlayerState p, int team)
        {
            float far = GameConfig.HalfLength + GameConfig.OutMargin;
            float near = GameConfig.NetKeepOut;
            float minX = team == 0 ? -far : near;
            float maxX = team == 0 ? -near : far;
            if (p.Pos.X < minX) { p.Pos.X = minX; p.Vel.X = 0f; }
            else if (p.Pos.X > maxX) { p.Pos.X = maxX; p.Vel.X = 0f; }

            float side = GameConfig.HalfWidth + GameConfig.OutMargin;
            if (p.Pos.Z < -side) { p.Pos.Z = -side; p.Vel.Z = 0f; }
            else if (p.Pos.Z > side) { p.Pos.Z = side; p.Vel.Z = 0f; }
        }
    }
}
