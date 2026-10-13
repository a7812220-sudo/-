using System;

namespace Volleyball.Core
{
    /// <summary>
    /// 電腦球員的大腦：一個 BotBrain 控制一個槽位。每個 tick 只「讀」比賽狀態、只產生一個 InputFrame，
    /// 跟人類一樣交給 VolleyballMatch.Step 去生效（不修改比賽）。
    /// 做法：用 BallPhysics 的拷貝預測球軌跡 → 各隊友算「誰最快趕到」決定誰負責 → 負責的人跑位、時機到了按擊球
    /// （擊球瞬間按住的方向 = 瞄準）；其他人補位，第一球後前往網前準備進攻。第三觸優先扣球（算好起跳時機），做不到就吊球。
    /// 只用可觀察的狀態（位置、觸球數、最後觸球者、揮臂狀態），所以隊友是人類或斷線閒置的人也能搭配。
    /// 同一個 (slot, seed) 在同一場比賽裡的行為完全可重現（只用 Rng，不用系統亂數）。
    /// </summary>
    public sealed class BotBrain
    {
        // ---- 難度旋鈕（只有一個預設：休閒玩家）----
        const float RunScale = 0.92f; // 搖桿最多推到這個比例：比人類全速慢一點
        const float RunSpeed = GameConfig.MoveSpeed * RunScale;
        const float ReactMin = 0.15f, ReactMax = 0.30f; // 對手擊球後多久才「看清楚」
        const int GapCandidates = 5; // 找空檔時隨機試幾個落點挑最好的（越大越準）
        const float AimNoise = 0.35f; // 落點誤差（公尺）
        const float BlunderBase = 0.08f; // 一球完全判斷錯誤、沒有出手的機率
        const float BlunderStretch = 0.45f; // 勉強才趕得到的球（時間餘裕不足）額外的失誤機率
        const float StretchSlack = 0.30f; // 餘裕少於這個秒數算勉強
        const float TiredPerHit = 0.03f; // 單一回合超過 14 次擊球後，每多一次失誤機率增加這麼多（避免無止境的對打）

        // ---- 常數 ----
        const int Horizon = 200; // 球軌跡取樣數（3.3 秒）
        const float JumpLead = GameConfig.JumpSpeed / GameConfig.PlayerGravity; // 起跳到跳躍頂點的時間
        const float SpikeBallY = GameConfig.SpikeMinBallY + 0.05f; // 扣球判定高度（加一點餘裕）
        const float SpikeAimY = GameConfig.SpikeMinBallY + 0.3f; // 計畫在球掉到這個高度時扣
        const float PrefHitY = 1.3f; // 地面接球偏好的球高度
        const float ReachD = GameConfig.ReachXZ - 0.15f; // 水平／垂直都留一點餘裕，才不會按了卻差一點構不到
        const float ReachDy = GameConfig.ReachUp - 0.1f;
        const float GroundMaxY = GameConfig.ReachUp - 0.2f; // 站著接球時，球要降到這個高度以下才算

        readonly int slot, team;
        readonly float fwdX, rightZ;
        readonly Rng rng;

        // 球軌跡快取：traj[head + i] = i 個 tick 之後的球心；球被打到（偏離預測）才重算
        readonly Vec3[] traj = new Vec3[Horizon + 1];
        int trajLen, head;
        uint trajTick;
        bool trajLanded;

        uint reactTick; // 這個 tick 之前還沒「反應過來」，維持原本動作
        float serveDelay = -1f;
        float hitY = PrefHitY, goodD = 0.45f, jumpJitter; // 每次有人擊球後重抽的小變化
        bool blunder, rolled; // 這一球我會不會失誤（第一次輪到我負責時才擲骰）
        int rallyHits;
        float slack; // Evaluate 對「我自己」算出的時間餘裕
        int prevResp = -1, highResp = -1; // 上一個 tick／球還高的時候，我認為誰負責
        readonly bool[] flaky = new bool[GameConfig.SlotCount]; // 隊友連兩球該接卻站著不動 → 當他不存在，直到他又有動作
        readonly int[] strikes = new int[GameConfig.SlotCount];
        bool struck; // 這一球已經記過一次
        InputFrame held;

        public BotBrain(int slot, uint seed)
        {
            this.slot = slot;
            team = Court.TeamOf(slot);
            fwdX = Court.Forward(team).X;
            rightZ = Court.Right(team).Z;
            rng = new Rng(seed * 2654435761u + (uint)slot * 40503u + 1u);
            for (int i = 0; i < 4; i++) rng.NextUInt(); // xorshift 的前幾個輸出跟種子太像，先丟掉
        }

        /// <summary>決定這個 tick 的輸入。必須在 VolleyballMatch.Step 之前呼叫；不會修改 m。</summary>
        public InputFrame Think(VolleyballMatch m, float dt)
        {
            switch (m.Phase)
            {
                case Phase.ServeWait: return ThinkServe(m);
                case Phase.Rally: return ThinkRally(m, dt);
                default:
                    prevResp = highResp = -1;
                    serveDelay = -1f;
                    rallyHits = 0;
                    return default;
            }
        }

        // ---------------------------------------------------------------- 發球

        InputFrame ThinkServe(VolleyballMatch m)
        {
            rallyHits = 0;
            if (m.ServerSlot != slot) return default;
            if (serveDelay < 0f) serveDelay = rng.Range(0.7f, 1.8f); // 像人一樣拖一下再發
            if (m.PhaseTime < serveDelay) return default;
            serveDelay = -1f;

            PickGap(m, 2.8f, 5.4f, 3.0f, rng.Value01() < 0.2f ? 1 : GapCandidates, out float bd, out float bl); // 偶爾亂發
            return new InputFrame
            {
                Hit = true,
                Right = InputFrame.Quantize(bl / 3.0f),
                Forward = InputFrame.Quantize((5.6f - bd) / 1.8f - 1f), // Serve 的深度映射見 HitSolver.Solve
            };
        }

        /// <summary>在對場的矩形範圍內隨機試 tries 個落點，挑離最近對手最遠的（空檔）。</summary>
        void PickGap(VolleyballMatch m, float dMin, float dMax, float latMax, int tries, out float depth, out float lat)
        {
            depth = (dMin + dMax) * 0.5f;
            lat = 0f;
            float best = -1f;
            int o = (1 - team) * GameConfig.PlayersPerTeam;
            for (int c = 0; c < tries; c++)
            {
                float d = rng.Range(dMin, dMax), l = rng.Range(-latMax, latMax);
                float tx = fwdX * d, tz = rightZ * l, min = float.MaxValue;
                for (int i = 0; i < GameConfig.PlayersPerTeam; i++)
                {
                    Vec3 p = m.Players[o + i].Pos;
                    min = MathF.Min(min, MathF.Sqrt((p.X - tx) * (p.X - tx) + (p.Z - tz) * (p.Z - tz)));
                }

                if (min > best) { best = min; depth = d; lat = l; }
            }
        }

        // ---------------------------------------------------------------- 回合

        InputFrame ThinkRally(VolleyballMatch m, float dt)
        {
            ref PlayerState me = ref m.Players[slot];

            for (int e = 0; e < m.Events.Count; e++) // 上一個 tick 發生的事
            {
                GameEvent ev = m.Events[e];
                if (ev.Kind == EventKind.NetTouch) trajLen = 0;
                if (ev.Kind != EventKind.Hit) continue;
                trajLen = 0; // 球被打了，重算軌跡
                rallyHits++;
                flaky[ev.Slot] = false;
                strikes[ev.Slot] = 0;
                struck = false;
                prevResp = highResp = -1;
                hitY = rng.Range(0.9f, 1.6f);
                goodD = rng.Range(0.3f, 0.7f);
                jumpJitter = rng.Range(-0.04f, 0.04f);
                rolled = false;
                blunder = false;
                if (ev.Team != team) reactTick = m.Tick + (uint)(rng.Range(ReactMin, ReactMax) / dt);
            }

            if (reactTick > m.Tick + 60) reactTick = 0; // 換了一場新比賽（Tick 歸零）
            if (m.Tick < reactTick) // 還沒看清對手那一球：維持原本動作
            {
                held.Jump = false;
                held.Hit = false;
                return held;
            }

            for (int k = 0; k < GameConfig.PlayersPerTeam; k++) // 隊友有在揮臂或跳 = 還活著
            {
                int j = team * GameConfig.PlayersPerTeam + k;
                if (m.Players[j].SwingTimer > 0f || m.Players[j].Pos.Y > 0.05f) { flaky[j] = false; strikes[j] = 0; }
            }

            UpdateTrajectory(m, dt);
            int n = trajLen - head;
            Vec3 end = traj[trajLen - 1];
            int touches = m.LastTouchTeam == team ? m.TouchCount : 0; // 己方已用掉幾觸

            // 這球最後會落在我方、我方還能碰、而且不是明顯出界（對手的出界球讓它飛）才去接
            bool clearlyOut = MathF.Abs(end.X) > GameConfig.HalfLength + 0.4f || MathF.Abs(end.Z) > GameConfig.HalfWidth + 0.4f;
            bool chase = touches < 3 && !(m.ServeInFlight && m.ServingTeam == team)
                         && SideOf(end) == team && !(clearlyOut && m.LastTouchTeam != team);

            bool iAm = false, spike = false;
            int ci = 0;
            if (chase)
            {
                // 每個能碰球的隊友算「攔球分數」，分數最小的負責；每個 bot 用同一套算法，所以彼此結論一致
                float bestKey = float.MaxValue, mySlack = 0f;
                int resp = -1;
                for (int k = 0; k < GameConfig.PlayersPerTeam; k++)
                {
                    int j = team * GameConfig.PlayersPerTeam + k;
                    if ((m.LastTouchTeam == team && j == m.LastToucherSlot) || flaky[j]) continue; // 不能連續觸球／當他不存在
                    float key = Evaluate(m, j, touches, dt, out int cj, out bool sj);
                    if (j == slot) { ci = cj; spike = sj; mySlack = slack; }
                    if (j == prevResp && key >= 0f) key -= 0.12f; // 黏性（對所有人一致，否則兩人會同時覺得自己負責）
                    if (key < bestKey) { bestKey = key; resp = j; }
                }

                iAm = resp == slot;
                prevResp = resp;
                if (m.Ball.Pos.Y > 0.8f) highResp = resp;
                else if (m.Ball.Pos.Y < 0.42f && highResp >= 0 && highResp != slot && !struck)
                {
                    // 球快落地了，我以為會接的隊友卻站著不動、連揮臂都沒有：記一次，兩次就別再指望他
                    struck = true;
                    PlayerState r = m.Players[highResp];
                    bool still = r.Vel.X * r.Vel.X + r.Vel.Z * r.Vel.Z < 0.25f;
                    if (still && r.SwingTimer <= 0f && r.HitBuffer <= 0f && ++strikes[highResp] >= 2) flaky[highResp] = true;
                }

                if (iAm && !rolled)
                {
                    rolled = true;
                    float p = BlunderBase + (mySlack < StretchSlack ? BlunderStretch : 0f) + TiredPerHit * MathF.Max(0f, rallyHits - 14);
                    blunder = rng.Value01() < p;
                }
            }

            InputFrame f;
            if (iAm)
            {
                Vec3 target = traj[head + ci];
                f = Move(me.Pos, target);
                if (spike && me.Pos.Y <= 0f && ci * dt <= JumpLead + jumpJitter && Dist(me.Pos, target) < 1.0f)
                    f.Jump = true; // 起跳時機：讓跳躍頂點剛好碰上球
                if (!blunder && ShouldPress(m, ref me, dt, n))
                {
                    f.Hit = true;
                    Aim(m, ref me, touches, ref f);
                }
            }
            else
            {
                f = Move(me.Pos, SupportSpot(m, touches, chase, end));
            }

            held = f;
            return f;
        }

        void UpdateTrajectory(VolleyballMatch m, float dt)
        {
            uint off = m.Tick - trajTick;
            if (trajLen > 0 && m.Tick >= trajTick && off < (uint)trajLen
                && MathF.Abs(traj[off].X - m.Ball.Pos.X) < 0.02f && MathF.Abs(traj[off].Y - m.Ball.Pos.Y) < 0.02f
                && MathF.Abs(traj[off].Z - m.Ball.Pos.Z) < 0.02f && (trajLanded || trajLen - (int)off > 80))
            {
                head = (int)off;
                return;
            }

            BallState b = m.Ball;
            head = 0;
            trajTick = m.Tick;
            trajLanded = false;
            trajLen = 1;
            traj[0] = b.Pos;
            while (trajLen < traj.Length)
            {
                BallStepResult r = BallPhysics.Step(ref b, dt);
                traj[trajLen++] = r.Landed ? r.LandPos : b.Pos;
                if (r.Landed) { trajLanded = true; break; }
            }
        }

        // ---------------------------------------------------------------- 誰負責

        /// <summary>
        /// 球員 j 的「攔球分數」（越小越適合負責）與攔截點 ci（球軌跡的第 ci 個取樣）。
        /// 第三觸優先評估扣球：球從高點掉到 SpikeAimY 時，能否站好並在起跳時機跳起。
        /// 分數：能扣球 = 0..2；來得及在球降到適合高度前趕到 = 5 + 所需時間；趕不到 = 15 + 遲到秒數；已經在空中的扣球手 = -1。
        /// </summary>
        float Evaluate(VolleyballMatch m, int j, int touches, float dt, out int ci, out bool spike)
        {
            PlayerState p = m.Players[j];
            int n = trajLen - head;
            ci = 0;
            spike = false;
            slack = 0f;

            if (touches == 2)
            {
                if (p.Pos.Y > 0.3f && Dist(p.Pos, traj[head]) < 2.5f) { spike = true; slack = 1f; return -1f; } // 已經在球附近起跳的人
                for (int i = 1; i < n; i++)
                {
                    if (!(traj[head + i - 1].Y > SpikeAimY && traj[head + i].Y <= SpikeAimY)) continue;
                    Vec3 b = traj[head + i];
                    float tc = i * dt;
                    if (SideOf(b) == team && MathF.Abs(b.X) < 3.3f && tc > JumpLead - 0.1f)
                    {
                        float run = MathF.Max(0f, Dist(p.Pos, b) - 0.5f) / RunSpeed;
                        if (run + JumpLead <= tc + 0.05f) { ci = i; spike = true; slack = tc - run - JumpLead; return run; }
                    }

                    break; // 這球扣不到：改用地面吊球
                }
            }

            // 地面接球：找第一個「來得及」而且高度合適（只算下降中的球）的取樣；沒有就取最後一個來得及的；全都來不及就取最不遲到的
            int feas = -1, late = n - 1;
            float feasNeed = 0f, bestLate = float.MaxValue;
            for (int i = 1; i < n; i++)
            {
                Vec3 b = traj[head + i];
                if (b.Y > GroundMaxY || b.Y < 0.3f || SideOf(b) != team || b.Y >= traj[head + i - 1].Y) continue;
                float need = MathF.Max(MathF.Max(0f, Dist(p.Pos, b) - 0.25f) / RunSpeed + 0.08f, p.HitCooldown);
                float t = i * dt;
                if (need <= t)
                {
                    feas = i;
                    feasNeed = need;
                    if (b.Y <= PrefHitY) break;
                }
                else if (need - t < bestLate)
                {
                    bestLate = need - t;
                    late = i;
                }
            }

            if (feas >= 0) { ci = feas; slack = feas * dt - feasNeed; return 5f + feasNeed; }
            ci = late;
            slack = -bestLate;
            return 15f + (bestLate == float.MaxValue ? 0f : bestLate);
        }

        // ---------------------------------------------------------------- 擊球

        /// <summary>要不要在這個 tick 按擊球：球在範圍內，而且是好時機（夠近且高度合適，或再不打就錯過）。</summary>
        bool ShouldPress(VolleyballMatch m, ref PlayerState me, float dt, int n)
        {
            if (me.SwingTimer > 0f || me.HitCooldown > 0.05f) return false; // 揮臂中按了也沒用
            Vec3 b = m.Ball.Pos;
            float px = me.Pos.X + me.Vel.X * dt, pz = me.Pos.Z + me.Vel.Z * dt; // 伺服器先移動球員再判定擊球
            float d = Dist2(px, pz, b.X, b.Z);
            float dy = b.Y - me.Pos.Y;
            if (d > ReachD || dy < 0f || dy > ReachDy || m.Ball.Vel.Y > 0f || SideOf(b) != team) return false; // 球還在上升（剛被打出去）、或還在網的另一邊，都不打

            Vec3 b1 = n > 1 ? traj[head + 1] : b;
            bool leaving = Dist2(px, pz, b1.X, b1.Z) > d + 0.004f || b1.Y < 0.45f; // 再等就錯過
            bool spikeZone = me.Pos.Y > 0.3f && b.Y >= SpikeBallY;
            return leaving || (d <= goodD && (spikeZone || b.Y <= hitY));
        }

        /// <summary>依這一觸的種類，把「瞄準」寫進 f 的移動軸（落點映射見 HitSolver.Solve）。</summary>
        void Aim(VolleyballMatch m, ref PlayerState me, int touches, ref InputFrame f)
        {
            float aimR, aimF;
            bool spike = me.Pos.Y > 0.3f && m.Ball.Pos.Y >= SpikeBallY; // 伺服器也會判成 Spike
            if (spike || touches >= 2)
            {
                // 扣球／吊球：瞄對場離三名對手最遠的空檔
                PickGap(m, 2.0f, 5.3f, 2.9f, GapCandidates, out float bd, out float bl);
                aimR = (bl + rng.Range(-AimNoise, AimNoise)) / (spike ? 3.1f : 2.8f);
                aimF = (3.8f - bd - rng.Range(-AimNoise, AimNoise)) / (spike ? 2.0f : 1.8f);
            }
            else if (touches == 1)
            {
                // 二傳：托給最適合進攻的隊友（離網前攻擊區最近的人），落點對準他的側向位置
                int a = BestMate(m, 1.6f, 2.2f);
                float l = a < 0 ? 0f : Mathx.Clamp(Lat(m.Players[a].Pos), -2.2f, 2.2f);
                aimR = (l + rng.Range(-AimNoise, AimNoise)) / 2.6f;
                aimF = rng.Range(-0.3f, 0.5f);
            }
            else
            {
                // 接球：送向最適合二傳的隊友（網前、偏他那一側）
                int s = BestMate(m, 2.2f, 1.4f);
                float l = s < 0 ? 0f : Mathx.Clamp(Lat(m.Players[s].Pos), -1.4f, 1.4f);
                aimR = (l + rng.Range(-AimNoise, AimNoise)) / 1.6f;
                aimF = rng.Range(-0.2f, 0.8f);
            }

            f.Right = InputFrame.Quantize(aimR);
            f.Forward = InputFrame.Quantize(aimF);
        }

        /// <summary>除了自己以外，走到「網前 depth、他自己的側向（限制在 ±maxLat）」最快的隊友；沒有回傳 -1。</summary>
        int BestMate(VolleyballMatch m, float depth, float maxLat)
        {
            int best = -1;
            float bestT = float.MaxValue;
            for (int k = 0; k < GameConfig.PlayersPerTeam; k++)
            {
                int j = team * GameConfig.PlayersPerTeam + k;
                if (j == slot || flaky[j]) continue;
                Vec3 p = m.Players[j].Pos;
                float t = Dist(p, Own(depth, Mathx.Clamp(Lat(p), -maxLat, maxLat))) + rng.Range(0f, 0.8f); // 加一點隨機：不總是托給同一個人
                if (t < bestT) { bestT = t; best = j; }
            }

            return best;
        }

        // ---------------------------------------------------------------- 站位與移動

        /// <summary>不負責這球時的位置：回到站位（朝球側移一點）；己方剛接完第一球時，前往網前準備進攻。</summary>
        Vec3 SupportSpot(VolleyballMatch m, int touches, bool chase, Vec3 end)
        {
            Vec3 f = Court.FormationPos(team, slot % GameConfig.PlayersPerTeam);
            float lat = Lat(f);
            if (touches == 1 && chase)
            {
                float side = lat != 0f ? MathF.Sign(lat) : (Lat(m.Players[slot].Pos) >= 0f ? 1f : -1f);
                return Own(2.2f, side * 2.0f);
            }

            Vec3 focus = chase ? end : m.Ball.Pos;
            return Own(Depth(f), lat + Mathx.Clamp(Lat(focus) * 0.35f, -1.1f, 1.1f));
        }

        InputFrame Move(Vec3 from, Vec3 to)
        {
            float dx = to.X - from.X, dz = to.Z - from.Z;
            float d = MathF.Sqrt(dx * dx + dz * dz);
            if (d < 0.03f) return default;
            float s = RunScale * MathF.Min(1f, d / 0.4f) / d; // 靠近目標時減速
            return new InputFrame
            {
                Right = InputFrame.Quantize(dz * s * rightZ),
                Forward = InputFrame.Quantize(dx * s * fwdX),
            };
        }

        // ---------------------------------------------------------------- 座標小工具（隊伍視角）

        int SideOf(Vec3 p) => p.X < 0f ? 0 : 1;
        float Depth(Vec3 p) => -fwdX * p.X; // 離網距離（己方為正）
        float Lat(Vec3 p) => rightZ * p.Z; // 向右為正
        Vec3 Own(float depth, float lat) => new Vec3(-fwdX * depth, 0f, rightZ * lat);
        static float Dist(Vec3 a, Vec3 b) => Vec3.DistanceXZ(a, b);
        static float Dist2(float ax, float az, float bx, float bz) => MathF.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));
    }
}
