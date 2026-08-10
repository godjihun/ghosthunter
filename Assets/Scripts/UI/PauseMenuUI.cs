using GhostHunter.Player;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// 백틱(`)으로 여는 일시정지 창. 메뉴(<see cref="menuPanel"/>)와 설정(<see cref="mousePanel"/>,
    /// 마우스 감도)이 전부 UGUI다 — IMGUI는 더 남아있지 않다.
    ///
    /// <b>ESC를 쓰면 안 된다.</b> 브라우저가 ESC를 포인터 잠금 해제·전체화면 종료에
    /// 먼저 써버려서 Unity까지 오지 않는다 — 웹 빌드에서 창이 아예 안 열린다.
    /// 백틱은 브라우저가 가로채지 않는다.
    ///
    /// <b>키는 액션맵을 거치지 않고 키보드를 직접 읽는다.</b> 액션맵은 진영·상태에 따라
    /// Exorcist / Ghost / Spectator로 갈리는데, 이 창은 <b>어느 상태에서든</b> 열려야 한다.
    /// 네 맵 모두에 같은 액션을 넣으면 하나 빠뜨렸을 때 "특정 상황에서만 안 되는"
    /// 찾기 어려운 버그가 된다. 레거시 Input Manager가 아니라 새 Input System의 API다.
    /// </summary>
    public class PauseMenuUI : MonoBehaviour
    {
        [Header("전체 (StopHud) — 편집 중 꺼둔 채로 저장해도 된다")]
        [Tooltip("이 패널의 부모. 시작할 때 한 번 강제로 켜서 자식들(MenuPanel/MousePanel/DisconnectedPanel)이 초기화되게 한다.")]
        [SerializeField] private GameObject stopHud;

        [Header("메뉴 패널 (StopHud/MenuPanel)")]
        [SerializeField] private GameObject menuPanel;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button leaveButton;
        [SerializeField] private Button resumeButton;

        [Header("설정 패널 (StopHud/MousePanel) — 마우스 감도")]
        [SerializeField] private GameObject mousePanel;
        [SerializeField] private Slider sensitivitySlider;
        [SerializeField] private TextMeshProUGUI sensitivityValueText;
        [SerializeField] private Button mousePanelBackButton;

        private const float MinSensitivity = 0.2f;
        private const float MaxSensitivity = 3f;

        private bool open;
        private bool settingsOpen;

        /// <summary>지난 프레임에 포인터가 잠겨 있었는가. 아래 Update 주석 참고.</summary>
        private bool wasLocked;

        private void Awake()
        {
            // 편집 중 StopHud를 꺼둔 채로 저장해도 상관없게, 시작할 때 한 번 강제로 켜서
            // 그 안의 오브젝트들이 확실히 초기화되게 한다(다른 Hud 그룹과 같은 패턴).
            if (stopHud != null && !stopHud.activeSelf)
            {
                stopHud.SetActive(true);
            }

            if (settingsButton != null) settingsButton.onClick.AddListener(() => SetSettingsOpen(true));
            if (leaveButton != null) leaveButton.onClick.AddListener(LeaveToLobby);
            if (resumeButton != null) resumeButton.onClick.AddListener(() => SetOpen(false));
            if (mousePanelBackButton != null) mousePanelBackButton.onClick.AddListener(() => SetSettingsOpen(false));

            if (sensitivitySlider != null)
            {
                sensitivitySlider.minValue = MinSensitivity;
                sensitivitySlider.maxValue = MaxSensitivity;
                sensitivitySlider.wholeNumbers = false;
                sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged);
            }

            if (menuPanel != null) menuPanel.SetActive(false);
            if (mousePanel != null) mousePanel.SetActive(false);
        }

        private void Update()
        {
            // 접속 전(로비 화면)에는 열 이유가 없다. 거기엔 이미 나가는 버튼이 있고,
            // 커서도 원래 풀려 있다.
            bool hasBody = NetworkPlayer.GetLocal() != null;

            DetectPointerLockLost(hasBody);

            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.backquoteKey.wasPressedThisFrame)
            {
                return;
            }

            if (!hasBody)
            {
                return;
            }

            // 설정을 열어둔 채 다시 누르면 설정만 닫는다. 한 번에 다 닫히면
            // 되돌아갈 방법이 없어 답답하다.
            if (open && settingsOpen)
            {
                SetSettingsOpen(false);
                return;
            }

            SetOpen(!open);
        }

        /// <summary>
        /// 포인터 잠금이 풀리는 순간을 잡아 메뉴를 연다. <b>ESC를 되살리는 우회로다.</b>
        ///
        /// 브라우저는 ESC로 포인터 잠금을 푸는 것을 <b>페이지가 막을 수 없게</b> 정해두었다
        /// (커서를 가둬놓고 못 빠져나가게 하는 것을 막기 위해). 그래서 우리 코드가
        /// 먼저 돌 방법은 없고, ESC 키 이벤트 자체가 안 오기도 한다.
        ///
        /// 그래서 키를 기다리는 대신 <b>결과를 신호로 쓴다</b> — 잠겨 있어야 할 상황에서
        /// 잠금이 풀렸다면 사용자가 ESC를 눌렀다는 뜻이다.
        ///
        /// <b>바뀌는 순간만</b> 본다. 매 프레임 "안 잠김"으로 판단하면, 창을 닫고
        /// 다시 잠기기까지의 한두 프레임 동안 창이 곧바로 다시 열린다.
        /// (WebGL은 사용자가 클릭해야 잠금이 걸려서 그 틈이 길 수도 있다.)
        ///
        /// 탭 전환이나 알트탭으로 잠금이 풀릴 때도 열린다 — 그건 오히려 자연스럽다.
        /// </summary>
        private void DetectPointerLockLost(bool hasBody)
        {
            bool locked = Cursor.lockState == CursorLockMode.Locked;

            bool shouldBeLocked = hasBody
                && Game.GameManager.IsFirstPersonActive
                && !PlayerRoleSetup.UiPanelOpen;

            if (wasLocked && !locked && shouldBeLocked && !open)
            {
                SetOpen(true);
            }

            wasLocked = locked;
        }

        private void SetOpen(bool value)
        {
            open = value;
            if (!value)
            {
                settingsOpen = false;
            }

            // 커서는 PlayerRoleSetup이 단독으로 관리한다. 여기서 Cursor를 직접 만지면
            // 매 프레임 서로 덮어써서 깜빡인다.
            PlayerRoleSetup.UiPanelOpen = value;
            RefreshPanels();
        }

        private void SetSettingsOpen(bool value)
        {
            settingsOpen = value;

            // 설정을 여는 순간 슬라이더를 현재 값으로 맞춘다. SetValueWithoutNotify를 써야
            // OnSensitivityChanged가 다시 불려서 PlayerPrefs에 헛되이 쓰는 걸 피한다.
            if (value && sensitivitySlider != null)
            {
                sensitivitySlider.SetValueWithoutNotify(PlayerLook.SensitivityScale);
                ApplySensitivityText(PlayerLook.SensitivityScale);
            }

            RefreshPanels();
        }

        /// <summary>메뉴/설정 패널은 서로 배타적이다 — 열려 있어도 한 번에 하나만 보인다.</summary>
        private void RefreshPanels()
        {
            if (menuPanel != null) menuPanel.SetActive(open && !settingsOpen);
            if (mousePanel != null) mousePanel.SetActive(open && settingsOpen);
        }

        /// <summary>
        /// 슬라이더 값을 감도에 반영한다. 값은 <see cref="PlayerLook.SensitivityScale"/>에 저장된다
        /// (static, PlayerPrefs 백업 — 씬을 넘나들어도 유지된다. SettingsPanel 주석 참고).
        ///
        /// 아주 미세한 값까지 그대로 적용하면 매 프레임 PlayerPrefs에 쓰게 되므로 반올림한 뒤,
        /// <b>슬라이더 핸들도 그 반올림된 값으로 다시 스냅</b>시킨다 — 안 하면 핸들 위치와
        /// 실제 적용된 감도가 미세하게 어긋난 채로 남는다.
        /// </summary>
        private void OnSensitivityChanged(float value)
        {
            float rounded = Mathf.Round(value * 20f) / 20f;
            PlayerLook.SensitivityScale = rounded;
            ApplySensitivityText(rounded);

            if (sensitivitySlider != null && !Mathf.Approximately(sensitivitySlider.value, rounded))
            {
                sensitivitySlider.SetValueWithoutNotify(rounded);
            }
        }

        private void ApplySensitivityText(float value)
        {
            if (sensitivityValueText != null)
            {
                sensitivityValueText.text = $"{value:F2}배";
            }
        }

        private void OnDisable()
        {
            // 창을 연 채로 정리되면 커서가 풀린 채 남는다.
            if (open)
            {
                SetOpen(false);
            }
        }

        /// <summary>
        /// 접속을 끊고 로비(MainMenuScene의 방 만들기·코드 참가 화면)로 돌아간다.
        ///
        /// 여기서 직접 씬을 로드하지 않는다 — 접속이 끊기면 <see cref="Game.NetworkBootstrapUI"/>의
        /// 연결 끊김 폴백이 알아서 MainMenuScene으로 되돌린다.
        ///
        /// <b>방장이 나가면 방이 사라진다</b> — 방장이 곧 서버이기 때문이다.
        /// 남은 사람들은 접속이 끊기고 각자 로비로 돌아간다.
        /// </summary>
        private void LeaveToLobby()
        {
            SetOpen(false);

            var nm = NetworkManager.Singleton;
            if (nm != null && (nm.IsClient || nm.IsServer))
            {
                nm.Shutdown();
            }
        }
    }
}
