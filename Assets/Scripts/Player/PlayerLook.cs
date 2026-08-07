using GhostHunter.Game;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GhostHunter.Player
{
    /// <summary>
    /// 1인칭 시점 회전 (기술 문서 1번 — 카메라는 1인칭으로 결정).
    /// 좌우는 몸체를, 상하는 카메라만 회전시킨다. 소유자 클라이언트에서만 동작한다.
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer))]
    public class PlayerLook : NetworkBehaviour
    {
        [Tooltip("머리 위치의 자식 트랜스폼. 여기에 카메라가 붙는다.")]
        [SerializeField] private Transform cameraPivot;

        [SerializeField] private float mouseSensitivity = 0.12f;
        [SerializeField] private float minPitch = -85f;
        [SerializeField] private float maxPitch = 85f;

        private const string SensitivityKey = "GhostHunter.Sensitivity";
        private static float sensitivityScale = -1f;

        /// <summary>
        /// 사용자가 정한 감도 배수. ESC 설정 창이 조절한다.
        ///
        /// <b>네트워크로 보내지 않는다.</b> 내 화면이 도는 속도일 뿐이라 남이 알 이유가 없다.
        /// <c>PlayerPrefs</c>에 남겨 다음에 접속해도 유지된다 — 매번 다시 맞추게 하면
        /// 설정을 만든 의미가 없다.
        /// </summary>
        public static float SensitivityScale
        {
            get
            {
                // 매 프레임 PlayerPrefs를 읽지 않도록 처음 한 번만 불러온다.
                if (sensitivityScale < 0f)
                {
                    sensitivityScale = PlayerPrefs.GetFloat(SensitivityKey, 1f);
                }
                return sensitivityScale;
            }
            set
            {
                sensitivityScale = Mathf.Clamp(value, 0.2f, 3f);
                PlayerPrefs.SetFloat(SensitivityKey, sensitivityScale);
            }
        }

        private Vector2 lookInput;
        private float pitch;
        private PlayerEmote emote;

        public Transform CameraPivot => cameraPivot;

        private NetworkPlayer player;

        private void Awake()
        {
            emote = GetComponent<PlayerEmote>();
            player = GetComponent<NetworkPlayer>();
        }

        public override void OnNetworkSpawn()
        {
            enabled = IsOwner;
        }

        public void OnLook(InputValue value)
        {
            lookInput = value.Get<Vector2>();
        }

        private void Update()
        {
            // 결과 화면에서는 시점을 돌리지 않는다. 커서로 UI를 눌러야 하기 때문에
            // 커서 잠금은 PlayerRoleSetup이 단독으로 관리한다 (여기서 만지지 말 것).
            if (!IsOwner || !GameManager.IsFirstPersonActive)
            {
                return;
            }

            // 창이 떠 있으면 마우스는 UI의 것이다. 시점까지 돌면 창을 누르려다
            // 화면이 홱 돌아간다.
            if (PlayerRoleSetup.UiPanelOpen)
            {
                lookInput = Vector2.zero;
                return;
            }

            // 죽으면 시점을 멈춘다. 관전으로 넘어가면 액션맵이 바뀌어 통지가 끊기는데,
            // <b>lookInput은 마지막 값을 그대로 들고 있다</b> — 죽는 순간 마우스를 움직이고
            // 있었다면 시체가 영원히 제자리에서 돈다. 카메라는 관전자가 따로 잡으므로
            // 여기서 멈춰도 화면에는 영향이 없다.
            if (player != null && !player.IsAlive.Value)
            {
                lookInput = Vector2.zero;
                return;
            }

            // 이모트 휠이 열려 있으면 마우스는 항목 선택에 쓰인다.
            // 여기서 시점까지 돌리면 고르는 동안 화면이 빙빙 돈다.
            if (emote != null && emote.WheelOpen)
            {
                return;
            }

            // 마우스 델타는 이미 프레임당 이동량이므로 Time.deltaTime을 곱하지 않는다.
            float speed = mouseSensitivity * SensitivityScale;
            float yaw = lookInput.x * speed;
            pitch = Mathf.Clamp(pitch - lookInput.y * speed, minPitch, maxPitch);

            transform.Rotate(Vector3.up, yaw, Space.World);

            if (cameraPivot != null)
            {
                cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }
        }
    }
}
