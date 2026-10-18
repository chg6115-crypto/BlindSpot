using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 발소리. Player_FPS 프리팹 루트에 붙인다 (내 플레이어 / 다른 플레이어 공통).
///  - 이동 거리 기준으로 한 발씩 재생 (걷기 약 2.0m, 달리기 약 2.6m마다)
///    위치 변화로 속도를 계산하므로 다른 플레이어도 네트워크 추가 전송 없이 똑같이 동작
///  - 걷기는 작게 / 달리기는 크게
///  - 다른 플레이어 발소리: 그 위치에서 3D로, 걷기 10m / 달리기 20m까지 들림 (멀수록 작게)
///  - 내 발소리: 2D로 작게
///  - 착지음: 일정 시간 이상 공중에 있다가 착지하면
///  - 공중 / 정지 / 사망 중에는 소리 없음
///  - 믹서 SFX 그룹으로 출력 → 설정의 효과음 볼륨 적용
/// 발소리는 소리만 낼 뿐, 주변 실루엣 표시(FootstepPulse)와는 별개.
/// </summary>
public class FootstepAudio : MonoBehaviour
{
    [Header("소리")]
    [SerializeField] private AudioClip[] footstepClips;
    [SerializeField] private AudioClip landClip;

    [Header("보폭 (m)")]
    [SerializeField, Min(0.3f)] private float walkStride = 2.0f;
    [SerializeField, Min(0.3f)] private float runStride = 2.6f;

    [Header("속도 기준 (m/s)")]
    [Tooltip("이 속도 이상이면 달리기 (FirstPersonController: 걷기 4, 달리기 6)")]
    [SerializeField] private float runSpeedThreshold = 5f;
    [Tooltip("이 속도 미만이면 정지")]
    [SerializeField] private float minMoveSpeed = 0.5f;

    [Header("볼륨")]
    [SerializeField, Range(0f, 1f)] private float walkVolume = 0.35f;
    [SerializeField, Range(0f, 1f)] private float runVolume = 0.85f;
    [Tooltip("내 발소리는 이 비율로 작게")]
    [SerializeField, Range(0f, 1f)] private float localVolumeScale = 0.6f;
    [SerializeField, Range(0f, 1f)] private float landVolume = 0.7f;
    [SerializeField] private Vector2 pitchRange = new Vector2(0.92f, 1.08f);

    [Header("다른 플레이어 발소리가 들리는 거리 (m)")]
    [SerializeField, Min(1f)] private float walkHearDistance = 10f;
    [SerializeField, Min(1f)] private float runHearDistance = 20f;

    [Header("바닥 체크")]
    [Tooltip("바닥으로 인정할 레이어 (Character 제외)")]
    [SerializeField] private LayerMask groundMask = ~(1 << 6);
    [SerializeField] private float groundCheckDistance = 0.3f;
    [Tooltip("이 시간 이상 공중에 있다가 착지하면 착지음")]
    [SerializeField] private float landMinAirTime = 0.3f;

    private static AudioMixerGroup sfxGroup;
    private static bool mixerSearched;

    private AudioSource source;
    private PlayerNetworkSetup setup;
    private Health health;
    private Vector3 lastPos;
    private float smoothSpeed;
    private float distanceSinceStep;
    private float airTime;
    private bool wasGrounded = true;
    private int lastClip = -1;

    private void Awake()
    {
        setup = GetComponent<PlayerNetworkSetup>();
        health = GetComponent<Health>();

        var go = new GameObject("FootstepAudio");
        go.transform.SetParent(transform, false);
        source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 1f;
        source.dopplerLevel = 0f;
        source.outputAudioMixerGroup = FindSfxGroup();
    }

    private void OnEnable()
    {
        lastPos = transform.position;
        smoothSpeed = 0f;
        distanceSinceStep = 0f;
    }

    private static AudioMixerGroup FindSfxGroup()
    {
        if (mixerSearched) return sfxGroup;
        mixerSearched = true;
        var mixer = Resources.Load<AudioMixer>("MainMixer");
        if (mixer != null)
        {
            var groups = mixer.FindMatchingGroups("SFX");
            if (groups != null && groups.Length > 0) sfxGroup = groups[0];
        }
        return sfxGroup;
    }

    private bool IsMine => setup == null || setup.IsMine;

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 delta = transform.position - lastPos;
        lastPos = transform.position;
        delta.y = 0f;
        float moved = delta.magnitude;

        // 순간이동(스폰/라운드 시작)은 걸음으로 치지 않음
        if (moved > 3f)
        {
            distanceSinceStep = 0f;
            smoothSpeed = 0f;
            return;
        }

        smoothSpeed = Mathf.Lerp(smoothSpeed, moved / dt, 10f * dt);

        bool dead = health != null && health.IsDead;
        bool grounded = IsGrounded();

        // 착지음
        if (!grounded) airTime += dt;
        else
        {
            if (!wasGrounded && airTime >= landMinAirTime && !dead) Play(landClip, landVolume, runHearDistance);
            airTime = 0f;
        }
        wasGrounded = grounded;

        if (dead || !grounded || smoothSpeed < minMoveSpeed)
        {
            // 멈추면 다음 걸음은 조금 빨리 나오게
            distanceSinceStep = Mathf.Min(distanceSinceStep, walkStride * 0.5f);
            return;
        }

        bool running = smoothSpeed >= runSpeedThreshold;
        distanceSinceStep += moved;
        float stride = running ? runStride : walkStride;
        if (distanceSinceStep >= stride)
        {
            distanceSinceStep = 0f;
            Play(PickClip(), running ? runVolume : walkVolume, running ? runHearDistance : walkHearDistance);
        }
    }

    private bool IsGrounded()
    {
        Vector3 origin = transform.position + Vector3.up * 0.1f;
        return Physics.Raycast(origin, Vector3.down, 0.1f + groundCheckDistance, groundMask, QueryTriggerInteraction.Ignore);
    }

    private AudioClip PickClip()
    {
        if (footstepClips == null || footstepClips.Length == 0) return null;
        if (footstepClips.Length == 1) return footstepClips[0];

        // 같은 소리가 연속으로 나오지 않게
        int i = Random.Range(0, footstepClips.Length - 1);
        if (i >= lastClip) i++;
        lastClip = i;
        return footstepClips[i];
    }

    private void Play(AudioClip clip, float volume, float hearDistance)
    {
        if (clip == null || source == null) return;

        bool mine = IsMine;
        source.spatialBlend = mine ? 0f : 1f;
        source.maxDistance = hearDistance;
        source.pitch = Random.Range(pitchRange.x, pitchRange.y);
        source.PlayOneShot(clip, mine ? volume * localVolumeScale : volume);
    }
}
