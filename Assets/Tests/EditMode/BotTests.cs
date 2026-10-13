using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using NUnit.Framework;
using Volleyball.Core;

namespace Volleyball.Tests
{
    /// <summary>某個槽位這個 tick 要做什麼（null = 閒置：不動、不擊球）。</summary>
    internal delegate InputFrame Controller(VolleyballMatch m);

    /// <summary>一批電腦對戰的統計數字。</summary>
    internal sealed class ArenaStats
    {
        public int Matches, Unfinished, Rallies, TotalHits, ChainRallies, SpikeRallies, QuickDoubles;
        public readonly int[] Reasons = new int[4]; // 以 PointReason 為索引
        public readonly int[] KindHits = new int[6]; // 以 HitKind 為索引
        public readonly int[] KindKills = new int[6]; // 該擊球是回合最後一擊、而且直接得分
        public readonly int[] Wins = new int[2];
        public readonly int[] PointsWon = new int[2];
        public readonly int[] ServesReceived = new int[2]; // 該隊當接發球方的次數
        public readonly int[] ServesReturned = new int[2]; // 其中接發球方把球打回對面（對手必須再處理一次，或球落在對手半場）的次數
        public float MaxRallySeconds, MaxServeWaitSeconds, MaxMatchSeconds;
        public ulong Hash = 14695981039346656037UL;

        public int Points => Reasons[1] + Reasons[2] + Reasons[3];
        public double HitsPerRally => Rallies == 0 ? 0 : (double)TotalHits / Rallies;
        public double ChainShare => Rallies == 0 ? 0 : (double)ChainRallies / Rallies;
        public double SpikeShare => Rallies == 0 ? 0 : (double)SpikeRallies / Rallies;
        public double ReasonShare(PointReason r) => Points == 0 ? 0 : (double)Reasons[(int)r] / Points;
        public double PointShare(int team) => Points == 0 ? 0 : (double)PointsWon[team] / Points;
        public double ReturnShare(int team) => ServesReceived[team] == 0 ? 0 : (double)ServesReturned[team] / ServesReceived[team];

        public void Mix(long v)
        {
            Hash ^= (ulong)v;
            Hash *= 1099511628211UL;
        }

        public override string ToString()
        {
            return string.Format(
                "matches={0} unfinished={1} rallies={2} hits/rally={3:0.00} chain={4:P0} spikeRallies={5:P0} " +
                "landed={6:P1} out={7:P1} fourTouches={8:P1} | maxRally={9:0.0}s maxServeWait={10:0.0}s longestMatch={11:0.0}min | " +
                "spike kill rate={12:P0} ({13} spikes) | team0 {14} - {15} team1 points, matches {16}-{17} | quick double touches {18}",
                Matches, Unfinished, Rallies, HitsPerRally, ChainShare, SpikeShare,
                ReasonShare(PointReason.Landed), ReasonShare(PointReason.Out), ReasonShare(PointReason.FourTouches),
                MaxRallySeconds, MaxServeWaitSeconds, MaxMatchSeconds / 60f,
                KindHits[4] == 0 ? 0 : (double)KindKills[4] / KindHits[4], KindHits[4],
                PointsWon[0], PointsWon[1], Wins[0], Wins[1], QuickDoubles);
        }
    }

    /// <summary>讓電腦（或不動的閒置球員、行為亂來的「人類」）打完整場比賽並收集統計。</summary>
    internal static class BotArena
    {
        public const float Dt = GameConfig.TickDt;

        public static Controller Bot(int slot, uint seed)
        {
            var brain = new BotBrain(slot, seed + (uint)slot * 7919u);
            return m => brain.Think(m, Dt);
        }

        /// <summary>只有指定的槽位是電腦，其餘閒置。</summary>
        public static Controller[] Bots(uint seed, params int[] slots)
        {
            var c = new Controller[GameConfig.SlotCount];
            foreach (int s in slots) c[s] = Bot(s, seed);
            return c;
        }

        public static Controller[] AllBots(uint seed) => Bots(seed, 0, 1, 2, 3, 4, 5);

        /// <summary>六個都是電腦，只有 idleSlot 是閒置球員。</summary>
        public static Controller[] WithIdle(uint seed, int idleSlot)
        {
            Controller[] c = AllBots(seed);
            c[idleSlot] = null;
            return c;
        }

        public static void Step(VolleyballMatch m, Controller[] ctl, InputFrame[] inputs)
        {
            for (int i = 0; i < inputs.Length; i++) inputs[i] = ctl[i] == null ? default : ctl[i](m);
            m.Step(inputs, Dt);
        }

        /// <summary>打完一場（或打到 maxMinutes 模擬分鐘）。hook 每個 tick 在大家思考之前呼叫，可以拿來中途換人。</summary>
        public static void Play(ArenaStats st, uint seed, int firstServe, Controller[] ctl, float maxMinutes = 20f, Action<VolleyballMatch> hook = null)
        {
            var m = new VolleyballMatch(seed, firstServe);
            var inputs = new InputFrame[GameConfig.SlotCount];
            int maxTicks = (int)(maxMinutes * 60f / Dt);
            int hits = 0, lastKind = 0, lastTeam = -1;
            uint lastHitTick = 0;
            bool chain = false, spike = false;
            int receiver = -1; // 這一球的接發球方
            bool recvTouched = false, returned = false;

            while (m.Phase != Phase.GameOver && m.Tick < maxTicks)
            {
                if (hook != null) hook(m);
                Step(m, ctl, inputs);

                if (m.Phase == Phase.Rally && m.PhaseTime > st.MaxRallySeconds) st.MaxRallySeconds = m.PhaseTime;
                if (m.Phase == Phase.ServeWait && m.PhaseTime > st.MaxServeWaitSeconds) st.MaxServeWaitSeconds = m.PhaseTime;

                for (int e = 0; e < m.Events.Count; e++)
                {
                    GameEvent ev = m.Events[e];
                    if (ev.Kind == EventKind.Hit)
                    {
                        if (ev.A == (byte)HitKind.Serve) { receiver = 1 - ev.Team; recvTouched = returned = false; st.ServesReceived[receiver]++; }
                        else if (ev.Team == receiver) recvTouched = true;
                        else if (recvTouched && ev.B == 1) MarkReturned(st, receiver, ref returned);
                        hits++;
                        if (hits > 1 && ev.Team == lastTeam && m.Tick - lastHitTick < 24) st.QuickDoubles++; // 同隊兩觸間隔不到 0.4 秒 = 搶球／誤觸
                        lastHitTick = m.Tick;
                        if (ev.B == 3) chain = true; // 同一隊連續第三觸 = 完成了 接→托→攻
                        if (ev.A == (byte)HitKind.Spike) spike = true;
                        st.KindHits[ev.A]++;
                        lastKind = ev.A;
                        lastTeam = ev.Team;
                        st.Mix(m.Tick * 31L + ev.Slot * 7 + ev.A);
                    }
                    else if (ev.Kind == EventKind.Landed && recvTouched && ev.Team != receiver) MarkReturned(st, receiver, ref returned);
                    else if (ev.Kind == EventKind.Point)
                    {
                        st.Rallies++;
                        st.TotalHits += hits;
                        if (chain) st.ChainRallies++;
                        if (spike) st.SpikeRallies++;
                        st.Reasons[ev.A]++;
                        st.PointsWon[ev.Team]++;
                        if (ev.A == (byte)PointReason.Landed && ev.Team == lastTeam) st.KindKills[lastKind]++;
                        st.Mix(m.Tick * 131L + ev.Team * 17 + ev.A);
                        hits = 0;
                        chain = false;
                        spike = false;
                    }
                }
            }

            st.Matches++;
            float secs = m.Tick * Dt;
            if (secs > st.MaxMatchSeconds) st.MaxMatchSeconds = secs;
            if (m.Phase != Phase.GameOver) st.Unfinished++;
            else st.Wins[m.Winner]++;
            st.Mix(m.Tick);
        }

        static void MarkReturned(ArenaStats st, int receiver, ref bool returned)
        {
            if (returned) return;
            returned = true;
            st.ServesReturned[receiver]++;
        }

        public static ArenaStats PlayMany(int matches, uint seedBase, Func<uint, Controller[]> makeControllers)
        {
            var st = new ArenaStats();
            for (int i = 0; i < matches; i++)
            {
                uint seed = seedBase + (uint)i * 7919u;
                Play(st, seed, i % 2, makeControllers(seed));
            }

            return st;
        }
    }

    /// <summary>
    /// 行為亂來的「人類」隊友：衝向球、有時發呆、亂按擊球、亂跳、亂瞄。
    /// 用來確認電腦隊友不會被它搞到卡死（電腦要用可觀察的狀態判斷誰負責）。
    /// </summary>
    internal sealed class ChaoticMate
    {
        readonly int slot, team;
        readonly Rng rng;
        int idleTicks;

        public ChaoticMate(int slot, uint seed)
        {
            this.slot = slot;
            team = Court.TeamOf(slot);
            rng = new Rng(seed * 977u + (uint)slot + 5u);
            for (int i = 0; i < 4; i++) rng.NextUInt();
        }

        public InputFrame Think(VolleyballMatch m)
        {
            var f = new InputFrame();
            if (idleTicks > 0) { idleTicks--; return f; }
            if (rng.Value01() < 0.004f) { idleTicks = 60; return f; } // 偶爾發呆一秒

            PlayerState me = m.Players[slot];
            Vec3 b = m.Ball.Pos;
            float dx = b.X - me.Pos.X, dz = b.Z - me.Pos.Z;
            float d = MathF.Sqrt(dx * dx + dz * dz);
            if (d > 0.05f)
            {
                f.Right = InputFrame.Quantize(dz / d * Court.Right(team).Z);
                f.Forward = InputFrame.Quantize(dx / d * Court.Forward(team).X);
            }

            if (d < 1.3f && b.Y < 2.4f && rng.Value01() < 0.35f)
            {
                f.Hit = true;
                f.Right = InputFrame.Quantize(rng.Range(-1f, 1f));
                f.Forward = InputFrame.Quantize(rng.Range(-1f, 1f));
            }

            f.Jump = rng.Value01() < 0.01f;
            return f;
        }
    }

    public class BotTests
    {
        const float Dt = GameConfig.TickDt;

        // ---------------------------------------------------------------- 發球

        [Test]
        public void ServerWaitsBetweenPoint7AndPoint8SecondsBeforeServing()
        {
            float min = 99f, max = 0f;
            for (uint seed = 1; seed <= 24; seed++)
            {
                var m = new VolleyballMatch(seed, (int)(seed % 2));
                Controller[] ctl = BotArena.AllBots(seed);
                var inputs = new InputFrame[GameConfig.SlotCount];
                bool served = false;
                for (int i = 0; i < 600 && !served; i++)
                {
                    float before = m.PhaseTime;
                    Phase phase = m.Phase;
                    BotArena.Step(m, ctl, inputs);
                    if (phase == Phase.ServeWait && m.Phase == Phase.Rally)
                    {
                        served = true;
                        float t = before + Dt;
                        min = Math.Min(min, t);
                        max = Math.Max(max, t);
                    }
                }

                Assert.IsTrue(served, "電腦發球員必須自己把球發出去 (seed " + seed + ")");
            }

            TestContext.WriteLine("serve delay range over 24 serves: " + min.ToString("0.00") + " .. " + max.ToString("0.00") + " s");
            Assert.GreaterOrEqual(min, 0.7f - Dt, "不能太快發球");
            Assert.LessOrEqual(max, 1.8f + 2 * Dt, "不能拖太久");
            Assert.Greater(max - min, 0.4f, "每次等待時間要有變化");
        }

        [Test]
        public void ServesAreVariedLandInBoundsAndAimAwayFromTheReceivers()
        {
            var lateral = new List<float>();
            float sumGap = 0f;
            int inBounds = 0, n = 30;
            for (uint seed = 1; seed <= n; seed++)
            {
                var m = new VolleyballMatch(seed, (int)(seed % 2));
                Controller[] ctl = BotArena.Bots(seed, m.ServerSlot); // 對手全是不動的靶
                var inputs = new InputFrame[GameConfig.SlotCount];
                bool landed = false;
                for (int i = 0; i < 60 * 8 && !landed; i++)
                {
                    BotArena.Step(m, ctl, inputs);
                    for (int e = 0; e < m.Events.Count; e++)
                    {
                        if (m.Events[e].Kind != EventKind.Landed) continue;
                        landed = true;
                        if (m.Events[e].A == 1) inBounds++;
                        Vec3 p = m.Events[e].Pos;
                        int team = 1 - Court.TeamOf(m.ServerSlot);
                        lateral.Add(p.Z);
                        float gap = float.MaxValue;
                        for (int k = 0; k < GameConfig.PlayersPerTeam; k++)
                        {
                            Vec3 r = Court.FormationPos(team, k);
                            gap = Math.Min(gap, MathF.Sqrt((r.X - p.X) * (r.X - p.X) + (r.Z - p.Z) * (r.Z - p.Z)));
                        }

                        sumGap += gap;
                    }
                }

                Assert.IsTrue(landed, "發出去的球應該落地");
            }

            float mean = sumGap / n, spread = 0f, avg = 0f;
            foreach (float z in lateral) avg += z / n;
            foreach (float z in lateral) spread += (z - avg) * (z - avg) / n;
            float std = MathF.Sqrt(spread);
            TestContext.WriteLine("serves: in-bounds " + inBounds + "/" + n + ", mean distance of the landing spot to the nearest receiver " + mean.ToString("0.00") + " m, lateral std " + std.ToString("0.00") + " m");
            Assert.GreaterOrEqual(inBounds, n - 1, "發球幾乎都要在界內");
            Assert.Greater(mean, 1.25f, "發球要避開接球的人（隨機落點平均只有約 1.0 m）");
            Assert.Greater(std, 1.0f, "發球落點要有變化");
        }

        // ---------------------------------------------------------------- 接球 → 二傳 → 進攻

        /// <summary>隊伍 1 的發球員（電腦）發球，隊伍 0 三個電腦接球；回傳隊伍 0 在這一球裡的擊球事件。</summary>
        static List<GameEvent> PlayReceiveScenario(uint seed, out int firstMoveTick, out int serveTick)
        {
            var m = new VolleyballMatch(seed, 1);
            Controller[] ctl = BotArena.Bots(seed, 0, 1, 2, m.ServerSlot);
            var inputs = new InputFrame[GameConfig.SlotCount];
            var hits = new List<GameEvent>();
            firstMoveTick = -1;
            serveTick = -1;
            for (int i = 0; i < 60 * 15; i++)
            {
                BotArena.Step(m, ctl, inputs);
                if (serveTick >= 0 && firstMoveTick < 0)
                {
                    for (int s = 0; s < 3; s++)
                        if (inputs[s].Right != 0 || inputs[s].Forward != 0) firstMoveTick = (int)m.Tick;
                }

                bool point = false;
                for (int e = 0; e < m.Events.Count; e++)
                {
                    GameEvent ev = m.Events[e];
                    if (ev.Kind == EventKind.Hit && ev.A == (byte)HitKind.Serve) serveTick = (int)m.Tick;
                    if (ev.Kind == EventKind.Hit && ev.Team == 0) hits.Add(ev);
                    if (ev.Kind == EventKind.Point) point = true;
                }

                if (point) break;
            }

            return hits;
        }

        [Test]
        public void ReceivingTeamBuildsABumpSetAttackChainWithDifferentPlayers()
        {
            int chains = 0, spikes = 0, runs = 30;
            for (uint seed = 1; seed <= runs; seed++)
            {
                List<GameEvent> hits = PlayReceiveScenario(seed, out _, out _);
                if (hits.Count < 3) continue;
                GameEvent a = hits[0], b = hits[1], c = hits[2];
                bool kinds = a.A == (byte)HitKind.Bump && b.A == (byte)HitKind.Set && (c.A == (byte)HitKind.Spike || c.A == (byte)HitKind.FreeBall);
                bool players = a.Slot != b.Slot && b.Slot != c.Slot; // 不能連續觸球
                if (!kinds || !players) continue;
                chains++;
                if (c.A == (byte)HitKind.Spike)
                {
                    spikes++;
                    Assert.GreaterOrEqual(c.Pos.Y, GameConfig.SpikeMinBallY, "扣球必須打在球夠高的時候");
                }
            }

            TestContext.WriteLine("receive scenario: " + chains + "/" + runs + " complete Bump-Set-Attack chains, " + spikes + " of them spikes");
            Assert.GreaterOrEqual(chains, runs * 3 / 5, "接發球後多半要完成 接→托→攻");
            Assert.GreaterOrEqual(spikes, chains / 2, "有機會就扣球，不是每次都吊球");
        }

        [Test]
        public void ReactsOnlyAfterTheReactionDelayFollowingTheOpponentsHit()
        {
            int shortest = 999, longest = 0;
            for (uint seed = 1; seed <= 20; seed++)
            {
                PlayReceiveScenario(seed, out int firstMove, out int serve);
                Assert.Greater(serve, 0);
                Assert.Greater(firstMove, serve, "球發出去之後接球的人才會開始動");
                shortest = Math.Min(shortest, firstMove - serve);
                longest = Math.Max(longest, firstMove - serve);
            }

            TestContext.WriteLine("ticks from the serve to the first receiver movement: " + shortest + " .. " + longest + " (" + (shortest * Dt).ToString("0.00") + " .. " + (longest * Dt).ToString("0.00") + " s)");
            Assert.GreaterOrEqual(shortest * Dt, 0.15f - Dt, "反應延遲不能短於 0.15 秒");
            Assert.LessOrEqual(shortest * Dt, 0.30f + 2 * Dt, "至少有人在 0.3 秒內反應過來");
            Assert.LessOrEqual(longest * Dt, 0.40f, "不能呆太久");
        }

        [Test]
        public void FallsBackToAFreeBallWhenNoSpikeIsPossible()
        {
            // 己方已經兩觸，球只飄到不到 3 m 高就往下掉（永遠到不了扣球高度）：第三觸要吊過網
            int freeBalls = 0, crossed = 0, runs = 16;
            for (uint seed = 1; seed <= runs; seed++)
            {
                var m = new VolleyballMatch(seed, 1);
                TestUtil.ForceRally(m, new Vec3(-2.6f, 2.0f, 0.4f), new Vec3(0.3f, 1.0f, 0f), 0, 2, 1);
                Controller[] ctl = BotArena.AllBots(seed);
                var inputs = new InputFrame[GameConfig.SlotCount];
                bool free = false, cross = false;
                for (int i = 0; i < 60 * 3 && m.Phase == Phase.Rally; i++)
                {
                    BotArena.Step(m, ctl, inputs);
                    for (int e = 0; e < m.Events.Count; e++)
                    {
                        if (m.Events[e].Kind != EventKind.Hit || m.Events[e].Team != 0) continue;
                        Assert.AreEqual((byte)HitKind.FreeBall, m.Events[e].A);
                        Assert.AreNotEqual(1, m.Events[e].Slot, "上一個觸球的人不能再碰");
                        free = true;
                    }

                    if (free && m.Ball.Pos.X > 0f) cross = true;
                }

                if (free) freeBalls++;
                if (cross) crossed++;
            }

            TestContext.WriteLine("free ball fallback: " + freeBalls + "/" + runs + " hit, " + crossed + " crossed the net");
            Assert.GreaterOrEqual(freeBalls, runs * 3 / 5);
            Assert.GreaterOrEqual(crossed, freeBalls * 3 / 4, "吊球要過網");
        }

        [Test]
        public void NeverDeliberatelyMakesTheFourthTouch()
        {
            for (uint seed = 1; seed <= 10; seed++)
            {
                var m = new VolleyballMatch(seed, 1);
                // 己方已經三觸，球卻還飄在己方半場上空：任何人再碰都是第四觸
                TestUtil.ForceRally(m, new Vec3(-3.0f, 3.0f, 0.5f), new Vec3(-0.5f, 0f, 0f), 0, 3, 1);
                Controller[] ctl = BotArena.AllBots(seed);
                var inputs = new InputFrame[GameConfig.SlotCount];
                PointReason reason = 0;
                for (int i = 0; i < 60 * 4 && m.Phase == Phase.Rally; i++)
                {
                    BotArena.Step(m, ctl, inputs);
                    for (int s = 0; s < 3; s++) Assert.IsFalse(inputs[s].Hit, "第四觸：隊伍 0 的電腦不該按擊球 (slot " + s + ")");
                    for (int e = 0; e < m.Events.Count; e++)
                        if (m.Events[e].Kind == EventKind.Point) reason = (PointReason)m.Events[e].A;
                }

                Assert.AreEqual(PointReason.Landed, reason, "球應該自己掉在地上，而不是被第四觸打斷");
            }
        }

        [Test]
        public void LetsAnOpponentsBallThatWillLandOutFly()
        {
            for (uint seed = 1; seed <= 10; seed++)
            {
                var m = new VolleyballMatch(seed, 1);
                // 隊伍 1 打出的球飛向隊伍 0 後方遠遠的界外
                TestUtil.ForceRally(m, new Vec3(-4.5f, 3.0f, 1.0f), new Vec3(-4.0f, 1.0f, 0.5f), 1, 1, 3);
                Controller[] ctl = BotArena.AllBots(seed);
                var inputs = new InputFrame[GameConfig.SlotCount];
                GameEvent point = default;
                for (int i = 0; i < 60 * 5 && m.Phase == Phase.Rally; i++)
                {
                    BotArena.Step(m, ctl, inputs);
                    for (int s = 0; s < 3; s++) Assert.IsFalse(inputs[s].Hit, "會出界的球不要碰");
                    for (int e = 0; e < m.Events.Count; e++)
                        if (m.Events[e].Kind == EventKind.Point) point = m.Events[e];
                }

                Assert.AreEqual(EventKind.Point, point.Kind);
                Assert.AreEqual((byte)PointReason.Out, point.A);
                Assert.AreEqual(0, point.Team, "對手出界，隊伍 0 得分");
            }
        }

        // ---------------------------------------------------------------- 整場比賽的統計

        static ArenaStats AllBotMatches()
        {
            return BotArena.PlayMany(30, 20240601u, BotArena.AllBots);
        }

        [Test]
        public void AllBotMatchesFinishAndNothingStalls()
        {
            ArenaStats st = AllBotMatches();
            TestContext.WriteLine("ALL BOTS: " + st);
            Assert.AreEqual(0, st.Unfinished, "每場都要打完");
            Assert.Less(st.MaxMatchSeconds, 20 * 60f, "一場比賽要在 20 分鐘（模擬時間）內結束");
            Assert.Less(st.MaxRallySeconds, 40f, "單一回合不能超過 40 秒");
            Assert.Less(st.MaxServeWaitSeconds, 4f, "電腦發球員不能讓發球等待超過 4 秒");
        }

        [Test]
        public void AllBotRalliesLookLikeVolleyball()
        {
            ArenaStats st = AllBotMatches();
            Assert.Greater(st.HitsPerRally, 3.0, "每回合平均擊球數");
            Assert.Less(st.HitsPerRally, 9.0, "每回合平均擊球數");
            Assert.GreaterOrEqual(st.ChainShare, 0.35, "至少 35% 的回合有同隊連續三觸");
            Assert.GreaterOrEqual(st.SpikeShare, 0.15, "至少 15% 的回合有扣球");
            Assert.Less(st.ReasonShare(PointReason.FourTouches), 0.08, "四次觸球失分");
            Assert.Less(st.ReasonShare(PointReason.Out), 0.25, "出界失分");
            Assert.Greater(st.ReasonShare(PointReason.Landed), 0.5, "多數分數應該是球落地");
            Assert.Greater(st.KindHits[(int)HitKind.Spike], 0);
            Assert.Less(st.QuickDoubles, st.TotalHits / 100, "隊友之間不能搶球（同隊兩觸間隔不到 0.4 秒）");
        }

        [Test]
        public void BothTeamsAreEquallyStrong()
        {
            ArenaStats st = BotArena.PlayMany(40, 777u, BotArena.AllBots);
            TestContext.WriteLine("SYMMETRY: " + st);
            Assert.GreaterOrEqual(st.PointShare(0), 0.42);
            Assert.LessOrEqual(st.PointShare(0), 0.58);
        }

        [Test]
        public void ATeamWithAnIdlePlayerStillReturnsBallsButLosesMoreOften()
        {
            for (int idle = 0; idle < 3; idle++)
            {
                int idleSlot = idle;
                // 隊伍 0 有一個閒置球員（不動、不按鍵），隊伍 1 是三個電腦
                ArenaStats st = BotArena.PlayMany(30, 4242u + (uint)idle, s => BotArena.WithIdle(s, idleSlot));
                TestContext.WriteLine("IDLE SLOT " + idleSlot + " (team 0 = 2 bots + 1 idle): " + st + " | idle-team point share " + st.PointShare(0).ToString("P0") + ", serves returned: idle team " + st.ReturnShare(0).ToString("P0") + " vs full team " + st.ReturnShare(1).ToString("P0"));
                Assert.AreEqual(0, st.Unfinished, "不能卡死");
                Assert.Less(st.MaxMatchSeconds, 20 * 60f);
                Assert.Less(st.MaxRallySeconds, 60f);
                Assert.GreaterOrEqual(st.PointShare(0), 0.25, "少一個人仍要贏得相當比例的分數");
                Assert.Less(st.PointShare(0), 0.5, "少一個人應該輸比較多");
                Assert.Less(st.Wins[0], st.Wins[1], "少一個人應該輸比較多場");
                Assert.GreaterOrEqual(st.ReturnShare(0), 0.35, "少一個人仍要把相當比例的發球接回去");
                Assert.Greater(st.ReturnShare(1), st.ReturnShare(0), "三個人的那隊接得比較好");
            }
        }

        [Test]
        public void BotsCooperateWithAnUnpredictableHumanTeammate()
        {
            ArenaStats st = BotArena.PlayMany(20, 99u, seed =>
            {
                Controller[] c = BotArena.AllBots(seed);
                var chaotic = new ChaoticMate(2, seed);
                c[2] = chaotic.Think; // 隊伍 0 的 slot 2 是亂來的人
                return c;
            });
            TestContext.WriteLine("CHAOTIC TEAMMATE (team 0 slot 2): " + st);
            Assert.AreEqual(0, st.Unfinished);
            Assert.Less(st.MaxRallySeconds, 60f);
            Assert.GreaterOrEqual(st.PointShare(0), 0.3, "亂來的隊友不該讓電腦隊友癱瘓");
            Assert.Less(st.ReasonShare(PointReason.FourTouches), 0.15, "別被搶球搞成一直四觸");
        }

        // ---------------------------------------------------------------- 可重現、不修改比賽、任何狀態都能接手

        [Test]
        public void SameSeedsGiveIdenticalMatches()
        {
            ArenaStats a = BotArena.PlayMany(4, 31337u, BotArena.AllBots);
            ArenaStats b = BotArena.PlayMany(4, 31337u, BotArena.AllBots);
            Assert.AreEqual(a.Hash, b.Hash);
            Assert.AreEqual(a.PointsWon[0], b.PointsWon[0]);
            Assert.AreEqual(a.PointsWon[1], b.PointsWon[1]);
            Assert.AreEqual(a.TotalHits, b.TotalHits);

            ArenaStats c = BotArena.PlayMany(4, 31338u, BotArena.AllBots);
            Assert.AreNotEqual(a.Hash, c.Hash, "不同種子應該打出不同的比賽");
        }

        static void Add(StringBuilder sb, params float[] values)
        {
            foreach (float v in values) sb.Append(v.ToString("R")).Append(',');
        }

        /// <summary>把比賽的全部可見狀態（浮點數用完整精度）串成一個字串，用來比對前後有沒有被改動。</summary>
        static string Snapshot(VolleyballMatch m)
        {
            var sb = new StringBuilder();
            foreach (PlayerState p in m.Players)
                Add(sb, p.Pos.X, p.Pos.Y, p.Pos.Z, p.Vel.X, p.Vel.Y, p.Vel.Z, p.SwingTimer, p.HitBuffer, p.HitCooldown);
            Add(sb, m.Ball.Pos.X, m.Ball.Pos.Y, m.Ball.Pos.Z, m.Ball.Vel.X, m.Ball.Vel.Y, m.Ball.Vel.Z, m.PhaseTime);
            sb.Append(m.Phase).Append(m.Score[0]).Append(m.Score[1]).Append(m.ServingTeam).Append(m.ServerSlot);
            sb.Append(m.LastTouchTeam).Append(m.LastToucherSlot).Append(m.TouchCount).Append(m.ServeInFlight).Append(m.Winner).Append(m.Tick).Append(m.Events.Count);
            return sb.ToString();
        }

        [Test]
        public void ThinkNeverMutatesTheMatch()
        {
            var m = new VolleyballMatch(5, 0);
            Controller[] ctl = BotArena.AllBots(5);
            var inputs = new InputFrame[GameConfig.SlotCount];
            var probes = new BotBrain[GameConfig.SlotCount]; // 只用來多叫幾次 Think 看會不會改到比賽
            for (int i = 0; i < probes.Length; i++) probes[i] = new BotBrain(i, 5u);
            var seen = new HashSet<Phase>();
            for (int t = 0; t < 60 * 60 && m.Phase != Phase.GameOver; t++)
            {
                string before = Snapshot(m);
                for (int i = 0; i < probes.Length; i++) probes[i].Think(m, Dt);
                Assert.AreEqual(before, Snapshot(m), "Think 不能改變比賽狀態 (tick " + m.Tick + ", " + m.Phase + ")");
                seen.Add(m.Phase);
                BotArena.Step(m, ctl, inputs);
            }

            Assert.GreaterOrEqual(seen.Count, 4, "測試要涵蓋倒數、等發球、回合、得分暫停");
        }

        [Test]
        public void CanTakeOverAnyStateWithoutWarmUp()
        {
            // 亂數把球與球員擺到各種怪狀態，全新的電腦立刻接手：不能丟例外、輸入要合法
            var rng = new Rng(2024u);
            for (int scenario = 0; scenario < 300; scenario++)
            {
                var m = new VolleyballMatch((uint)scenario + 1u, scenario % 2);
                if (scenario % 3 != 0)
                {
                    for (int i = 0; i < GameConfig.SlotCount; i++)
                    {
                        float side = Court.TeamOf(i) == 0 ? -1f : 1f;
                        m.Players[i].Pos = new Vec3(side * rng.Range(0.6f, 8.5f), 0f, rng.Range(-5f, 5f));
                    }

                    int last = rng.Value01() < 0.5f ? 0 : 1; // 回合進行中一定有人碰過球
                    int count = 1 + (int)(rng.Value01() * 3f);
                    TestUtil.ForceRally(m,
                        new Vec3(rng.Range(-8f, 8f), rng.Range(0.3f, 5f), rng.Range(-4.5f, 4.5f)),
                        new Vec3(rng.Range(-9f, 9f), rng.Range(-6f, 8f), rng.Range(-6f, 6f)),
                        last, count, last * 3 + (int)(rng.Value01() * 3f));
                }

                Controller[] ctl = BotArena.AllBots((uint)scenario);
                var inputs = new InputFrame[GameConfig.SlotCount];
                for (int t = 0; t < 60 * 6; t++)
                {
                    BotArena.Step(m, ctl, inputs);
                    foreach (InputFrame f in inputs)
                    {
                        Assert.GreaterOrEqual(f.Right, -100);
                        Assert.LessOrEqual(f.Right, 100);
                        Assert.GreaterOrEqual(f.Forward, -100);
                        Assert.LessOrEqual(f.Forward, 100);
                    }
                }
            }
        }

        [Test]
        public void ReplacementBotsTakeOverMidMatchWhenAHumanLeaves()
        {
            // 大約每 20 秒把一個隨機槽位換成「全新的」電腦（模擬人類斷線，落在不同的相位）
            var st = new ArenaStats();
            for (uint seed = 1; seed <= 10; seed++)
            {
                Controller[] ctl = BotArena.AllBots(seed);
                var rng = new Rng(seed * 13u);
                uint swaps = 0;
                uint nextSwap = 600;
                BotArena.Play(st, seed, (int)(seed % 2), ctl, 20f, m =>
                {
                    if (m.Tick < nextSwap) return;
                    nextSwap = m.Tick + 1200 + (uint)(rng.Value01() * 300f);
                    int slot = (int)(rng.Value01() * 6f) % 6;
                    ctl[slot] = BotArena.Bot(slot, seed + 1000u + swaps++);
                });
            }

            TestContext.WriteLine("MID-MATCH REPLACEMENTS: " + st);
            Assert.AreEqual(0, st.Unfinished);
            Assert.Less(st.MaxRallySeconds, 40f);
            Assert.Less(st.MaxServeWaitSeconds, 4f);
            Assert.Greater(st.HitsPerRally, 3.0);
            Assert.Less(st.HitsPerRally, 9.0);
        }

        // ---------------------------------------------------------------- 人性化與效能

        [Test]
        public void BotsRunSlightlySlowerThanAHumanAtFullStick()
        {
            var m = new VolleyballMatch(9, 0);
            Controller[] ctl = BotArena.AllBots(9);
            var inputs = new InputFrame[GameConfig.SlotCount];
            float maxMove = 0f;
            for (int t = 0; t < 60 * 120 && m.Phase != Phase.GameOver; t++)
            {
                BotArena.Step(m, ctl, inputs);
                foreach (InputFrame f in inputs)
                {
                    if (f.Hit) continue; // 擊球那一幀的方向是瞄準，不是移動
                    maxMove = Math.Max(maxMove, MathF.Sqrt(f.AxisRight * f.AxisRight + f.AxisForward * f.AxisForward));
                }
            }

            TestContext.WriteLine("largest movement input magnitude used by bots: " + maxMove.ToString("0.00"));
            Assert.Greater(maxMove, 0.8f, "電腦要有在跑");
            Assert.LessOrEqual(maxMove, 0.95f, "電腦的跑速要比人類全速慢一點");
        }

        [Test]
        public void ThinkIsCheap()
        {
            var m = new VolleyballMatch(11, 0);
            var brains = new BotBrain[GameConfig.SlotCount];
            for (int i = 0; i < brains.Length; i++) brains[i] = new BotBrain(i, 11u);
            var inputs = new InputFrame[GameConfig.SlotCount];
            for (int i = 0; i < brains.Length; i++) brains[i].Think(m, Dt); // 暖機（JIT）
            var total = new Stopwatch();
            var rally = new Stopwatch();
            long calls = 0, rallyCalls = 0;
            for (int t = 0; t < 60 * 60 * 5 && m.Phase != Phase.GameOver; t++)
            {
                bool inRally = m.Phase == Phase.Rally;
                Stopwatch sw = inRally ? rally : total;
                sw.Start();
                for (int i = 0; i < brains.Length; i++) inputs[i] = brains[i].Think(m, Dt);
                sw.Stop();
                calls += brains.Length;
                if (inRally) rallyCalls += brains.Length;
                m.Step(inputs, Dt);
            }

            double allUs = (total.Elapsed.TotalMilliseconds + rally.Elapsed.TotalMilliseconds) * 1000.0 / calls;
            double rallyUs = rallyCalls == 0 ? 0 : rally.Elapsed.TotalMilliseconds * 1000.0 / rallyCalls;
            TestContext.WriteLine("Think(): " + allUs.ToString("0.00") + " us per call on average over all phases, " + rallyUs.ToString("0.00") + " us during rallies (" + calls + " calls)");
            Assert.Less(allUs, 30.0, "平均每次 Think 要小於 30 微秒");
            Assert.Less(rallyUs, 30.0);
        }
    }
}
