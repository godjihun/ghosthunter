using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로비 배경에 나간 전구 같은 불안정한 조명 느낌을 준다.
///
/// 두 겹으로 이뤄진다:
///  - <b>은은한 일렁임</b>: Perlin 노이즈로 평소 밝기가 천천히 오르내린다 (조명이 불안정한 느낌).
///  - <b>순간 깜빡임</b>: 불규칙한 간격(<see cref="minInterval"/>~<see cref="maxInterval"/>)마다
///    몇 번 어두워졌다 밝아지길 빠르게 반복한다.
///
/// 매 프레임 이 둘을 곱해서 적용하므로, 일렁이는 도중에 깜빡임이 겹쳐도 자연스럽게 이어진다.
/// 배경 오브젝트의 <c>RectTransform</c>도 아주 조금씩 표류하듯 움직인다.
/// </summary>
public class LobbyLightFlicker : MonoBehaviour
{
    [Header("대상")]
    [Tooltip("밝기를 조절할 그래픽. 비워두면 같은 오브젝트의 Image/RawImage를 찾는다.")]
    [SerializeField] private Graphic targetGraphic;

    [Header("은은한 일렁임")]
    [Tooltip("평소 밝기가 이 폭만큼 1(원래 밝기) 근처를 오간다. 0이면 일렁이지 않는다.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float waverAmount = 0.12f;

    [Tooltip("일렁이는 속도. 클수록 빠르게 출렁인다.")]
    [SerializeField] private float waverSpeed = 0.6f;

    [Header("깜빡이는 간격 (초)")]
    [Tooltip("다음 깜빡임까지 최소 대기 시간.")]
    [SerializeField] private float minInterval = 2f;
    [Tooltip("다음 깜빡임까지 최대 대기 시간.")]
    [SerializeField] private float maxInterval = 7f;

    [Header("한 번 깜빡일 때")]
    [Tooltip("어두워졌다 밝아지길 몇 번 반복하는지 (최소).")]
    [SerializeField] private int minFlickerCount = 2;
    [Tooltip("어두워졌다 밝아지길 몇 번 반복하는지 (최대).")]
    [SerializeField] private int maxFlickerCount = 4;

    [Tooltip("깜빡일 때 가장 어두워지는 밝기 배수. 0이면 새까맣게.")]
    [Range(0f, 1f)]
    [SerializeField] private float dimBrightness = 0.15f;

    [Tooltip("한 번 어두워지거나 밝아지는 데 걸리는 시간. 짧을수록 빠르게 깜빡인다.")]
    [SerializeField] private float stepDuration = 0.05f;

    [Header("아주 살짝 흔들리는 위치")]
    [Tooltip("원래 위치에서 벗어날 수 있는 최대 거리(px). 0이면 움직이지 않는다.")]
    [SerializeField] private float driftAmount = 2f;

    [Tooltip("떠도는 속도. 클수록 빠르게 움직인다.")]
    [SerializeField] private float driftSpeed = 0.15f;

    private Color baseColor;
    private RectTransform rectTransform;
    private Vector2 basePosition;

    // Perlin 샘플링 좌표를 채널마다 다르게 띄워야 밝기·X·Y가 서로 다른 패턴으로 움직인다.
    // 전부 같은 시드로 읽으면 셋이 똑같이 움직여 "일렁인다"는 느낌이 안 산다.
    private float noiseSeedWaver;
    private float noiseSeedX;
    private float noiseSeedY;

    private float waitTimer;
    private float stepTimer;
    private int stepsRemaining;
    private bool flickering;
    private bool dimmedNow;

    private void Awake()
    {
        if (targetGraphic == null)
        {
            targetGraphic = GetComponent<Graphic>();
        }
        if (targetGraphic != null)
        {
            baseColor = targetGraphic.color;
        }

        rectTransform = GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            basePosition = rectTransform.anchoredPosition;
        }

        noiseSeedWaver = Random.Range(0f, 1000f);
        noiseSeedX = Random.Range(0f, 1000f);
        noiseSeedY = Random.Range(0f, 1000f);

        ScheduleNext();
    }

    private void Update()
    {
        if (targetGraphic == null)
        {
            return;
        }

        TickFlicker();
        ApplyBrightness();
        ApplyDrift();
    }

    private void TickFlicker()
    {
        if (!flickering)
        {
            waitTimer -= Time.deltaTime;
            if (waitTimer <= 0f)
            {
                StartFlicker();
            }
            return;
        }

        stepTimer -= Time.deltaTime;
        if (stepTimer > 0f)
        {
            return;
        }

        dimmedNow = !dimmedNow;
        stepTimer = stepDuration;
        stepsRemaining--;

        if (stepsRemaining <= 0)
        {
            // 항상 밝은 쪽으로 끝난다 — 어두운 채로 끝나면 다음 깜빡임까지 계속 어둡게 남는다.
            flickering = false;
            dimmedNow = false;
            ScheduleNext();
        }
    }

    private void StartFlicker()
    {
        flickering = true;
        dimmedNow = false;
        stepTimer = stepDuration;

        int flickerCount = Random.Range(minFlickerCount, maxFlickerCount + 1);
        // 한 번 "깜빡"은 어두워짐+밝아짐 두 단계다.
        stepsRemaining = flickerCount * 2;
    }

    private void ScheduleNext()
    {
        waitTimer = Random.Range(minInterval, maxInterval);
    }

    /// <summary>일렁임과 깜빡임을 곱해서 매 프레임 밝기를 다시 계산한다.</summary>
    private void ApplyBrightness()
    {
        float wave = Mathf.PerlinNoise(Time.time * waverSpeed, noiseSeedWaver);
        float waverMultiplier = 1f - waverAmount * 0.5f + wave * waverAmount;
        float flickerMultiplier = dimmedNow ? dimBrightness : 1f;

        float brightness = waverMultiplier * flickerMultiplier;
        targetGraphic.color = new Color(
            baseColor.r * brightness, baseColor.g * brightness, baseColor.b * brightness, baseColor.a);
    }

    /// <summary>원래 위치에서 아주 조금 표류하듯 움직인다.</summary>
    private void ApplyDrift()
    {
        if (rectTransform == null || driftAmount <= 0f)
        {
            return;
        }

        float x = (Mathf.PerlinNoise(Time.time * driftSpeed, noiseSeedX) - 0.5f) * 2f * driftAmount;
        float y = (Mathf.PerlinNoise(Time.time * driftSpeed, noiseSeedY) - 0.5f) * 2f * driftAmount;
        rectTransform.anchoredPosition = basePosition + new Vector2(x, y);
    }
}
