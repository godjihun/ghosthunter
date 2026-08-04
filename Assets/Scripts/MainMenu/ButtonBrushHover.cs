using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ButtonBrushHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("붓 이미지 (Filled로 설정된 것)")]
    public Image brushImage;

    [Header("채워지는 속도 (클수록 빠름)")]
    public float fillSpeed = 5f;

    private bool isHovering = false;

    void Update()
    {
    if (brushImage == null) return;
    float target = isHovering ? 1f : 0f;
    brushImage.fillAmount = Mathf.MoveTowards(
        brushImage.fillAmount, target, fillSpeed * Time.deltaTime);
    
    brushImage.SetVerticesDirty();   // 강제 갱신
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovering = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovering = false;
    }
}