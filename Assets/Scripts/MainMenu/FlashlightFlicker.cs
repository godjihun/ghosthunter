using UnityEngine;

public class FlashlightFlicker : MonoBehaviour
{
    [Header("밝기 설정")]
    public float baseIntensity = 300000f;   // 기본 밝기 (지금 손전등 값)
    public float flickerAmount = 100000f;    // 깜빡일 때 밝기 변동 폭

    [Header("깜빡임 속도")]
    public float flickerSpeed = 8f;          // 미세한 떨림 속도

    [Header("가끔 확 꺼지는 효과")]
    public bool enableBlackout = true;       // 순간적으로 꺼지는 효과 on/off
    public float blackoutChance = 0.02f;     // 꺼질 확률 (0~1, 낮을수록 드묾)
    public float blackoutDuration = 0.1f;    // 꺼져있는 시간

    private Light spotLight;
    private float blackoutTimer = 0f;

    void Start()
    {
        spotLight = GetComponent<Light>();
        if (spotLight != null)
            baseIntensity = spotLight.intensity;  // 시작 밝기를 자동으로 기억
    }

    void Update()
    {
        if (spotLight == null) return;

        // 순간 꺼짐 처리 중이면
        if (blackoutTimer > 0f)
        {
            blackoutTimer -= Time.deltaTime;
            spotLight.intensity = baseIntensity * 0.05f;  // 거의 꺼진 상태
            return;
        }

        // 가끔 확 꺼지는 효과 (랜덤)
        if (enableBlackout && Random.value < blackoutChance)
        {
            blackoutTimer = blackoutDuration;
            return;
        }

        // 평소엔 Perlin 노이즈로 자연스럽게 떨림
        float noise = Mathf.PerlinNoise(Time.time * flickerSpeed, 0f);
        spotLight.intensity = baseIntensity + (noise - 0.5f) * 2f * flickerAmount;
    }
}