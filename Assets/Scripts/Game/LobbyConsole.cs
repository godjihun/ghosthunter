using GhostHunter.Core;
using GhostHunter.Player;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace GhostHunter.Game
{
    /// <summary>
    /// 대기방의 게임 설정 단말. 방장이 F로 열어 밸런스를 조절하고 게임을 시작한다.
    ///
    /// <b>NetworkBehaviour가 아니다.</b> 자체 동기화 상태가 없고, 조작할 수 있는 사람이
    /// 방장 = 서버뿐이라 RPC를 거칠 이유가 없다. 방장이 누른 것은 그 자리에서
    /// 서버 코드(<see cref="GameManager.StartGame"/> 등)로 바로 들어간다.
    /// 씬에 놓인 오브젝트라 전원이 같은 것을 갖고 있고, 남들에게는 프롬프트만 다르게 보인다.
    ///
    /// <b>이 패널은 방장만 열 수 있다</b>(<see cref="CanInteract"/>) — 그래서 패널 안에서
    /// 따로 "너 방장 아니잖아"를 또 검사하지 않는다. 못 여는 사람은 애초에 못 들어온다.
    /// </summary>
    public class LobbyConsole : MonoBehaviour, IInteractable
    {
        [Header("F키 상호작용")]
        [Tooltip("프롬프트를 띄울 위치. 비우면 오브젝트 원점.")]
        [SerializeField] private Transform promptAnchor;

        [Header("UGUI 패널")]
        [Tooltip("F로 여닫는 설정 패널(RoomSettingPanel). 기본은 꺼져 있어야 한다.")]
        [SerializeField] private GameObject settingPanelRoot;

        [Header("밸런스 슬라이더 (LobbySettings.Fields와 순서·개수가 일치해야 함)")]
        [SerializeField] private Slider[] settingSliders;
        [SerializeField] private TextMeshProUGUI[] settingValueTexts;

        [Header("버튼")]
        [SerializeField] private Button startButton;
        [SerializeField] private Button closeButton;

        private bool panelOpen;
        private bool slidersInitialized;
        private bool suppressSliderCallback;

        /// <summary>방장인가. 방을 만든 사람이 곧 서버라 클라이언트 번호로 가른다.</summary>
        private static bool IsHostPlayer(NetworkPlayer viewer)
        {
            return viewer != null && viewer.OwnerClientId == NetworkManager.ServerClientId;
        }

        private static bool InLobby => GameManager.CurrentPhase == GamePhase.Lobby;

        private void Awake()
        {
            if (startButton != null)
            {
                startButton.onClick.AddListener(OnStartClicked);
            }
            if (closeButton != null)
            {
                closeButton.onClick.AddListener(() => SetPanelOpen(false));
            }

            if (settingSliders != null)
            {
                for (int i = 0; i < settingSliders.Length; i++)
                {
                    int index = i; // 클로저 캡처용 지역 복사
                    if (settingSliders[i] != null)
                    {
                        settingSliders[i].onValueChanged.AddListener(v => OnSliderChanged(index, v));
                    }
                }
            }

            if (settingPanelRoot != null)
            {
                settingPanelRoot.SetActive(false);
            }
        }

        // ── IInteractable ──────────────────────────────────────────

        public Transform PromptAnchor => promptAnchor != null ? promptAnchor : transform;

        public bool CanInteract(NetworkPlayer viewer) => InLobby && IsHostPlayer(viewer);

        public string GetPrompt(NetworkPlayer viewer)
        {
            if (!InLobby)
            {
                return null;
            }

            // <b>남에게도 문구는 띄운다.</b> 아무 반응이 없으면 고장인 줄 알고
            // 계속 누르게 된다. 왜 안 되는지 알려주는 편이 낫다.
            return IsHostPlayer(viewer) ? "게임 설정 (F)" : "방장만 사용할 수 있다";
        }

        /// <summary>PlayerInteractor가 F 입력을 넘겨준다.</summary>
        public void Interact(NetworkPlayer viewer)
        {
            if (!CanInteract(viewer))
            {
                return;
            }

            SetPanelOpen(!panelOpen);
        }

        // ── 패널 ──────────────────────────────────────────────────

        private void SetPanelOpen(bool open)
        {
            panelOpen = open;

            if (settingPanelRoot != null)
            {
                settingPanelRoot.SetActive(open);
            }

            // 커서는 PlayerRoleSetup이 단독으로 관리한다. 여기서 Cursor를 직접 만지면
            // 매 프레임 서로 덮어써서 깜빡인다.
            PlayerRoleSetup.UiPanelOpen = open;
        }

        private void Update()
        {
            if (!panelOpen)
            {
                return;
            }

            // 게임이 시작되면 창을 붙들고 있을 이유가 없다. 열린 채로 저택에 떨어지면
            // 커서가 풀린 상태로 게임이 시작된다.
            if (!InLobby)
            {
                SetPanelOpen(false);
                return;
            }

            RefreshSliders();
        }

        private void OnDisable()
        {
            // 창을 연 채로 씬이 정리되면 커서가 풀린 채 남는다.
            if (panelOpen)
            {
                SetPanelOpen(false);
            }
        }

        // ── 밸런스 슬라이더 ─────────────────────────────────────────

        private void RefreshSliders()
        {
            var manager = GameManager.Instance;
            if (manager == null || settingSliders == null)
            {
                return;
            }

            int count = Mathf.Min(settingSliders.Length, LobbySettings.Fields.Length);
            var settings = manager.Settings.Value;

            if (!slidersInitialized)
            {
                slidersInitialized = true;
                suppressSliderCallback = true;

                for (int i = 0; i < count; i++)
                {
                    if (settingSliders[i] == null)
                    {
                        continue;
                    }
                    var (_, min, max, _, _) = LobbySettings.Fields[i];
                    settingSliders[i].minValue = min;
                    settingSliders[i].maxValue = max;
                    settingSliders[i].wholeNumbers = false;
                }

                suppressSliderCallback = false;
            }

            for (int i = 0; i < count; i++)
            {
                float value = settings[i];
                var (_, _, _, _, unit) = LobbySettings.Fields[i];

                if (settingSliders[i] != null && !Mathf.Approximately(settingSliders[i].value, value))
                {
                    // 방장이 곧 서버라 슬라이더에 도착한 값은 자기 자신이 방금 바꾼 값이거나
                    // 초기값뿐이다 — 그래도 되돌려 쓰는 값이 또 전송되는 걸 막기 위해 억제한다.
                    suppressSliderCallback = true;
                    settingSliders[i].value = value;
                    suppressSliderCallback = false;
                }

                if (settingValueTexts != null && i < settingValueTexts.Length && settingValueTexts[i] != null)
                {
                    settingValueTexts[i].text = $"{value:0.#}{unit}";
                }
            }
        }

        private void OnSliderChanged(int index, float raw)
        {
            if (suppressSliderCallback || index >= LobbySettings.Fields.Length)
            {
                return;
            }

            var manager = GameManager.Instance;
            if (manager == null)
            {
                return;
            }

            var (_, _, _, step, _) = LobbySettings.Fields[index];
            float quantized = Mathf.Round(raw / step) * step;

            var settings = manager.Settings.Value;
            if (Mathf.Approximately(settings[index], quantized))
            {
                return;
            }

            settings[index] = quantized;

            // 방장이 곧 서버라 그대로 서버 함수를 부른다.
            manager.ServerSetSettings(settings);
        }

        private void OnStartClicked()
        {
            // 이 패널은 방장만 열 수 있으므로(CanInteract) 여기 도달했다는 것 자체가
            // 이미 방장이라는 뜻이다. 그래도 서버 함수 쪽의 IsServer 가드가 최종 방어선이다.
            GameManager.Instance?.StartGame();
            SetPanelOpen(false);
        }
    }
}
