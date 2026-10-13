using UnityEngine;
using Volleyball.Core;

namespace Volleyball.Game
{
    /// <summary>
    /// 鍵盤／滑鼠 → InputFrame。跳躍與擊球是「按下」事件，先鎖存起來，
    /// 等網路層下一個 tick 取樣時才交出去，所以即使畫面幀率比 tick 高也不會漏鍵。
    /// 使用舊版 Input Manager（Project Settings → Player → Active Input Handling 要是 Old 或 Both）。
    /// </summary>
    sealed class InputReader : IInputSource
    {
        float right, forward;
        bool jumpLatched, hitLatched;

        /// <summary>輸入文字或開啟選單時設為 false。</summary>
        public bool Enabled = true;

        /// <summary>舊版 Input 無法使用時的說明文字（否則為 null）。</summary>
        public string Error { get; private set; }

        /// <summary>這個畫面是否剛按下 Esc（即使 Enabled 為 false 也會偵測，供開關選單用）。</summary>
        public bool EscapePressed { get; private set; }

        /// <summary>每個畫面呼叫一次。</summary>
        public void Poll()
        {
            EscapePressed = false;
            if (Error != null) return;

            try
            {
                EscapePressed = Input.GetKeyDown(KeyCode.Escape);
                if (!Enabled)
                {
                    right = forward = 0f;
                    jumpLatched = hitLatched = false;
                    return;
                }

                right = Axis(KeyCode.D, KeyCode.RightArrow, KeyCode.A, KeyCode.LeftArrow);
                forward = Axis(KeyCode.W, KeyCode.UpArrow, KeyCode.S, KeyCode.DownArrow);
                if (Input.GetKeyDown(KeyCode.Space)) jumpLatched = true;
                if (Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.K) || Input.GetMouseButtonDown(0)) hitLatched = true;
            }
            catch (System.InvalidOperationException)
            {
                Error = "偵測到專案只啟用了新版 Input System。請到 Edit → Project Settings → Player → Other Settings，" +
                        "把 Active Input Handling 改成「Both」（或 Input Manager (Old)），然後重新啟動編輯器。";
            }
        }

        static float Axis(KeyCode plus1, KeyCode plus2, KeyCode minus1, KeyCode minus2)
        {
            float v = 0f;
            if (Input.GetKey(plus1) || Input.GetKey(plus2)) v += 1f;
            if (Input.GetKey(minus1) || Input.GetKey(minus2)) v -= 1f;
            return v;
        }

        public InputFrame Sample()
        {
            var f = new InputFrame
            {
                Right = InputFrame.Quantize(right),
                Forward = InputFrame.Quantize(forward),
                Jump = jumpLatched,
                Hit = hitLatched,
            };
            jumpLatched = hitLatched = false;
            return f;
        }
    }
}
