using System;
using System.Collections.Generic;
using NUnit.Framework;
using Volleyball.Core;

namespace Volleyball.Tests
{
    public class RoomTests
    {
        static LoopbackNetwork ThreePlayerRoom(out TestClient host, out TestClient a, out TestClient b)
        {
            var net = new LoopbackNetwork();
            host = net.Connect(0, "阿明", 0f);
            a = net.Connect(1, "小華");
            b = net.Connect(2, "小美");
            net.Advance(0.5f);
            return net;
        }

        [Test]
        public void PlayersAreBalancedAcrossTheTwoTeamsAndEveryoneSeesTheSameSeating()
        {
            var net = ThreePlayerRoom(out var host, out var a, out var b);

            Assert.AreEqual(0, host.Client.MySlot);
            Assert.IsTrue(host.Client.Room.YouAreHost);
            Assert.AreEqual(3, a.Client.MySlot, "第二位加入的人去人數較少的隊伍 1");
            Assert.AreEqual(1, b.Client.MySlot);
            Assert.IsFalse(a.Client.Room.YouAreHost);

            foreach (TestClient c in new[] { host, a, b })
            {
                Assert.AreEqual("阿明", c.Client.Room.Slots[0].Name);
                Assert.AreEqual("小美", c.Client.Room.Slots[1].Name);
                Assert.AreEqual(SlotKind.Empty, c.Client.Room.Slots[2].Kind);
                Assert.AreEqual("小華", c.Client.Room.Slots[3].Name);
                Assert.IsFalse(c.Client.InMatch);
            }
        }

        [Test]
        public void SetTeamMovesAPlayerOnlyWhenTheOtherTeamHasRoom()
        {
            var net = ThreePlayerRoom(out var host, out var a, out var b);

            a.Client.SetTeam(0);
            net.Advance(0.3f);
            Assert.AreEqual(2, a.Client.MySlot, "隊伍 0 的空位是 2 號");

            b.Client.SetTeam(1);
            net.Advance(0.3f);
            Assert.AreEqual(3, b.Client.MySlot, "隊伍 1 現在有空位");

            var net2 = new LoopbackNetwork();
            var p = new List<TestClient>();
            for (int i = 0; i < 4; i++) p.Add(net2.Connect(i, "P" + i, 0f));
            net2.Advance(0.5f);
            // 4 人時 2 + 2；再讓隊伍 1 的人想換去滿的隊伍 0 前，先把隊伍 0 補滿
            var extra = net2.Connect(4, "P4", 0f);
            net2.Advance(0.3f);
            int team0Humans = 0;
            foreach (var c in p) if (c.Client.MyTeam == 0) team0Humans++;
            if (extra.Client.MyTeam == 0) team0Humans++;
            Assert.AreEqual(3, team0Humans, "5 人時隊伍 0 補滿 3 人");

            TestClient onTeam1 = null;
            foreach (var c in p) if (c.Client.MyTeam == 1) onTeam1 = c;
            int before = onTeam1.Client.MySlot;
            onTeam1.Client.SetTeam(0);
            net2.Advance(0.3f);
            Assert.AreEqual(before, onTeam1.Client.MySlot, "隊伍 0 滿了，換隊被拒絕");
        }

        [Test]
        public void OnlyTheHostCanStartAndEmptySeatsBecomeBots()
        {
            var net = ThreePlayerRoom(out var host, out var a, out var b);

            a.Client.RequestStart();
            net.Advance(0.3f);
            Assert.IsFalse(a.Client.InMatch, "非房主不能開始");

            host.Client.RequestStart();
            net.Advance(0.3f);
            foreach (TestClient c in new[] { host, a, b }) Assert.IsTrue(c.Client.InMatch);

            int bots = 0, humans = 0;
            foreach (SlotInfo s in host.Client.Room.Slots)
            {
                if (s.Kind == SlotKind.Bot)
                {
                    bots++;
                    StringAssert.StartsWith("電腦", s.Name);
                }
                else if (s.Kind == SlotKind.Human)
                {
                    humans++;
                }
            }

            Assert.AreEqual(3, humans);
            Assert.AreEqual(3, bots, "6 個座位 − 3 位真人 = 3 個電腦");
        }

        [Test]
        public void ASoloHostCanPlayAgainstFiveBots()
        {
            var net = new LoopbackNetwork();
            var host = net.Connect(0, "我", 0f);
            net.Advance(0.3f);
            host.Client.RequestStart();
            net.Advance(0.3f);

            Assert.IsTrue(host.Client.InMatch);
            int bots = 0;
            foreach (SlotInfo s in host.Client.Room.Slots) if (s.Kind == SlotKind.Bot) bots++;
            Assert.AreEqual(5, bots);
        }

        [Test]
        public void ASeventhPlayerIsTurnedAway()
        {
            var net = new LoopbackNetwork();
            for (int i = 0; i < 6; i++) net.Connect(i, "P" + i, 0f);
            net.Advance(0.5f);
            var late = net.Connect(6, "太晚了", 0f);
            net.Advance(0.5f);

            Assert.AreEqual("房間已滿", late.Client.RejectReason);
            Assert.AreEqual(-1, late.Client.MySlot);
        }

        [Test]
        public void LongOrOddNamesAreCleanedUp()
        {
            var net = new LoopbackNetwork();
            var host = net.Connect(0, "  這是一個非常非常長的玩家名字超過限制\u0001  ", 0f);
            var anon = net.Connect(1, "", 0f);
            net.Advance(0.5f);

            Assert.LessOrEqual(host.Client.Room.Slots[0].Name.Length, Protocol.MaxNameLength);
            Assert.IsFalse(host.Client.Room.Slots[0].Name.Contains("\u0001"));
            StringAssert.StartsWith("玩家", anon.Client.Room.Slots[anon.Client.MySlot].Name);
        }
    }

    public class MatchOverTheNetworkTests
    {
        static LoopbackNetwork StartedMatch(out TestClient host, out TestClient friend, float friendLatency = 0.05f)
        {
            var net = new LoopbackNetwork();
            host = net.Connect(0, "房主", 0f);
            friend = net.Connect(1, "朋友", friendLatency);
            net.Advance(0.5f);
            host.Client.RequestStart();
            net.Advance(1.0f);
            return net;
        }

        [Test]
        public void EveryoneSeesTheSameCountdownScene()
        {
            var net = StartedMatch(out var host, out var friend);

            Assert.IsTrue(host.Client.Render.Valid && friend.Client.Render.Valid);
            Assert.AreEqual(Phase.Countdown, friend.Client.Render.Phase);
            for (int i = 0; i < GameConfig.SlotCount; i++)
            {
                Assert.AreEqual(host.Client.Render.Players[i].Pos.X, friend.Client.Render.Players[i].Pos.X, 0.05f, "slot " + i);
                Assert.AreEqual(host.Client.Render.Players[i].Pos.Z, friend.Client.Render.Players[i].Pos.Z, 0.05f, "slot " + i);
            }

            Assert.AreEqual(host.Client.Render.BallPos.Y, friend.Client.Render.BallPos.Y, 0.05f);
        }

        [Test]
        public void SnapshotsAreSmall()
        {
            var m = new VolleyballMatch(1, 0);
            var w = new ByteWriter();
            Protocol.WriteSnapshotBody(w, m);
            Protocol.AppendAck(w, 123);
            Assert.LessOrEqual(w.Length, 130, "快照大小：" + w.Length + " 位元組");
        }

        [Test]
        public void SnapshotRoundTripKeepsEverythingTheViewNeeds()
        {
            var m = new VolleyballMatch(1, 1);
            m.Score[0] = 7;
            m.Score[1] = 9;
            m.Ball.Pos = new Vec3(-2.34f, 3.21f, 1.5f);
            m.Ball.Vel = new Vec3(4.5f, -2.25f, 0.75f);
            m.Players[4].Pos = new Vec3(3.3f, 0.88f, -1.25f);
            m.Players[4].SwingTimer = 0.2f;
            m.LastTouchTeam = 1;
            m.TouchCount = 2;

            var w = new ByteWriter();
            Protocol.WriteSnapshotBody(w, m);
            Protocol.AppendAck(w, 4242);
            var s = new Snapshot();
            var r = new ByteReader(w.Buffer, w.Length);
            Assert.AreEqual((byte)MsgType.Snapshot, r.ReadByte());
            Assert.IsTrue(Protocol.ReadSnapshot(r, s));

            Assert.AreEqual(7, s.Score0);
            Assert.AreEqual(9, s.Score1);
            Assert.AreEqual(1, s.LastTouchTeam);
            Assert.AreEqual(2, s.TouchCount);
            Assert.AreEqual(4242u, s.AckSeq);
            Assert.AreEqual(-2.34f, s.Ball.Pos.X, 0.011f);
            Assert.AreEqual(-2.25f, s.Ball.Vel.Y, 0.011f);
            Assert.AreEqual(3.3f, s.Players[4].Pos.X, 0.011f);
            Assert.AreEqual(0.2f, s.Players[4].SwingTimer, 0.011f);
            Assert.AreEqual(Phase.Countdown, s.Phase);
        }

        [Test]
        public void TheLocalPlayerMovesImmediatelyEvenWithHighLatency()
        {
            var net = StartedMatch(out var host, out var friend, 0.1f);
            net.Advance(0.5f); // 等第一批快照讓預測生效
            int slot = friend.Client.MySlot;
            float startX = friend.Client.Render.Players[slot].Pos.X;

            friend.Forward = 1f;
            net.Advance(0.25f);

            float predictedMoved = Math.Abs(friend.Client.Render.Players[slot].Pos.X - startX);
            float serverMoved = Math.Abs(net.Server.Match.Players[slot].Pos.X - startX);
            Assert.Greater(predictedMoved, 0.8f, "本機預測：按下就動");
            Assert.Less(serverMoved, predictedMoved - 0.3f, "伺服器還沒收到完整輸入，落後約一個單程延遲");
        }

        [Test]
        public void PredictionConvergesToTheServerOnceInputStops()
        {
            var net = StartedMatch(out var host, out var friend, 0.08f);
            net.Advance(0.5f);
            int slot = friend.Client.MySlot;

            friend.Forward = 1f;
            friend.Right = 0.5f;
            net.Advance(0.8f);
            friend.Forward = 0f;
            friend.Right = 0f;
            net.Advance(1.0f);

            Vec3 shown = friend.Client.Render.Players[slot].Pos;
            Vec3 truth = net.Server.Match.Players[slot].Pos;
            Assert.AreEqual(truth.X, shown.X, 0.05f);
            Assert.AreEqual(truth.Z, shown.Z, 0.05f);
        }

        [Test]
        public void PredictionStaysCloseToTheServerWhilePacketsAreLostAndDelayed()
        {
            var net = StartedMatch(out var host, out var friend, 0.06f);
            net.Loss = 0.15f;
            net.Jitter = 0.03f;
            net.Advance(0.5f);
            int slot = friend.Client.MySlot;

            var rng = new Rng(5);
            float worst = 0f;
            for (int step = 0; step < 6; step++)
            {
                friend.Forward = rng.Range(-1f, 1f);
                friend.Right = rng.Range(-1f, 1f);
                for (int f = 0; f < 30; f++)
                {
                    net.Advance(1f / 60f);
                    // 預測位置與「伺服器此刻位置」的差：只比較有收斂時間的後半段
                    if (f > 20) worst = Math.Max(worst, Vec3.DistanceXZ(friend.Client.Render.Players[slot].Pos, net.Server.Match.Players[slot].Pos));
                }
            }

            Assert.Less(worst, 1.2f, "掉包 15% + 抖動時，畫面與伺服器的最大差距（公尺）");
        }

        [Test]
        public void TheLocalPlayerNeverVisiblyJumpsWhileRunningAroundOnABadNetwork()
        {
            var net = StartedMatch(out var host, out var friend, 0.06f);
            net.Loss = 0.1f;
            net.Jitter = 0.04f;
            net.Advance(0.5f);
            int slot = friend.Client.MySlot;

            Vec3 prev = friend.Client.Render.Players[slot].Pos;
            float worst = 0f;
            for (int i = 0; i < 360; i++)
            {
                if (i % 40 == 0)
                {
                    friend.Forward = (i / 40) % 2 == 0 ? 1f : -1f; // 來回跑，不會一直頂到邊界
                    friend.Right = (i / 80) % 2 == 0 ? 0.5f : -0.5f;
                    if (i % 120 == 0) friend.PressJump();
                }

                net.Advance(1f / 60f);
                Vec3 cur = friend.Client.Render.Players[slot].Pos;
                worst = Math.Max(worst, Vec3.DistanceXZ(prev, cur));
                prev = cur;
            }

            Assert.Less(worst, 0.2f, "單一畫面內本機角色的最大位移（公尺），全速一格只有 " + GameConfig.MoveSpeed / 60f);
        }

        [Test]
        public void OtherPlayersAreRenderedSmoothlyWithoutJumps()
        {
            var net = StartedMatch(out var host, out var friend, 0.05f);
            net.Jitter = 0.02f;
            net.Advance(0.5f);
            int slot = friend.Client.MySlot;

            friend.Forward = 1f;
            friend.Right = 1f;
            Vec3 prev = host.Client.Render.Players[slot].Pos;
            float worst = 0f;
            for (int i = 0; i < 120; i++)
            {
                net.Advance(1f / 60f);
                Vec3 cur = host.Client.Render.Players[slot].Pos;
                worst = Math.Max(worst, Vec3.DistanceXZ(prev, cur));
                prev = cur;
            }

            Assert.Less(worst, GameConfig.MoveSpeed / 60f * 2f, "每個畫面的位移不該超過最高速度的兩倍");
        }

        [Test]
        public void ButtonPressesSurvivePacketLoss()
        {
            var net = StartedMatch(out var host, out var friend, 0.04f);
            net.Loss = 0.2f;
            net.Advance(0.5f);
            int slot = friend.Client.MySlot;

            int jumpsSeenByServer = 0;
            const int presses = 30;
            for (int i = 0; i < presses; i++)
            {
                friend.PressJump();
                bool seen = false;
                for (int f = 0; f < 60; f++)
                {
                    net.Advance(1f / 60f);
                    seen |= net.Server.Match.Players[slot].Pos.Y > 0.1f;
                }

                if (seen) jumpsSeenByServer++;
            }

            Assert.GreaterOrEqual(jumpsSeenByServer, presses - 1, "20% 掉包下，每個按鍵會被送 3 次，全部掉光的機率不到 1%");
        }

        [Test]
        public void ARunningPlayerStopsWhenTheirInputsStopArriving()
        {
            var net = StartedMatch(out var host, out var friend, 0.02f);
            net.Advance(0.5f);
            int slot = friend.Client.MySlot;

            friend.Forward = 1f;
            net.Advance(0.3f);
            Assert.Greater(net.Server.Match.Players[slot].Vel.LengthXZ(), 3f, "正在跑");

            net.Loss = 1f; // 輸入封包全丟：連線還在，但再也收不到新輸入
            net.Advance(0.6f);
            Assert.AreEqual(0f, net.Server.Match.Players[slot].Vel.LengthXZ(), 0.01f, "不該一直朝同一方向跑下去");
        }

        [Test]
        public void WhenAHumanLeavesMidMatchABotTakesTheSeat()
        {
            var net = StartedMatch(out var host, out var friend);
            int slot = friend.Client.MySlot;
            Assert.AreEqual(SlotKind.Human, host.Client.Room.Slots[slot].Kind);

            net.Disconnect(1);
            net.Advance(0.5f);

            Assert.AreEqual(SlotKind.Bot, host.Client.Room.Slots[slot].Kind);
            StringAssert.StartsWith("電腦", host.Client.Room.Slots[slot].Name);
            Assert.IsTrue(host.Client.InMatch, "比賽繼續");
        }

        [Test]
        public void JoiningMidMatchReplacesABot()
        {
            var net = StartedMatch(out var host, out var friend);
            var late = net.Connect(2, "路人", 0.05f);
            net.Advance(1.0f);

            Assert.IsTrue(late.Client.InMatch);
            int slot = late.Client.MySlot;
            Assert.GreaterOrEqual(slot, 0);
            Assert.AreEqual(SlotKind.Human, host.Client.Room.Slots[slot].Kind);
            Assert.AreEqual("路人", host.Client.Room.Slots[slot].Name);
            Assert.IsTrue(late.Client.Render.Valid, "加入後馬上收得到比賽畫面");

            int humans = 0;
            foreach (SlotInfo s in host.Client.Room.Slots) if (s.Kind == SlotKind.Human) humans++;
            Assert.AreEqual(3, humans);
        }

        [Test]
        public void AFullHumanMatchRejectsLatecomers()
        {
            var net = new LoopbackNetwork();
            var all = new List<TestClient>();
            for (int i = 0; i < 6; i++) all.Add(net.Connect(i, "P" + i, 0f));
            net.Advance(0.5f);
            all[0].Client.RequestStart();
            net.Advance(0.5f);

            var late = net.Connect(9, "遲到", 0f);
            net.Advance(0.5f);
            Assert.AreEqual("房間已滿", late.Client.RejectReason);
        }

        [Test]
        public void TheMatchEndsAndEveryoneReturnsToTheLobbyWithoutBots()
        {
            var net = StartedMatch(out var host, out var friend);
            net.Advance(0.5f);

            var m = net.Server.Match;
            m.Score[0] = 14;
            // 球落在任何人都構不到的遠處界外，最後觸球的是隊伍 1 → 隊伍 0 得第 15 分
            TestUtil.ForceRally(m, new Vec3(11f, 1.5f, 0f), default, 1, 1, 3);
            net.Advance(1.5f);
            Assert.AreEqual(Phase.GameOver, m.Phase);
            Assert.AreEqual(Phase.GameOver, friend.Client.Render.Phase);
            Assert.AreEqual(0, m.Winner);

            net.Advance(GameConfig.ResultsTime + 1f);
            Assert.IsFalse(host.Client.InMatch);
            Assert.IsFalse(friend.Client.InMatch);
            int bots = 0, humans = 0;
            foreach (SlotInfo s in host.Client.Room.Slots)
            {
                if (s.Kind == SlotKind.Bot) bots++;
                if (s.Kind == SlotKind.Human) humans++;
            }

            Assert.AreEqual(0, bots);
            Assert.AreEqual(2, humans);

            // 房主可以再開一場
            host.Client.RequestStart();
            net.Advance(0.5f);
            Assert.IsTrue(friend.Client.InMatch);
        }

        [Test]
        public void EventsReachTheViewAtTheMomentTheirTickIsRendered()
        {
            var net = StartedMatch(out var host, out var friend, 0.05f);
            net.Advance(GameConfig.CountdownTime);

            var events = new List<GameEvent>();
            bool sawServeReady = false;
            for (int i = 0; i < 120 && !sawServeReady; i++)
            {
                net.Advance(1f / 60f);
                events.Clear();
                friend.Client.PopDueEvents(events);
                foreach (var e in events) sawServeReady |= e.Kind == EventKind.ServeReady;
            }

            Assert.IsTrue(sawServeReady);
        }
    }

    public class RobustnessTests
    {
        [Test]
        public void SixHumansMashingButtonsOverALossyNetworkPlayForMinutesWithoutErrors()
        {
            var net = new LoopbackNetwork();
            net.Loss = 0.1f;
            net.Jitter = 0.04f;
            var players = new List<TestClient>();
            for (int i = 0; i < 6; i++) players.Add(net.Connect(i, "P" + i, i == 0 ? 0f : 0.03f + 0.01f * i));
            net.Advance(0.5f);
            players[0].Client.RequestStart();
            net.Advance(0.5f);
            foreach (TestClient p in players) Assert.IsTrue(p.Client.InMatch);

            var rng = new Rng(31337);
            int pointsSeen = 0;
            for (int second = 0; second < 180; second++)
            {
                foreach (TestClient p in players)
                {
                    p.Right = rng.Range(-1f, 1f);
                    p.Forward = rng.Range(-1f, 1f);
                    if (rng.Value01() < 0.5f) p.PressHit();
                    if (rng.Value01() < 0.3f) p.PressJump();
                }

                for (int f = 0; f < 6; f++)
                {
                    net.Advance(1f / 6f);
                    foreach (TestClient p in players)
                    {
                        if (!p.Client.Render.Valid) continue;
                        Vec3 b = p.Client.Render.BallPos;
                        Assert.IsFalse(float.IsNaN(b.X + b.Y + b.Z), "畫面上的球座標不該是 NaN");
                        Assert.Less(Math.Abs(b.X), 80f);
                    }
                }

                if (net.Server.InMatch) pointsSeen = Math.Max(pointsSeen, net.Server.Match.Score[0] + net.Server.Match.Score[1]);
            }

            Assert.Greater(pointsSeen, 0, "三分鐘內應該已經有人得分");
            foreach (TestClient p in players)
                Assert.IsTrue(p.Client.Room != null && p.Client.MySlot >= 0, "每個人到最後都還在房間裡");
        }

        [Test]
        public void GarbageAndTruncatedPacketsNeverThrow()
        {
            var net = new LoopbackNetwork();
            var host = net.Connect(0, "房主", 0f);
            var friend = net.Connect(1, "朋友", 0f);
            net.Advance(0.3f);
            host.Client.RequestStart();
            net.Advance(0.5f);

            var rng = new Rng(2025);
            var data = new byte[300];
            for (int i = 0; i < 6000; i++)
            {
                int len = (int)(rng.NextUInt() % 200);
                for (int k = 0; k < len; k++) data[k] = (byte)rng.NextUInt();
                if (i % 3 == 0 && len > 0) data[0] = (byte)(1 + rng.NextUInt() % 4);
                if (i % 3 == 1 && len > 0) data[0] = (byte)(20 + rng.NextUInt() % 4);

                net.Server.OnMessage(1, data, len);
                friend.Client.OnMessage(data, len);
            }

            net.Advance(1.0f);
            Assert.IsTrue(friend.Client.InMatch, "亂碼之後連線與比賽仍然正常");
        }

        [Test]
        public void EveryTruncationOfAValidSnapshotIsRejectedCleanly()
        {
            var m = new VolleyballMatch(1, 0);
            var w = new ByteWriter();
            Protocol.WriteSnapshotBody(w, m);
            Protocol.AppendAck(w, 1);

            for (int cut = 1; cut < w.Length; cut++)
            {
                var r = new ByteReader(w.Buffer, cut);
                r.ReadByte();
                Assert.IsFalse(Protocol.ReadSnapshot(r, new Snapshot()), "截到 " + cut + " 位元組應被拒絕");
            }
        }

        [Test]
        public void InputsFromAnUnseatedClientAreIgnored()
        {
            var net = new LoopbackNetwork();
            var host = net.Connect(0, "房主", 0f);
            net.Advance(0.2f);
            host.Client.RequestStart();
            net.Advance(0.3f);

            var w = new ByteWriter();
            Protocol.WriteInput(w, new[] { new InputFrame { Seq = 1, Forward = 100, Hit = true } }, 0, 1);
            net.Server.OnMessage(77, w.Buffer, w.Length); // 77 從沒報到過
            net.Advance(0.2f);
            Assert.IsTrue(host.Client.InMatch);
        }
    }
}
