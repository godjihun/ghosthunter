using System.Collections.Generic;
using GhostHunter.Core;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Tools
{
    /// <summary>
    /// 게임 시작 시 서버가 맵에 도구를 뿌린다 (기술 문서 5-1).
    ///
    /// 배치는 비밀이 아니다 — 찾으면 되는 것이므로 그냥 서버가 스폰하면 전원에게 보인다.
    ///
    /// <b>종류당 정확히 한 개씩, 총 6개다.</b> 도구가 소모품이던 시절에는 종류당 여러 개를
    /// 뿌렸지만, 지금은 쓰면 쿨타임이 도는 재사용 도구라 하나면 충분하다.
    /// 여러 개가 있으면 같은 도구를 겹쳐 들고 쿨타임 규칙을 우회할 수 있다.
    /// </summary>
    public class ToolSpawner : NetworkBehaviour
    {
        public static ToolSpawner Instance { get; private set; }

        [Tooltip("WorldTool 컴포넌트가 붙은 프리팹. NetworkManager의 프리팹 목록에도 등록해야 한다.")]
        [SerializeField] private GameObject toolPrefab;

        [Tooltip("비워두면 씬에서 ToolSpawnPoint를 전부 찾아 쓴다.")]
        [SerializeField] private List<ToolSpawnPoint> spawnPoints = new();

        private readonly List<NetworkObject> spawned = new();

        private void Awake()
        {
            Instance = this;

            if (spawnPoints.Count == 0)
            {
                spawnPoints.AddRange(FindObjectsByType<ToolSpawnPoint>(FindObjectsInactive.Exclude));
            }
        }

        public override void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            base.OnDestroy();
        }

        public void SpawnAllTools(GameConfig config)
        {
            if (!IsServer || config == null || toolPrefab == null)
            {
                return;
            }

            DespawnAll();

            if (spawnPoints.Count == 0)
            {
                Debug.LogWarning("[ToolSpawner] ToolSpawnPoint가 씬에 하나도 없습니다. 도구를 배치할 수 없습니다.");
                return;
            }

            // <b>종류당 정확히 하나씩.</b> 도구는 소모품이 아니라 쿨타임이 도는
            // 재사용 도구라, 저택 전체에 6개만 존재한다. 여러 개를 뿌리면
            // 한 사람이 같은 도구를 여러 개 쥐고 쿨타임 규칙을 우회하게 된다.
            var toPlace = new List<ToolType>();
            for (int t = 0; t < ToolTypeExtensions.Count; t++)
            {
                toPlace.Add((ToolType)t);
            }

            var points = UsablePoints();
            Shuffle(points);
            Shuffle(toPlace);

            int count = Mathf.Min(toPlace.Count, points.Count);
            if (toPlace.Count > points.Count)
            {
                Debug.LogWarning($"[ToolSpawner] 스폰 지점이 {points.Count}개뿐이라 도구 {toPlace.Count}개를 다 놓을 수 없습니다.");
            }

            for (int i = 0; i < count; i++)
            {
                var instance = Instantiate(toolPrefab, points[i].transform.position, points[i].transform.rotation);
                var netObj = instance.GetComponent<NetworkObject>();
                var tool = instance.GetComponent<WorldTool>();

                netObj.Spawn(true);
                if (tool != null)
                {
                    tool.Type.Value = toPlace[i];
                }

                spawned.Add(netObj);
            }
        }

        /// <summary>
        /// 실제로 쓸 수 있는 스폰 지점만 남긴다.
        ///
        /// <b>제단 안에 겹친 지점은 뺀다.</b> 거기 도구가 뜨면 제단 모델에 파묻혀
        /// 보이지도 집히지도 않는데, 종류당 개수는 이미 균등하게 뽑아둔 뒤라
        /// <b>그 한 종류만 조용히 한 개 사라진다</b> — 개수 불균등은 곧 약점 오추론이 된다.
        ///
        /// 지점을 지우는 대신 스폰 때 걸러내는 이유는, 제단을 옮기면 겹치는 지점도
        /// 같이 바뀌기 때문이다. 씬에서 손으로 맞춰 두면 다음에 옮길 때 또 틀어진다.
        /// </summary>
        private List<ToolSpawnPoint> UsablePoints()
        {
            var points = new List<ToolSpawnPoint>();
            var altar = Game.Altar.Instance != null
                ? Game.Altar.Instance.GetComponent<Collider>()
                : null;

            foreach (var point in spawnPoints)
            {
                if (point == null)
                {
                    continue;
                }

                // ClosestPoint는 콜라이더 안의 점을 받으면 그 점을 그대로 돌려준다.
                // 즉 "제자리로 돌아오면 내부"다.
                if (altar != null)
                {
                    Vector3 p = point.transform.position;
                    if ((altar.ClosestPoint(p) - p).sqrMagnitude < 0.0001f)
                    {
                        Debug.Log($"[ToolSpawner] {point.name}이(가) 제단과 겹쳐 제외했습니다.");
                        continue;
                    }
                }

                points.Add(point);
            }

            return points;
        }

        /// <summary>퇴마사가 도구를 버릴 때 서버가 호출.</summary>
        public WorldTool ServerDropTool(ToolType type, Vector3 position, Quaternion rotation)
        {
            if (!IsServer || toolPrefab == null)
            {
                return null;
            }

            var instance = Instantiate(toolPrefab, position, rotation);
            var netObj = instance.GetComponent<NetworkObject>();
            var tool = instance.GetComponent<WorldTool>();

            netObj.Spawn(true);
            if (tool != null)
            {
                tool.Type.Value = type;
            }

            spawned.Add(netObj);
            return tool;
        }

        public void DespawnAll()
        {
            if (!IsServer)
            {
                return;
            }

            foreach (var obj in spawned)
            {
                if (obj != null && obj.IsSpawned)
                {
                    obj.Despawn(true);
                }
            }
            spawned.Clear();
        }

        private static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
