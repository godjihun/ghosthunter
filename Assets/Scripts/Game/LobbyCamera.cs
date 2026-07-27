using UnityEngine;

namespace GhostHunter.Game
{
    /// <summary>
    /// 로비·결과 화면을 비추는 씬 카메라 (기술 문서 4-1).
    ///
    /// 게임이 시작되면 각자 플레이어의 1인칭 카메라로 넘어가므로 이 카메라는 꺼진다.
    /// 둘 다 켜져 있으면 화면이 겹쳐 보이기 때문에 <b>정확히 하나만</b> 켜져 있어야 한다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class LobbyCamera : MonoBehaviour
    {
        private Camera cam;
        private AudioListener listener;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            listener = GetComponent<AudioListener>();
        }

        private void LateUpdate()
        {
            // 플레이어 카메라가 켜지는 것보다 늦게 판단해야 한 프레임 깜빡임이 없다.
            bool lobbyView = !GameManager.IsGameplayActive;

            if (cam.enabled != lobbyView)
            {
                cam.enabled = lobbyView;
            }

            // 오디오 리스너가 씬에 2개면 Unity가 경고를 뱉는다.
            if (listener != null && listener.enabled != lobbyView)
            {
                listener.enabled = lobbyView;
            }
        }
    }
}
