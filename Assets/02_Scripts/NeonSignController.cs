using System.Collections;
using TMPro;
using UnityEngine;

public class NeonSignController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TMP_Text neonText;
    [SerializeField] private Light[] neonLights;

    [Header("Faulty Character (zero-based index)")]
    [SerializeField, Min(0)] private int faultyCharacterIndex = 4; // BLINDSPOT: D
    [SerializeField, Min(0.1f)] private float minWait = 2f;
    [SerializeField, Min(0.1f)] private float maxWait = 6f;
    [SerializeField, Range(1, 5)] private int maxFlickers = 3;
    [SerializeField] private Vector2 flickerDuration = new Vector2(0.05f, 0.14f);

    [Header("Rare Whole-Sign Blackout")]
    [SerializeField, Range(0f, 1f)] private float blackoutChance = 0.08f;
    [SerializeField] private Vector2 blackoutDuration = new Vector2(0.08f, 0.2f);

    private float[] initialLightIntensities;
    private bool wholeSignOff;
    private bool faultyCharacterOff;

    private void Awake()
    {
        if (neonLights == null) neonLights = new Light[0];
        initialLightIntensities = new float[neonLights.Length];
        for (int i = 0; i < neonLights.Length; i++)
            if (neonLights[i] != null)
                initialLightIntensities[i] = neonLights[i].intensity;
    }

    private void OnEnable()
    {
        PowerOutage.Changed += OnPowerChanged;
        StartCoroutine(FlickerRoutine());
        if (PowerOutage.IsOut) ApplyState();
    }

    private void OnPowerChanged(bool outage) => ApplyState();

    private void OnDisable()
    {
        PowerOutage.Changed -= OnPowerChanged;
        StopAllCoroutines();
        wholeSignOff = false;
        faultyCharacterOff = false;
        ApplyState();
    }

    private IEnumerator FlickerRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(minWait, Mathf.Max(minWait, maxWait)));

            if (Random.value < blackoutChance)
            {
                wholeSignOff = true;
                ApplyState();
                yield return new WaitForSeconds(Random.Range(blackoutDuration.x, blackoutDuration.y));
                wholeSignOff = false;
                ApplyState();
            }
            else
            {
                int count = Random.Range(1, Mathf.Max(2, maxFlickers + 1));
                for (int i = 0; i < count; i++)
                {
                    faultyCharacterOff = true;
                    ApplyState();
                    yield return new WaitForSeconds(Random.Range(flickerDuration.x, flickerDuration.y));
                    faultyCharacterOff = false;
                    ApplyState();
                    yield return new WaitForSeconds(Random.Range(0.04f, 0.12f));
                }
            }
        }
    }

    private void ApplyState()
    {
        bool signOff = wholeSignOff || PowerOutage.IsOut;   // 정전이면 통째로 꺼짐
        if (neonText != null)
        {
            neonText.enabled = !signOff;
            if (!signOff)
            {
                neonText.ForceMeshUpdate();
                TMP_TextInfo info = neonText.textInfo;
                for (int i = 0; i < info.characterCount; i++)
                {
                    TMP_CharacterInfo ch = info.characterInfo[i];
                    if (!ch.isVisible) continue;
                    Color32[] colors = info.meshInfo[ch.materialReferenceIndex].colors32;
                    byte alpha = (byte)(faultyCharacterOff && i == faultyCharacterIndex ? 0 : 255);
                    for (int v = 0; v < 4; v++)
                        colors[ch.vertexIndex + v].a = alpha;
                }
                neonText.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
            }
        }

        for (int i = 0; i < neonLights.Length; i++)
            if (neonLights[i] != null)
                neonLights[i].intensity = signOff ? 0f : initialLightIntensities[i];
    }
}
