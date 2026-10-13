using UnityEngine;

namespace Volleyball.Game
{
    /// <summary>
    /// 進入 Play（或啟動遊戲）時自動建立遊戲，所以不需要在場景裡放任何物件：
    /// 隨便開一個空場景按 Play 就能玩。
    /// </summary>
    static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (GameApp.Instance != null) return;
            var go = new GameObject("VolleyballApp");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<GameApp>();
        }
    }
}
