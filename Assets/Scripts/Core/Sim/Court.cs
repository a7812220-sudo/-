namespace Volleyball.Core
{
    /// <summary>球場幾何：隊伍視角（朝網、向右）與世界座標之間的換算。</summary>
    public static class Court
    {
        public static int TeamOf(int slot) => slot / GameConfig.PlayersPerTeam;

        /// <summary>朝網的方向。</summary>
        public static Vec3 Forward(int team) => team == 0 ? new Vec3(1f, 0f, 0f) : new Vec3(-1f, 0f, 0f);

        /// <summary>隊伍視角的「右」。左手座標系中 right = up × forward。</summary>
        public static Vec3 Right(int team) => team == 0 ? new Vec3(0f, 0f, -1f) : new Vec3(0f, 0f, 1f);

        /// <summary>己方半場的點。depth = 離網距離；lateral = 向右為正。</summary>
        public static Vec3 OwnSide(int team, float depth, float lateral, float y)
            => new Vec3(-Forward(team).X * depth, y, Right(team).Z * lateral);

        /// <summary>對方半場的點。lateral 仍以「發球／擊球者的右手邊」為正。</summary>
        public static Vec3 OppSide(int team, float depth, float lateral, float y)
            => new Vec3(Forward(team).X * depth, y, Right(team).Z * lateral);

        /// <summary>每回合開始時的站位。index 0/1 = 後排左/右，2 = 前排。</summary>
        public static Vec3 FormationPos(int team, int index)
        {
            switch (index)
            {
                case 0: return OwnSide(team, 4.2f, -1.9f, 0f);
                case 1: return OwnSide(team, 4.2f, 1.9f, 0f);
                default: return OwnSide(team, 2.3f, 0f, 0f);
            }
        }

        /// <summary>發球員站的位置（底線後方）。</summary>
        public static Vec3 ServePos(int team) => OwnSide(team, GameConfig.HalfLength + 0.8f, 0f, 0f);
    }
}
