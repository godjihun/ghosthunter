using GhostHunter.Core;
using GhostHunter.Game;
using GhostHunter.Player;
using UnityEngine;

namespace GhostHunter.UI
{
    /// <summary>
    /// GameScene의 HUD 그룹(대기방·역할 설명·공통·귀신·퇴마사·관전·결과) 전체의 표시 여부를 관리한다.
    ///
    /// <b>이 스크립트는 자신이 관리하는 패널들과 다른 오브젝트에 있어야 한다</b> — 관리 대상
    /// 안에 넣고 그 오브젝트를 SetActive(false)로 끄면, 그 순간 이 스크립트의 Update도
    /// 같이 멈춰서 다시 켜줄 사람이 없어진다(FadeTransition에서 겪은 것과 같은 함정).
    ///
    /// <b>관리 대상은 에디터에서 꺼둔 채로 저장해도 된다.</b> <see cref="Awake"/>에서 한 번
    /// 전부 강제로 켰다가(그 안의 스크립트가 Awake/OnEnable을 확실히 거치도록) 바로 실제
    /// 초기 상태로 되돌리기 때문이다.
    /// </summary>
    public class GameHudController : MonoBehaviour
    {
        [Header("대기방 (Lobby 단계에만 표시)")]
        [SerializeField] private GameObject waitingHud;

        [Header("결과 (Result 단계에만 표시)")]
        [SerializeField] private GameObject resultHud;

        [Header("역할 설명 (게임 시작 시 한 번, 페이드로 몇 초 보여줌)")]
        [Tooltip("페이드를 담당할 CanvasGroup. ExplainHud 자신에 붙인다.")]
        [SerializeField] private CanvasGroup explainHudGroup;
        [SerializeField] private GameObject explainGhostPanel;
        [SerializeField] private GameObject explainExorcistPanel;
        [SerializeField] private float explainFadeDuration = 0.4f;
        [SerializeField] private float explainHoldDuration = 5f;

        [Header("게임 중 HUD")]
        [Tooltip("양 진영·관전자 공통. 조사·사냥 단계에 항상 켜진다.")]
        [SerializeField] private GameObject commonHud;
        [SerializeField] private GameObject ghostHud;
        [SerializeField] private GameObject exorcistHud;
        [Tooltip("죽어서 관전 중일 때 ghostHud/exorcistHud 대신 켜진다.")]
        [SerializeField] private GameObject spectatorHud;

        private enum ExplainStage { Hidden, FadingIn, Holding, FadingOut }

        private GamePhase lastPhase;
        private bool phaseKnown;
        private ExplainStage explainStage = ExplainStage.Hidden;
        private float explainTimer;

        private void Awake()
        {
            // 편집 중 꺼둔 채로 저장해도 상관없게, 시작할 때 한 번 강제로 켜서
            // 그 안의 스크립트들(Awake/OnEnable)이 확실히 초기화되게 한다.
            ForceActivateOnce(waitingHud);
            ForceActivateOnce(resultHud);
            ForceActivateOnce(explainHudGroup != null ? explainHudGroup.gameObject : null);
            ForceActivateOnce(commonHud);
            ForceActivateOnce(ghostHud);
            ForceActivateOnce(exorcistHud);
            ForceActivateOnce(spectatorHud);

            // ExplainHud 자신은 항상 켜둔다 — 알파로만 여닫으므로 (FadeTransition과 같은 이유).
            if (explainHudGroup != null)
            {
                explainHudGroup.alpha = 0f;
                explainHudGroup.interactable = false;
                explainHudGroup.blocksRaycasts = false;
            }

            if (resultHud != null) resultHud.SetActive(false);
            SetGameplayHudActive(false);
        }

        private static void ForceActivateOnce(GameObject go)
        {
            if (go != null && !go.activeSelf)
            {
                go.SetActive(true);
            }
        }

        private void Update()
        {
            TickPhaseTransition();

            var phase = GameManager.CurrentPhase;

            if (phase == GamePhase.Lobby)
            {
                CancelExplain();
                SetGameplayHudActive(false);
                if (resultHud != null) resultHud.SetActive(false);
                if (waitingHud != null) waitingHud.SetActive(true);
                return;
            }

            if (waitingHud != null)
            {
                waitingHud.SetActive(false);
            }

            if (phase == GamePhase.Result)
            {
                CancelExplain();
                SetGameplayHudActive(false);
                if (resultHud != null) resultHud.SetActive(true);
                return;
            }

            if (resultHud != null)
            {
                resultHud.SetActive(false);
            }

            TickExplain();

            if (explainStage != ExplainStage.Hidden)
            {
                return; // 설명 화면이 떠 있는 동안은 나머지 HUD를 아직 안 켠다.
            }

            ApplyRoleHud();
        }

        /// <summary>Lobby → Hiding으로 바뀌는 순간(=판이 막 시작한 순간)만 잡는다. 재은신은 포함하지 않는다.</summary>
        private void TickPhaseTransition()
        {
            var phase = GameManager.CurrentPhase;

            if (!phaseKnown)
            {
                phaseKnown = true;
                lastPhase = phase;
                return;
            }

            if (phase == lastPhase)
            {
                return;
            }

            bool justStarted = phase == GamePhase.Hiding && lastPhase == GamePhase.Lobby;
            lastPhase = phase;

            if (justStarted)
            {
                StartExplain();
            }
        }

        private void StartExplain()
        {
            bool isGhost = NetworkPlayer.GetLocal()?.IsGhost ?? false;

            SetGameplayHudActive(false);

            if (explainGhostPanel != null) explainGhostPanel.SetActive(isGhost);
            if (explainExorcistPanel != null) explainExorcistPanel.SetActive(!isGhost);

            explainStage = ExplainStage.FadingIn;
            explainTimer = 0f;
        }

        private void CancelExplain()
        {
            explainStage = ExplainStage.Hidden;
            if (explainHudGroup != null)
            {
                explainHudGroup.alpha = 0f;
                explainHudGroup.interactable = false;
                explainHudGroup.blocksRaycasts = false;
            }
        }

        private void TickExplain()
        {
            if (explainStage == ExplainStage.Hidden || explainHudGroup == null)
            {
                return;
            }

            explainTimer += Time.deltaTime;

            switch (explainStage)
            {
                case ExplainStage.FadingIn:
                    explainHudGroup.alpha = Mathf.Clamp01(explainTimer / Mathf.Max(0.01f, explainFadeDuration));
                    if (explainTimer >= explainFadeDuration)
                    {
                        explainHudGroup.alpha = 1f;
                        explainTimer = 0f;
                        explainStage = ExplainStage.Holding;
                    }
                    break;

                case ExplainStage.Holding:
                    if (explainTimer >= explainHoldDuration)
                    {
                        explainTimer = 0f;
                        explainStage = ExplainStage.FadingOut;
                    }
                    break;

                case ExplainStage.FadingOut:
                    explainHudGroup.alpha = 1f - Mathf.Clamp01(explainTimer / Mathf.Max(0.01f, explainFadeDuration));
                    if (explainTimer >= explainFadeDuration)
                    {
                        explainHudGroup.alpha = 0f;
                        explainStage = ExplainStage.Hidden;
                    }
                    break;
            }
        }

        private void ApplyRoleHud()
        {
            var local = NetworkPlayer.GetLocal();
            bool isGhost = local != null && local.IsGhost;

            var spectator = local != null ? local.GetComponent<PlayerSpectator>() : null;
            bool isSpectating = spectator != null && spectator.IsSpectating;

            if (commonHud != null) commonHud.SetActive(true);

            if (isSpectating)
            {
                if (ghostHud != null) ghostHud.SetActive(false);
                if (exorcistHud != null) exorcistHud.SetActive(false);
                if (spectatorHud != null) spectatorHud.SetActive(true);
                return;
            }

            if (spectatorHud != null) spectatorHud.SetActive(false);
            if (ghostHud != null) ghostHud.SetActive(isGhost);
            if (exorcistHud != null) exorcistHud.SetActive(!isGhost);
        }

        private void SetGameplayHudActive(bool value)
        {
            if (commonHud != null) commonHud.SetActive(value);
            if (ghostHud != null) ghostHud.SetActive(value);
            if (exorcistHud != null) exorcistHud.SetActive(value);
            if (spectatorHud != null) spectatorHud.SetActive(value);
        }
    }
}
