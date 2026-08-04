using UnityEngine;

public class MenuCameraLook : MonoBehaviour
{
    [Header("얼마나 움직일지 (작을수록 미묘함)")]
    public float maxYaw = 5f;      // 좌우 회전 최대 각도
    public float maxPitch = 3f;    // 상하 회전 최대 각도

    [Header("따라오는 부드러움 (클수록 빠름)")]
    public float smooth = 3f;

    private Quaternion initialRotation;

    void Start()
    {
        initialRotation = transform.localRotation;
    }

    void Update()
    {
        // 마우스 위치를 -1 ~ 1 범위로 변환
        float mouseX = (Input.mousePosition.x / Screen.width) * 2f - 1f;
        float mouseY = (Input.mousePosition.y / Screen.height) * 2f - 1f;

        // 목표 회전 (마우스 방향으로 살짝 기울임)
        float yaw = mouseX * maxYaw;
        float pitch = -mouseY * maxPitch;
        Quaternion targetRotation = initialRotation * Quaternion.Euler(pitch, yaw, 0f);

        // 부드럽게 따라가기
        transform.localRotation = Quaternion.Slerp(
            transform.localRotation, targetRotation, Time.deltaTime * smooth);
    }
}