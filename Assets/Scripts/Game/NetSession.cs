using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using Volleyball.Core;

namespace Volleyball.Game
{
    /// <summary>
    /// 一次遊戲連線的生命週期：單機練習、Relay 房主／加入、區網直連房主／加入。
    ///
    /// 房主的程式同時跑 GameServer 和自己的 GameClient，兩者在記憶體內直接對接、不經網路；
    /// 其他玩家的訊息用 NGO 的自訂訊息（Unnamed Message）傳遞。
    /// NGO 負責 Relay 穿透、連線、逾時與斷線偵測；比賽狀態同步則用 Volleyball.Core 的二進位協定。
    /// 這個設計刻意不使用 NetworkObject / NetworkVariable / RPC，所以不需要任何 Prefab 或場景設定。
    /// </summary>
    sealed class NetSession
    {
        const int HostId = (int)NetworkManager.ServerClientId;

        // NetworkManager 全程只建一個，跨連線重複使用（NGO 支援 Shutdown 後再啟動）
        static NetworkManager nm;
        static UnityTransport transport;
        static NetSession current;

        public GameServer Server { get; private set; }
        public GameClient Client { get; private set; }
        public string JoinCode { get; private set; }
        public string DirectAddress { get; private set; }
        public string Status { get; private set; } = "";
        public string Error { get; private set; }
        public bool Closed { get; private set; }
        public bool IsHost => Server != null;

        readonly string playerName;
        bool cancelled;
        bool helloSent;
        byte[] recvBuf = new byte[1024];

        NetSession(string playerName)
        {
            this.playerName = playerName;
        }

        // ---------- 建立連線 ----------

        /// <summary>單機練習：自己當房主、立刻開賽，其餘 5 位由電腦補滿。完全不需要網路。</summary>
        public static NetSession StartSolo(string playerName)
        {
            var s = new NetSession(playerName);
            s.BeginHostRole(true);
            return s;
        }

        public static NetSession StartRelayHost(string playerName)
        {
            var s = new NetSession(playerName) { Status = "連線到 Unity 服務…" };
            _ = s.RunRelayHost();
            return s;
        }

        public static NetSession StartRelayJoin(string playerName, string joinCode)
        {
            var s = new NetSession(playerName) { Status = "連線到 Unity 服務…" };
            _ = s.RunRelayJoin(joinCode);
            return s;
        }

        public static NetSession StartDirectHost(string playerName, ushort port)
        {
            var s = new NetSession(playerName) { Status = "啟動主機…" };
            _ = s.RunDirectHost(port);
            return s;
        }

        public static NetSession StartDirectJoin(string playerName, string ip, ushort port)
        {
            var s = new NetSession(playerName) { Status = "連線中…" };
            _ = s.RunDirectJoin(ip, port);
            return s;
        }

        public void Close()
        {
            if (Closed) return;
            Closed = true;
            cancelled = true;
            if (current == this) current = null;
            if (nm != null && (nm.IsListening || nm.IsServer || nm.IsClient)) nm.Shutdown();
        }

        void Fail(string message)
        {
            if (Closed) return;
            Error = message;
            Close();
        }

        /// <summary>每個畫面呼叫一次：推進伺服器與客戶端。</summary>
        public void Tick(float dt, IInputSource input)
        {
            if (Closed) return;
            Server?.Update(dt);
            Client?.Update(dt, input);
            if (Client != null && Client.RejectReason != null) Fail(Client.RejectReason);
        }

        // ---------- Relay ----------

        async Task RunRelayHost()
        {
            try
            {
                await EnsureServicesAsync();
                if (cancelled) return;
                Status = "建立房間…";
                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(GameConfig.SlotCount - 1);
                if (cancelled) return;
                JoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                if (cancelled) return;

                await WaitUntilIdle();
                if (cancelled) return;
                RelayServerEndpoint endpoint = PickEndpoint(allocation.ServerEndpoints);
                // 房主不需要另外給 hostConnectionData（傳 null 會沿用自己的 connectionData）
                transport.SetRelayServerData(endpoint.Host, (ushort)endpoint.Port, allocation.AllocationIdBytes,
                    allocation.Key, allocation.ConnectionData, null, endpoint.Secure);
                StartNgoHost();
            }
            catch (Exception e)
            {
                Fail(Describe(e));
            }
        }

        async Task RunRelayJoin(string joinCode)
        {
            try
            {
                await EnsureServicesAsync();
                if (cancelled) return;
                Status = "加入房間…";
                JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode.Trim().ToUpperInvariant());
                if (cancelled) return;

                await WaitUntilIdle();
                if (cancelled) return;
                RelayServerEndpoint endpoint = PickEndpoint(allocation.ServerEndpoints);
                transport.SetRelayServerData(endpoint.Host, (ushort)endpoint.Port, allocation.AllocationIdBytes,
                    allocation.Key, allocation.ConnectionData, allocation.HostConnectionData, endpoint.Secure);
                StartNgoClient();
            }
            catch (Exception e)
            {
                Fail(Describe(e));
            }
        }

        static async Task EnsureServicesAsync()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized) await UnityServices.InitializeAsync();
            while (UnityServices.State == ServicesInitializationState.Initializing) await Task.Yield();
            while (AuthenticationService.Instance.State == AuthenticationState.SigningIn) await Task.Yield();
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        /// <summary>
        /// 從 allocation 的端點清單挑出要連的那個：優先用加密的 DTLS，沒有就用 UDP。
        /// 刻意不用 new RelayServerData(allocation, "dtls")：那個建構子只有在 Transport 套件偵測到 Relay SDK 時才存在，
        /// 在部分 Unity 版本會消失（CS1729）。直接把端點資料交給 UnityTransport 的原始參數版本就沒有這個問題。
        /// </summary>
        static RelayServerEndpoint PickEndpoint(System.Collections.Generic.List<RelayServerEndpoint> endpoints)
        {
            RelayServerEndpoint udp = null;
            foreach (RelayServerEndpoint e in endpoints)
            {
                if (e.ConnectionType == RelayServerEndpoint.ConnectionTypeDtls) return e;
                if (e.ConnectionType == RelayServerEndpoint.ConnectionTypeUdp) udp = e;
            }

            if (udp == null) throw new InvalidOperationException("Relay 沒有提供可用的連線端點（需要 DTLS 或 UDP）。");
            return udp;
        }

        // ---------- 區網直連 ----------

        async Task RunDirectHost(ushort port)
        {
            try
            {
                await WaitUntilIdle();
                if (cancelled) return;
                transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
                StartNgoHost();
                if (!Closed) DirectAddress = LocalIp() + ":" + port;
            }
            catch (Exception e)
            {
                Fail(Describe(e));
            }
        }

        async Task RunDirectJoin(string ip, ushort port)
        {
            try
            {
                await WaitUntilIdle();
                if (cancelled) return;
                transport.SetConnectionData(ip.Trim(), port);
                StartNgoClient();
            }
            catch (Exception e)
            {
                Fail(Describe(e));
            }
        }

        // ---------- 啟動 NGO ----------

        static async Task WaitUntilIdle()
        {
            EnsureNetworkManager();
            // Shutdown() 是延後執行的；房主還要先通知所有客戶端斷線（最多約 5 秒）。沒收乾淨就重啟會被拒絕。
            while (nm.ShutdownInProgress || nm.IsListening) await Task.Yield();
        }

        static void EnsureNetworkManager()
        {
            if (nm != null) return;

            var go = new GameObject("NetworkManager");
            UnityEngine.Object.DontDestroyOnLoad(go);
            transport = go.AddComponent<UnityTransport>();
            transport.ConnectTimeoutMS = 1000;
            transport.MaxConnectAttempts = 8; // 連不上時約 8 秒就放棄，而不是預設的一分鐘
            transport.DisconnectTimeoutMS = 10000;
            nm = go.AddComponent<NetworkManager>();
            nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = false, // 我們只用自訂訊息，不需要 NGO 的場景同步
            };

            nm.OnClientConnectedCallback += id => current?.OnNgoConnected(id);
            nm.OnClientDisconnectCallback += id => current?.OnNgoDisconnected(id);
            nm.OnTransportFailure += () => current?.Fail("網路傳輸層啟動失敗（連線被拒絕或中斷）");
        }

        void StartNgoHost()
        {
            current = this;
            BeginHostRole(false); // 先建好 Server/Client：nm.StartHost() 會同步觸發房主自己的連線回呼
            if (!nm.StartHost())
            {
                Fail("無法啟動主機。連接埠可能被占用，或 Relay 設定有誤。");
                return;
            }

            nm.CustomMessagingManager.OnUnnamedMessage += OnNgoMessage; // 訊息管理器在啟動後才存在
            Status = "";
        }

        void StartNgoClient()
        {
            current = this;
            Client = new GameClient(ClientSend, playerName);
            if (!nm.StartClient())
            {
                Fail("無法啟動連線。");
                return;
            }

            nm.CustomMessagingManager.OnUnnamedMessage += OnNgoMessage;
            Status = "連線中…";
        }

        /// <summary>房主身分：伺服器邏輯 + 自己這位玩家的客戶端，在記憶體內對接。</summary>
        void BeginHostRole(bool autoStart)
        {
            Server = new GameServer(ServerSend, HostId, unchecked((uint)Environment.TickCount));
            Client = new GameClient(ClientSend, playerName);
            Server.OnClientConnected(HostId);
            Client.OnConnected();
            if (autoStart) Client.RequestStart();
        }

        // ---------- NGO 事件 ----------

        void OnNgoConnected(ulong id)
        {
            if (Closed) return;
            if (IsHost)
            {
                if (id != NetworkManager.ServerClientId) Server.OnClientConnected((int)id);
            }
            else if (!helloSent)
            {
                helloSent = true;
                Status = "進入房間…";
                Client.OnConnected();
            }
        }

        void OnNgoDisconnected(ulong id)
        {
            if (Closed) return;
            if (IsHost)
            {
                if (id != NetworkManager.ServerClientId) Server.OnClientDisconnected((int)id);
                return;
            }

            // NGO 斷線後 DisconnectReason 一定有值：沒有伺服器給的理由時是 "[Disconnect Event][Client-n]…" 這種內部字串
            string reason = nm != null ? nm.DisconnectReason : null;
            if (string.IsNullOrEmpty(reason) || reason.StartsWith("[Disconnect Event]"))
                reason = helloSent ? "與主機的連線中斷。" : "無法連線到主機。請確認代碼／位址正確，而且雙方使用相同版本的遊戲。";
            Fail(reason);
        }

        void OnNgoMessage(ulong senderId, FastBufferReader reader)
        {
            if (Closed) return;
            int n = reader.Length - reader.Position;
            if (n <= 0 || n > 4096) return;
            if (recvBuf.Length < n) recvBuf = new byte[n];
            reader.ReadBytesSafe(ref recvBuf, n);

            if (Server != null) Server.OnMessage((int)senderId, recvBuf, n);
            else Client.OnMessage(recvBuf, n);
        }

        // ---------- 送出 ----------

        void ServerSend(int clientId, byte[] data, int length, bool reliable)
        {
            if (clientId == HostId) Client.OnMessage(data, length);
            else SendNgo((ulong)clientId, data, length, reliable);
        }

        void ClientSend(byte[] data, int length, bool reliable)
        {
            if (Server != null) Server.OnMessage(HostId, data, length);
            else SendNgo(NetworkManager.ServerClientId, data, length, reliable);
        }

        static void SendNgo(ulong target, byte[] data, int length, bool reliable)
        {
            if (nm == null || !nm.IsListening || nm.CustomMessagingManager == null) return;
            using (var writer = new FastBufferWriter(length, Allocator.Temp))
            {
                writer.WriteBytesSafe(data, length);
                nm.CustomMessagingManager.SendUnnamedMessage(target, writer,
                    reliable ? NetworkDelivery.ReliableSequenced : NetworkDelivery.UnreliableSequenced);
            }
        }

        // ---------- 其他 ----------

        static string LocalIp()
        {
            try
            {
                foreach (IPAddress a in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                        return a.ToString();
            }
            catch (Exception)
            {
                // 查不到就用本機位址
            }

            return "127.0.0.1";
        }

        static string Describe(Exception e)
        {
            const string setup = "請確認：① 專案已連結 Unity Cloud（Edit → Project Settings → Services） " +
                                 "② 已在 Unity Dashboard 啟用 Relay ③ 網路可以連線。";
            if (e is RelayServiceException relay)
            {
                switch (relay.Reason)
                {
                    case RelayExceptionReason.JoinCodeNotFound:
                        return "找不到這個房間代碼。請確認有沒有輸入錯誤，以及房主是否還在線上。";
                    case RelayExceptionReason.InactiveProject:
                        return "這個 Unity 專案尚未啟用 Relay。" + setup;
                    default:
                        return "Relay 錯誤：" + relay.Message;
                }
            }

            if (e is AuthenticationException auth) return "Unity 帳號登入失敗：" + auth.Message + "\n" + setup;
            if (e is ServicesInitializationException) return "Unity 服務初始化失敗：" + e.Message + "\n" + setup;
            if (e is RequestFailedException failed) return "Unity 服務錯誤 (" + failed.ErrorCode + ")：" + failed.Message;
            return e.Message;
        }
    }
}
