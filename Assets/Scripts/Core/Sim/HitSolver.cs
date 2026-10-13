using System;

namespace Volleyball.Core
{
    /// <summary>
    /// 擊球解算：玩家只給「方向」（移動鍵），系統依觸球次數、球高度與是否在空中決定擊球種類，
    /// 再反推初速，讓球在指定秒數後剛好抵達目標點。
    /// </summary>
    public static class HitSolver
    {
        public static HitKind Classify(in PlayerState p, in BallState ball, int touchIndex)
        {
            bool airborne = p.Pos.Y > GameConfig.SpikeMinPlayerY;
            if (airborne
                && ball.Pos.Y >= GameConfig.SpikeMinBallY
                && MathF.Abs(p.Pos.X) <= GameConfig.SpikeMaxNetDist)
            {
                return HitKind.Spike;
            }

            if (touchIndex == 0) return HitKind.Bump;
            if (touchIndex == 1) return HitKind.Set;
            return HitKind.FreeBall;
        }

        /// <summary>
        /// 求出擊球後的球速。distNorm = 球離球員多遠 / 可觸及半徑（0..1），越勉強誤差越大。
        /// aimRight / aimForward 為擊球者視角的方向輸入（-1..1）。
        /// </summary>
        public static Vec3 Solve(HitKind kind, int team, Vec3 ballPos, float aimRight, float aimForward, float distNorm, Rng rng)
        {
            float r = GameConfig.BallRadius;
            float aimR = Mathx.Clamp(aimRight, -1f, 1f);
            float aimF = Mathx.Clamp(aimForward, -1f, 1f);

            Vec3 target;
            float t0;
            bool overNet;
            switch (kind)
            {
                case HitKind.Serve:
                    target = Court.OppSide(team, Mathx.Lerp(5.6f, 2.0f, (aimF + 1f) * 0.5f), aimR * 3.0f, r);
                    t0 = 1.45f;
                    overNet = true;
                    break;
                case HitKind.Spike:
                    target = Court.OppSide(team, 3.8f - aimF * 2.0f, aimR * 3.1f, r);
                    t0 = 0.6f;
                    overNet = true;
                    break;
                case HitKind.FreeBall:
                    target = Court.OppSide(team, 3.8f - aimF * 1.8f, aimR * 2.8f, r);
                    t0 = 1.35f;
                    overNet = true;
                    break;
                case HitKind.Bump:
                    target = Court.OwnSide(team, 2.4f - aimF * 0.6f, aimR * 1.6f, 2.6f);
                    t0 = 1.25f;
                    overNet = false;
                    break;
                default: // Set
                    target = Court.OwnSide(team, 1.2f - aimF * 0.3f, aimR * 2.6f, 2.9f);
                    t0 = 1.0f;
                    overNet = false;
                    break;
            }

            if (kind != HitKind.Serve)
            {
                float err = GameConfig.AimErrorMax * distNorm * distNorm;
                if (err > 0f)
                {
                    float ang = rng.Value01() * 6.2831855f;
                    float mag = err * MathF.Sqrt(rng.Value01());
                    target.X += MathF.Cos(ang) * mag;
                    target.Z += MathF.Sin(ang) * mag;
                }

                // 誤差不能把目標推到網的另一側
                float sideSign = overNet ? Court.Forward(team).X : -Court.Forward(team).X;
                float minDepth = overNet ? 0.5f : 0.7f;
                float depth = target.X * sideSign;
                if (depth < minDepth) target.X = sideSign * minDepth;
            }

            float clearance = kind == HitKind.Spike ? GameConfig.SpikeNetClearance : GameConfig.NetClearance;
            return overNet ? OverNet(ballPos, target, t0, clearance) : Ballistic(ballPos, target, t0);
        }

        /// <summary>讓球在 t 秒後剛好抵達 target 的初速（重力為 BallGravity）。</summary>
        public static Vec3 Ballistic(Vec3 from, Vec3 target, float t)
        {
            float g = GameConfig.BallGravity;
            return new Vec3(
                (target.X - from.X) / t,
                (target.Y - from.Y + 0.5f * g * t * t) / t,
                (target.Z - from.Z) / t);
        }

        /// <summary>
        /// 同 Ballistic，但從 t0 起逐步拉長飛行時間（弧度變高），直到球越過網時夠高為止。
        /// 若連最長的吊球都過不了網（例如在網前很低的位置），就維持最長吊球，讓物理決定是否撞網。
        /// </summary>
        public static Vec3 OverNet(Vec3 from, Vec3 target, float t0, float clearance)
        {
            const float maxT = 2.2f;
            float g = GameConfig.BallGravity;
            float clearY = GameConfig.NetTop + GameConfig.BallRadius + clearance;
            float dx = target.X - from.X;
            bool crosses = from.X * target.X < 0f;

            Vec3 v = Ballistic(from, target, t0);
            if (!crosses) return v;

            for (float t = t0; t <= maxT + 1e-4f; t += 0.05f)
            {
                v = Ballistic(from, target, t);
                float tn = t * (-from.X) / dx; // 球飛到 X=0 的時刻
                float y = from.Y + v.Y * tn - 0.5f * g * tn * tn;
                if (y >= clearY) return v;
            }

            return v;
        }
    }
}
