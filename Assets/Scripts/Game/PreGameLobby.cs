using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GhostHunter.Game
{
    /// <summary>
    /// MainMenuScene에 놓이는 씬 배치 NetworkObject. <see cref="NetworkPersistence"/>가 붙은
    /// NetworkManager의 자식이라 DontDestroyOnLoad로 함께 살아남는다.
    ///
    /// 하는 일은 하나뿐이다 — 방장이 방을 만들면 곧바로 GameScene으로 <b>NGO 동기화 씬 전환</b>을
    /// 트리거한다. 방을 만든 순간 바로 저택(걸어다니는 대기방)으로 들어가고, 그 뒤로 사람들이
    /// 모이는 걸 보면서 방장이 <c>LobbyConsole</c>(F키 키오스크)에서 밸런스를 정하고 실제로
    /// "게임 시작"을 누른다 — 어몽어스·챠메레온류처럼 로비 자체가 게임 공간이다.
    ///
    /// ⚠️ <b>이 클래스가 이 설계 전체의 전제를 담당한다</b>: <c>NetworkManager.SceneManager.LoadScene</c>
    /// 호출 시점에 이미 접속해 있던 플레이어 오브젝트(PlayerPrefab, <c>SceneMigrationSynchronization</c>
    /// 켜짐)와 이 오브젝트 자신이 새 씬으로 살아남는지가 검증 대상이다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class PreGameLobby : NetworkBehaviour
    {
        public static PreGameLobby Instance { get; private set; }

        private const string GameSceneName = "GameScene";

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// 방을 만든 직후 GameScene으로 넘어간다. 방장(= 서버)만 호출한다 —
        /// LobbyConsole과 같은 이유로 RPC를 거치지 않는다.
        /// </summary>
        public void ServerStartGame()
        {
            if (!IsServer)
            {
                return;
            }

            NetworkManager.Singleton.SceneManager.LoadScene(GameSceneName, LoadSceneMode.Single);
        }
    }
}
