using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace GhostHunter.Game
{
    /// <summary>
    /// UGS Relay를 통한 접속 (기술 문서 1번, 8-3).
    ///
    /// <b>브라우저는 리스닝 소켓을 열 수 없어 서버가 될 수 없다.</b> Relay가 유일한 예외로,
    /// 호스트도 클라이언트로서 Relay에 <b>나가는 연결</b>을 맺고, Relay가 패킷을 중계한다.
    /// 그래서 아무도 포트를 열지 않아도 호스트-클라이언트 구조가 성립한다.
    ///
    /// <b>Relay는 게임 서버가 아니다.</b> 판정도 비밀 관리도 하지 않는 우체국이며,
    /// 게임 로직은 여전히 호스트에서 돈다 — 서버 권한 설계(2-1)가 그대로 유지된다.
    /// </summary>
    public static class RelayConnection
    {
        /// <summary>한 방의 최대 인원 (시나리오 6번: 귀신 1 + 퇴마사 4).</summary>
        public const int MaxPlayers = 5;

        /// <summary>호스트가 발급받은 참가 코드. 이걸 상대에게 알려주면 들어올 수 있다.</summary>
        public static string JoinCode { get; private set; }

        /// <summary>마지막 실패 사유. UI가 그대로 보여준다.</summary>
        public static string LastError { get; private set; }

        /// <summary>지금 접속을 시도하는 중인가. 버튼 중복 클릭을 막는 데 쓴다.</summary>
        public static bool IsBusy { get; private set; }

        private static bool servicesReady;

        /// <summary>
        /// UGS 초기화 + 익명 로그인.
        ///
        /// Relay는 <b>인증된 사용자만</b> 쓸 수 있다. 계정을 만들 필요는 없고
        /// 익명 로그인이면 충분하다 — 심사자가 아무것도 가입하지 않아도 되어야 한다.
        /// </summary>
        private static async Task EnsureServicesAsync()
        {
            if (servicesReady)
            {
                return;
            }

            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            servicesReady = true;
        }

        /// <summary>
        /// 방을 만든다. 성공하면 <see cref="JoinCode"/>에 참가 코드가 들어간다.
        /// </summary>
        public static async Task<bool> StartHostAsync()
        {
            if (IsBusy) return false;
            IsBusy = true;
            LastError = null;

            try
            {
                await EnsureServicesAsync();

                // 최대 인원에서 호스트 자신은 빼고 요청한다 — Relay가 세는 건 '접속할 다른 사람' 수다.
                // 리전을 지정하지 않으면 기본값이 잡히는데, 그게 지구 반대편이면
                // 모든 조작이 그 왕복만큼 늦어진다. 가까운 곳을 골라준다.
                string region = await PickNearestRegionAsync();
                Allocation allocation = region == null
                    ? await RelayService.Instance.CreateAllocationAsync(MaxPlayers - 1)
                    : await RelayService.Instance.CreateAllocationAsync(MaxPlayers - 1, region);

                Debug.Log($"[Relay] 리전 = {allocation.Region}");
                JoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

                // 호스트는 자기 자신이 호스트이므로 hostConnectionData도 자기 ConnectionData다.
                ApplyRelayData(BuildRelayData(
                    allocation.ServerEndpoints, allocation.AllocationIdBytes,
                    allocation.ConnectionData, allocation.ConnectionData, allocation.Key));

                if (!NetworkManager.Singleton.StartHost())
                {
                    LastError = "호스트 시작에 실패했습니다.";
                    return false;
                }

                Debug.Log($"[Relay] 방 생성 완료. 참가 코드: {JoinCode}");
                return true;
            }
            catch (Exception e)
            {
                LastError = "방 생성 실패: " + e.Message;
                Debug.LogError("[Relay] " + e);
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>참가 코드로 방에 들어간다.</summary>
        public static async Task<bool> StartClientAsync(string joinCode)
        {
            if (IsBusy) return false;
            if (string.IsNullOrWhiteSpace(joinCode))
            {
                LastError = "참가 코드를 입력하세요.";
                return false;
            }

            IsBusy = true;
            LastError = null;

            try
            {
                await EnsureServicesAsync();

                // 코드는 대소문자를 가리지 않게 받아 실수를 줄인다.
                JoinAllocation join = await RelayService.Instance.JoinAllocationAsync(joinCode.Trim().ToUpperInvariant());

                ApplyRelayData(BuildRelayData(
                    join.ServerEndpoints, join.AllocationIdBytes,
                    join.ConnectionData, join.HostConnectionData, join.Key));

                if (!NetworkManager.Singleton.StartClient())
                {
                    LastError = "접속을 시작하지 못했습니다.";
                    return false;
                }

                JoinCode = joinCode;
                return true;
            }
            catch (Exception e)
            {
                LastError = "참가 실패: 코드를 확인하세요.";
                Debug.LogError("[Relay] " + e);
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// WebGL은 <b>UDP를 쓸 수 없어 WebSocket 전용</b>이다 (기술 문서 1번).
        /// 에디터·데스크톱에서는 UDP가 더 나으므로 플랫폼에 따라 가른다.
        /// </summary>
        private static string ConnectionType =>
#if UNITY_WEBGL && !UNITY_EDITOR
            "wss";
#else
            "dtls";
#endif

        private static string cachedRegion;
        private static bool regionResolved;

        /// <summary>
        /// 한국에서 가장 가까울 리전을 고른다. 못 고르면 null(= Relay 기본값).
        ///
        /// <b>리전이 멀면 모든 조작이 늦어진다.</b> 도구를 줍는 것 하나도
        /// 클라이언트 → 서버 → 클라이언트로 Relay를 두 번 건너가기 때문에,
        /// 편도 200ms짜리 리전이면 체감 지연이 0.5초에 가까워진다.
        ///
        /// 조회는 <b>세션당 한 번</b>만 한다 — 목록이 바뀌지 않으므로 매번 왕복할 이유가 없다.
        /// </summary>
        private static async Task<string> PickNearestRegionAsync()
        {
            // 리전 목록은 바뀌지 않으므로 한 번만 조회한다.
            // 방을 다시 만들 때마다 왕복을 한 번씩 더 쓸 이유가 없다.
            if (regionResolved)
            {
                return cachedRegion;
            }

            try
            {
                var regions = await RelayService.Instance.ListRegionsAsync();

                // 사용 가능한 리전은 계정·시점에 따라 다르다. 항상 남겨서 확인할 수 있게 한다.
                var all = new System.Text.StringBuilder();
                foreach (var r in regions) all.Append(r.Id).Append("  ");
                Debug.Log("[Relay] 사용 가능한 리전: " + all);

                // 가까운 순서대로 찾는다. 이름 규칙이 바뀔 수 있으므로 부분 일치로 본다.
                // seoul/korea가 있으면 그게 1순위, 없으면 도쿄(asia-northeast) 쪽으로.
                string[] preferred =
                {
                    "seoul", "korea", "asia-northeast", "japan", "tokyo",
                    "asia-east", "asia-southeast", "asia",
                };

                foreach (var key in preferred)
                {
                    foreach (var r in regions)
                    {
                        if (r.Id != null && r.Id.ToLowerInvariant().Contains(key))
                        {
                            Debug.Log($"[Relay] 리전 선택: {r.Id}");
                            cachedRegion = r.Id;
                            regionResolved = true;
                            return cachedRegion;
                        }
                    }
                }

                // 후보가 없으면 기본값에 맡긴다. 목록은 로그로 남겨 다음에 참고한다.
                var names = new System.Text.StringBuilder();
                foreach (var r in regions) names.Append(r.Id).Append(", ");
                Debug.Log("[Relay] 가까운 리전을 못 찾음 — 기본값에 맡긴다.");
                regionResolved = true;
                cachedRegion = null;
                return null;
            }
            catch (Exception e)
            {
                // 조회 실패는 일시적일 수 있으니 캐시하지 않는다 — 다음에 다시 시도한다.
                Debug.LogWarning("[Relay] 리전 목록 조회 실패, 기본값 사용: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Relay 할당 정보를 트랜스포트가 쓰는 형태로 옮긴다.
        ///
        /// <b>엔드포인트를 목록에서 골라야 한다.</b> Relay는 udp·dtls·ws·wss를 각각 다른
        /// 주소·포트로 제공하므로, 우리가 쓸 방식에 맞는 것을 집어야 한다.
        /// 아무거나 쓰면 주소는 맞는데 연결이 안 되는, 원인 찾기 어려운 상태가 된다.
        /// </summary>
        private static RelayServerData BuildRelayData(
            System.Collections.Generic.List<RelayServerEndpoint> endpoints,
            byte[] allocationId, byte[] connectionData, byte[] hostConnectionData, byte[] key)
        {
            RelayServerEndpoint endpoint = null;
            foreach (var e in endpoints)
            {
                if (e.ConnectionType == ConnectionType)
                {
                    endpoint = e;
                    break;
                }
            }

            if (endpoint == null)
            {
                throw new Exception($"Relay가 '{ConnectionType}' 엔드포인트를 주지 않았습니다.");
            }

            bool isWebSocket = ConnectionType == "wss" || ConnectionType == "ws";

            return new RelayServerData(
                endpoint.Host, (ushort)endpoint.Port,
                allocationId, connectionData, hostConnectionData, key,
                endpoint.Secure, isWebSocket);
        }

        private static void ApplyRelayData(RelayServerData data)
        {
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.UseWebSockets = ConnectionType == "wss" || ConnectionType == "ws";
            transport.SetRelayServerData(data);
        }
    }
}
