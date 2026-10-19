using UnityEngine;

[RequireComponent(typeof(Light))]
public class NeonLightFlicker : MonoBehaviour
{
    [SerializeField] private float minIntensity = 0f;
    [SerializeField] private float maxIntensity = 1.5f;
    [SerializeField] private float minInterval = 0.03f;
    [SerializeField] private float maxInterval = 0.3f;
    [SerializeField, Range(0f, 1f)]
    private float offChance = 0.25f;

    private Light targetLight;
    private float nextChangeTime;

    private void Awake()
    {
        targetLight = GetComponent<Light>();
    }

    private void Update()
    {
        if (PowerOutage.IsOut)   // 정전: 꺼둠
        {
            targetLight.intensity = 0f;
            nextChangeTime = 0f;
            return;
        }

        if (Time.time < nextChangeTime)
            return;

        targetLight.intensity = Random.value < offChance
            ? 0f
            : Random.Range(minIntensity, maxIntensity);

        nextChangeTime = Time.time +
            Random.Range(minInterval, maxInterval);
    }
}