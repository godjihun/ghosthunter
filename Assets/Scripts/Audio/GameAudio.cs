using GhostHunter.Game;
using UnityEngine;

namespace GhostHunter.Audio
{
    /// <summary>
    /// 소리 재생 창구. 호출부는 "누가 듣는가"만 고르고 나머지는 여기가 처리한다.
    ///
    /// <b>씬에 배치하지 않는다.</b> 실행되는 순간 스스로 만들어지고
    /// <see cref="Object.DontDestroyOnLoad"/>로 살아남는다. 씬이나 프리팹에 물려두면
    /// 메인메뉴 씬에도 따로 놓아야 하고, 무엇보다 이 프로젝트에서 <b>씬·프리팹 저장이
    /// 조용히 실패한 적이 여러 번 있었다</b> — 배선이 없으면 그 위험 자체가 없다.
    ///
    /// <b>세 가지 전달 방식</b>이 있고, 이 게임에서는 이걸 잘못 고르는 것이 곧 정보 누설이다:
    ///  - <see cref="PlayLocal"/>  이 클라이언트에서만. 탐지 결과처럼 <b>비밀</b>인 소리
    ///  - <see cref="PlayGlobal"/> 위치와 무관하게 전원. 제단처럼 공개된 사건
    ///  - <see cref="PlayAt"/>     월드 좌표에서 3D로. 멀면 안 들린다
    ///
    /// 전원에게 들려야 하는 소리라고 RPC를 새로 파지 않는다 — 상태는 이미 동기화돼 있으므로
    /// <b>각 클라이언트가 자기 쪽 변화를 보고 스스로 재생</b>하면 대역폭이 들지 않는다.
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        private const string LibraryResourcePath = "AudioLibrary";

        private static GameAudio instance;
        private static bool bootstrapFailed;

        private AudioLibrary library;
        private AudioSource localSource;
        private AudioSource ambientSource;

        /// <summary>소리 표. 아직 못 찾았으면 null이다 — 호출부는 신경 쓰지 않아도 된다.</summary>
        public static AudioLibrary Library => Ensure()?.library;

        /// <summary>
        /// 씬이 올라온 뒤 자동으로 한 번 불린다. 첫 소리가 날 때까지 기다렸다 만들면
        /// 그 순간 프레임이 튀므로 미리 준비해 둔다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap() => Ensure();

        private static GameAudio Ensure()
        {
            if (instance != null)
            {
                return instance;
            }

            // 한 번 실패했으면 매 프레임 Resources를 뒤지지 않는다.
            if (bootstrapFailed || !Application.isPlaying)
            {
                return null;
            }

            var go = new GameObject("[GameAudio]");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<GameAudio>();
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

            library = Resources.Load<AudioLibrary>(LibraryResourcePath);
            if (library == null)
            {
                bootstrapFailed = true;
                Debug.LogWarning($"[GameAudio] Resources/{LibraryResourcePath} 를 못 찾았다. 소리가 나지 않는다.");
            }

            localSource = gameObject.AddComponent<AudioSource>();
            localSource.playOnAwake = false;
            localSource.spatialBlend = 0f;   // 2D — 거리와 무관

            ambientSource = gameObject.AddComponent<AudioSource>();
            ambientSource.playOnAwake = false;
            ambientSource.loop = true;
            ambientSource.spatialBlend = 0f;
        }

        /// <summary>
        /// 배경음은 <b>게임이 도는 동안만</b> 흐른다.
        ///
        /// 대기방·결과 화면에서까지 틀면 로비에서 사람을 기다리는 내내 같은 곡이 돌고,
        /// 무엇보다 "언제부터 게임인가"가 소리로 드러나지 않는다.
        /// </summary>
        private void Update()
        {
            if (library == null || library.Ambient == null || ambientSource == null)
            {
                return;
            }

            bool shouldPlay = GameManager.IsGameplayActive;

            if (shouldPlay && !ambientSource.isPlaying)
            {
                ambientSource.clip = library.Ambient;
                ambientSource.volume = library.AmbientVolume;
                ambientSource.Play();
            }
            else if (!shouldPlay && ambientSource.isPlaying)
            {
                ambientSource.Stop();
            }
            else if (ambientSource.isPlaying)
            {
                // 인스펙터에서 볼륨을 만지면 바로 반영된다. 값 맞추기가 훨씬 빠르다.
                ambientSource.volume = library.AmbientVolume;
            }
        }

        // ── 재생 창구 ──────────────────────────────────────────────

        /// <summary>이 클라이언트에서만 들린다. 위치와 무관.</summary>
        public static void PlayLocal(AudioClip clip, float volumeScale = 1f)
        {
            var audio = Ensure();
            if (audio == null || clip == null || audio.localSource == null)
            {
                return;
            }

            audio.localSource.PlayOneShot(clip, audio.Volume(volumeScale));
        }

        /// <summary>
        /// 전원이 위치와 무관하게 듣는다.
        ///
        /// 구현은 <see cref="PlayLocal"/>과 같다 — <b>전원이 각자 자기 쪽에서 부르기 때문에</b>
        /// 결과적으로 모두가 듣는다. 이름을 나눠둔 건 호출부에서 의도가 보이게 하려는 것이다.
        /// </summary>
        public static void PlayGlobal(AudioClip clip, float volumeScale = 1f) => PlayLocal(clip, volumeScale);

        /// <summary>
        /// 월드 좌표에서 3D로 재생한다. <paramref name="range"/> 밖에서는 들리지 않는다.
        ///
        /// <see cref="AudioSource.PlayClipAtPoint"/>를 쓰지 않는 이유는 그쪽이
        /// 감쇠를 기본값(로그, 500m)으로 강제해서 <b>저택 반대편까지 들리기</b> 때문이다.
        /// </summary>
        public static void PlayAt(AudioClip clip, Vector3 position, float range, float volumeScale = 1f)
        {
            var audio = Ensure();
            if (audio == null || clip == null)
            {
                return;
            }

            var go = new GameObject($"SFX_{clip.name}");
            go.transform.position = position;

            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = audio.Volume(volumeScale);
            source.spatialBlend = 1f;                       // 완전 3D
            source.rolloffMode = AudioRolloffMode.Linear;   // 거리에 비례해 일정하게 줄어든다
            source.minDistance = 1f;
            source.maxDistance = Mathf.Max(2f, range);
            source.dopplerLevel = 0f;                       // 걸어다니며 들으면 음정이 흔들린다
            source.Play();

            Destroy(go, clip.length + 0.2f);
        }

        private float Volume(float scale) => Mathf.Clamp01((library != null ? library.SfxVolume : 1f) * scale);

        // ── 상황별 단축 호출 ───────────────────────────────────────
        //
        // 호출부가 라이브러리 null 검사를 반복하지 않도록 여기서 한 번에 처리한다.

        /// <summary>제단 헌납. 전원 공유.</summary>
        public static void PlayAltarOffer() => PlayGlobal(Library?.AltarOffer);

        /// <summary>조사·사냥 단계 시작. 전원 공유.</summary>
        public static void PlayStageAlarm() => PlayGlobal(Library?.StageAlarm);

        /// <summary>사냥 단계 처치. 전원 공유 — 누가 죽었는지는 모두가 알아야 한다.</summary>
        public static void PlayKill() => PlayGlobal(Library?.KillSound);

        /// <summary>점프. 발소리와 같은 거리까지만 들린다.</summary>
        public static void PlayJump(Vector3 position)
        {
            var lib = Library;
            if (lib == null)
            {
                return;
            }

            PlayAt(lib.Jump, position, lib.FootstepHearingRange, lib.FootstepVolume);
        }

        /// <summary>도구 획득. 주운 본인만.</summary>
        public static void PlayCollectItem() => PlayLocal(Library?.CollectItem);

        /// <summary>탐지 결과. 쓴 본인(성공이면 귀신도).</summary>
        public static void PlayDetection(bool success)
            => PlayLocal(success ? Library?.DetectSuccess : Library?.DetectFail);

        /// <summary>공포스킬. 당한 사람과 귀신만.</summary>
        public static void PlayJumpScare() => PlayLocal(Library?.JumpScare);

        /// <summary>문 여닫기. 근처에서만 들린다.</summary>
        public static void PlayDoor(bool opening, Vector3 position)
        {
            var lib = Library;
            if (lib == null)
            {
                return;
            }

            PlayAt(opening ? lib.DoorOpening : lib.DoorClosed, position, lib.DoorHearingRange);
        }
    }
}
