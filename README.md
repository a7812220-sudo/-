# 3v3 排球（Unity 6）

3D 街機排球，每隊 3 人、可多人連線；**真人不足的位子由電腦補滿**，比賽中有人離線由電腦接手，新玩家中途加入則取代一位電腦。

> **請先看這段：驗證範圍**
> 開發這個專案的環境沒有 Unity 編輯器，所以我**沒有在 Unity 裡實際執行過**。已經做到的驗證：
> - 遊戲邏輯（物理、規則、電腦 AI、網路協定、伺服器／客戶端）是不依賴 Unity 的純 C#，有自動化測試（`dotnet test`，同一批測試也能在 Unity Test Runner 執行）。
> - Unity 層程式碼（連線、3D 場景、輸入、介面）用 Unity 2021.3 參考組件，加上 **NGO / Transport / Relay / Authentication 套件的真實原始碼**做過編譯檢查，0 個錯誤。
>
> 沒有驗證到的：畫面實際長相與手感、IMGUI 版面、Relay 實際連線。第一次打開預期可能需要微調數值（都集中在 `Assets/Scripts/Core/Sim/GameConfig.cs`）。遇到問題請把 Console 的錯誤訊息貼給我。

## 快速開始

1. 安裝 **Unity 6**（6000.x）。
2. 取得專案。repo 名稱是 `-`，在終端機裡容易被當成指令參數，建議 clone 時改個資料夾名稱：
   ```
   git clone <repo 網址> volleyball3v3
   ```
3. Unity Hub → **Add project from disk** → 選這個資料夾。Hub 會問版本，選你安裝的 Unity 6（若提示版本差異，選繼續／升級）。
4. 第一次開啟會自動下載 Netcode for GameObjects、Relay、Authentication 等套件，需要網路，約幾分鐘。
5. 開啟任何場景（預設的空場景就行），按 **▶ Play**。選單會自動出現，**不需要在場景裡放任何東西**。
6. 想馬上試玩：選 **單人練習（5 位電腦）**，完全不需要帳號或網路設定。

> 第一次開啟時 Unity 會產生 `.meta` 檔、`ProjectSettings/*.asset` 和 `Packages/packages-lock.json`。
> 這些請一併提交（`.gitignore` 已排除 `Library/`、`Temp/` 等不該提交的東西）。

## 多人連線

### A. 輸入代碼加入（Unity Relay）

不用開 port、不用自己架伺服器，房主建立房間會得到一組代碼，朋友輸入代碼就能加入。需要一次性設定（約 5 分鐘）：

1. Unity 裡：**Edit → Project Settings → Services**，登入 Unity 帳號，建立或連結一個 Unity Cloud 專案。
2. 到 [Unity Dashboard](https://cloud.unity.com)：該專案 → **Multiplayer → Relay** → 啟用（有免費額度）。
3. 再按 Play → **建立房間（Unity Relay）**，大廳會顯示房間代碼，按「複製」傳給朋友。
4. 朋友用同一個專案開啟，或用你在這個專案裡 **Build** 出來的執行檔（執行檔會內建專案 ID，朋友不用任何設定），在選單輸入代碼加入。

出現錯誤時，選單會直接顯示原因（沒連結專案、沒啟用 Relay、代碼錯誤……）。

### B. 區網直連（不需要帳號）

選單 → 勾選 **進階：區網直連**：
- 房主按 **建立區網主機**，大廳會顯示區網位址（例如 `192.168.1.20:7777`）。
- 朋友輸入這個位址，按 **連線**。
- 同一台電腦測試：先 **Build** 一份執行檔，編輯器當房主、執行檔輸入 `127.0.0.1` 連線。
- 跨網路需要 VPN（Tailscale、ZeroTier 等）或自行設定 port forwarding。

### 房間流程

大廳可以 **換隊**；**只有房主能按「開始比賽」**，空位會由電腦補滿。比賽結束後 10 秒回到大廳，房主可以再開一場。

## 操作

| 按鍵 | 動作 |
| --- | --- |
| `W` `A` `S` `D`／方向鍵 | 移動（`W` = 朝網） |
| `空白鍵` | 跳 |
| `J`／`K`／滑鼠左鍵 | 擊球（可以提早按一下，球進入範圍會自動擊出） |
| 擊球時按住方向鍵 | 決定球路：左右 = 落點偏左右、`W` = 短球／靠網、`S` = 長球／深遠 |
| `Esc` | 選單（離開比賽） |

球的種類**自動判斷**，不需要選：

- **接球**（第一觸）：把球送到己方網前的二傳區。
- **托球**（第二觸）：把球高高托到網前，讓隊友起跳攻擊。
- **扣球**：人在空中、球比網高、靠近網時，擊球會用力扣向對場。球越高、離網越近，扣得越快。
- **吊球**（第三觸，或沒辦法扣球時）：高吊過網。
- **發球**：輪到你發球時按擊球鍵；方向鍵決定落點。

球離你越遠（勉強構到），落點誤差越大。

## 規則

- 每隊 3 人，**得分制**（每個回合都得分），先到 **15 分且領先 2 分**獲勝。
- 每隊每次最多連續觸球 3 次；第 4 次觸球失分。**同一個人不能連續觸球**（按了會被忽略並提示）。
- 球落在界內（壓線算界內）→ 落地那一側失分；出界 → 最後觸球的一隊失分。
- 不能伸手過網；發球後，發球隊在球過網之前不能碰球。
- 贏得發球權後，發球員在隊友之間輪流。

## 專案結構

```
Assets/Scripts/Core/      純 C#（不依賴 UnityEngine），可在 Unity 之外編譯與測試
  Sim/                    物理、擊球解算、規則、比賽狀態機（VolleyballMatch）
  Ai/                     電腦 AI（BotBrain）
  Net/                    二進位協定、GameServer（房間／座位／補位）、GameClient（預測與插值）
Assets/Scripts/Game/      Unity 層：連線（NetSession）、3D 場景、輸入、IMGUI 介面
Assets/Tests/EditMode/    NUnit 測試
tests/Volleyball.Core.Tests/   用 dotnet 執行同一批測試的專案
```

### 連線架構

- **權威式伺服器**：球、球員、判定與得分全部只在房主的程式裡計算，其他玩家只送「輸入」、收「快照」，所以不會不同步，也不能作弊。
- 房主的程式同時是伺服器與一位玩家（兩者在記憶體內直接對接）；其他玩家經 NGO + Relay 連入。
- 本專案只把 NGO 當作**連線與傳輸層**（Relay 穿透、連線管理、逾時、可靠／不可靠傳輸），用它的自訂訊息傳遞自己的二進位協定；**沒有使用 NetworkObject／NetworkVariable／RPC**，所以不需要任何 Prefab 或場景設定，邏輯也能在 Unity 之外測試。
- 60 Hz 模擬，30 Hz 快照（約 110 位元組）。本機球員用相同的移動程式立即預測，收到快照後重播尚未確認的輸入來校正；其他球員與球在快照之間插值（刻意落後 0.1 秒換取平滑）。輸入封包每次附帶最近 6 個 tick，所以掉包也不會漏掉按鍵。

## 測試

```bash
dotnet test tests/Volleyball.Core.Tests       # 需要 .NET 8 SDK
```

或在 Unity：**Window → General → Test Runner → EditMode → Run All**。

測試涵蓋：球員與球的物理、每種擊球的軌跡（過網、落點、誤差）、完整規則（發球輪轉、換發、四次觸球、出界、勝負）、電腦 AI 對打、協定序列化、房間座位／換隊／補位／接手／中途加入，以及在延遲、抖動與掉包下的連線整合測試。

## 疑難排解

- **按了 Play 沒有任何反應／畫面上方出現紅色提示**：專案只啟用了新版 Input System。到 *Edit → Project Settings → Player → Other Settings → Active Input Handling*，改成 **Both**（或 Input Manager (Old)），重新啟動編輯器。
- **中文變成方塊**：介面用系統字型（Windows 正黑體、macOS 蘋方、Linux Noto CJK）。找不到中文字型時請安裝一套。
- **只有 Windows／macOS／Linux 單機版**：WebGL 與手機不支援（Relay 傳輸方式與鍵盤操作都沒有針對它們處理）。
- **調整手感**：移動速度、跳躍、球網高度、可觸及範圍、各種球路的參數全在 `Assets/Scripts/Core/Sim/GameConfig.cs` 與 `HitSolver.cs`。
