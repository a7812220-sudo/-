namespace Volleyball.Core
{
    /// <summary>
    /// 全部遊戲常數集中在這裡（單位：公尺、秒）。
    /// 球網在 X=0；隊伍 0 在 X&lt;0，隊伍 1 在 X&gt;0。隊伍 0 = 槽位 0..2，隊伍 1 = 槽位 3..5。
    /// </summary>
    public static class GameConfig
    {
        // 時間
        public const float TickRate = 60f;
        public const float TickDt = 1f / TickRate;
        public const int SnapshotInterval = 2; // 每 2 個 tick 送一次快照（30 Hz）

        // 球場
        public const float HalfLength = 6f;
        public const float HalfWidth = 3.5f;
        public const float NetTop = 2.24f;
        public const float OutMargin = 3f; // 球員可以跑到界外多遠
        public const float NetKeepOut = 0.5f; // 球員離網的最小距離

        // 球員
        public const float MoveSpeed = 5.2f;
        public const float GroundAccel = 50f;
        public const float AirAccel = 16f;
        public const float JumpSpeed = 6.8f;
        public const float PlayerGravity = 22f;

        // 擊球
        public const float ReachXZ = 1.25f; // 水平可觸及半徑
        public const float ReachUp = 2.4f; // 腳底往上可觸及高度
        public const float HitBufferTime = 0.2f; // 提早按擊球鍵，球進入範圍時仍會觸發
        public const float SwingDuration = 0.35f;
        public const float HitCooldown = 0.45f; // 同一人兩次擊球的最短間隔
        public const float BallLockout = 0.15f; // 任何人擊球後，球在這段時間內不能再被碰
        public const float SpikeMinBallY = 2.7f;
        public const float SpikeMinPlayerY = 0.25f;
        public const float SpikeMaxNetDist = 3.5f;
        public const float AimErrorMax = 1.3f; // 勉強構到球時的最大落點誤差
        public const float NetClearance = 0.3f; // 越網時球心至少高於「網頂 + 球半徑」這麼多
        public const float SpikeNetClearance = 0.08f; // 扣球可以貼網而過

        // 球
        public const float BallRadius = 0.2f;
        public const float BallGravity = 8.5f;
        public const float NetRestitution = 0.3f;
        public const float FloorRestitution = 0.55f;

        // 比賽
        public const int WinScore = 15;
        public const int WinMargin = 2;
        public const float CountdownTime = 3f;
        public const float PointPauseTime = 2.5f;
        public const float ServeTimeout = 12f;
        public const float ResultsTime = 10f;

        public const int PlayersPerTeam = 3;
        public const int SlotCount = PlayersPerTeam * 2;
    }
}
