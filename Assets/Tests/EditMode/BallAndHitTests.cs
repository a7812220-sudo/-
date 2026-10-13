using NUnit.Framework;
using Volleyball.Core;

namespace Volleyball.Tests
{
    public class BallPhysicsTests
    {
        [Test]
        public void FreeFallLandsAtTheExactTimeAndPlace()
        {
            var b = new BallState { Pos = new Vec3(-3f, 5f, 1f), Vel = new Vec3(2f, 0f, -1f) };
            float expectT = System.MathF.Sqrt(2f * (5f - GameConfig.BallRadius) / GameConfig.BallGravity);

            float t = 0f;
            BallStepResult r = default;
            while (!r.Landed && t < 5f)
            {
                r = BallPhysics.Step(ref b, TestUtil.Dt);
                t += TestUtil.Dt;
            }

            Assert.IsTrue(r.Landed);
            Assert.AreEqual(expectT, t, TestUtil.Dt);
            Assert.AreEqual(-3f + 2f * expectT, r.LandPos.X, 0.03f);
            Assert.AreEqual(1f - expectT, r.LandPos.Z, 0.03f);
            Assert.AreEqual(GameConfig.BallRadius, r.LandPos.Y, 1e-4f);
        }

        [Test]
        public void BallisticSolutionArrivesAtTargetOnTime()
        {
            var from = new Vec3(-4f, 1.2f, -1f);
            var target = new Vec3(-1.5f, 2.9f, 1.8f);
            const float flight = 1.0f;
            var b = new BallState { Pos = from, Vel = HitSolver.Ballistic(from, target, flight) };

            int n = TestUtil.Ticks(flight);
            for (int i = 0; i < n; i++) BallPhysics.Step(ref b, TestUtil.Dt);

            Assert.AreEqual(target.X, b.Pos.X, 0.02f);
            Assert.AreEqual(target.Y, b.Pos.Y, 0.02f);
            Assert.AreEqual(target.Z, b.Pos.Z, 0.02f);
        }

        [Test]
        public void LowBallHitsTheNetAndBouncesBack()
        {
            var b = new BallState { Pos = new Vec3(-1f, 1.0f, 0f), Vel = new Vec3(6f, 0f, 0f) };
            bool hit = false;
            for (int i = 0; i < 60 && !hit; i++) hit = BallPhysics.Step(ref b, TestUtil.Dt).HitNet;

            Assert.IsTrue(hit);
            Assert.Less(b.Pos.X, 0f, "球留在原本那一側");
            Assert.Less(b.Vel.X, 0f, "被彈回來");
        }

        [Test]
        public void HighBallCrossesTheNet()
        {
            var b = new BallState { Pos = new Vec3(-1f, 3.5f, 0f), Vel = new Vec3(6f, 0f, 0f) };
            bool crossed = false, hit = false;
            for (int i = 0; i < 60 && !crossed; i++)
            {
                var r = BallPhysics.Step(ref b, TestUtil.Dt);
                crossed |= r.CrossedNet;
                hit |= r.HitNet;
            }

            Assert.IsTrue(crossed);
            Assert.IsFalse(hit);
            Assert.Greater(b.Pos.X, 0f);
        }

        [Test]
        public void BallSettlesOnTheFloor()
        {
            var b = new BallState { Pos = new Vec3(-3f, 3f, 0f), Vel = new Vec3(1f, 0f, 0f) };
            for (int i = 0; i < TestUtil.Ticks(8f); i++) BallPhysics.Step(ref b, TestUtil.Dt);

            Assert.AreEqual(GameConfig.BallRadius, b.Pos.Y, 1e-3f);
            Assert.Less(System.MathF.Abs(b.Vel.Y), 0.01f);
            Assert.Less(System.MathF.Abs(b.Vel.X), 0.05f);
        }
    }

    public class HitSolverTests
    {
        /// <summary>模擬一次擊球後球的去向：回傳落地點，並指出途中有沒有撞網、有沒有過網。</summary>
        static Vec3 Fly(Vec3 pos, Vec3 vel, out bool crossed, out bool hitNet, out float maxY)
        {
            var b = new BallState { Pos = pos, Vel = vel };
            crossed = hitNet = false;
            maxY = pos.Y;
            for (int i = 0; i < TestUtil.Ticks(8f); i++)
            {
                var r = BallPhysics.Step(ref b, TestUtil.Dt);
                crossed |= r.CrossedNet;
                hitNet |= r.HitNet;
                maxY = System.MathF.Max(maxY, b.Pos.Y);
                if (r.Landed) return r.LandPos;
            }

            Assert.Fail("球一直沒有落地");
            return default;
        }

        static bool InBounds(Vec3 p) =>
            System.MathF.Abs(p.X) <= GameConfig.HalfLength && System.MathF.Abs(p.Z) <= GameConfig.HalfWidth;

        [TestCase(0)]
        [TestCase(1)]
        public void ServeAlwaysClearsTheNetAndLandsInTheOpponentCourt(int team)
        {
            var rng = new Rng(7);
            Vec3 hold = Court.ServePos(team) + Court.Forward(team) * 0.45f + new Vec3(0f, 1.5f, 0f);
            for (float aimR = -1f; aimR <= 1f; aimR += 0.5f)
            for (float aimF = -1f; aimF <= 1f; aimF += 0.5f)
            {
                Vec3 v = HitSolver.Solve(HitKind.Serve, team, hold, aimR, aimF, 0f, rng);
                Vec3 land = Fly(hold, v, out bool crossed, out bool hitNet, out _);
                string tag = "team " + team + " aim(" + aimR + "," + aimF + ") -> " + land;
                Assert.IsTrue(crossed && !hitNet, "發球要過網：" + tag);
                Assert.AreEqual(Court.Forward(team).X > 0f, land.X > 0f, "要落在對場：" + tag);
                Assert.IsTrue(InBounds(land), "發球不該出界：" + tag);
            }
        }

        [Test]
        public void SpikeFromNearTheNetClearsTheNetAndLandsInBounds()
        {
            var rng = new Rng(3);
            var ball = new Vec3(-0.9f, 2.9f, 0.5f);
            for (float aimR = -1f; aimR <= 1f; aimR += 0.5f)
            for (float aimF = -1f; aimF <= 1f; aimF += 0.5f)
            {
                Vec3 v = HitSolver.Solve(HitKind.Spike, 0, ball, aimR, aimF, 0f, rng);
                Vec3 land = Fly(ball, v, out bool crossed, out bool hitNet, out _);
                Assert.IsTrue(crossed && !hitNet, "扣球要過網 aim(" + aimR + "," + aimF + ")");
                Assert.Greater(land.X, 0f);
                Assert.IsTrue(InBounds(land), "無誤差時扣球落點在界內 " + land);
            }
        }

        [Test]
        public void SpikeIsFasterThanOtherHits()
        {
            var rng = new Rng(1);
            var ball = new Vec3(-1.2f, 3.3f, 0f); // 高點、貼網：好的扣球時機
            Vec3 spike = HitSolver.Solve(HitKind.Spike, 0, ball, 0f, 0f, 0f, rng);
            Vec3 free = HitSolver.Solve(HitKind.FreeBall, 0, ball, 0f, 0f, 0f, rng);
            float Speed(Vec3 v) => System.MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            Assert.Greater(Speed(spike), Speed(free) * 1.3f);
        }

        [TestCase(0)]
        [TestCase(1)]
        public void BumpStaysOnOwnSideAndPeaksHighEnoughToSet(int team)
        {
            var rng = new Rng(5);
            var ball = Court.OwnSide(team, 4.5f, 0.5f, 0.9f);
            Vec3 v = HitSolver.Solve(HitKind.Bump, team, ball, 0f, 0f, 0f, rng);
            var b = new BallState { Pos = ball, Vel = v };
            float maxY = 0f;
            bool crossedNet = false;
            for (int i = 0; i < TestUtil.Ticks(1.25f); i++)
            {
                var r = BallPhysics.Step(ref b, TestUtil.Dt);
                crossedNet |= r.CrossedNet || r.HitNet;
                maxY = System.MathF.Max(maxY, b.Pos.Y);
            }

            Assert.IsFalse(crossedNet, "接球不該直接過網");
            Assert.AreEqual(Court.Forward(team).X > 0f, b.Pos.X < 0f, "球要留在己方半場");
            Assert.AreEqual(2.6f, b.Pos.Y, 0.05f, "1.25 秒後抵達二傳區高度");
            Assert.Greater(maxY, GameConfig.NetTop + 0.5f, "要彈得夠高才好托球");
        }

        [Test]
        public void SetArrivesWhereAnAttackerCanSpikeIt()
        {
            var rng = new Rng(5);
            var ball = Court.OwnSide(0, 2.4f, 0f, 1.3f);
            Vec3 v = HitSolver.Solve(HitKind.Set, 0, ball, 0f, 0f, 0f, rng);
            var b = new BallState { Pos = ball, Vel = v };
            for (int i = 0; i < TestUtil.Ticks(1.0f); i++) BallPhysics.Step(ref b, TestUtil.Dt);

            Assert.AreEqual(-1.2f, b.Pos.X, 0.05f, "托到網前 1.2 公尺");
            Assert.GreaterOrEqual(b.Pos.Y, GameConfig.SpikeMinBallY, "高度要夠讓人跳起來扣");
        }

        [Test]
        public void FreeBallGoesOverTheNetEvenFromTheBackCourt()
        {
            var rng = new Rng(9);
            foreach (float depth in new[] { 1.5f, 3f, 5f })
            {
                var ball = Court.OwnSide(1, depth, 0f, 1.0f);
                Vec3 v = HitSolver.Solve(HitKind.FreeBall, 1, ball, 0f, 0f, 0f, rng);
                Vec3 land = Fly(ball, v, out bool crossed, out bool hitNet, out _);
                Assert.IsTrue(crossed && !hitNet, "高吊要過網（從離網 " + depth + " 公尺處擊球）");
                Assert.Less(land.X, 0f, "隊伍 1 的球要落在 X<0");
            }
        }

        [Test]
        public void AimErrorGrowsWithDistanceFromTheBall()
        {
            var ball = new Vec3(-1.2f, 3.3f, 0f);
            float MeanMiss(float distNorm)
            {
                var rng = new Rng(11);
                float sum = 0f;
                const int n = 200;
                for (int i = 0; i < n; i++)
                {
                    Vec3 v = HitSolver.Solve(HitKind.Spike, 0, ball, 0f, 0f, distNorm, rng);
                    Vec3 land = Fly(ball, v, out _, out _, out _);
                    Vec3 ideal = Court.OppSide(0, 3.8f, 0f, GameConfig.BallRadius);
                    sum += Vec3.DistanceXZ(land, ideal);
                }

                return sum / n;
            }

            Assert.Less(MeanMiss(0f), 0.05f);
            Assert.Greater(MeanMiss(1f), 0.5f);
            Assert.Less(MeanMiss(0.5f), MeanMiss(1f));
        }
    }
}
