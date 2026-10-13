namespace Volleyball.Core
{
    public enum Phase : byte
    {
        Countdown = 1,
        ServeWait = 2,
        Rally = 3,
        PointPause = 4,
        GameOver = 5,
    }

    public enum HitKind : byte
    {
        Serve = 1,
        Bump = 2, // 第一觸：接球，送往己方二傳區
        Set = 3, // 第二觸：托球，送往網前讓人扣球
        Spike = 4, // 空中、球高於網：用力扣向對場
        FreeBall = 5, // 第三觸（或不適合扣球時）：高吊過網
    }

    public enum PointReason : byte
    {
        Landed = 1, // 球在界內落地（落地方輸）
        Out = 2, // 球出界（最後觸球方輸）
        FourTouches = 3, // 同隊第 4 次觸球
    }

    public enum EventKind : byte
    {
        Hit = 1, // Team, Slot, A=HitKind, B=該隊連續觸球數
        NetTouch = 2, // Pos
        Landed = 3, // Team=落地所在半場, A=1 界內 / 0 界外, Pos
        Point = 4, // Team=得分方, A=PointReason
        ServeReady = 5, // Team=發球方, Slot=發球員
        GameOver = 6, // Team=獲勝方
        DoubleTouch = 7, // Slot=想連續觸球的人（被忽略）
    }

    /// <summary>
    /// 單一 tick 的輸入。Right/Forward 為隊伍視角（Forward = 朝網），單位 1/100，範圍 -100..100。
    /// Jump / Hit 是邊緣觸發：只在「按下」的那一幀為 true。
    /// </summary>
    public struct InputFrame
    {
        public uint Seq;
        public sbyte Right;
        public sbyte Forward;
        public bool Jump;
        public bool Hit;

        public float AxisRight => Right / 100f;
        public float AxisForward => Forward / 100f;

        public static sbyte Quantize(float v) => (sbyte)System.MathF.Round(Mathx.Clamp(v, -1f, 1f) * 100f);
    }

    public struct PlayerState
    {
        public Vec3 Pos; // 腳底位置
        public Vec3 Vel;
        public float SwingTimer; // > 0：揮臂中
        public float HitBuffer; // > 0：擊球意圖仍有效
        public float HitCooldown;
    }

    public struct BallState
    {
        public Vec3 Pos; // 球心
        public Vec3 Vel;
    }

    public struct GameEvent
    {
        public EventKind Kind;
        public byte Team;
        public sbyte Slot;
        public byte A;
        public byte B;
        public Vec3 Pos;
    }
}
