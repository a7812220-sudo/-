using System;
using Volleyball.Core;

namespace Volleyball.Tests
{
    static class TestUtil
    {
        public const float Dt = GameConfig.TickDt;

        public static InputFrame[] Idle() => new InputFrame[GameConfig.SlotCount];

        public static int Ticks(float seconds) => (int)(seconds / Dt + 0.5f);

        public static void Run(VolleyballMatch m, InputFrame[] inputs, float seconds)
        {
            int n = Ticks(seconds);
            for (int i = 0; i < n; i++) m.Step(inputs, Dt);
        }

        /// <summary>一路跑到 cond 成立（每個 tick 檢查），回傳是否在 maxSeconds 內成立。</summary>
        public static bool RunUntil(VolleyballMatch m, InputFrame[] inputs, Func<bool> cond, float maxSeconds)
        {
            int n = Ticks(maxSeconds);
            for (int i = 0; i < n; i++)
            {
                m.Step(inputs, Dt);
                if (cond()) return true;
            }

            return false;
        }

        /// <summary>建立比賽並跑完倒數，停在等待發球。</summary>
        public static VolleyballMatch NewServeWait(uint seed = 1, int firstServingTeam = 0)
        {
            var m = new VolleyballMatch(seed, firstServingTeam);
            Run(m, Idle(), GameConfig.CountdownTime + 0.1f);
            return m;
        }

        /// <summary>直接把比賽擺成「回合進行中」的指定狀態（供規則測試使用）。</summary>
        public static void ForceRally(VolleyballMatch m, Vec3 ballPos, Vec3 ballVel, int lastTouchTeam, int touchCount, int lastToucherSlot)
        {
            m.Phase = Phase.Rally;
            m.PhaseTime = 0f;
            m.Ball.Pos = ballPos;
            m.Ball.Vel = ballVel;
            m.LastTouchTeam = lastTouchTeam;
            m.TouchCount = touchCount;
            m.LastToucherSlot = lastToucherSlot;
            m.ServeInFlight = false;
        }

        public static bool HasEvent(VolleyballMatch m, EventKind kind)
        {
            foreach (var e in m.Events)
                if (e.Kind == kind)
                    return true;
            return false;
        }

        public static GameEvent GetEvent(VolleyballMatch m, EventKind kind)
        {
            foreach (var e in m.Events)
                if (e.Kind == kind)
                    return e;
            throw new InvalidOperationException("本 tick 沒有事件 " + kind);
        }
    }
}
