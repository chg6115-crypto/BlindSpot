using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>로비 방 목록의 한 줄 (방 제목은 왼쪽, 인원은 오른쪽). 줄 전체가 참가 버튼</summary>
public class RoomListItem : MonoBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Button joinButton;

    public void Setup(string roomName, int players, int maxPlayers, Action onJoin)
    {
        nameText.richText = true;
        nameText.text = $"{roomName}<pos=78%>{players} / {maxPlayers}";
        joinButton.onClick.RemoveAllListeners();
        joinButton.onClick.AddListener(() => onJoin?.Invoke());
    }

    public void SetInteractable(bool on) => joinButton.interactable = on;
}
