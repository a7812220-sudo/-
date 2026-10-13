using NUnit.Framework;
using Volleyball.Core;

namespace Volleyball.Tests
{
    public class PlayerMotionTests
    {
        static PlayerState Run(int team, Vec3 start, InputFrame input, int ticks)
        {
            var p = new PlayerState { Pos = start };
            for (int i = 0; i < ticks; i++) PlayerMotion.Step(ref p, team, input, TestUtil.Dt);
            return p;
        }

        [Test]
        public void AcceleratesToMoveSpeedTowardNet()
        {
            var fwd = new InputFrame { Forward = 100 };
            var p0 = Run(0, new Vec3(-5f, 0f, 0f), fwd, TestUtil.Ticks(0.5f));
            Assert.AreEqual(GameConfig.MoveSpeed, p0.Vel.X, 0.01f, "隊伍 0 朝網 = +X");

            var p1 = Run(1, new Vec3(5f, 0f, 0f), fwd, TestUtil.Ticks(0.5f));
            Assert.AreEqual(-GameConfig.MoveSpeed, p1.Vel.X, 0.01f, "隊伍 1 朝網 = -X");
        }

        [Test]
        public void RightIsMirroredBetweenTeams()
        {
            var right = new InputFrame { Right = 100 };
            Assert.Less(Run(0, new Vec3(-5f, 0f, 0f), right, 30).Vel.Z, -1f);
            Assert.Greater(Run(1, new Vec3(5f, 0f, 0f), right, 30).Vel.Z, 1f);
        }

        [Test]
        public void DiagonalIsNotFasterThanStraight()
        {
            var diag = new InputFrame { Forward = 100, Right = 100 };
            var p = Run(0, new Vec3(-5f, 0f, 0f), diag, 60);
            float speed = System.MathF.Sqrt(p.Vel.X * p.Vel.X + p.Vel.Z * p.Vel.Z);
            Assert.LessOrEqual(speed, GameConfig.MoveSpeed + 0.01f);
        }

        [Test]
        public void JumpHasExpectedApexAndAirtime()
        {
            var p = new PlayerState { Pos = new Vec3(-3f, 0f, 0f) };
            var jump = new InputFrame { Jump = true };
            PlayerMotion.Step(ref p, 0, jump, TestUtil.Dt);

            float apex = p.Pos.Y;
            int airTicks = 1;
            while (p.Pos.Y > 0f && airTicks < 600)
            {
                PlayerMotion.Step(ref p, 0, default, TestUtil.Dt);
                apex = System.MathF.Max(apex, p.Pos.Y);
                airTicks++;
            }

            float expectApex = GameConfig.JumpSpeed * GameConfig.JumpSpeed / (2f * GameConfig.PlayerGravity);
            float expectAir = 2f * GameConfig.JumpSpeed / GameConfig.PlayerGravity;
            Assert.AreEqual(expectApex, apex, 0.03f);
            Assert.AreEqual(expectAir, airTicks * TestUtil.Dt, 0.03f);
            Assert.AreEqual(0f, p.Pos.Y);
        }

        [Test]
        public void CannotJumpAgainWhileAirborne()
        {
            var p = new PlayerState { Pos = new Vec3(-3f, 0f, 0f) };
            PlayerMotion.Step(ref p, 0, new InputFrame { Jump = true }, TestUtil.Dt);
            for (int i = 0; i < 20; i++) PlayerMotion.Step(ref p, 0, default, TestUtil.Dt);
            float vyBefore = p.Vel.Y;
            PlayerMotion.Step(ref p, 0, new InputFrame { Jump = true }, TestUtil.Dt);
            Assert.Less(p.Vel.Y, vyBefore, "空中按跳不該再給向上速度");
        }

        [Test]
        public void StaysOnOwnSideOfTheNet()
        {
            var fwd = new InputFrame { Forward = 100 };
            var p0 = Run(0, new Vec3(-3f, 0f, 0f), fwd, 600);
            Assert.AreEqual(-GameConfig.NetKeepOut, p0.Pos.X, 0.001f);
            var p1 = Run(1, new Vec3(3f, 0f, 0f), fwd, 600);
            Assert.AreEqual(GameConfig.NetKeepOut, p1.Pos.X, 0.001f);
        }

        [Test]
        public void CannotLeaveTheAllowedArea()
        {
            var left = new InputFrame { Right = -100, Forward = -100 };
            var p = Run(0, new Vec3(-3f, 0f, 0f), left, 1200);
            Assert.GreaterOrEqual(p.Pos.X, -(GameConfig.HalfLength + GameConfig.OutMargin) - 0.001f);
            Assert.LessOrEqual(System.MathF.Abs(p.Pos.Z), GameConfig.HalfWidth + GameConfig.OutMargin + 0.001f);
        }
    }
}
