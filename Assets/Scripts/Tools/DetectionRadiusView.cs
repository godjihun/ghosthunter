using GhostHunter.Exorcist;
using GhostHunter.Game;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Tools
{
    /// <summary>
    /// 도구를 손에 들면 바닥에 탐지 반경을 원으로 표시한다 (시나리오 3-1).
    ///
    /// <b>테두리만</b> 그린다. 원판을 꽉 채우면 바닥 텍스처와 발밑이 가려져
    /// 도구를 든 동안 계속 시야가 답답해진다. 반경만 알면 되는 표시다.
    ///
    /// <b>로컬 표시 전용</b>이다. 반경은 설정값에서 계산되므로 동기화할 필요가 없다.
    /// </summary>
    [RequireComponent(typeof(ExorcistInventory))]
    public class DetectionRadiusView : NetworkBehaviour
    {
        [Tooltip("예전 원판 오브젝트. 남아 있으면 꺼둔다.")]
        [SerializeField] private Transform radiusVisual;

        [SerializeField] private Color ringColor = new(0.45f, 0.85f, 1f, 0.9f);
        [SerializeField] private float lineWidth = 0.04f;

        [Tooltip("원을 몇 개의 점으로 그릴지. 낮으면 각져 보인다.")]
        [SerializeField] private int segments = 72;

        private ExorcistInventory inventory;
        private LineRenderer ring;

        private void Awake()
        {
            inventory = GetComponent<ExorcistInventory>();
        }

        public override void OnNetworkSpawn()
        {
            // 남의 반경까지 그릴 이유가 없다.
            if (!IsOwner)
            {
                if (radiusVisual != null)
                {
                    radiusVisual.gameObject.SetActive(false);
                }
                enabled = false;
                return;
            }

            // 채워진 원판은 더 이상 쓰지 않는다.
            if (radiusVisual != null)
            {
                radiusVisual.gameObject.SetActive(false);
            }

            BuildRing();
            inventory.OnHeldToolChanged += Refresh;
            Refresh();
        }

        public override void OnNetworkDespawn()
        {
            if (inventory != null)
            {
                inventory.OnHeldToolChanged -= Refresh;
            }
        }

        private void BuildRing()
        {
            var go = new GameObject("DetectionRing");
            go.transform.SetParent(transform, false);

            // 바닥과 겹쳐 깜빡이지 않도록 살짝 띄운다.
            go.transform.localPosition = new Vector3(0f, 0.05f, 0f);

            ring = go.AddComponent<LineRenderer>();
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.widthMultiplier = lineWidth;
            ring.numCapVertices = 2;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.alignment = LineAlignment.TransformZ;

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", ringColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", ringColor);
            ring.material = mat;

            ring.startColor = ringColor;
            ring.endColor = ringColor;
            ring.enabled = false;
        }

        private void Refresh()
        {
            if (ring == null)
            {
                return;
            }

            bool show = inventory.HasTool.Value;
            ring.enabled = show;
            if (!show)
            {
                return;
            }

            var config = GameManager.Config;
            float radius = config != null ? config.DetectionRadius : 3f;

            int count = Mathf.Max(8, segments);
            ring.positionCount = count;
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count;
                // 원은 XZ 평면(바닥)에 눕는다. 이 오브젝트의 로컬 y는 위쪽이다.
                ring.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
            }
        }
    }
}
