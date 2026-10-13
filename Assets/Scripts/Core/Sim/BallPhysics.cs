using System;

namespace Volleyball.Core
{
    public struct BallStepResult
    {
        public bool HitNet; // 撞網被彈回
        public bool CrossedNet; // 球心這個 tick 從網的一側飛到另一側（沒撞網）
        public bool Landed; // 觸地（無論是否反彈）
        public Vec3 LandPos; // 觸地的精確位置
    }

    public static class BallPhysics
    {
        /// <summary>
        /// 前進一個 tick。重力是定加速度，所以位置／速度用精確解（不是歐拉近似），
        /// 這樣 HitSolver 算出的「幾秒後抵達哪裡」才會準。
        /// </summary>
        public static BallStepResult Step(ref BallState b, float dt)
        {
            var res = new BallStepResult();
            float g = GameConfig.BallGravity;
            float r = GameConfig.BallRadius;

            Vec3 p0 = b.Pos, v0 = b.Vel;
            Vec3 p1 = new Vec3(p0.X + v0.X * dt, p0.Y + v0.Y * dt - 0.5f * g * dt * dt, p0.Z + v0.Z * dt);
            Vec3 v1 = new Vec3(v0.X, v0.Y - g * dt, v0.Z);

            // 球網：球心穿過 X=0 平面。網視為一路延伸到地面的牆，高度 NetTop。
            if ((p0.X < 0f) != (p1.X < 0f))
            {
                float f = p0.X == p1.X ? 0f : -p0.X / (p1.X - p0.X);
                float yAt = p0.Y + (p1.Y - p0.Y) * f;
                if (yAt < GameConfig.NetTop + r)
                {
                    res.HitNet = true;
                    float side = p0.X < 0f ? -1f : 1f;
                    p1 = new Vec3(side * 0.05f, yAt, p0.Z + (p1.Z - p0.Z) * f);
                    v1 = new Vec3(side * MathF.Abs(v1.X) * GameConfig.NetRestitution, v1.Y * 0.7f, v1.Z * 0.7f);
                }
                else
                {
                    res.CrossedNet = true;
                }
            }

            // 地面
            if (!res.HitNet && p1.Y <= r && v1.Y < 0f)
            {
                float disc = v0.Y * v0.Y + 2f * g * (p0.Y - r);
                float t = disc > 0f ? (v0.Y + MathF.Sqrt(disc)) / g : 0f;
                t = Mathx.Clamp(t, 0f, dt);
                res.Landed = true;
                res.LandPos = new Vec3(p0.X + v0.X * t, r, p0.Z + v0.Z * t);

                float vyAtLand = v0.Y - g * t;
                float vyBounce = -vyAtLand * GameConfig.FloorRestitution;
                if (vyBounce < 1f) vyBounce = 0f; // 彈不起來就停在地上滾
                float friction = Mathx.Clamp(1f - 2.5f * dt, 0f, 1f);
                p1 = res.LandPos;
                v1 = new Vec3(v0.X * friction, vyBounce, v0.Z * friction);
            }

            b.Pos = p1;
            b.Vel = v1;
            return res;
        }
    }
}
