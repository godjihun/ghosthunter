using UnityEngine;

namespace GhostHunter.UI
{
    /// <summary>
    /// 흡수당한 순간의 갑툭튀 (시나리오 3-4 / 기술 문서 6-2).
    ///
    /// <b>3D 모델을 화면 앞으로 끌어오지 않는다.</b> 미리 준비해 둔 얼굴 그림을
    /// 화면 가득 띄운다. <c>Resources/JumpScare</c> 폴더의 그림을 <b>이름순으로</b> 읽으므로,
    /// 한 장만 두면 정지 화면이고 여러 장을 두면 gif처럼 순환한다 —
    /// 유니티는 gif를 직접 재생하지 못하므로 낱장 텍스처로 두고 시간에 맞춰 갈아끼운다.
    ///
    /// <b>모델을 쓰지 않는 이유</b>는 셋이다. 연출을 그림에서 완결지을 수 있어 결과가
    /// 예측 가능하고, 스킨 메시 계층을 복제·활성화하는 비용이 사라지며, 무엇보다
    /// <b>진짜 귀신 오브젝트를 건드릴 일이 없어</b> 위치가 샐 여지가 원천 차단된다.
    ///
    /// <b>씬에 배치하지 않는다.</b> 실행되는 순간 스스로 만들어진다 —
    /// 이 프로젝트에서 씬·프리팹 저장이 여러 번 조용히 실패했던 터라 배선을 두지 않는다.
    /// </summary>
    public class JumpScareOverlay : MonoBehaviour
    {
        /// <summary><c>Assets/Resources/</c> 아래 프레임이 담긴 폴더.</summary>
        private const string FrameFolder = "JumpScare";

        /// <summary>
        /// 얼굴이 나오기 전 <b>검은 화면</b>을 유지하는 시간(초).
        ///
        /// 이 공백이 연출의 핵심이다. 화면이 한 번 까맣게 끊긴 다음 얼굴이 터져 나와야
        /// 놀란다 — 서서히 다가오면 대비할 시간을 주게 되어 무섭지 않다.
        /// </summary>
        private const float BlackDelay = 0.12f;

        /// <summary>
        /// 얼굴이 화면을 채우고 있는 시간(초). 프레임이 여러 장이면 이 안에서 순환한다.
        ///
        /// 공포음(<c>jump_scare.wav</c>)이 1.76초라 소리가 먼저 끝나고 그림이 조금 더 남는다.
        /// 소리와 정확히 맞추려면 1.8로, 여운을 더 주려면 늘리면 된다.
        /// </summary>
        private const float FaceDuration = 2.0f;

        private static JumpScareOverlay instance;
        private static bool loadFailed;

        private Texture2D[] frames;
        private float startedAt = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap() => Ensure();

        private static JumpScareOverlay Ensure()
        {
            if (instance != null)
            {
                return instance;
            }

            if (loadFailed || !Application.isPlaying)
            {
                return null;
            }

            var go = new GameObject("[JumpScare]");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<JumpScareOverlay>();
            return instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;

            // 파일 이름순으로 정렬해야 scare_00 → scare_15 순서가 보장된다.
            // LoadAll의 반환 순서는 정해져 있지 않다.
            frames = Resources.LoadAll<Texture2D>(FrameFolder);
            if (frames == null || frames.Length == 0)
            {
                loadFailed = true;
                Debug.LogWarning($"[JumpScare] Resources/{FrameFolder} 에 프레임이 없다. 연출이 나오지 않는다.");
                return;
            }

            System.Array.Sort(frames, (a, b) => string.CompareOrdinal(a.name, b.name));
        }

        /// <summary>이 클라이언트 화면에만 띄운다. 흡수당한 본인이 호출한다.</summary>
        public static void Play()
        {
            var overlay = Ensure();
            if (overlay == null || overlay.frames == null || overlay.frames.Length == 0)
            {
                return;
            }

            // 연달아 당하면 처음부터 다시. 겹쳐 재생할 이유가 없다.
            overlay.startedAt = Time.unscaledTime;
        }

        private void OnGUI()
        {
            if (startedAt < 0f || frames == null || frames.Length == 0)
            {
                return;
            }

            float elapsed = Time.unscaledTime - startedAt;
            if (elapsed > BlackDelay + FaceDuration)
            {
                startedAt = -1f;
                return;
            }

            // <b>모든 UI 위에 그린다.</b> IMGUI는 depth가 작을수록 위에 오는데,
            // 이 값은 OnGUI가 <b>끝난 뒤</b>에 읽혀 그리기 순서를 정한다.
            // 그래서 예전처럼 함수 끝에서 원래 값으로 되돌리면 설정 자체가 무효가 된다 —
            // 실제로 그것 때문에 게이지·좌상단 글씨가 얼굴 위에 겹쳐 보였다.
            GUI.depth = -10000;

            var full = new Rect(0f, 0f, Screen.width, Screen.height);

            // 화면비가 그림(정사각형)과 달라 남는 자리는 검게 채운다.
            var previousColor = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(full, Texture2D.whiteTexture);
            GUI.color = previousColor;

            // 앞구간은 검은 화면만. 여기서 얼굴을 그리지 않는 것이 "갑툭튀"를 만든다.
            if (elapsed < BlackDelay)
            {
                return;
            }

            // 첫 장부터 이미 화면을 가득 채운 크기다 — 다가오는 연출이 아니라
            // <b>있던 자리에 갑자기 나타나는</b> 것이라야 한다.
            float t = (elapsed - BlackDelay) / FaceDuration;
            int index = Mathf.Clamp((int)(t * frames.Length), 0, frames.Length - 1);

            // ScaleAndCrop: 비율을 지키며 화면을 덮는다. 넓은 화면에서는 위아래가 잘리지만
            // 얼굴은 가운데 있으므로 남는다. 늘어나 보이는 것보다 낫다.
            GUI.DrawTexture(full, frames[index], ScaleMode.ScaleAndCrop);
        }
    }
}
