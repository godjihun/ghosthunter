using GhostHunter.Core;
using GhostHunter.Exorcist;
using GhostHunter.Game;
using GhostHunter.Ghost;
using GhostHunter.Player;
using UnityEngine;

namespace GhostHunter.UI
{
    /// <summary>
    /// 게임 중 화면 표시 (기술 문서 6-2).
    ///
    ///  - 조준한 도구 옆에 "줍기 (E)" 프롬프트와 홀드 게이지
    ///  - 화면 하단 중앙 아이템 창
    ///  - 화면 중앙 조준점
    ///
    /// IMGUI라 씬 세팅이 필요 없다. 정식 UI Toolkit 화면으로 교체될 자리다.
    /// </summary>
    public class GameHudUI : MonoBehaviour
    {
        [SerializeField] private float slotSize = 84f;
        [SerializeField] private float slotBottomMargin = 24f;

        private GUIStyle promptStyle;
        private GUIStyle slotLabelStyle;
        private GUIStyle leftLabelStyle;
        private GUIStyle resultStyle;
        private GUIStyle wheelStyle;

        // ── 탐지 결과 표시 ──
        private ExorcistInventory subscribed;
        private string detectionText;
        private float detectionTimer;
        private bool detectionSuccess;
        private Texture2D barFill;
        private Texture2D barBack;

        /// <summary>
        /// 내 인벤토리의 탐지 결과 이벤트를 구독한다.
        ///
        /// 플레이어는 게임 시작 후에 스폰되므로 Start에서 한 번 잡을 수 없다.
        /// 매 프레임 확인하되 대상이 바뀔 때만 다시 연결한다.
        /// </summary>
        private void Update()
        {
            var local = NetworkPlayer.GetLocal();
            var inventory = local != null ? local.GetComponent<ExorcistInventory>() : null;

            if (inventory != subscribed)
            {
                if (subscribed != null)
                {
                    subscribed.OnDetectionResult -= OnDetectionResult;
                }
                subscribed = inventory;
                if (subscribed != null)
                {
                    subscribed.OnDetectionResult += OnDetectionResult;
                }
            }

            if (detectionTimer > 0f)
            {
                detectionTimer -= Time.deltaTime;
            }
        }

        private void OnDestroy()
        {
            if (subscribed != null)
            {
                subscribed.OnDetectionResult -= OnDetectionResult;
            }
        }

        /// <summary>
        /// 탐지 결과 수신 (시나리오 3-2).
        ///
        /// <b>성공과 실패의 표시 시간이 반드시 같아야 한다.</b> 실패가 빨리 사라지면
        /// 표시 길이 자체가 단서가 되어 "탐지 실패는 두 가지 가능성이 겹쳐 있다"는
        /// 이 게임의 추리 구조가 무너진다.
        /// </summary>
        private void OnDetectionResult(bool detected, ToolType tool)
        {
            detectionSuccess = detected;
            detectionText = detected
                ? $"반응 있음!  {tool.ToKorean()}은(는) 약점이고 귀신이 근처에 있다"
                : $"반응 없음.  {tool.ToKorean()}이(가) 약점이 아니거나 귀신이 없다";

            detectionTimer = GameManager.Config != null
                ? GameManager.Config.DetectionFeedbackDuration
                : 1.5f;
        }

        private void OnGUI()
        {
            if (!GameManager.IsGameplayActive)
            {
                return;
            }

            var local = NetworkPlayer.GetLocal();
            if (local == null)
            {
                return;
            }

            EnsureStyles();
            DrawCrosshair();

            // 도구·문 프롬프트는 양쪽 진영 공통.
            DrawInteractPrompt(local);
            DrawAltarPanel();
            DrawHidingNotice(local);
            DrawEmoteWheel(local);

            if (local.IsGhost)
            {
                DrawFearSkillPrompt(local);
                DrawSoulState(local);
            }
            else
            {
                DrawInventorySlot(local);
                DrawDetectionResult();
            }
        }

        /// <summary>탐지 결과를 화면 중앙 위쪽에 띄운다. 본인에게만 보인다.</summary>
        private void DrawDetectionResult()
        {
            if (detectionTimer <= 0f || string.IsNullOrEmpty(detectionText))
            {
                return;
            }

            resultStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };

            resultStyle.normal.textColor = detectionSuccess
                ? new Color(1f, 0.55f, 0.55f)
                : new Color(0.8f, 0.85f, 0.9f);

            var rect = new Rect(Screen.width * 0.5f - 260f, Screen.height * 0.26f, 520f, 56f);
            GUI.DrawTexture(rect, barBack);
            GUI.Label(rect, detectionText, resultStyle);
        }

        private void EnsureStyles()
        {
            promptStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
            };

            slotLabelStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
            };

            leftLabelStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white },
            };

            if (barFill == null)
            {
                barFill = MakeTexture(new Color(1f, 0.85f, 0.3f, 0.95f));
                barBack = MakeTexture(new Color(0f, 0f, 0f, 0.55f));
            }
        }

        private static Texture2D MakeTexture(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        private void DrawCrosshair()
        {
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), barFill);
        }

        /// <summary>조준한 대상(도구·문) 위에 프롬프트를 띄운다.</summary>
        private void DrawInteractPrompt(NetworkPlayer local)
        {
            var interactor = local.GetComponent<PlayerInteractor>();
            if (interactor == null || interactor.Target == null)
            {
                return;
            }

            var anchor = interactor.Target.PromptAnchor;
            if (anchor == null)
            {
                return;
            }

            if (!TryWorldToGui(anchor.position + Vector3.up * 0.5f, out float x, out float y))
            {
                return;
            }

            DrawLabel(x, y, interactor.Target.GetPrompt(local));
        }

        /// <summary>귀신이 밀착했을 때 상대 위에 "스킬 (Ctrl)"을 띄운다.</summary>
        private void DrawFearSkillPrompt(NetworkPlayer local)
        {
            var fear = local.GetComponent<FearSkill>();
            if (fear == null || fear.CurrentTarget == null)
            {
                return;
            }

            if (!TryWorldToGui(fear.CurrentTarget.transform.position + Vector3.up * 2.0f,
                    out float x, out float y))
            {
                return;
            }

            // 같은 Ctrl이지만 조사 단계는 흡수, 사냥 단계는 처형이다.
            string action = FearSkill.IsHuntPhase ? "처형 (Ctrl)" : "영혼 흡수 (Ctrl)";
            string text = fear.CooldownRemaining > 0f
                ? $"재사용까지 {Mathf.CeilToInt(fear.CooldownRemaining)}초"
                : action;

            DrawLabel(x, y, text);
        }

        /// <summary>
        /// 화면 우측 제단 현황 (시나리오 3-3).
        ///
        /// 헌납 목록은 <b>전원 공개</b>다 — 귀신도 본다. 자기 약점이 하나씩 맞춰지는 걸
        /// 지켜보는 압박이 이 게임의 긴장이고, 어차피 제단은 눈에 보이는 물건이다.
        /// </summary>
        private void DrawAltarPanel()
        {
            var altar = Altar.Instance;
            if (altar == null)
            {
                return;
            }

            var offered = altar.GetOffered();
            int capacity = GameManager.Config != null ? GameManager.Config.AltarCapacity : 3;

            const float w = 170f;
            float h = 52f + capacity * 24f;
            var rect = new Rect(Screen.width - w - 16f, 90f, w, h);

            GUI.DrawTexture(rect, barBack);
            GUI.Box(rect, GUIContent.none);

            GUILayout.BeginArea(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, rect.height - 16f));
            GUILayout.Label($"제단  {offered.Count}/{capacity}", leftLabelStyle);
            GUILayout.Space(4);

            for (int i = 0; i < capacity; i++)
            {
                GUILayout.Label(i < offered.Count ? $"· {offered[i].ToKorean()}" : "· ―", leftLabelStyle);
            }

            GUILayout.EndArea();
        }

        /// <summary>
        /// 귀신 전용 영혼 상태 표시 (시나리오 4번 [4]).
        ///
        /// 본체를 어디에 두고 왔는지 보이지 않으면 분리 자체를 쓸 수 없다.
        /// 본체 위치에 표식을 띄우고, 화면 밖이면 남은 거리만 알려준다.
        /// </summary>
        private void DrawSoulState(NetworkPlayer local)
        {
            var ghost = local.GetComponent<GhostController>();
            if (ghost == null)
            {
                return;
            }

            bool soulOut = ghost.IsSoulOut.Value;
            var hint = new Rect(Screen.width * 0.5f - 150f, Screen.height - 78f, 300f, 22f);
            GUI.DrawTexture(hint, barBack);
            GUI.Label(hint, soulOut ? "영혼 상태 — Q: 본체로 복귀" : "본체 상태 — Q: 영혼 분리", slotLabelStyle);

            if (!soulOut)
            {
                return;
            }

            Vector3 body = ghost.BodyWorldPosition.Value;
            float distance = Vector3.Distance(local.transform.position, body);

            if (TryWorldToGui(body + Vector3.up * 1.2f, out float x, out float y))
            {
                DrawLabel(x, y, $"본체  {distance:F0}m");
            }
            else
            {
                // 화면 밖이면 위치를 그릴 수 없으니 거리만.
                var far = new Rect(Screen.width * 0.5f - 150f, Screen.height - 102f, 300f, 22f);
                GUI.DrawTexture(far, barBack);
                GUI.Label(far, $"본체까지 {distance:F0}m (시야 밖)", slotLabelStyle);
            }
        }

        /// <summary>
        /// 은신 단계 안내 (시나리오 4번 [3]).
        ///
        /// 조작이 막혀 있다는 걸 알려주지 않으면 <b>고장난 줄 안다.</b>
        /// 왜 못 움직이는지와 언제 풀리는지를 같이 보여준다.
        /// </summary>
        private void DrawHidingNotice(NetworkPlayer local)
        {
            var manager = GameManager.Instance;
            if (manager == null || manager.Phase.Value != GamePhase.Hiding)
            {
                return;
            }

            int left = Mathf.CeilToInt(manager.PhaseTimeRemaining.Value);
            string text = local.IsGhost
                ? $"숨을 곳을 정하세요.  조사 시작까지 {left}초"
                : $"귀신이 숨는 중입니다. 대기하세요.  {left}초 후 조사 시작";

            resultStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };
            resultStyle.normal.textColor = Color.white;

            var rect = new Rect(Screen.width * 0.5f - 260f, Screen.height * 0.26f, 520f, 40f);
            GUI.DrawTexture(rect, barBack);
            GUI.Label(rect, text, resultStyle);
        }

        /// <summary>
        /// Tab을 누르고 있는 동안 뜨는 이모트 선택 휠.
        ///
        /// 마우스를 움직인 <b>방향</b>으로 고른다. 항목이 둘뿐이라 위/아래로 갈리는데,
        /// 나중에 늘어나도 각도 계산이 자동으로 나눠 갖는다.
        /// </summary>
        private void DrawEmoteWheel(NetworkPlayer local)
        {
            var emote = local.GetComponent<PlayerEmote>();
            if (emote == null || !emote.WheelOpen)
            {
                return;
            }

            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            const float radius = 110f;

            wheelStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };

            // 가운데 안내 — 어떻게 고르고 어떻게 취소하는지 둘 다 알려준다.
            var hint = new Rect(cx - 110f, cy - 22f, 220f, 44f);
            GUI.DrawTexture(hint, barBack);
            GUI.Label(new Rect(hint.x, hint.y + 2f, hint.width, 20f),
                emote.HighlightedIndex >= 0 ? "좌클릭으로 재생" : "마우스를 움직여 선택", slotLabelStyle);
            GUI.Label(new Rect(hint.x, hint.y + 22f, hint.width, 20f),
                "Tab을 놓으면 취소", slotLabelStyle);

            var names = PlayerEmote.EmoteNames;
            for (int i = 0; i < names.Length; i++)
            {
                float angle = PlayerEmote.AngleOf(i) * Mathf.Deg2Rad;
                float x = cx + Mathf.Sin(angle) * radius;
                float y = cy - Mathf.Cos(angle) * radius;

                bool selected = emote.HighlightedIndex == i;
                var rect = new Rect(x - 62f, y - 20f, 124f, 40f);

                GUI.DrawTexture(rect, selected ? barFill : barBack);
                wheelStyle.normal.textColor = selected ? Color.black : Color.white;
                GUI.Label(rect, names[i], wheelStyle);
            }
        }

        /// <summary>월드 좌표를 GUI 좌표로. GUI는 y가 위아래 뒤집혀 있다.</summary>
        private static bool TryWorldToGui(Vector3 world, out float x, out float y)
        {
            x = y = 0f;
            var cam = Camera.main;
            if (cam == null)
            {
                return false;
            }

            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f)
            {
                return false; // 카메라 뒤
            }

            x = sp.x;
            y = Screen.height - sp.y;
            return true;
        }

        private void DrawLabel(float x, float y, string text)
        {
            var size = promptStyle.CalcSize(new GUIContent(text));
            float w = Mathf.Max(size.x + 20f, 90f);
            var rect = new Rect(x - w * 0.5f, y - 26f, w, 24f);
            GUI.DrawTexture(rect, barBack);
            GUI.Label(rect, text, promptStyle);
        }


        /// <summary>화면 하단 중앙 아이템 창. 비어 있어도 자리를 유지해 위치를 익히게 한다.</summary>
        private void DrawInventorySlot(NetworkPlayer local)
        {
            var inventory = local.GetComponent<ExorcistInventory>();
            if (inventory == null)
            {
                return;
            }

            var rect = new Rect(
                (Screen.width - slotSize) * 0.5f,
                Screen.height - slotSize - slotBottomMargin,
                slotSize, slotSize);

            GUI.DrawTexture(rect, barBack);
            GUI.Box(rect, GUIContent.none);

            if (inventory.HasTool.Value)
            {
                var type = inventory.HeldTool.Value;
                GUI.Label(new Rect(rect.x, rect.y + rect.height * 0.5f - 10f, rect.width, 20f),
                    type.ToKorean(), slotLabelStyle);
                GUI.Label(new Rect(rect.x, rect.yMax + 2f, rect.width, 18f),
                    "좌클릭 사용 / G 버리기", slotLabelStyle);

            }
            else
            {
                GUI.Label(new Rect(rect.x, rect.y + rect.height * 0.5f - 10f, rect.width, 20f),
                    "비어 있음", slotLabelStyle);
            }
        }
    }
}
