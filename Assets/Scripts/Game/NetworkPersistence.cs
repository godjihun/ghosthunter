using UnityEngine;

namespace GhostHunter.Game
{
    /// <summary>
    /// <c>NetworkManager</c>를 씬 전환 사이에 살려둔다.
    ///
    /// 로비(MainMenuScene)에서 접속하고 "게임 시작"을 누르면 NGO의 동기화된 씬 전환으로
    /// GameScene으로 넘어간다 — 그 순간 NetworkManager 자신이 파괴되면 접속이 끊긴다.
    /// <c>NetworkManager</c> GameObject에 붙인다. 자식으로 둔 <see cref="PreGameLobby"/>도
    /// <c>DontDestroyOnLoad</c>는 계층 전체에 적용되므로 함께 살아남는다.
    /// </summary>
    public class NetworkPersistence : MonoBehaviour
    {
        private static NetworkPersistence instance;

        private void Awake()
        {
            // 씬을 다시 열거나(에디터 테스트) MainMenuScene으로 되돌아오면 중복이 생길 수 있다.
            // 기존 GameManager.Awake() 등과 같은 싱글턴 가드 패턴.
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }
    }
}
