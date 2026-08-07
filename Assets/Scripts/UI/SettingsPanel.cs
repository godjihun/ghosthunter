using GhostHunter.Player;
using UnityEngine;

namespace GhostHunter.UI
{
    /// <summary>
    /// 설정 화면의 <b>내용</b>. 메인 메뉴와 대기방 ESC 메뉴가 이걸 같이 쓴다.
    ///
    /// <b>한 곳에만 두는 이유</b>: 두 화면에 각각 슬라이더를 그리면 항목을 하나
    /// 추가할 때마다 두 군데를 고쳐야 하고, 한쪽만 고쳐 화면마다 다른 설정이
    /// 보이는 상태가 된다. 그리는 코드가 하나면 그럴 일이 없다.
    ///
    /// 값 자체는 <see cref="PlayerLook.SensitivityScale"/>가 갖는다 — static이고
    /// <c>PlayerPrefs</c>에 저장되므로, 씬을 넘나들어도 같은 값이 이어진다.
    /// 별도의 동기화 코드가 필요 없는 이유다.
    /// </summary>
    public static class SettingsPanel
    {
        public const float Width = 320f;
        public const float Height = 200f;

        private static GUIStyle titleStyle;
        private static GUIStyle noticeStyle;

        /// <summary>
        /// 제목과 항목들을 그린다. <b>이미 열려 있는 레이아웃 영역 안</b>에서 부른다 —
        /// 창 자체(배경·위치·닫기 버튼)는 부르는 쪽이 각자 사정에 맞게 그린다.
        /// </summary>
        public static void Draw()
        {
            EnsureStyles();

            GUILayout.Label("설정", titleStyle);
            GUILayout.Space(12);

            GUILayout.Label("마우스 감도");

            float value = PlayerLook.SensitivityScale;
            GUILayout.BeginHorizontal();
            float next = GUILayout.HorizontalSlider(value, 0.2f, 3f);
            GUILayout.Label($"{next:F2}배", GUILayout.Width(56));
            GUILayout.EndHorizontal();

            // 슬라이더는 아주 미세한 값도 뱉는다. 반올림해야 매 프레임
            // PlayerPrefs에 쓰지 않는다.
            next = Mathf.Round(next * 20f) / 20f;
            if (!Mathf.Approximately(next, value))
            {
                PlayerLook.SensitivityScale = next;
            }

            GUILayout.Space(10);
            GUILayout.Label("내 화면에만 적용되며 다음 접속에도 유지됩니다.", noticeStyle);
        }

        private static void EnsureStyles()
        {
            titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };

            noticeStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true,
            };
        }
    }
}
