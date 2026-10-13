using System.Collections.Generic;

namespace Volleyball.Core
{
    /// <summary>
    /// 權威式比賽模擬（只跑在主機／伺服器）：球員移動、擊球、球物理、得分規則、發球輪轉。
    /// 人類與電腦 AI 都只是「每個 tick 餵進來的 InputFrame」，這裡不分辨。
    /// </summary>
    public sealed class VolleyballMatch
    {
        public readonly PlayerState[] Players = new PlayerState[GameConfig.SlotCount];
        public BallState Ball;
        public Phase Phase;
        public float PhaseTime;
        public readonly int[] Score = new int[2];
        public int ServingTeam;
        public int ServerSlot;
        public int LastTouchTeam = -1;
        public int LastToucherSlot = -1;
        public int TouchCount; // LastTouchTeam 連續觸球數
        public bool ServeInFlight; // 發球後、球過網（或對方碰到）之前，發球方不能再碰球
        public int Winner = -1;
        public uint Tick;

        /// <summary>本 tick 產生的事件；每次 Step 開頭會清空。</summary>
        public readonly List<GameEvent> Events = new List<GameEvent>();

        readonly int[] serveIndex = { -1, -1 };
        readonly Rng rng;
        float touchLockout;

        public int BallSide => Ball.Pos.X < 0f ? 0 : 1;

        public VolleyballMatch(uint seed, int firstServingTeam)
        {
            rng = new Rng(seed);
            GainServe(firstServingTeam);
            BeginRally(true);
        }

        public void Step(InputFrame[] inputs, float dt)
        {
            Events.Clear();
            Tick++;
            PhaseTime += dt;
            if (touchLockout > 0f) touchLockout -= dt;

            for (int i = 0; i < Players.Length; i++)
            {
                ref PlayerState p = ref Players[i];
                PlayerMotion.Step(ref p, Court.TeamOf(i), inputs[i], dt);
                if (p.HitCooldown > 0f) p.HitCooldown -= dt;
                if (p.HitBuffer > 0f) p.HitBuffer -= dt;
                if (p.SwingTimer > 0f)
                {
                    p.SwingTimer -= dt;
                }
                else if (inputs[i].Hit)
                {
                    p.SwingTimer = GameConfig.SwingDuration;
                    p.HitBuffer = GameConfig.HitBufferTime;
                }
            }

            switch (Phase)
            {
                case Phase.Countdown:
                    HoldBall();
                    if (PhaseTime >= GameConfig.CountdownTime) EnterServeWait();
                    break;

                case Phase.ServeWait:
                    HoldBall();
                    if (Players[ServerSlot].HitBuffer > 0f) DoServe(inputs[ServerSlot]);
                    else if (PhaseTime >= GameConfig.ServeTimeout) DoServe(default);
                    break;

                case Phase.Rally:
                    TryResolveHit(inputs);
                    if (Phase == Phase.Rally) StepBallInRally(dt);
                    break;

                case Phase.PointPause:
                    BallPhysics.Step(ref Ball, dt);
                    if (PhaseTime >= GameConfig.PointPauseTime) BeginRally(false);
                    break;

                case Phase.GameOver:
                    BallPhysics.Step(ref Ball, dt);
                    break;
            }
        }

        void StepBallInRally(float dt)
        {
            BallStepResult r = BallPhysics.Step(ref Ball, dt);
            if (r.CrossedNet) ServeInFlight = false;
            if (r.HitNet) AddEvent(EventKind.NetTouch, 0, -1, 0, 0, Ball.Pos);
            if (r.Landed) OnLanded(r.LandPos);
        }

        void TryResolveHit(InputFrame[] inputs)
        {
            if (touchLockout > 0f) return;

            int best = -1;
            float bestDist = float.MaxValue;
            int ballSide = BallSide;
            for (int i = 0; i < Players.Length; i++)
            {
                ref PlayerState p = ref Players[i];
                if (p.HitBuffer <= 0f || p.HitCooldown > 0f) continue;

                int team = Court.TeamOf(i);
                if (team != ballSide) continue; // 不能伸手過網
                if (ServeInFlight && team == ServingTeam) continue;

                float dist = Vec3.DistanceXZ(p.Pos, Ball.Pos);
                float dy = Ball.Pos.Y - p.Pos.Y;
                if (dist > GameConfig.ReachXZ || dy < -0.05f || dy > GameConfig.ReachUp) continue;

                if (team == LastTouchTeam && i == LastToucherSlot)
                {
                    p.HitBuffer = 0f; // 不能連續觸球：這次按鍵作廢
                    AddEvent(EventKind.DoubleTouch, (byte)team, (sbyte)i, 0, 0, Ball.Pos);
                    continue;
                }

                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = i;
                }
            }

            if (best >= 0) ApplyHit(best, bestDist, inputs[best]);
        }

        void ApplyHit(int slot, float dist, InputFrame input)
        {
            int team = Court.TeamOf(slot);
            int touches = LastTouchTeam == team ? TouchCount + 1 : 1;
            if (touches > 3)
            {
                ScorePoint(1 - team, PointReason.FourTouches);
                return;
            }

            if (team != LastTouchTeam) ServeInFlight = false;

            ref PlayerState p = ref Players[slot];
            HitKind kind = HitSolver.Classify(p, Ball, touches - 1);
            Ball.Vel = HitSolver.Solve(kind, team, Ball.Pos, input.AxisRight, input.AxisForward,
                Mathx.Clamp01(dist / GameConfig.ReachXZ), rng);

            LastTouchTeam = team;
            LastToucherSlot = slot;
            TouchCount = touches;
            p.HitCooldown = GameConfig.HitCooldown;
            touchLockout = GameConfig.BallLockout;
            // 同隊其他人這時按的鍵也作廢：否則鎖定時間一過，沒被選中的人會「補打」第二觸（甚至第四觸犯規）
            for (int i = team * GameConfig.PlayersPerTeam; i < (team + 1) * GameConfig.PlayersPerTeam; i++)
                Players[i].HitBuffer = 0f;
            AddEvent(EventKind.Hit, (byte)team, (sbyte)slot, (byte)kind, (byte)touches, Ball.Pos);
        }

        void DoServe(InputFrame input)
        {
            int team = ServingTeam;
            ref PlayerState p = ref Players[ServerSlot];
            Ball.Vel = HitSolver.Solve(HitKind.Serve, team, Ball.Pos, input.AxisRight, input.AxisForward, 0f, rng);

            LastTouchTeam = team;
            LastToucherSlot = ServerSlot;
            TouchCount = 1;
            ServeInFlight = true;
            p.HitBuffer = 0f;
            p.HitCooldown = GameConfig.HitCooldown;
            touchLockout = GameConfig.BallLockout;
            Phase = Phase.Rally;
            PhaseTime = 0f;
            AddEvent(EventKind.Hit, (byte)team, (sbyte)ServerSlot, (byte)HitKind.Serve, 1, Ball.Pos);
        }

        void OnLanded(Vec3 pos)
        {
            int side = pos.X < 0f ? 0 : 1;
            bool inBounds = System.MathF.Abs(pos.X) <= GameConfig.HalfLength + GameConfig.BallRadius
                            && System.MathF.Abs(pos.Z) <= GameConfig.HalfWidth + GameConfig.BallRadius;
            AddEvent(EventKind.Landed, (byte)side, -1, (byte)(inBounds ? 1 : 0), 0, pos);

            if (inBounds) ScorePoint(1 - side, PointReason.Landed);
            else ScorePoint(1 - LastTouchTeam, PointReason.Out);
        }

        void ScorePoint(int winner, PointReason reason)
        {
            Score[winner]++;
            AddEvent(EventKind.Point, (byte)winner, -1, (byte)reason, 0, Ball.Pos);
            PhaseTime = 0f;

            if (Score[winner] >= GameConfig.WinScore && Score[winner] - Score[1 - winner] >= GameConfig.WinMargin)
            {
                Winner = winner;
                Phase = Phase.GameOver;
                AddEvent(EventKind.GameOver, (byte)winner, -1, 0, 0, Ball.Pos);
                return;
            }

            if (winner != ServingTeam) GainServe(winner);
            Phase = Phase.PointPause;
        }

        void GainServe(int team)
        {
            ServingTeam = team;
            serveIndex[team]++;
        }

        void BeginRally(bool countdown)
        {
            ServerSlot = ServingTeam * GameConfig.PlayersPerTeam + serveIndex[ServingTeam] % GameConfig.PlayersPerTeam;
            for (int i = 0; i < Players.Length; i++)
            {
                int team = Court.TeamOf(i);
                Players[i] = new PlayerState
                {
                    Pos = i == ServerSlot ? Court.ServePos(team) : Court.FormationPos(team, i % GameConfig.PlayersPerTeam),
                };
            }

            LastTouchTeam = -1;
            LastToucherSlot = -1;
            TouchCount = 0;
            ServeInFlight = false;
            touchLockout = 0f;
            Phase = Phase.Countdown;
            PhaseTime = 0f;
            HoldBall();
            if (!countdown) EnterServeWait();
        }

        void EnterServeWait()
        {
            Phase = Phase.ServeWait;
            PhaseTime = 0f;
            AddEvent(EventKind.ServeReady, (byte)ServingTeam, (sbyte)ServerSlot, 0, 0, Ball.Pos);
        }

        void HoldBall()
        {
            Vec3 hand = Players[ServerSlot].Pos + Court.Forward(ServingTeam) * 0.45f;
            Ball.Pos = new Vec3(hand.X, hand.Y + 1.5f, hand.Z);
            Ball.Vel = default;
        }

        void AddEvent(EventKind kind, byte team, sbyte slot, byte a, byte b, Vec3 pos)
        {
            Events.Add(new GameEvent { Kind = kind, Team = team, Slot = slot, A = a, B = b, Pos = pos });
        }
    }
}
