using NUnit.Framework;
using Volleyball.Core;

namespace Volleyball.Tests
{
    public class MatchRulesTests
    {
        static InputFrame[] Press(int slot, bool hit = true, bool jump = false)
        {
            var inputs = TestUtil.Idle();
            inputs[slot].Hit = hit;
            inputs[slot].Jump = jump;
            return inputs;
        }

        /// <summary>讓 winner 得一分：把球擺在輸方半場上空，跑到球落地、進入 PointPause。</summary>
        static void AwardPoint(VolleyballMatch m, int winner)
        {
            int loser = 1 - winner;
            Vec3 pos = Court.OwnSide(loser, 3f, 0f, 1.5f);
            TestUtil.ForceRally(m, pos, default, winner, 1, winner * 3);
            Assert.IsTrue(TestUtil.RunUntil(m, TestUtil.Idle(), () => m.Phase != Phase.Rally, 3f), "球應該落地");
        }

        [Test]
        public void StartsWithCountdownThenWaitsForTheServe()
        {
            var m = new VolleyballMatch(1, 0);
            Assert.AreEqual(Phase.Countdown, m.Phase);

            TestUtil.Run(m, TestUtil.Idle(), GameConfig.CountdownTime - 0.2f);
            Assert.AreEqual(Phase.Countdown, m.Phase);

            Assert.IsTrue(TestUtil.RunUntil(m, TestUtil.Idle(), () => TestUtil.HasEvent(m, EventKind.ServeReady), 1f));
            Assert.AreEqual(Phase.ServeWait, m.Phase);
            var e = TestUtil.GetEvent(m, EventKind.ServeReady);
            Assert.AreEqual(0, e.Team);
            Assert.AreEqual(0, e.Slot);
        }

        [Test]
        public void PlayersStartInFormationAndTheServerStandsBehindTheBaseline()
        {
            var m = new VolleyballMatch(1, 1);
            Assert.AreEqual(3, m.ServerSlot, "隊伍 1 先發，由它的第一位球員發球");
            Assert.Greater(m.Players[3].Pos.X, GameConfig.HalfLength, "發球員在底線後方");
            Assert.AreEqual(Court.FormationPos(0, 1).X, m.Players[1].Pos.X, 1e-4f);
            Assert.AreEqual(Court.FormationPos(0, 1).Z, m.Players[1].Pos.Z, 1e-4f);
        }

        [Test]
        public void ServeStartsTheRallyWhenTheServerPressesHit()
        {
            var m = TestUtil.NewServeWait();
            m.Step(Press(m.ServerSlot), TestUtil.Dt);

            Assert.AreEqual(Phase.Rally, m.Phase);
            Assert.IsTrue(m.ServeInFlight);
            Assert.AreEqual(0, m.LastTouchTeam);
            Assert.Greater(m.Ball.Vel.X, 3f, "隊伍 0 發球 → 球往 +X");
            var e = TestUtil.GetEvent(m, EventKind.Hit);
            Assert.AreEqual((byte)HitKind.Serve, e.A);
        }

        [Test]
        public void OnlyTheDesignatedServerCanServe()
        {
            var m = TestUtil.NewServeWait();
            int other = m.ServerSlot == 1 ? 2 : 1;
            m.Step(Press(other), TestUtil.Dt);
            Assert.AreEqual(Phase.ServeWait, m.Phase);
        }

        [Test]
        public void ServesAutomaticallyWhenTheServerTakesTooLong()
        {
            var m = TestUtil.NewServeWait();
            Assert.IsTrue(TestUtil.RunUntil(m, TestUtil.Idle(), () => m.Phase == Phase.Rally, GameConfig.ServeTimeout + 0.5f));
        }

        [Test]
        public void AnUnreturnedServeScoresForTheServingTeam()
        {
            var m = TestUtil.NewServeWait();
            m.Step(Press(m.ServerSlot), TestUtil.Dt);
            Assert.IsTrue(TestUtil.RunUntil(m, TestUtil.Idle(), () => m.Phase == Phase.PointPause, 4f));

            Assert.AreEqual(1, m.Score[0]);
            Assert.AreEqual(0, m.Score[1]);
            Assert.AreEqual(0, m.ServingTeam, "得分方繼續發球");
        }

        [Test]
        public void BallLandingInBoundsLosesThePointForTheSideItLandedOn()
        {
            var m = TestUtil.NewServeWait();
            TestUtil.ForceRally(m, Court.OwnSide(0, 3f, 0f, 1.5f), default, 0, 1, 0);
            Assert.IsTrue(TestUtil.RunUntil(m, TestUtil.Idle(), () => m.Phase == Phase.PointPause, 3f));

            Assert.AreEqual(1, m.Score[1], "球落在隊伍 0 的場內 → 隊伍 1 得分");
            Assert.AreEqual(1, m.ServingTeam, "拿到發球權");
        }

        [Test]
        public void BallLandingOutCostsTheLastToucher()
        {
            var m = TestUtil.NewServeWait();
            TestUtil.ForceRally(m, new Vec3(GameConfig.HalfLength + 2f, 1.5f, 0f), default, 0, 1, 0);
            Assert.IsTrue(TestUtil.RunUntil(m, TestUtil.Idle(), () => m.Phase == Phase.PointPause, 3f));
            Assert.AreEqual(1, m.Score[1], "隊伍 0 最後觸球卻出界 → 隊伍 1 得分");

            var m2 = TestUtil.NewServeWait();
            TestUtil.ForceRally(m2, new Vec3(0.5f, 1.5f, GameConfig.HalfWidth + 2f), default, 1, 1, 3);
            Assert.IsTrue(TestUtil.RunUntil(m2, TestUtil.Idle(), () => m2.Phase == Phase.PointPause, 3f));
            Assert.AreEqual(1, m2.Score[0], "隊伍 1 最後觸球卻出界 → 隊伍 0 得分");
        }

        [Test]
        public void BallOnTheLineIsIn()
        {
            var m = TestUtil.NewServeWait();
            // 球心在線外 0.1 公尺，但球本身有半徑 0.2，球邊壓到線 → 界內
            TestUtil.ForceRally(m, new Vec3(-GameConfig.HalfLength - 0.1f, 1.5f, 0f), default, 1, 1, 3);
            Assert.IsTrue(TestUtil.RunUntil(m, TestUtil.Idle(), () => m.Phase == Phase.PointPause, 3f));
            Assert.AreEqual(1, m.Score[1], "壓線算界內，落在隊伍 0 場內 → 隊伍 1 得分");
        }

        [Test]
        public void FourthTouchByTheSameTeamIsAFault()
        {
            var m = TestUtil.NewServeWait();
            m.Players[2].Pos = new Vec3(-3f, 0f, 0f);
            TestUtil.ForceRally(m, new Vec3(-3f, 1.2f, 0.3f), default, 0, 3, 1);
            m.Step(Press(2), TestUtil.Dt);

            Assert.AreEqual(Phase.PointPause, m.Phase);
            Assert.AreEqual(1, m.Score[1]);
            var e = TestUtil.GetEvent(m, EventKind.Point);
            Assert.AreEqual((byte)PointReason.FourTouches, e.A);
        }

        [Test]
        public void ThirdTouchIsStillLegal()
        {
            var m = TestUtil.NewServeWait();
            m.Players[2].Pos = new Vec3(-3f, 0f, 0f);
            TestUtil.ForceRally(m, new Vec3(-3f, 1.2f, 0.3f), default, 0, 2, 1);
            m.Step(Press(2), TestUtil.Dt);

            Assert.AreEqual(Phase.Rally, m.Phase);
            Assert.AreEqual(3, m.TouchCount);
            Assert.AreEqual((byte)HitKind.FreeBall, TestUtil.GetEvent(m, EventKind.Hit).A, "第三觸若不是扣球就吊過網");
        }

        [Test]
        public void ThePlayerWhoJustTouchedCannotTouchAgain()
        {
            var m = TestUtil.NewServeWait();
            m.Players[2].Pos = new Vec3(-3f, 0f, 0f);
            TestUtil.ForceRally(m, new Vec3(-3f, 1.2f, 0.3f), default, 0, 1, 2);
            m.Step(Press(2), TestUtil.Dt);

            Assert.IsTrue(TestUtil.HasEvent(m, EventKind.DoubleTouch));
            Assert.IsFalse(TestUtil.HasEvent(m, EventKind.Hit));
            Assert.AreEqual(1, m.TouchCount);
            Assert.AreEqual(0f, m.Ball.Vel.X, 1e-4f, "球不受影響");
        }

        [Test]
        public void ATeammateWhoLostTheRaceDoesNotHitTheBallRightAfterwards()
        {
            // 兩位隊友同時按擊球：只有離球最近的人擊球，另一位按鍵不能留著，等鎖定時間過了再「補打」
            var m = TestUtil.NewServeWait();
            m.Players[1].Pos = new Vec3(-3f, 0f, 0.1f);
            m.Players[2].Pos = new Vec3(-3f, 0f, 0.5f);
            TestUtil.ForceRally(m, new Vec3(-3f, 1.2f, 0f), default, 1, 1, 3);

            var both = TestUtil.Idle();
            both[1].Hit = true;
            both[2].Hit = true;
            m.Step(both, TestUtil.Dt);
            Assert.AreEqual(1, m.LastToucherSlot, "離球較近的 1 號擊球");
            Assert.AreEqual(1, m.TouchCount);

            // 把 2 號留在球的下方，讓他隨時構得到球；鎖定與緩衝時間都過了也不能補打
            for (int i = 0; i < TestUtil.Ticks(0.6f); i++)
            {
                m.Players[2].Pos = new Vec3(m.Ball.Pos.X, 0f, m.Ball.Pos.Z);
                m.Step(TestUtil.Idle(), TestUtil.Dt);
                Assert.IsFalse(TestUtil.HasEvent(m, EventKind.Hit), "沒按鍵的人不該擊球（第 " + i + " tick）");
            }

            Assert.AreEqual(1, m.TouchCount);
        }

        [Test]
        public void CannotReachOverTheNet()
        {
            var m = TestUtil.NewServeWait();
            m.Players[2].Pos = new Vec3(-0.6f, 0f, 0f);
            TestUtil.ForceRally(m, new Vec3(0.3f, 1.2f, 0f), default, 1, 1, 3);
            m.Step(Press(2), TestUtil.Dt);
            Assert.IsFalse(TestUtil.HasEvent(m, EventKind.Hit), "球在對方半場，隊伍 0 不能伸手過網");
        }

        [Test]
        public void ServingTeamCannotTouchItsOwnServeBeforeItCrossesTheNet()
        {
            var m = TestUtil.NewServeWait();
            m.Step(Press(m.ServerSlot), TestUtil.Dt);
            TestUtil.Run(m, TestUtil.Idle(), 0.25f);
            Assert.IsTrue(m.ServeInFlight);
            Assert.Less(m.Ball.Pos.X, 0f, "球還在發球方半場");

            int mate = m.ServerSlot == 1 ? 2 : 1;
            m.Players[mate].Pos = new Vec3(m.Ball.Pos.X, 0f, m.Ball.Pos.Z);
            m.Step(Press(mate), TestUtil.Dt);
            Assert.IsFalse(TestUtil.HasEvent(m, EventKind.Hit));
        }

        [Test]
        public void ReceivingTheServeStartsTheReceiversTouchCount()
        {
            var m = TestUtil.NewServeWait();
            m.Step(Press(m.ServerSlot), TestUtil.Dt);

            // 讓隊伍 1 的前排球員等球飛到可觸及的高度
            int receiver = 5;
            bool hit = false;
            for (int i = 0; i < TestUtil.Ticks(3f) && !hit; i++)
            {
                m.Players[receiver].Pos = new Vec3(m.Ball.Pos.X, 0f, m.Ball.Pos.Z);
                var inputs = Press(receiver);
                m.Step(inputs, TestUtil.Dt);
                hit = TestUtil.HasEvent(m, EventKind.Hit) && m.LastTouchTeam == 1;
            }

            Assert.IsTrue(hit, "接發球的人應該碰得到球");
            Assert.AreEqual(1, m.TouchCount);
            Assert.AreEqual(1, m.LastTouchTeam);
            Assert.IsFalse(m.ServeInFlight);
        }

        [Test]
        public void BumpSetSpikeChainSendsTheBallBackOverTheNet()
        {
            var m = TestUtil.NewServeWait();
            // 對方剛把球打過來，球落向隊伍 0 的後場
            TestUtil.ForceRally(m, Court.OwnSide(0, 4.5f, 0f, 1.6f), new Vec3(0f, -1f, 0f), 1, 1, 3);

            // 1) 接球：球員 2 衝到球下
            m.Players[2].Pos = new Vec3(m.Ball.Pos.X, 0f, m.Ball.Pos.Z);
            m.Step(Press(2), TestUtil.Dt);
            Assert.AreEqual((byte)HitKind.Bump, TestUtil.GetEvent(m, EventKind.Hit).A);

            // 2) 托球：等球飛到二傳區附近，球員 0 到球下
            Assert.IsTrue(TestUtil.RunUntil(m, TestUtil.Idle(), () => m.Ball.Vel.Y < 0f && m.Ball.Pos.Y < 2.0f, 3f));
            m.Players[0].Pos = new Vec3(m.Ball.Pos.X, 0f, m.Ball.Pos.Z);
            m.Step(Press(0), TestUtil.Dt);
            Assert.AreEqual((byte)HitKind.Set, TestUtil.GetEvent(m, EventKind.Hit).A);

            // 3) 扣球：球從頂點下落到 3.1 公尺時，球員 1 已跳到接近最高點
            Assert.IsTrue(TestUtil.RunUntil(m, TestUtil.Idle(), () => m.Ball.Vel.Y < 0f && m.Ball.Pos.Y <= 3.1f, 3f));
            m.Players[1].Pos = new Vec3(m.Ball.Pos.X, 0.9f, m.Ball.Pos.Z);
            m.Players[1].Vel = new Vec3(0f, 0f, 0f);
            m.Step(Press(1), TestUtil.Dt);
            var spike = TestUtil.GetEvent(m, EventKind.Hit);
            Assert.AreEqual((byte)HitKind.Spike, spike.A);
            Assert.AreEqual(3, spike.B, "這是第三觸");

            // 球越過網、落在對場界內，隊伍 0 得分
            Assert.IsTrue(TestUtil.RunUntil(m, TestUtil.Idle(), () => m.Phase == Phase.PointPause, 3f));
            Assert.AreEqual(1, m.Score[0]);
        }

        [Test]
        public void ServeRotatesThroughTeammatesWhenTheServeIsWonBack()
        {
            var m = TestUtil.NewServeWait();
            Assert.AreEqual(0, m.ServerSlot);

            // 每次得分後等到下一球預備，才會輪到新的發球員
            int NextServer(int winner)
            {
                AwardPoint(m, winner);
                Assert.IsTrue(TestUtil.RunUntil(m, TestUtil.Idle(), () => m.Phase == Phase.ServeWait, GameConfig.PointPauseTime + 0.5f));
                return m.ServerSlot;
            }

            Assert.AreEqual(3, NextServer(1), "隊伍 1 拿到發球權，第一位球員發球");
            Assert.AreEqual(1, NextServer(0), "隊伍 0 拿回發球權 → 輪到它的下一位");
            Assert.AreEqual(1, NextServer(0), "連續得分，同一人繼續發");
            Assert.AreEqual(4, NextServer(1));
            Assert.AreEqual(2, NextServer(0));
            Assert.AreEqual(5, NextServer(1));
            Assert.AreEqual(0, NextServer(0), "輪完一圈回到第一位");
        }

        [Test]
        public void PointPauseEndsWithPlayersBackInFormationAndABallReadyToServe()
        {
            var m = TestUtil.NewServeWait();
            AwardPoint(m, 1);
            m.Players[4].Pos = new Vec3(5f, 0f, 3f); // 被移到別處
            Assert.IsTrue(TestUtil.RunUntil(m, TestUtil.Idle(), () => m.Phase == Phase.ServeWait, GameConfig.PointPauseTime + 0.5f));

            Assert.IsTrue(TestUtil.HasEvent(m, EventKind.ServeReady));
            Assert.AreEqual(Court.FormationPos(1, 1).X, m.Players[4].Pos.X, 1e-4f);
            Assert.AreEqual(Court.FormationPos(1, 1).Z, m.Players[4].Pos.Z, 1e-4f);
            Assert.AreEqual(0, m.TouchCount);
        }

        [TestCase(14, 13, 0, true, TestName = "15比13：贏")]
        [TestCase(14, 14, 0, false, TestName = "15比14：只領先1分，繼續")]
        [TestCase(15, 15, 1, false, TestName = "15比16：只領先1分，繼續")]
        [TestCase(14, 15, 1, true, TestName = "14比16：隊伍1贏")]
        [TestCase(10, 14, 1, true, TestName = "10比15：隊伍1贏")]
        [TestCase(0, 14, 0, false, TestName = "1比14：還沒到")]
        public void WinRequiresFifteenAndATwoPointLead(int s0, int s1, int scorer, bool expectedOver)
        {
            var m = TestUtil.NewServeWait();
            m.Score[0] = s0;
            m.Score[1] = s1;
            AwardPoint(m, scorer);

            Assert.AreEqual(expectedOver, m.Phase == Phase.GameOver);
            if (expectedOver) Assert.AreEqual(scorer, m.Winner);
        }

        [Test]
        public void GameOverRecordsTheWinnerAndStaysOver()
        {
            var m = TestUtil.NewServeWait();
            m.Score[0] = 14;
            m.Score[1] = 10;
            AwardPoint(m, 0);

            Assert.AreEqual(Phase.GameOver, m.Phase);
            Assert.AreEqual(0, m.Winner);
            Assert.AreEqual(15, m.Score[0]);
            TestUtil.Run(m, TestUtil.Idle(), 10f);
            Assert.AreEqual(Phase.GameOver, m.Phase, "結束後不會自己重新開始");
        }

        [Test]
        public void DeuceNeedsTwoClearPoints()
        {
            var m = TestUtil.NewServeWait();
            m.Score[0] = 24;
            m.Score[1] = 24;
            AwardPoint(m, 0);
            Assert.AreNotEqual(Phase.GameOver, m.Phase, "25比24");
            Assert.IsTrue(TestUtil.RunUntil(m, TestUtil.Idle(), () => m.Phase == Phase.ServeWait, 4f));
            AwardPoint(m, 0);
            Assert.AreEqual(Phase.GameOver, m.Phase, "26比24");
            Assert.AreEqual(0, m.Winner);
        }

        [Test]
        public void SameSeedAndSameInputsGiveTheSameMatch()
        {
            VolleyballMatch Play()
            {
                var m = new VolleyballMatch(42, 0);
                var rng = new Rng(99);
                var inputs = TestUtil.Idle();
                for (int t = 0; t < TestUtil.Ticks(40f); t++)
                {
                    for (int i = 0; i < inputs.Length; i++)
                    {
                        inputs[i].Right = (sbyte)(rng.Range(-100f, 100f));
                        inputs[i].Forward = (sbyte)(rng.Range(-100f, 100f));
                        inputs[i].Jump = rng.Value01() < 0.03f;
                        inputs[i].Hit = rng.Value01() < 0.1f;
                    }

                    m.Step(inputs, TestUtil.Dt);
                }

                return m;
            }

            var a = Play();
            var b = Play();
            Assert.AreEqual(a.Score[0], b.Score[0]);
            Assert.AreEqual(a.Score[1], b.Score[1]);
            Assert.AreEqual(a.Ball.Pos.X, b.Ball.Pos.X);
            Assert.AreEqual(a.Ball.Pos.Y, b.Ball.Pos.Y);
            Assert.AreEqual(a.Players[3].Pos.Z, b.Players[3].Pos.Z);
            Assert.AreEqual(a.Tick, b.Tick);
        }

        [Test]
        public void RandomButtonMashingNeverBreaksTheSimulation()
        {
            var m = new VolleyballMatch(5, 1);
            var rng = new Rng(2024);
            var inputs = TestUtil.Idle();
            for (int t = 0; t < TestUtil.Ticks(300f) && m.Phase != Phase.GameOver; t++)
            {
                for (int i = 0; i < inputs.Length; i++)
                {
                    inputs[i].Right = (sbyte)(rng.Range(-100f, 100f));
                    inputs[i].Forward = (sbyte)(rng.Range(-100f, 100f));
                    inputs[i].Jump = rng.Value01() < 0.05f;
                    inputs[i].Hit = rng.Value01() < 0.2f;
                }

                m.Step(inputs, TestUtil.Dt);
                Assert.IsFalse(float.IsNaN(m.Ball.Pos.X + m.Ball.Pos.Y + m.Ball.Pos.Z), "球座標不該出現 NaN");
                Assert.Less(System.MathF.Abs(m.Ball.Pos.X), 60f);
                Assert.Less(m.Ball.Pos.Y, 60f);
            }

            Assert.Greater(m.Score[0] + m.Score[1], 3, "亂按也應該產生一些得分");
        }
    }
}
