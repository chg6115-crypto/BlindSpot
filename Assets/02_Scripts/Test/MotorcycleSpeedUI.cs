using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 현재 속도 표시 UI. Canvas 아래 오브젝트에 붙이고 Drive / Speed Text를 연결한다.
/// Gauge Fill(선택)은 Image Type = Filled 인 이미지를 넣으면 속도 게이지로 동작.
/// </summary>
public class MotorcycleSpeedUI : MonoBehaviour
{
    [SerializeField] MotorcycleTestDrive drive;
    [SerializeField] TMP_Text speedText;
    [SerializeField] Image gaugeFill;            // 선택: 없으면 비워둠

    [Header("Display")]
    [SerializeField] bool useKmh = true;         // false면 m/s
    [SerializeField] Color normalColor = Color.white;
    [SerializeField] Color boostColor = new Color(0.3f, 0.85f, 1f);
    [SerializeField] float displaySmooth = 10f;  // 숫자가 너무 튀지 않게

    float shown;

    void Update()
    {
        if (drive == null || speedText == null) return;

        float raw = Mathf.Abs(useKmh ? drive.SpeedKmh : drive.Speed);
        shown = Mathf.Lerp(shown, raw, 1f - Mathf.Exp(-displaySmooth * Time.deltaTime));

        speedText.text = $"{Mathf.RoundToInt(shown)} <size=60%>{(useKmh ? "km/h" : "m/s")}</size>"
                       + (drive.IsBoosting ? "\n<size=50%>BOOST</size>" : "");
        speedText.color = drive.IsBoosting ? boostColor : normalColor;

        if (gaugeFill != null)
        {
            gaugeFill.fillAmount = Mathf.Abs(drive.Speed) / drive.BoostMaxSpeed;
            gaugeFill.color = speedText.color;
        }
    }
}