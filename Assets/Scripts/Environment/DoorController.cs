using GhostHunter.Core;
using GhostHunter.Player;
using UnityEngine;

namespace GhostHunter.Environment
{
    /// <summary>
    /// 여닫이문 하나. 이 컴포넌트는 <b>경첩 오브젝트에</b> 붙고, 문 메시가 자식으로 들어간다.
    /// 그래야 문짝 중심이 아니라 경첩을 축으로 돌아간다.
    ///
    /// 상태는 자기가 갖지 않는다 — <see cref="DoorManager"/>가 서버 권한으로 전부 관리하고
    /// 여기는 <see cref="SetOpen"/>으로 받은 값에 맞춰 회전만 한다.
    /// 문마다 NetworkObject를 두면 27개가 되므로 그렇게 하지 않았다.
    /// </summary>
    public class DoorController : MonoBehaviour, IInteractable
    {
        [Tooltip("DoorManager가 비트마스크에서 이 문을 찾는 번호.")]
        [SerializeField] private int doorIndex;

        [Tooltip("열렸을 때 경첩 회전각(도).")]
        [SerializeField] private float openAngle = 95f;

        [SerializeField] private float openSpeed = 260f;

        private Quaternion closedRotation;
        private Quaternion openRotation;
        private bool isOpen;

        /// <summary>서버 값을 한 번이라도 받았는가. 첫 반영은 소리를 내지 않는다.</summary>
        private bool stateKnown;

        public int DoorIndex => doorIndex;
        public bool IsOpen => isOpen;

        public Transform PromptAnchor => transform;

        private void Awake()
        {
            closedRotation = transform.localRotation;

            // 문은 반드시 <b>월드 수직축</b>을 중심으로 돌아야 한다.
            // 로컬 Y축을 그냥 쓰면 안 된다 — 저택 FBX가 (270,0,0)으로 회전돼 있어서
            // 경첩의 로컬 Y가 월드에서는 (0,0,-1)을 가리키고, 문이 옆으로 열리는 대신
            // 앞으로 넘어진다. 부모 기준으로 환산한 축을 미리 구해둔다.
            Vector3 axis = transform.parent != null
                ? transform.parent.InverseTransformDirection(Vector3.up)
                : Vector3.up;

            openRotation = Quaternion.AngleAxis(openAngle, axis.normalized) * closedRotation;
        }

        /// <summary>
        /// 열림 상태를 반영한다.
        ///
        /// <paramref name="silent"/>는 <b>일괄 동기화</b>용이다. 접속 직후나 새 판 시작 때
        /// 문 27개의 상태가 한꺼번에 들어오는데, 그때 소리를 내면 저택 전체에서
        /// 문 여닫는 소리가 동시에 터진다.
        /// </summary>
        public void SetOpen(bool value, bool silent = false)
        {
            // 상태가 실제로 바뀔 때만 소리를 낸다. DoorManager는 값이 갱신될 때마다
            // 문 전체에 다시 뿌리므로, 여기서 거르지 않으면 남이 연 문 때문에
            // 내 옆의 안 움직인 문에서도 소리가 난다.
            bool changed = value != isOpen;
            isOpen = value;

            if (!changed || silent || !stateKnown)
            {
                stateKnown = true;
                return;
            }

            Audio.GameAudio.PlayDoor(value, transform.position);
        }

        private void Update()
        {
            var target = isOpen ? openRotation : closedRotation;

            if (Quaternion.Angle(transform.localRotation, target) > 0.05f)
            {
                transform.localRotation = Quaternion.RotateTowards(
                    transform.localRotation, target, openSpeed * Time.deltaTime);
            }
        }

        /// <summary>
        /// <b>귀신에게는 프롬프트를 띄우지 않는다</b> (시나리오 2번).
        ///
        /// 귀신은 문을 통과하므로 열 이유가 없고, 무엇보다 <b>저절로 열리는 문이
        /// 곧 위치 제보</b>가 된다. 문 통과를 허용한 이유 자체가 그것이므로,
        /// 열 수 있게 두면 설계가 무의미해진다.
        /// </summary>
        public string GetPrompt(NetworkPlayer viewer)
        {
            if (viewer == null || viewer.IsGhost)
            {
                return null;
            }

            return isOpen ? "닫기 (F)" : "열기 (F)";
        }

        public bool CanInteract(NetworkPlayer viewer) => viewer != null && !viewer.IsGhost;

        public void Interact(NetworkPlayer viewer)
        {
            DoorManager.Instance?.RequestToggle(doorIndex);
        }
    }
}
