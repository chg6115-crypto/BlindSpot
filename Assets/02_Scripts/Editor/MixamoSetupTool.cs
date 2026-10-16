using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Mixamo 팩 임포트 설정 + 몸 Animator Controller 자동 생성 (에디터 전용).
/// 메뉴: BlindSpot > Animation
///   1. Mixamo 팩 임포트 설정  : 폴더 안 FBX 전부 Humanoid, Y Bot 아바타 공유, Loop/Bake Into Pose 설정
///   2. 몸 컨트롤러 생성 + 적용 : 8방향 걷기/달리기/스프린트 블렌드 트리 + 점프 → CharacterBody 프리팹에 연결
/// 폴더를 Project 창에서 선택한 상태로 실행하면 그 폴더를, 아니면 기본 폴더를 사용.
/// </summary>
public static class MixamoSetupTool
{
    const string DefaultFolder = "Assets/07_Animations/Pro Rifle Pack";
    const string ControllerPath = "Assets/07_Animations/BlindSpotRifleBody.controller";
    const string CharacterBodyPrefab = "Assets/03_Prefabs/CharacterBody.prefab";

    // 이름에 이 단어가 들어가면 반복 재생
    static readonly string[] LoopKeywords = { "idle", "walk", "run", "sprint", "jump loop" };

    // ───────── 1. 임포트 설정 ─────────

    [MenuItem("BlindSpot/Animation/1. Mixamo 팩 임포트 설정")]
    public static void SetupImport()
    {
        string folder = GetSelectedFolder() ?? DefaultFolder;
        string[] paths = AssetDatabase.FindAssets("t:Model", new[] { folder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (paths.Length == 0)
        {
            EditorUtility.DisplayDialog("Mixamo", $"{folder} 에 FBX가 없습니다.", "확인");
            return;
        }

        // 기준 캐릭터 (Y Bot / X Bot / T-pose)
        string basePath = paths.FirstOrDefault(p =>
        {
            string n = Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
            return n.Contains("bot") || n.Contains("t-pose") || n.Contains("tpose");
        });
        if (basePath == null)
        {
            EditorUtility.DisplayDialog("Mixamo", "기준 캐릭터(Y Bot 등) FBX를 찾지 못했습니다.\nPose를 T-pose로 받았는지 확인하세요.", "확인");
            return;
        }

        try
        {
            // 기준 캐릭터: Humanoid 아바타 생성
            var baseImp = (ModelImporter)AssetImporter.GetAtPath(basePath);
            baseImp.animationType = ModelImporterAnimationType.Human;
            baseImp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            baseImp.importAnimation = false;
            baseImp.importCameras = false;
            baseImp.importLights = false;
            baseImp.materialImportMode = ModelImporterMaterialImportMode.None;
            baseImp.SaveAndReimport();

            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(basePath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isHuman)
            {
                EditorUtility.DisplayDialog("Mixamo", "기준 캐릭터에서 Humanoid 아바타를 만들지 못했습니다.", "확인");
                return;
            }

            int done = 0;
            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                if (path == basePath) continue;
                EditorUtility.DisplayProgressBar("Mixamo 임포트 설정", Path.GetFileName(path), (float)i / paths.Length);

                var imp = (ModelImporter)AssetImporter.GetAtPath(path);
                imp.animationType = ModelImporterAnimationType.Human;
                imp.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                imp.sourceAvatar = avatar;
                imp.importAnimation = true;
                imp.importCameras = false;
                imp.importLights = false;
                imp.materialImportMode = ModelImporterMaterialImportMode.None;

                string clipName = Path.GetFileNameWithoutExtension(path);
                bool loop = IsLoop(clipName);

                var clips = imp.defaultClipAnimations;
                foreach (var c in clips)
                {
                    c.name = clipName;
                    c.loopTime = loop;
                    c.loopPose = false;
                    // Bake Into Pose (제자리) - 이동은 CharacterController가 담당
                    c.lockRootRotation = true;
                    c.keepOriginalOrientation = true;   // 기준: Original
                    c.lockRootHeightY = true;
                    c.keepOriginalPositionY = false;
                    c.heightFromFeet = true;            // 기준: Feet
                    c.lockRootPositionXZ = true;
                    c.keepOriginalPositionXZ = true;
                }
                imp.clipAnimations = clips;
                imp.SaveAndReimport();
                done++;
            }

            Debug.Log($"[MixamoSetupTool] {folder}: 기준 아바타 = {Path.GetFileName(basePath)}, 애니메이션 {done}개 설정 완료");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // ───────── 2. 컨트롤러 생성 ─────────

    [MenuItem("BlindSpot/Animation/2. 몸 컨트롤러 생성 + CharacterBody에 적용")]
    public static void CreateController()
    {
        string folder = GetSelectedFolder() ?? DefaultFolder;
        var clips = LoadClips(folder);
        if (clips.Count == 0)
        {
            EditorUtility.DisplayDialog("Mixamo", $"{folder} 에서 애니메이션 클립을 찾지 못했습니다.\n먼저 1번을 실행하세요.", "확인");
            return;
        }

        if (File.Exists(ControllerPath) &&
            !EditorUtility.DisplayDialog("Mixamo", $"{ControllerPath} 가 이미 있습니다. 새로 만들까요?", "새로 만들기", "취소"))
            return;
        AssetDatabase.DeleteAsset(ControllerPath);

        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        ctrl.AddParameter("MoveX", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
        ctrl.AddParameter(new AnimatorControllerParameter
        {
            name = "IsGrounded",
            type = AnimatorControllerParameterType.Bool,
            defaultBool = true
        });

        // Base Layer: IK Pass 켜기 (HandIK용)
        var layers = ctrl.layers;
        layers[0].iKPass = true;
        ctrl.layers = layers;
        var sm = ctrl.layers[0].stateMachine;

        // 이동 블렌드 트리 (2D Freeform Directional)
        var loco = ctrl.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
        tree.blendType = BlendTreeType.FreeformDirectional2D;
        tree.blendParameter = "MoveX";
        tree.blendParameterY = "MoveZ";
        tree.useAutomaticThresholds = false;

        AddChild(tree, clips, "idle", Vector2.zero);

        // 8방향: 이름 접미사 → 방향
        var dirs = new (string suffix, Vector2 dir)[]
        {
            ("forward", new Vector2(0f, 1f)),
            ("forward right", new Vector2(0.7071f, 0.7071f)),
            ("right", new Vector2(1f, 0f)),
            ("backward right", new Vector2(0.7071f, -0.7071f)),
            ("backward", new Vector2(0f, -1f)),
            ("backward left", new Vector2(-0.7071f, -0.7071f)),
            ("left", new Vector2(-1f, 0f)),
            ("forward left", new Vector2(-0.7071f, 0.7071f)),
        };
        // 반경: 걷기 0.35 / 달리기 1 / 스프린트 1.5 (BodyAnimatorDriver의 MoveX/MoveZ 기준)
        foreach (var (prefix, radius) in new[] { ("walk", 0.35f), ("run", 1f), ("sprint", 1.5f) })
            foreach (var (suffix, dir) in dirs)
                AddChild(tree, clips, $"{prefix} {suffix}", dir * radius);

        sm.defaultState = loco;

        // 점프 (공중)
        if (clips.TryGetValue("jump loop", out var jumpClip))
        {
            var air = sm.AddState("InAir");
            air.motion = jumpClip;

            var toAir = loco.AddTransition(air);
            toAir.hasExitTime = false;
            toAir.duration = 0.1f;
            toAir.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsGrounded");

            var toGround = air.AddTransition(loco);
            toGround.hasExitTime = false;
            toGround.duration = 0.15f;
            toGround.AddCondition(AnimatorConditionMode.If, 0f, "IsGrounded");
        }

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();

        // CharacterBody 프리팹에 연결 (Mannequin, Player_FPS는 이 프리팹을 공유)
        var root = PrefabUtility.LoadPrefabContents(CharacterBodyPrefab);
        try
        {
            var anim = root.GetComponent<Animator>();
            if (anim != null)
            {
                anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion = false;
                PrefabUtility.SaveAsPrefabAsset(root, CharacterBodyPrefab);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        Selection.activeObject = ctrl;
        Debug.Log($"[MixamoSetupTool] {ControllerPath} 생성 (클립 {clips.Count}개) → CharacterBody에 적용");
    }

    // ───────── 공통 ─────────

    static bool IsLoop(string clipName)
    {
        string n = clipName.ToLowerInvariant();
        if (n.Contains("death") || n.Contains("turn")) return false;
        if (n.Contains("jump up") || n.Contains("jump down")) return false;
        return LoopKeywords.Any(k => n.Contains(k));
    }

    static Dictionary<string, AnimationClip> LoadClips(string folder)
    {
        var dict = new Dictionary<string, AnimationClip>();
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
            {
                if (clip.name.StartsWith("__preview__")) continue;
                dict[clip.name.ToLowerInvariant()] = clip;
            }
        }
        return dict;
    }

    static void AddChild(BlendTree tree, Dictionary<string, AnimationClip> clips, string name, Vector2 pos)
    {
        if (clips.TryGetValue(name, out var clip)) tree.AddChild(clip, pos);
        else Debug.LogWarning($"[MixamoSetupTool] 클립 없음: {name}");
    }

    static string GetSelectedFolder()
    {
        foreach (var obj in Selection.GetFiltered<Object>(SelectionMode.Assets))
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (AssetDatabase.IsValidFolder(path)) return path;
        }
        return null;
    }
}
