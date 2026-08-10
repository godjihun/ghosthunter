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
        private GUIStyle hintStyle;

        // ── 우측 하단 조작 안내 ──
        //
        // 상황마다 쓸 수 있는 키가 다르므로 미리 나눠 둔다. 매 프레임 문자열을
        // 조립하면 OnGUI가 프레임당 여러 번 불리는 만큼 쓰레기가 쌓인다.
        private static readonly string[] ExorcistHints =
        {
            "WASD 이동   Shift 달리기   Space 점프",
            "F 줍기·문·헌납   좌클릭 사용   G 버리기",
            "Tab 이모트",
            "` 또는 ESC   메뉴",
        };

        private static readonly string[] GhostHints =
        {
            "WASD 이동   Shift 달리기   Space 점프",
            "Q 영혼 분리 / 복귀",
            "Ctrl 흡수(조사) / 처형(사냥)",
            "` 또는 ESC   메뉴",
        };

        private static readonly string[] SpectatorHints =
        {
            "A / D   관전 대상 변경",
            "` 또는 ESC   메뉴",
        };

        // 단계 전환·약점 발견 안내는 AnnouncementUI(UGUI)로 옮겼다 — 여기서는 더 이상 다루지 않는다.

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
            // ⚠️ 게이지·제단 현황·귀신 스킬바·인벤토리·탐지 결과·안내 문구·조작 안내는
            // CommonHud/GhostHud/ExorcistHud(UGUI, GameHudController가 관리)로 옮겨갔으니
            // 여기서 다시 그리지 않는다 — 그 Draw* 메서드들은 참고용으로 아래에 남겨뒀다.
            //
            // 아직 UGUI로 안 옮긴 것만 여기서 계속 그린다: 조준점, F 상호작용 프롬프트, 이모트 휠.
            // 이 셋은 도구·문·제단·키오스크 앞에서 "F로 뭘 할 수 있는지"를 보여주는 것이라
            // 게임 진행에 필요하다 — UGUI로 옮기기 전까지는 계속 켜둔다.

            HudFont.ApplyToSkin();

            if (!GameManager.IsFirstPersonActive)
            {
                return;
            }

            var local = NetworkPlayer.GetLocal();
            if (local == null)
            {
                return;
            }

            EnsureStyles();

            // 관전 중에는 조준점·프롬프트가 의미 없다 — 남의 시점을 보는데 내 조작 UI가
            // 떠 있으면 조작되는 줄 안다.
            var spectator = local.GetComponent<PlayerSpectator>();
            if (spectator != null && spectator.IsSpectating)
            {
                return;
            }

            DrawCrosshair();
            DrawInteractPrompt(local);
            DrawEmoteWheel(local);
        }

        /// <summary>
        /// 현실화 게이지 (시나리오 3-4, UI 명세 S5-A).
        ///
        /// <b>양 진영 모두에게 보여준다.</b> 게이지는 위치 정보를 담지 않으므로 공개해도
        /// 은닉이 깨지지 않고, 퇴마사에게는 "현실화가 임박했다"는 압박이 된다.
        ///
        /// 조사 단계에만 띄운다 — 사냥에 들어가면 이미 현실화된 뒤라 진행도가 의미 없고,
        /// 그 자리는 남은 사냥 시간이 대신한다.
        /// </summary>
        private void DrawMaterializeGauge()
        {
            var manager = GameManager.Instance;
            if (manager == null || manager.Phase.Value != GamePhase.Investigation)
            {
                return;
            }

            float ratio = Mathf.Clamp01(manager.MaterializeGauge.Value / 100f);

            const float width = 260f;
            const float height = 18f;
            var back = new Rect(Screen.width * 0.5f - width * 0.5f, 14f, width, height);

            GUI.DrawTexture(back, barBack);
            if (ratio > 0f)
            {
                GUI.DrawTexture(new Rect(back.x, back.y, back.width * ratio, back.height), barFill);
            }

            GUI.Label(back, $"현실화 {manager.MaterializeGauge.Value:F0}%", slotLabelStyle);
        }

        /// <summary>
        /// 관전 화면 (시나리오 3-6 / UI 명세 S6).
        ///
        /// 하단 중앙에 대상 닉네임, 그 좌우에 화살표를 둔다.
        /// 화살표는 <b>클릭도 되고 A/D로도 된다</b> — 관전 중에는 커서가 잠겨 있어
        /// 실제로는 키보드를 쓰게 되지만, 무엇을 누르면 되는지 눈에 보여야 한다.
        /// </summary>
        private void DrawSpectatorPanel(PlayerSpectator spectator)
        {
            var banner = new Rect(Screen.width * 0.5f - 130f, 44f, 260f, 26f);
            GUI.DrawTexture(banner, barBack);
            GUI.Label(banner, "사망 — 관전 중", resultStyle);

            float cx = Screen.width * 0.5f;
            float y = Screen.height - 78f;

            if (spectator.Target == null)
            {
                var none = new Rect(cx - 150f, y, 300f, 26f);
                GUI.DrawTexture(none, barBack);
                GUI.Label(none, "관전할 수 있는 생존자가 없습니다", slotLabelStyle);
                return;
            }

            var name = new Rect(cx - 90f, y, 180f, 26f);
            GUI.DrawTexture(name, barBack);
            GUI.Label(name, spectator.Target.DisplayName, slotLabelStyle);

            var left = new Rect(name.x - 34f, y, 30f, 26f);
            var right = new Rect(name.xMax + 4f, y, 30f, 26f);
            GUI.DrawTexture(left, barBack);
            GUI.DrawTexture(right, barBack);
            GUI.Label(left, "◀", slotLabelStyle);
            GUI.Label(right, "▶", slotLabelStyle);

            var hint = new Rect(cx - 150f, y + 28f, 300f, 20f);
            GUI.Label(hint, "A / D 로 대상 변경", slotLabelStyle);
        }

        /// <summary>
        /// 귀신 전용 스킬바 (좌하단).
        ///
        /// 단계에 따라 첫 칸의 내용이 바뀐다 — 조사에서는 <b>공포스킬</b>, 사냥에서는 <b>킬</b>이다.
        /// 같은 `Ctrl` 키가 하는 일이 달라지므로, 칸 자체가 바뀌어야 지금 뭘 하는지 알 수 있다.
        ///
        /// <b>흐릿함 = 지금은 못 쓴다</b>는 뜻이다. 대상이 사거리에 들어오면 밝아진다.
        /// 쿨타임 중에는 남은 초를 대신 보여준다.
        /// </summary>
        private void DrawGhostSkillBar(NetworkPlayer local)
        {
            var fear = local.GetComponent<FearSkill>();
            var ghost = local.GetComponent<GhostController>();
            var phase = GameManager.CurrentPhase;

            const float w = 190f;
            const float h = 38f;
            const float gap = 6f;
            float x = 16f;
            float y = Screen.height - 16f - h;

            // 아래에서 위로 쌓는다. 영혼 분리는 조사 단계에만 의미가 있다.
            if (phase == GamePhase.Investigation && ghost != null)
            {
                bool soulOut = ghost.IsSoulOut.Value;
                DrawSkillSlot(new Rect(x, y, w, h),
                    soulOut ? "본체로 복귀" : "영혼 분리", "Q",
                    active: true, cooldown: 0f);
                y -= h + gap;
            }

            if (fear == null)
            {
                return;
            }

            // 사냥 단계에서는 같은 자리가 처형으로 바뀐다.
            bool hunt = phase == GamePhase.Hunt;
            string label = hunt ? "킬" : "공포스킬";

            // 조사 단계의 흡수는 영혼 상태에서만 쓸 수 있다.
            bool phaseOk = hunt || (phase == GamePhase.Investigation
                                    && ghost != null && ghost.IsSoulOut.Value);

            DrawSkillSlot(new Rect(x, y, w, h), label, "Ctrl",
                active: phaseOk && fear.HasTargetInRange && fear.CooldownRemaining <= 0f,
                cooldown: fear.CooldownRemaining);
        }

        /// <summary>스킬 한 칸. 쓸 수 없으면 전체를 흐리게 그린다.</summary>
        private void DrawSkillSlot(Rect rect, string label, string key, bool active, float cooldown)
        {
            var prev = GUI.color;

            // 흐리게 만드는 건 알파가 아니라 색 전체다 — 배경까지 같이 흐려져야
            // "꺼져 있다"로 읽힌다. 알파만 낮추면 그냥 반투명 UI로 보인다.
            GUI.color = active ? Color.white : new Color(1f, 1f, 1f, 0.35f);

            GUI.DrawTexture(rect, barBack);

            // 쓸 수 있을 때만 왼쪽에 강조 띠를 둔다. 눈이 여기부터 간다.
            if (active)
            {
                GUI.DrawTexture(new Rect(rect.x, rect.y, 4f, rect.height), barFill);
            }

            var textRect = new Rect(rect.x + 12f, rect.y, rect.width - 24f, rect.height);

            if (cooldown > 0f)
            {
                GUI.Label(textRect, label + "   " + Mathf.CeilToInt(cooldown) + "초", slotLabelStyle);
            }
            else
            {
                GUI.Label(textRect, label + "   [" + key + "]", slotLabelStyle);
            }

            GUI.color = prev;
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

        /// <summary>
        /// 우측 하단 조작 안내.
        ///
        /// <b>우측 하단인 이유</b>: 좌하단은 귀신 스킬바, 하단 중앙은 인벤토리 칸,
        /// 좌상단은 단계·시간 표시가 이미 쓰고 있다. 남은 자리가 여기뿐이다.
        ///
        /// 폭은 글자에서 재서 잡는다. 숫자로 박아두면 폰트나 문구가 바뀔 때
        /// 글자가 배경 밖으로 삐져나온다.
        /// </summary>
        private void DrawKeyHints(string[] lines)
        {
            const float LineHeight = 17f;
            const float PadX = 10f;
            const float PadY = 6f;
            const float Margin = 12f;

            float width = 0f;
            foreach (var line in lines)
            {
                width = Mathf.Max(width, hintStyle.CalcSize(new GUIContent(line)).x);
            }

            float boxW = width + PadX * 2f;
            float boxH = lines.Length * LineHeight + PadY * 2f;
            var box = new Rect(Screen.width - boxW - Margin, Screen.height - boxH - Margin, boxW, boxH);

            GUI.DrawTexture(box, barBack);

            for (int i = 0; i < lines.Length; i++)
            {
                GUI.Label(new Rect(box.x + PadX, box.y + PadY + i * LineHeight, width, LineHeight),
                    lines[i], hintStyle);
            }
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

            // 조작 안내는 배경이므로 흐리게. 1인칭 화면을 가리면 안 된다.
            hintStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(1f, 1f, 1f, 0.72f) },
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

            // <b>파괴 여부까지 확인해야 한다.</b> 도구를 줍는 순간 서버가 그 오브젝트를
            // 디스폰하는데, Target은 인터페이스라 <c>== null</c>로는 걸러지지 않는다
            // (PlayerInteractor.IsAlive 주석 참고).
            if (interactor == null || !PlayerInteractor.IsAlive(interactor.Target))
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

            // 탐지로 밝혀낸 약점도 같이 보여준다 — 승리 경로가 둘이라
            // 한쪽만 보이면 팀이 진행도를 가늠할 수 없다.
            var manager = GameManager.Instance;
            int found = manager != null ? manager.FoundWeaknesses.Count : 0;
            int weaknessCount = GameManager.Config != null ? GameManager.Config.WeaknessCount : 3;

            const float w = 190f;
            float h = 52f + capacity * 24f + 30f + weaknessCount * 24f;
            var rect = new Rect(Screen.width - w - 16f, 90f, w, h);

            GUI.DrawTexture(rect, barBack);
            GUI.Box(rect, GUIContent.none);

            GUILayout.BeginArea(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, rect.height - 16f));

            GUILayout.Label($"밝혀낸 약점  {found}/{weaknessCount}", leftLabelStyle);
            for (int i = 0; i < weaknessCount; i++)
            {
                GUILayout.Label(manager != null && i < found
                    ? $"· {((Core.ToolType)manager.FoundWeaknesses[i]).ToKorean()}"
                    : "· ―", leftLabelStyle);
            }

            GUILayout.Space(8);
            GUILayout.Label($"제단  {offered.Count}/{capacity}", leftLabelStyle);

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

            // 영혼 분리는 조사 단계 전용이다(GhostController.ToggleSoulRpc).
            // 다른 단계에서 안내를 띄우면 Q를 눌러도 아무 일이 없어 고장으로 오해한다.
            if (GameManager.CurrentPhase != GamePhase.Investigation)
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


        /// <summary>
        /// 화면 하단 중앙 아이템 창. <b>칸 수는 항상 최대치만큼</b> 그린다 —
        /// 빈 칸이 보여야 "몇 개까지 들 수 있는지"와 "1~4로 고른다"가 눈에 익는다.
        /// </summary>
        private void DrawInventorySlot(NetworkPlayer local)
        {
            var inventory = local.GetComponent<ExorcistInventory>();
            if (inventory == null)
            {
                return;
            }

            int capacity = inventory.Capacity;

            const float Gap = 6f;
            float total = capacity * slotSize + (capacity - 1) * Gap;
            float left = (Screen.width - total) * 0.5f;
            float top = Screen.height - slotSize - slotBottomMargin;

            for (int i = 0; i < capacity; i++)
            {
                var rect = new Rect(left + i * (slotSize + Gap), top, slotSize, slotSize);
                // 칸은 고정이다 — i번 칸에 도구가 있는지를 직접 묻는다.
                bool filled = inventory.HasToolAt(i);

                // 빈 칸을 고른 상태(= 맨손)도 표시해야 한다. 줍기·회수가 그 상태에서 된다.
                bool selected = i == inventory.SelectedSlot.Value;

                GUI.DrawTexture(rect, barBack);
                GUI.Box(rect, GUIContent.none);

                // 고른 칸은 테두리를 한 겹 더 그려 구분한다.
                if (selected)
                {
                    GUI.Box(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f), GUIContent.none);
                }

                GUI.Label(new Rect(rect.x + 4f, rect.y + 2f, 20f, 16f), (i + 1).ToString(), slotLabelStyle);

                if (!filled)
                {
                    continue;
                }

                var type = inventory.TypeAt(i);
                GUI.Label(new Rect(rect.x, rect.y + rect.height * 0.5f - 10f, rect.width, 20f),
                    type.ToKorean(), slotLabelStyle);

                // 이미 밝혀낸 약점은 쿨타임과 무관하게 영영 못 쓴다. 그 사실을 먼저 알린다.
                var manager = GameManager.Instance;
                if (manager != null && manager.IsWeaknessFound(type))
                {
                    GUI.Label(new Rect(rect.x, rect.yMax - 20f, rect.width, 18f), "사용됨", slotLabelStyle);
                    continue;
                }

                // 쿨타임은 숫자로만 보여준다. 남은 초를 알면 팀이 사용 순서를 짤 수 있다.
                float cooldown = inventory.CooldownAt(i);
                if (cooldown > 0.05f)
                {
                    GUI.Label(new Rect(rect.x, rect.yMax - 20f, rect.width, 18f),
                        $"{Mathf.CeilToInt(cooldown)}초", slotLabelStyle);
                }
            }

            // 은신 중에는 탐지가 아예 성립하지 않는다. 눌러도 아무 일이 없으면
            // 고장으로 오해하므로 이유를 적어준다.
            string hint = GameManager.CurrentPhase == GamePhase.Investigation
                ? "1~4 선택  좌클릭 사용  G 내려놓기"
                : "은신 중 — 도구를 쓸 수 없다  (1~4 선택 / G 내려놓기)";

            GUI.Label(new Rect(left, top + slotSize + 2f, total, 18f), hint, slotLabelStyle);
        }
    }
}
