using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 로비 버튼 스타일: 평소엔 어두운 배경 + 흐린 테두리 + 청록 글자,
/// 마우스를 올리면 밝은 배경 + 청록 테두리 + 왼쪽 청록 막대 + 흰 글자. 비활성 시 흐리게.
/// </summary>
[RequireComponent(typeof(Selectable))]
public class UIHoverStyle : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Graphic background;
    public Outline outline;
    public Graphic accentBar;
    public TMP_Text label;

    public Color normalBg = new Color32(16, 24, 46, 230);
    public Color hoverBg = new Color32(20, 60, 80, 230);
    public Color normalBorder = new Color32(60, 90, 130, 255);
    public Color hoverBorder = new Color32(64, 230, 240, 255);
    public Color normalText = new Color32(64, 230, 240, 255);
    public Color hoverText = Color.white;
    public Color disabledText = new Color32(64, 90, 110, 255);

    private Selectable selectable;
    private bool hovered;
    private bool lastInteractable = true;

    private void Awake()
    {
        selectable = GetComponent<Selectable>();
    }

    private void OnEnable()
    {
        hovered = false;
        Apply();
    }

    private void Update()
    {
        // interactable 이 코드에서 바뀌면 바로 반영
        if (selectable != null && selectable.interactable != lastInteractable) Apply();
    }

    public void OnPointerEnter(PointerEventData e) { hovered = true; Apply(); }
    public void OnPointerExit(PointerEventData e) { hovered = false; Apply(); }

    private void Apply()
    {
        bool interactable = selectable == null || selectable.interactable;
        lastInteractable = interactable;
        bool on = hovered && interactable;

        if (background != null) background.color = on ? hoverBg : normalBg;
        if (outline != null) outline.effectColor = on ? hoverBorder : normalBorder;
        if (accentBar != null) accentBar.enabled = on;
        if (label != null) label.color = !interactable ? disabledText : (on ? hoverText : normalText);
    }
}
