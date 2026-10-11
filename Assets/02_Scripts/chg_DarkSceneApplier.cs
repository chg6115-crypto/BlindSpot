#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// BlindSpot 어둠 연출용 에디터 도구.
/// 메뉴: BlindSpot > ...
///  1) 어두운 머티리얼 적용 : Wall_Mat / Floor_Mat / Player_Mat 을 쓰는 렌더러를 chg_ 머티리얼로 교체 (Ctrl+Z 가능)
///  2) 원래 머티리얼로 되돌리기 : 반대로 교체
///  3) 환경광/반사 끄기 : Skybox·Ambient·Environment Reflection 제거 + 씬 카메라 배경 검정
/// 원본 머티리얼 파일은 수정하지 않는다.
/// </summary>
public static class chg_DarkSceneApplier
{
    const string MatDir = "Assets/05_Materials/";

    static readonly (string original, string dark)[] Pairs =
    {
        ("Wall_Mat",   "chg_Wall_Dark"),
        ("Floor_Mat",  "chg_Floor_Dark"),
        ("Player_Mat", "chg_Player_Dark"),
    };

    [MenuItem("BlindSpot/chg_ 어두운 머티리얼 적용 (Wall·Floor·Player)")]
    static void ApplyDark() => Swap(toDark: true);

    [MenuItem("BlindSpot/chg_ 원래 머티리얼로 되돌리기")]
    static void RevertDark() => Swap(toDark: false);

    static void Swap(bool toDark)
    {
        var map = new Dictionary<Material, Material>();
        foreach (var (original, dark) in Pairs)
        {
            var o = AssetDatabase.LoadAssetAtPath<Material>(MatDir + original + ".mat");
            var d = AssetDatabase.LoadAssetAtPath<Material>(MatDir + dark + ".mat");
            if (o == null || d == null)
            {
                Debug.LogWarning($"[chg_DarkSceneApplier] 머티리얼을 찾을 수 없음: {original} / {dark}");
                continue;
            }
            if (toDark) map[o] = d; else map[d] = o;
        }
        if (map.Count == 0) return;

        int changed = 0;
        Undo.SetCurrentGroupName(toDark ? "chg_ Apply Dark Materials" : "chg_ Revert Dark Materials");
        var renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var r in renderers)
        {
            var mats = r.sharedMaterials;
            bool dirty = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null && map.TryGetValue(mats[i], out var swap))
                {
                    mats[i] = swap;
                    dirty = true;
                }
            }
            if (!dirty) continue;

            Undo.RecordObject(r, "chg_ Swap Material");
            r.sharedMaterials = mats;
            PrefabUtility.RecordPrefabInstancePropertyModifications(r);
            changed++;
            Debug.Log($"[chg_DarkSceneApplier] {(toDark ? "적용" : "복구")}: {GetPath(r.transform)}", r);
        }

        EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"[chg_DarkSceneApplier] 렌더러 {changed}개 교체 완료. 씬을 저장해야 반영됩니다.");
    }

    [MenuItem("BlindSpot/chg_ 환경광·반사 끄기 (완전 검정 배경)")]
    static void KillEnvironmentLight()
    {
        Debug.Log("[chg_DarkSceneApplier] 이전 값 → " +
                  $"ambientMode={RenderSettings.ambientMode}, ambientLight={RenderSettings.ambientLight}, " +
                  $"skybox={(RenderSettings.skybox ? RenderSettings.skybox.name : "None")}, " +
                  $"reflectionMode={RenderSettings.defaultReflectionMode}, reflectionIntensity={RenderSettings.reflectionIntensity}");

        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;
        RenderSettings.ambientIntensity = 0f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = null;
        RenderSettings.reflectionIntensity = 0f;
        RenderSettings.fog = false;
        DynamicGI.UpdateEnvironment();

        // 씬 안 카메라 배경을 단색 검정으로
        var cams = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var cam in cams)
        {
            Undo.RecordObject(cam, "chg_ Camera Black Background");
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            PrefabUtility.RecordPrefabInstancePropertyModifications(cam);
        }

        EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"[chg_DarkSceneApplier] 환경광/반사 제거, 카메라 {cams.Length}개 배경 검정. " +
                  "베이크된 라이트맵이 있다면 Lighting 창에서 Clear Baked Data도 실행하세요.");
    }

    static string GetPath(Transform t)
    {
        var path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }
}
#endif
