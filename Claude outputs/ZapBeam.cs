using UnityEngine;

/// <summary>
/// 번개(Zap) 이펙트를 "총구 → 맞은 지점" 빔으로 바꿔 준다.
/// 이펙트 프리팹(VFX_Zap 복사본)의 루트에 붙인다. GunWeapon이 생성 직후 Fire()를 호출함.
///  - 원래 Zap은 루트 위치(바닥)에서 위쪽(+Y)으로 번개가 솟는 구조
///    → 루트를 맞은 지점에 두고 +Y가 총구를 향하게 돌리면 총구에서 날아온 번개처럼 보임
///  - 번개 길이를 총구까지의 거리에 맞춰 늘리거나 줄임 (Stretched Billboard 파티클의 Start Size Y 조절)
///  - 아무것도 안 맞았으면 Impact Parts(그을음, 바닥 빛, 라이트 등)는 끔
/// </summary>
public class ZapBeam : MonoBehaviour
{
    [Tooltip("총구 쪽으로 늘어날 번개 파티클 (Zap, Zap Add, Zap LUT, Zap BG). 비우면 Stretched Billboard 렌더러를 쓰는 자식을 자동 검색")]
    [SerializeField] private ParticleSystem[] beamParts;
    [Tooltip("맞았을 때만 보일 부분 (Scorch, Zap Add Floor, Flare, Light Spawn, Point Light)")]
    [SerializeField] private GameObject[] impactParts;

    [Header("길이")]
    [Tooltip("번개 길이를 총구까지 거리에 맞출지")]
    [SerializeField] private bool fitLength = true;
    [Tooltip("시작할 때 번개 길이를 자동으로 측정 (끄면 Base Length 사용)")]
    [SerializeField] private bool autoMeasure = true;
    [Tooltip("자동 측정을 끈 경우의 번개 길이 (m). 자동 측정 중에는 측정값이 여기에 표시됨")]
    [SerializeField, Min(0.01f)] private float baseLength = 2f;
    [Tooltip("길이 미세 조정 (1보다 크면 번개가 더 길어짐)")]
    [SerializeField, Min(0.01f)] private float lengthMultiplier = 1f;
    [Tooltip("측정된 길이를 Console에 출력")]
    [SerializeField] private bool logMeasuredLength = false;
    [Tooltip("번개가 총구 반대쪽으로 나가면 체크")]
    [SerializeField] private bool invertDirection = false;

    private float[] originalSizeY;
    private ParticleSystem rootSystem;

    private void Awake()
    {
        if (beamParts == null || beamParts.Length == 0)
        {
            var list = new System.Collections.Generic.List<ParticleSystem>();
            foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
            {
                var r = ps.GetComponent<ParticleSystemRenderer>();
                if (r != null && r.renderMode == ParticleSystemRenderMode.Stretch) list.Add(ps);
            }
            beamParts = list.ToArray();
        }

        originalSizeY = new float[beamParts.Length];
        for (int i = 0; i < beamParts.Length; i++)
        {
            var main = beamParts[i].main;
            originalSizeY[i] = main.startSize3D ? main.startSizeYMultiplier : main.startSizeMultiplier;
        }

        rootSystem = GetComponent<ParticleSystem>();

        if (autoMeasure) MeasureLength();
    }

    /// <summary>
    /// 번개 파티클을 잠깐 시뮬레이션해서 루트에서 위쪽(+Y)으로 얼마나 솟는지 잰다.
    /// (Instantiate 직후 = 회전 없음 상태라 월드 Y로 재면 됨)
    /// </summary>
    private void MeasureLength()
    {
        float top = 0f;
        foreach (var ps in beamParts)
        {
            if (ps == null || !ps.gameObject.activeInHierarchy) continue;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            ps.Simulate(0.03f, false, true);
            if (ps.particleCount > 0 && r != null)
                top = Mathf.Max(top, r.bounds.max.y - transform.position.y);
            ps.Clear(false);
        }

        if (top > 0.01f) baseLength = top;
        if (logMeasuredLength)
            Debug.Log($"[ZapBeam] 측정된 번개 길이: {baseLength:0.00}m", this);
    }

    /// <summary>
    /// 빔 발사. from = 총구, to = 맞은 지점(또는 사거리 끝), hit = 무언가 맞았는지
    /// </summary>
    public void Fire(Vector3 from, Vector3 to, bool hit)
    {
        Vector3 toMuzzle = from - to;
        float dist = toMuzzle.magnitude;
        if (dist < 0.01f) return;

        Vector3 up = toMuzzle / dist;
        if (invertDirection) up = -up;
        transform.SetPositionAndRotation(to, Quaternion.FromToRotation(Vector3.up, up));

        // 맞은 게 없으면 그을음/바닥 빛/라이트 끄기
        if (impactParts != null)
            foreach (var go in impactParts)
                if (go != null) go.SetActive(hit);

        // 이미 재생 중인 파티클을 지우고, 길이 맞춘 뒤 다시 재생
        Stop();

        float k = fitLength ? dist / baseLength * lengthMultiplier : 1f;
        for (int i = 0; i < beamParts.Length; i++)
        {
            if (beamParts[i] == null) continue;
            var main = beamParts[i].main;
            if (main.startSize3D) main.startSizeYMultiplier = originalSizeY[i] * k;
            else main.startSizeMultiplier = originalSizeY[i] * k;
        }

        Play();
    }

    private void Stop()
    {
        if (rootSystem != null) rootSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        else foreach (var ps in GetComponentsInChildren<ParticleSystem>()) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void Play()
    {
        if (rootSystem != null) rootSystem.Play(true);
        else foreach (var ps in GetComponentsInChildren<ParticleSystem>()) ps.Play(false);
    }
}
