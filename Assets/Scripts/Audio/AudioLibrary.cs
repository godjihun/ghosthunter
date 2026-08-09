using UnityEngine;

namespace GhostHunter.Audio
{
    /// <summary>
    /// 게임에서 쓰는 소리를 한 곳에 모아둔 표.
    ///
    /// <b>왜 ScriptableObject인가</b> — 클립을 씬이나 프리팹에 직접 물리면, 소리를 하나
    /// 갈아끼울 때마다 그 오브젝트를 열어야 하고 씬 파일에 변경이 남는다. 표를 따로 두면
    /// 에셋 하나만 고치면 되고, 그 에셋이 <c>Resources</c>에 있어서 아무 데서나 꺼내 쓸 수 있다.
    ///
    /// <b>클립 자체는 Resources에 두지 않는다.</b> 이 표만 Resources에 있고 클립은
    /// <c>Assets/Audio/</c>에 그대로 있다 — 참조를 따라 자동으로 빌드에 포함된다.
    /// </summary>
    [CreateAssetMenu(menuName = "GhostHunter/Audio Library", fileName = "AudioLibrary")]
    public class AudioLibrary : ScriptableObject
    {
        [Header("── 전원에게 들리는 소리 ──")]

        [Tooltip("제단에 도구를 바칠 때. 위치와 무관하게 전원이 듣는다.")]
        public AudioClip AltarOffer;

        [Tooltip("조사·사냥 단계가 시작될 때 울리는 경보음.")]
        public AudioClip StageAlarm;

        [Tooltip("사냥 단계에서 귀신이 퇴마사를 처치했을 때. 위치와 무관하게 전원이 듣는다.")]
        public AudioClip KillSound;

        [Tooltip("게임 내내 도는 배경음.")]
        public AudioClip Ambient;

        [Header("── 본인에게만 들리는 소리 ──")]

        [Tooltip("퇴마 도구를 주웠을 때.")]
        public AudioClip CollectItem;

        [Tooltip("도구를 썼는데 탐지에 실패했을 때.")]
        public AudioClip DetectFail;

        [Tooltip("도구를 썼는데 탐지에 성공했을 때. 쓴 사람과 귀신이 듣는다.")]
        public AudioClip DetectSuccess;

        [Tooltip("공포스킬에 당했을 때. 당한 사람과 귀신이 듣는다.")]
        public AudioClip JumpScare;

        [Header("── 근처에서만 들리는 소리 (3D) ──")]

        public AudioClip DoorOpening;
        public AudioClip DoorClosed;

        [Tooltip("퇴마사가 걸을 때. 멈출 때까지 반복 재생된다.")]
        public AudioClip Walking;

        [Tooltip("퇴마사가 뛸 때. 멈출 때까지 반복 재생된다.")]
        public AudioClip Running;

        [Tooltip("퇴마사가 점프할 때. 한 번만 재생된다.")]
        public AudioClip Jump;

        [Header("── 볼륨 ──")]

        [Range(0f, 1f)] public float SfxVolume = 1f;
        [Range(0f, 1f)] public float AmbientVolume = 0.35f;
        [Range(0f, 1f)] public float FootstepVolume = 0.6f;

        [Header("── 들리는 거리 (m) ──")]

        // 이 거리를 넘으면 소리가 0이 된다. 발소리가 너무 멀리 들리면
        // 귀신이 퇴마사 위치를 앉아서 파악하게 되므로 짧게 잡는다.

        [Tooltip("문 여닫는 소리가 들리는 최대 거리.")]
        public float DoorHearingRange = 18f;

        [Tooltip("발소리·점프 소리가 들리는 최대 거리. 플레이어가 내는 이동 소음은 이 값을 함께 쓴다.")]
        public float FootstepHearingRange = 14f;
    }
}
