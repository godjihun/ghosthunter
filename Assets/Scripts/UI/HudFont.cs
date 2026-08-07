using UnityEngine;

namespace GhostHunter.UI
{
    /// <summary>
    /// IMGUI가 쓸 한글 폰트를 한 곳에서 관리한다.
    ///
    /// <b>지정하지 않으면 빌드에서 한글이 통째로 사라진다.</b> 유니티 기본 폰트
    /// (Liberation Sans)에는 한글 글리프가 없는데, 에디터에서는 윈도우의 시스템 폰트로
    /// 대체돼서 잘 보인다. <b>WebGL 빌드에는 시스템 폰트가 없다</b> — 그래서
    /// "모드: 호스트(방장)"이 ":  ( )"로 나온다. 실제로 그 버그를 냈다.
    ///
    /// 폰트는 <c>Resources</c>에서 이름으로 불러온다. 인스펙터 참조로 하면
    /// HUD 스크립트마다 따로 연결해야 하고, 하나만 빠뜨려도 그 화면만 깨진다.
    /// </summary>
    public static class HudFont
    {
        private const string FontName = "BookkGothic_Bold";

        private static Font cached;
        private static bool tried;

        /// <summary>한글 폰트. 못 찾으면 null — 그 경우 기본 폰트로 두고 넘어간다.</summary>
        public static Font Font
        {
            get
            {
                if (tried)
                {
                    return cached;
                }

                tried = true;
                cached = Resources.Load<Font>("Fonts/" + FontName);

                if (cached == null)
                {
                    Debug.LogWarning($"[HudFont] '{FontName}'을(를) 찾지 못했습니다. " +
                                     "Resources/Fonts 아래에 있어야 빌드에 포함됩니다.");
                }

                return cached;
            }
        }

        /// <summary>스타일에 한글 폰트를 입힌다. 폰트가 없으면 아무것도 하지 않는다.</summary>
        public static GUIStyle Apply(GUIStyle style)
        {
            if (style != null && Font != null)
            {
                style.font = Font;
            }

            return style;
        }

        /// <summary>
        /// 이번 OnGUI 전체에 기본 폰트를 적용한다.
        /// <c>GUILayout.Label</c>처럼 스타일을 직접 안 주는 호출까지 한 번에 덮는다.
        /// </summary>
        public static void ApplyToSkin()
        {
            if (Font != null && GUI.skin != null)
            {
                GUI.skin.font = Font;
            }
        }
    }
}
