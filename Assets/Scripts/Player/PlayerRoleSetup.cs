using GhostHunter.Core;
using GhostHunter.Exorcist;
using GhostHunter.Game;
using GhostHunter.Ghost;
using GhostHunter.Tools;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GhostHunter.Player
{
    /// <summary>
    /// 진영이 배정되면 그에 맞는 입력 액션맵과 컴포넌트만 켠다 (기술 문서 4-1).
    ///
    /// 퇴마사 클라이언트에는 귀신 조작이 아예 바인딩되지 않고, 반대도 마찬가지다.
    /// 카메라도 소유자 것만 켠다 — 안 그러면 여러 카메라가 겹쳐 화면이 엉킨다.
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer))]
    public class PlayerRoleSetup : NetworkBehaviour
    {
        [SerializeField] private Camera playerCamera;
        [SerializeField] private AudioListener audioListener;
        [SerializeField] private PlayerInput playerInput;

        [Tooltip("퇴마사 외형 루트. 진영이 정해지면 해당하는 쪽만 켜진다.")]
        [SerializeField] private GameObject exorcistModel;

        [Tooltip("귀신 외형 루트.")]
        [SerializeField] private GameObject ghostModel;

        [Header("── 이모트 3인칭 카메라 ──")]

        [Tooltip("이모트 중 캐릭터 뒤로 물러나는 거리(m).")]
        [SerializeField] private float thirdPersonDistance = 2.6f;

        [Tooltip("이모트 중 카메라 높이(m).")]
        [SerializeField] private float thirdPersonHeight = 0.8f;

        /// <summary>소유자 본인의 몸만 올려두는 레이어. 자기 카메라가 이 레이어를 걸러낸다.</summary>
        public const string LocalBodyLayerName = "LocalBody";

        private NetworkPlayer player;
        private PlayerEmote emote;

        /// <summary>마지막으로 적용한 시점 모드. 매 프레임 카메라를 만지지 않기 위한 캐시.</summary>
        private bool viewModeApplied;

        /// <summary>내 몸이 지금 내 화면에 보이는가 (3인칭 이모트 중에만 true).</summary>
        private bool selfVisible;

        private void Awake()
        {
            player = GetComponent<NetworkPlayer>();
            emote = GetComponent<PlayerEmote>();
            if (playerInput == null)
            {
                playerInput = GetComponent<PlayerInput>();
            }
        }

        public override void OnNetworkSpawn()
        {
            // 입력은 내 것만 켠다.
            // 카메라와 오디오 리스너는 여기서 켜지 않는다 — 로비에서는 씬 카메라가
            // 화면과 소리를 담당하고, 게임이 시작돼야 1인칭으로 넘어간다 (ApplyViewMode 참고).
            if (playerInput != null)
            {
                playerInput.enabled = IsOwner;
            }
            if (playerCamera != null)
            {
                playerCamera.enabled = false;

                // 내 카메라는 내 몸을 보지 않는다. 프리팹 설정에 의존하지 않고
                // 여기서 직접 걸러야 나중에 카메라를 갈아끼워도 안전하다.
                int localBodyLayer = LayerMask.NameToLayer(LocalBodyLayerName);
                if (localBodyLayer >= 0)
                {
                    playerCamera.cullingMask &= ~(1 << localBodyLayer);
                }
            }
            if (audioListener != null)
            {
                audioListener.enabled = false;
            }

            player.Faction.OnValueChanged += OnFactionChanged;
            ApplyFaction(player.Faction.Value);

            if (IsOwner)
            {
                viewModeApplied = GameManager.IsGameplayActive;
                ApplyViewMode(viewModeApplied);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (player != null)
            {
                player.Faction.OnValueChanged -= OnFactionChanged;
            }

            // 나갈 때 커서를 잠근 채로 두면 에디터를 빠져나갈 수 없다.
            if (IsOwner)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void OnFactionChanged(Faction previous, Faction current)
        {
            ApplyFaction(current);
        }

        /// <summary>
        /// 로비/결과와 게임 중 사이의 시점 전환.
        ///
        /// NetworkVariable 구독 대신 매 프레임 확인한다 — 상태가 bool 하나뿐이라 비용이 없고,
        /// 스폰 순서에 따라 구독을 놓쳐 "커서가 잠긴 채 로비에 갇히는" 사고를 막을 수 있다.
        /// 커서는 <b>이 컴포넌트만</b> 건드린다.
        /// </summary>
        private void Update()
        {
            if (!IsOwner)
            {
                return;
            }

            bool active = GameManager.IsGameplayActive;
            if (active == viewModeApplied)
            {
                return;
            }

            viewModeApplied = active;
            ApplyViewMode(active);
        }

        private void LateUpdate()
        {
            UpdateEmoteCamera();
            UpdateOwnBodyVisibility();
        }

        /// <summary>
        /// 3인칭일 때는 <b>자기 몸이 보여야 한다.</b>
        ///
        /// 1인칭용으로 자기 모델을 <c>LocalBody</c> 레이어에 넣어 카메라에서 걸러내고 있는데,
        /// 이모트 중에는 그걸 되돌리지 않으면 <b>춤추는 자기 모습이 안 보인다.</b>
        /// </summary>
        private void UpdateOwnBodyVisibility()
        {
            if (!IsOwner || emote == null)
            {
                return;
            }

            bool showSelf = emote.IsEmoting;
            if (showSelf == selfVisible)
            {
                return;
            }

            selfVisible = showSelf;

            var active = player != null && player.IsGhost ? ghostModel : exorcistModel;
            if (active == null)
            {
                return;
            }

            int layer = LayerMask.NameToLayer(showSelf ? "Character" : LocalBodyLayerName);
            if (layer < 0)
            {
                return;
            }

            foreach (var t in active.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = layer;
            }

            var mode = showSelf
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            foreach (var r in active.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = mode;
            }
        }

        /// <summary>
        /// 이모트 중에는 카메라를 뒤로 빼 3인칭으로 본다.
        ///
        /// 카메라를 새로 만들지 않고 <b>있는 카메라를 옮긴다</b> — 두 대가 동시에 켜지면
        /// 화면이 겹친다. 벽에 파묻히지 않도록 뒤쪽을 레이캐스트해 거리를 줄인다.
        /// </summary>
        private void UpdateEmoteCamera()
        {
            if (!IsOwner || playerCamera == null || emote == null)
            {
                return;
            }

            bool wantThirdPerson = emote.IsEmoting;
            var camT = playerCamera.transform;

            if (!wantThirdPerson)
            {
                // 1인칭 복귀. 부모(CameraPivot) 원점이 곧 눈 위치다.
                camT.localPosition = Vector3.MoveTowards(camT.localPosition, Vector3.zero, 12f * Time.deltaTime);
                return;
            }

            float back = thirdPersonDistance;
            float up = thirdPersonHeight;

            // 뒤가 벽이면 그만큼 당긴다.
            Vector3 pivot = camT.parent != null ? camT.parent.position : transform.position;
            Vector3 dir = (-transform.forward * back + Vector3.up * up).normalized;
            float wanted = new Vector2(back, up).magnitude;

            int mask = LayerMask.GetMask("Wall", "Door");
            if (Physics.SphereCast(pivot, 0.25f, dir, out var hit, wanted, mask, QueryTriggerInteraction.Ignore))
            {
                wanted = Mathf.Max(0.4f, hit.distance - 0.15f);
            }

            Vector3 target = camT.parent != null
                ? camT.parent.InverseTransformPoint(pivot + dir * wanted)
                : dir * wanted;

            camT.localPosition = Vector3.MoveTowards(camT.localPosition, target, 12f * Time.deltaTime);
        }

        private void ApplyViewMode(bool gameplayActive)
        {
            if (playerCamera != null)
            {
                playerCamera.enabled = gameplayActive;
            }

            // 오디오 리스너도 카메라와 함께 넘긴다. 씬 카메라와 동시에 켜져 있으면
            // Unity가 "리스너가 2개"라며 매 프레임 경고를 뱉는다.
            if (audioListener != null)
            {
                audioListener.enabled = gameplayActive;
            }

            Cursor.lockState = gameplayActive ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !gameplayActive;
        }

        private void ApplyFaction(Faction faction)
        {
            bool isGhost = faction == Faction.Ghost;
            bool isExorcist = faction == Faction.Exorcist;

            // 컴포넌트는 전원에게 동일하게 켜고 끈다.
            // 서버도 판정을 위해 이 컴포넌트들에 접근하기 때문이다.
            SetEnabled<GhostController>(isGhost);
            SetEnabled<FearSkill>(isGhost);
            SetEnabled<ExorcistInventory>(isExorcist);
            // 상호작용은 양쪽 다 쓴다 — 귀신도 문을 연다. 도구 줍기는 WorldTool 쪽에서 막는다.
            SetEnabled<PlayerInteractor>(IsOwner);
            SetEnabled<DetectionRadiusView>(isExorcist && IsOwner);

            ApplyModel(isGhost, isExorcist);

            // 귀신은 <b>항상</b> Soul 레이어다 — 벽·바닥에는 막히고 문만 통과한다 (시나리오 2번).
            // 영혼일 때만 바꾸면 본체 상태에서 문에 막혀, 문을 열 수밖에 없고
            // 저절로 열리는 문이 곧 위치 제보가 된다.
            // 레이어는 물리 판정이라 서버·클라이언트가 같아야 하는데, 이 메서드는
            // Faction(공개 NetworkVariable)을 따라 전원에게서 같이 돌아가므로 안전하다.
            int bodyLayer = LayerMask.NameToLayer(isGhost ? "Soul" : "Character");
            if (bodyLayer >= 0 && faction != Faction.Unassigned)
            {
                gameObject.layer = bodyLayer;
            }

            if (!IsOwner || playerInput == null || faction == Faction.Unassigned)
            {
                return;
            }

            // 액션맵은 자체 완결형이다 — Move/Look이 각 맵에 들어 있으므로
            // 맵 하나만 활성화해도 이동과 진영 조작이 함께 동작한다.
            playerInput.SwitchCurrentActionMap(isGhost ? "Ghost" : "Exorcist");
        }

        /// <summary>
        /// 진영에 맞는 외형만 켠다.
        ///
        /// 진영이 정해지기 전(로비)에는 <b>퇴마사 외형을 기본으로</b> 보여준다.
        /// 둘 다 꺼두면 대기방에서 서로가 투명인간으로 보인다.
        ///
        /// <b>소유자 본인의 외형은 전용 레이어로 옮겨 자기 카메라가 아예 안 보게 한다.</b>
        /// 1인칭 카메라는 눈높이 1.6m — 즉 모델의 머리·가슴 안쪽에 들어가 있어서,
        /// 그대로 두면 화면 전체가 자기 옷 안쪽으로 뭉개진다.
        /// <c>ShadowsOnly</c>만으로는 실제로 막히지 않는 경우가 있어 레이어 제외를 함께 쓴다
        /// (컬링 마스크는 <see cref="OnNetworkSpawn"/>에서 카메라에 적용).
        /// 남에게는 정상적으로 보여야 하므로 <b>소유자 화면에서만</b> 적용한다.
        /// </summary>
        private void ApplyModel(bool isGhost, bool isExorcist)
        {
            if (exorcistModel != null)
            {
                exorcistModel.SetActive(!isGhost);
            }
            if (ghostModel != null)
            {
                ghostModel.SetActive(isGhost);
            }

            if (!IsOwner)
            {
                return;
            }

            var active = isGhost ? ghostModel : exorcistModel;
            if (active == null)
            {
                return;
            }

            int localBodyLayer = LayerMask.NameToLayer(LocalBodyLayerName);
            foreach (var t in active.GetComponentsInChildren<Transform>(true))
            {
                if (localBodyLayer >= 0)
                {
                    t.gameObject.layer = localBodyLayer;
                }
            }

            // 그림자는 남긴다 — 자기 그림자가 없으면 붕 떠 보인다.
            foreach (var r in active.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }
        }

        private void SetEnabled<T>(bool value) where T : MonoBehaviour
        {
            var component = GetComponent<T>();
            if (component != null)
            {
                component.enabled = value;
            }
        }
    }
}
