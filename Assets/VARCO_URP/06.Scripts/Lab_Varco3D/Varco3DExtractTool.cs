using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static class Varco3DExtractTool
{
    private const string DefaultOutputFolder = "Assets/VARCO_Converted";
    private const string FbxExporterTypeName = "UnityEditor.Formats.Fbx.Exporter.ModelExporter";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly Regex TupleRegex = new Regex(@"\(([^\)]*)\)", RegexOptions.Compiled);
    private static readonly Regex QuotedTokenRegex = new Regex("\\\"([^\\\"]+)\\\"", RegexOptions.Compiled);

    private static readonly HumanBodyBones[] RequiredHumanoidBones =
    {
        HumanBodyBones.Hips,
        HumanBodyBones.Spine,
        HumanBodyBones.Head,
        HumanBodyBones.LeftUpperArm,
        HumanBodyBones.LeftLowerArm,
        HumanBodyBones.LeftHand,
        HumanBodyBones.RightUpperArm,
        HumanBodyBones.RightLowerArm,
        HumanBodyBones.RightHand,
        HumanBodyBones.LeftUpperLeg,
        HumanBodyBones.LeftLowerLeg,
        HumanBodyBones.LeftFoot,
        HumanBodyBones.RightUpperLeg,
        HumanBodyBones.RightLowerLeg,
        HumanBodyBones.RightFoot
    };

    private static readonly Dictionary<HumanBodyBones, string[]> HumanoidBoneAliases =
        new Dictionary<HumanBodyBones, string[]>
        {
            { HumanBodyBones.Hips, new[] { "hips", "hip", "pelvis", "rootpelvis" } },
            { HumanBodyBones.Spine, new[] { "spine", "spine0", "spine01" } },
            { HumanBodyBones.Chest, new[] { "chest", "spine1", "spine02" } },
            { HumanBodyBones.UpperChest, new[] { "upperchest", "spine2", "spine03" } },
            { HumanBodyBones.Neck, new[] { "neck", "neck1", "neck01" } },
            { HumanBodyBones.Head, new[] { "head" } },
            { HumanBodyBones.LeftShoulder, new[] { "leftshoulder", "lshoulder", "shoulderl", "leftclavicle", "claviclel" } },
            { HumanBodyBones.LeftUpperArm, new[] { "leftupperarm", "lupperarm", "upperarml", "leftarm", "arml" } },
            { HumanBodyBones.LeftLowerArm, new[] { "leftlowerarm", "llowerarm", "lowerarml", "leftforearm", "forearml" } },
            { HumanBodyBones.LeftHand, new[] { "lefthand", "lhand", "handl", "leftwrist", "wristl" } },
            { HumanBodyBones.RightShoulder, new[] { "rightshoulder", "rshoulder", "shoulderr", "rightclavicle", "clavicler" } },
            { HumanBodyBones.RightUpperArm, new[] { "rightupperarm", "rupperarm", "upperarmr", "rightarm", "armr" } },
            { HumanBodyBones.RightLowerArm, new[] { "rightlowerarm", "rlowerarm", "lowerarmr", "rightforearm", "forearmr" } },
            { HumanBodyBones.RightHand, new[] { "righthand", "rhand", "handr", "rightwrist", "wristr" } },
            { HumanBodyBones.LeftUpperLeg, new[] { "leftupperleg", "lupperleg", "upperlegl", "leftthigh", "thighl" } },
            { HumanBodyBones.LeftLowerLeg, new[] { "leftlowerleg", "llowerleg", "lowerlegl", "leftcalf", "calfl", "leftshin", "shinl" } },
            { HumanBodyBones.LeftFoot, new[] { "leftfoot", "lfoot", "footl", "leftankle", "anklel" } },
            { HumanBodyBones.LeftToes, new[] { "lefttoes", "lefttoe", "ltoes", "toesl" } },
            { HumanBodyBones.RightUpperLeg, new[] { "rightupperleg", "rupperleg", "upperlegr", "rightthigh", "thighr" } },
            { HumanBodyBones.RightLowerLeg, new[] { "rightlowerleg", "rlowerleg", "lowerlegr", "rightcalf", "calfr", "rightshin", "shinr" } },
            { HumanBodyBones.RightFoot, new[] { "rightfoot", "rfoot", "footr", "rightankle", "ankler" } },
            { HumanBodyBones.RightToes, new[] { "righttoes", "righttoe", "rtoes", "toesr" } }
        };

    // Public entry used by the SceneView overlay. Hierarchy selections do not have asset GUIDs.
    public static void RunFromCurrentSelection()
    {
        try
        {
            UnityEngine.Object active = Selection.activeObject;
            GameObject sceneObject = Selection.activeGameObject;
            GameObject characterRoot = FindCharacterRoot(sceneObject);
            string selectedAssetPath = AssetDatabase.GetAssetPath(active);

            Debug.Log($"[Varco3D] Run Extract clicked. Selection: {(active != null ? active.name : "None")}, AssetPath: {selectedAssetPath}");

            if (IsFbxPath(selectedAssetPath))
            {
                Run(selectedAssetPath);
                return;
            }

            if (IsUsdZPath(selectedAssetPath))
            {
                GameObject source = characterRoot ?? FindSceneCharacterForUsdZ(selectedAssetPath) ?? ResolveGameObjectFromAsset(selectedAssetPath);
                if (source == null)
                {
                    EditorUtility.DisplayDialog(
                        "VARCO USDZ Converter",
                        "USDZ is selected, but a character root could not be found in the Hierarchy.\n\n" +
                        "Import the character with VARCO Bridge, select its root in the Hierarchy, then click Run Extract again.",
                        "OK");
                    return;
                }

                ConvertUsdZ(selectedAssetPath, source);
                return;
            }

            if (characterRoot != null)
            {
                string matchedUsdZ = FindBestUsdZAssetForGameObject(characterRoot);
                if (!string.IsNullOrEmpty(matchedUsdZ))
                {
                    Debug.Log($"[Varco3D] Matched USDZ automatically: {matchedUsdZ}");
                    ConvertUsdZ(matchedUsdZ, characterRoot);
                    return;
                }

                string pickedUsdZ = PickOrImportUsdZ();
                if (!string.IsNullOrEmpty(pickedUsdZ))
                    ConvertUsdZ(pickedUsdZ, characterRoot);
                return;
            }

            EditorUtility.DisplayDialog(
                "VARCO Extract",
                "Select one of the following first:\n\n" +
                "• VARCO character root in the Hierarchy\n" +
                "• .usdz asset in the Project window\n" +
                "• .fbx asset in the Project window",
                "OK");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("VARCO Extract", "Run Extract failed. Check Console for details.", "OK");
        }
    }

    // ---------------------------------------------------------------------
    // MENU
    // ---------------------------------------------------------------------

    [MenuItem("Tools/URPLab/VARCO/Run Extract (Auto)", false, 0)]
    private static void RunExtractAutoMenu()
    {
        RunFromCurrentSelection();
    }

    [MenuItem("Tools/URPLab/VARCO/Extract USDZ Textures Only", false, 1)]
    private static void ExtractUsdZTexturesOnlyMenu()
    {
        string path = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (!IsUsdZPath(path) && Selection.activeGameObject != null)
            path = FindBestUsdZAssetForGameObject(Selection.activeGameObject);

        if (string.IsNullOrEmpty(path))
        {
            EditorUtility.DisplayDialog("VARCO Texture Extract", "Select a USDZ asset or a VARCO character root first.", "OK");
            return;
        }

        ExtractUsdZTexturesOnly(path);
    }

    [MenuItem("Assets/VARCO/Convert USDZ to FBX (Motion + Textures)", false, 2000)]
    private static void ConvertSelectedUsdZAsset()
    {
        string usdzAssetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (!IsUsdZPath(usdzAssetPath))
            return;

        GameObject source = ResolveGameObjectFromAsset(usdzAssetPath);
        if (source == null)
        {
            EditorUtility.DisplayDialog(
                "VARCO USDZ Converter",
                "The USDZ asset was found, but Unity did not expose it as a GameObject.\n\n" +
                "Use the VARCO Bridge first, select the imported character root in the Hierarchy, then run:\n" +
                "Tools > URPLab > VARCO > Convert Character + Pick USDZ",
                "OK");
            return;
        }

        ConvertUsdZ(usdzAssetPath, source);
    }

    [MenuItem("Assets/VARCO/Convert USDZ to FBX (Motion + Textures)", true)]
    private static bool ValidateConvertSelectedUsdZAsset()
    {
        return IsUsdZPath(AssetDatabase.GetAssetPath(Selection.activeObject));
    }

    [MenuItem("Tools/URPLab/VARCO/Convert Character + Pick USDZ")]
    private static void ConvertSelectedCharacterWithPickedUsdZ()
    {
        GameObject source = FindCharacterRoot(Selection.activeGameObject);
        if (source == null)
        {
            EditorUtility.DisplayDialog(
                "VARCO USDZ Converter",
                "Select the VARCO character root in the Hierarchy first.",
                "OK");
            return;
        }

        string usdzAssetPath = PickOrImportUsdZ();
        if (string.IsNullOrEmpty(usdzAssetPath))
            return;

        ConvertUsdZ(usdzAssetPath, source);
    }

    [MenuItem("Tools/URPLab/VARCO/Extract Selected FBX")]
    private static void ExtractSelectedFbx()
    {
        string path = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (!IsFbxPath(path))
        {
            EditorUtility.DisplayDialog("VARCO Extract", "Select an FBX asset in the Project window.", "OK");
            return;
        }

        Run(path);
    }

    [MenuItem("Tools/URPLab/VARCO/Extract Selected FBX", true)]
    private static bool ValidateExtractSelectedFbx()
    {
        return IsFbxPath(AssetDatabase.GetAssetPath(Selection.activeObject));
    }

    // Compatibility entry point for older VARCO scripts.
    public static void Run(string assetPath)
    {
        if (IsFbxPath(assetPath))
        {
            ProcessFbx(assetPath, null);
            return;
        }

        if (IsUsdZPath(assetPath))
        {
            GameObject source = ResolveGameObjectFromAsset(assetPath);
            if (source == null)
            {
                Debug.LogError(
                    "[Varco3D] USDZ was selected but Unity did not expose a GameObject. " +
                    "Select the Bridge-imported character root and use Tools/URPLab/VARCO/Convert Character + Pick USDZ.");
                return;
            }

            ConvertUsdZ(assetPath, source);
            return;
        }

        Debug.LogError("[Varco3D] Run requires an .usdz or .fbx asset path.");
    }

    // ---------------------------------------------------------------------
    // USDZ -> ANIMATION CLIP + TEXTURES -> FBX
    // ---------------------------------------------------------------------

    private static void ConvertUsdZ(string usdzAssetPath, GameObject source)
    {
        if (!TryFindFbxExporter(out MethodInfo exportMethod))
        {
            EditorUtility.DisplayDialog(
                "FBX Exporter Missing",
                "Unity FBX Exporter is required. Install package: com.unity.formats.fbx",
                "OK");
            return;
        }

        string absoluteUsdZPath = AssetPathToAbsolutePath(usdzAssetPath);
        if (!File.Exists(absoluteUsdZPath))
        {
            Debug.LogError($"[Varco3D] USDZ file not found: {absoluteUsdZPath}");
            return;
        }

        string modelName = SanitizeFileName(Path.GetFileNameWithoutExtension(usdzAssetPath));
        string rootFolder = $"{DefaultOutputFolder}/{modelName}";
        string textureFolder = $"{rootFolder}/Textures";
        string animationFolder = $"{rootFolder}/Animations";
        string materialFolder = $"{rootFolder}/Materials";
        string prefabFolder = $"{rootFolder}/Prefabs";

        EnsureAssetFolderRecursive(rootFolder);
        EnsureAssetFolderRecursive(textureFolder);
        EnsureAssetFolderRecursive(animationFolder);
        EnsureAssetFolderRecursive(materialFolder);
        EnsureAssetFolderRecursive(prefabFolder);

        string usdaText;
        List<string> extractedTexturePaths;

        try
        {
            EditorUtility.DisplayProgressBar("VARCO USDZ Converter", "Reading USDZ package...", 0.10f);
            usdaText = ExtractUsdTextAndTextures(absoluteUsdZPath, textureFolder, out extractedTexturePaths);
        }
        catch (Exception ex)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("VARCO USDZ Converter", "Failed to read the USDZ package. Check Console.", "OK");
            return;
        }

        if (string.IsNullOrEmpty(usdaText))
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError("[Varco3D] No readable USDA text was found inside the USDZ.");
            return;
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        extractedTexturePaths = NormalizeTextureAssetNames(modelName, extractedTexturePaths);
        ConfigureExtractedTextureImporters(extractedTexturePaths);

        EditorUtility.DisplayProgressBar("VARCO USDZ Converter", "Parsing SkelAnimation...", 0.30f);
        UsdSkelAnimationData animationData;
        try
        {
            animationData = ParseSkelAnimation(usdaText);
        }
        catch (Exception ex)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("VARCO USDZ Converter", "SkelAnimation parse failed. Check Console.", "OK");
            return;
        }

        if (animationData.Joints.Count == 0 || animationData.FrameNumbers.Count == 0)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError("[Varco3D] USDZ contains no usable SkelAnimation joint/timeSample data.");
            return;
        }

        LogSkeletonHierarchy(source, animationData);

        EditorUtility.DisplayProgressBar("VARCO USDZ Converter", "Building Unity AnimationClip...", 0.48f);
        string originalClipAssetPath = $"{animationFolder}/{modelName}_Original.anim";
        string clipAssetPath = $"{animationFolder}/{modelName}_RetargetClean.anim";
        AnimationClip originalClip = CopyImportedUsdAnimationClip(usdzAssetPath, source, originalClipAssetPath, out int boundJointCount);
        AnimationClip clip = originalClip != null
            ? CreateRootHeightCleanClip(originalClip, source, clipAssetPath)
            : null;
        if (originalClip == null)
        {
            Debug.LogWarning(
                "[Varco3D] Unity-imported USDZ AnimationClip was not available. " +
                "Falling back to direct USDA curve parsing; verify bone axes carefully.");
            clip = BuildAnimationClip(animationData, source, clipAssetPath, out boundJointCount);
        }
        if (clip == null)
        {
            EditorUtility.ClearProgressBar();
            return;
        }

        if (originalClip != null)
            AnalyzeAnimationClip(originalClip, source.transform, "Original USDZ clip (preserved)");
        AnalyzeAnimationClip(clip, source.transform, "Retarget clean clip");

        Material material = CreateOrUpdateUrpMaterial(modelName, materialFolder, extractedTexturePaths);

        EditorUtility.DisplayProgressBar("VARCO USDZ Converter", "Preparing animated character...", 0.62f);
        bool applyFacingCorrection = HasManualFacingCorrection(source);
        GameObject exportRoot = UnityEngine.Object.Instantiate(source);
        exportRoot.name = modelName;

        try
        {
            GameObject referencePoseSource = ResolveGameObjectFromAsset(usdzAssetPath);
            RestoreReferencePoseForExport(exportRoot, referencePoseSource ?? source);
            if (applyFacingCorrection)
            {
                exportRoot.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                Debug.Log("[Varco3D] Preserved the detected 180-degree front-facing correction on the exported FBX root.");
            }

            if (material != null)
                AssignMaterial(exportRoot, material);

            foreach (Animator animator in exportRoot.GetComponentsInChildren<Animator>(true))
            {
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                animator.enabled = false;
            }

            foreach (Animation existingAnimation in exportRoot.GetComponentsInChildren<Animation>(true))
                UnityEngine.Object.DestroyImmediate(existingAnimation);

            Animation animation = exportRoot.AddComponent<Animation>();

            clip.legacy = true;
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            animation.AddClip(clip, clip.name);
            animation.clip = clip;
            animation.playAutomatically = false;

            string fbxAssetPath = $"{rootFolder}/{modelName}.fbx";
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(fbxAssetPath) != null)
                AssetDatabase.DeleteAsset(fbxAssetPath);

            string absoluteFbxPath = AssetPathToAbsolutePath(fbxAssetPath);

            EditorUtility.DisplayProgressBar("VARCO USDZ Converter", "Exporting FBX with baked motion...", 0.76f);
            object result = exportMethod.Invoke(null, new object[] { absoluteFbxPath, exportRoot });
            string exportedPath = result as string;

            if (string.IsNullOrEmpty(exportedPath) || !File.Exists(absoluteFbxPath))
            {
                Debug.LogError("[Varco3D] FBX export failed. No FBX file was created.");
                return;
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            HumanoidReport humanoidReport = ConfigureFbxImporter(fbxAssetPath);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            EditorUtility.DisplayProgressBar("VARCO USDZ Converter", "Creating URP prefab...", 0.90f);
            ProcessFbx(fbxAssetPath, material);

            AnimationClip[] importedClips = GetImportedAnimationClips(fbxAssetPath);
            foreach (AnimationClip importedClip in importedClips)
                AnalyzeAnimationClip(importedClip, null, $"FBX clip '{importedClip.name}'");

            humanoidReport = ValidateImportedHumanoid(fbxAssetPath, humanoidReport);
            int textureCount = extractedTexturePaths.Count;

            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(fbxAssetPath);
            EditorGUIUtility.PingObject(Selection.activeObject);

            Debug.Log(
                $"[Varco3D] USDZ conversion completed.\n" +
                $"FBX: {fbxAssetPath}\n" +
                $"USD joints: {animationData.Joints.Count}, Bound joints: {boundJointCount}\n" +
                $"Frames: {animationData.StartFrame:0.###} - {animationData.EndFrame:0.###} @ {animationData.FramesPerSecond:0.###} FPS\n" +
                $"Extracted textures: {textureCount}\n" +
                $"FBX AnimationClips: {(importedClips.Length == 0 ? "None" : string.Join(", ", importedClips.Select(c => c.name)))}\n" +
                $"Humanoid Mapping: {humanoidReport.MappedCount}/{RequiredHumanoidBones.Length} required bones\n" +
                $"Avatar Valid: {humanoidReport.AvatarValid}, Human: {humanoidReport.AvatarHuman}\n" +
                $"Retarget Ready: {humanoidReport.RetargetReady}");

            if (importedClips.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "VARCO USDZ Converter",
                    "FBX was created, but Unity did not detect an AnimationClip inside the exported FBX.\n\n" +
                    "The extracted .anim and textures are preserved. Check Console and FBX Exporter package version.",
                    "OK");
            }
            else
            {
                EditorUtility.DisplayDialog(
                    "VARCO USDZ Converter",
                    $"Conversion completed.\n\nMotion: {importedClips.Length} clip(s)\nTextures: {textureCount}\nBound joints: {boundJointCount}/{animationData.Joints.Count}",
                    "OK");
            }
        }
        catch (TargetInvocationException ex)
        {
            Debug.LogException(ex.InnerException ?? ex);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
        finally
        {
            if (exportRoot != null)
                UnityEngine.Object.DestroyImmediate(exportRoot);

            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }

    private static string ExtractUsdTextAndTextures(string absoluteUsdZPath, string textureAssetFolder, out List<string> textureAssetPaths)
    {
        textureAssetPaths = new List<string>();
        string usdaText = null;
        string textureAbsoluteFolder = AssetPathToAbsolutePath(textureAssetFolder);
        Directory.CreateDirectory(textureAbsoluteFolder);

        int textureEntriesFound = 0;
        int textureFilesWritten = 0;

        using (ZipArchive archive = ZipFile.OpenRead(absoluteUsdZPath))
        {
            ZipArchiveEntry usdEntry = archive.Entries.FirstOrDefault(e =>
                e.FullName.EndsWith(".usda", StringComparison.OrdinalIgnoreCase));

            if (usdEntry == null)
            {
                ZipArchiveEntry binaryUsd = archive.Entries.FirstOrDefault(e =>
                    e.FullName.EndsWith(".usd", StringComparison.OrdinalIgnoreCase) ||
                    e.FullName.EndsWith(".usdc", StringComparison.OrdinalIgnoreCase));

                if (binaryUsd != null)
                    throw new NotSupportedException("This USDZ contains binary USD/USDC instead of USDA text.");

                throw new InvalidDataException("No .usda file was found inside the USDZ package.");
            }

            using (Stream stream = usdEntry.Open())
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                usdaText = reader.ReadToEnd();

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name) || !IsTextureEntry(entry.FullName))
                    continue;

                textureEntriesFound++;
                string fileName = SanitizeFileName(Path.GetFileName(entry.FullName));
                if (string.IsNullOrEmpty(fileName))
                    continue;

                string outputAbsolutePath = Path.Combine(textureAbsoluteFolder, fileName);
                using (Stream input = entry.Open())
                using (FileStream output = new FileStream(outputAbsolutePath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    input.CopyTo(output);

                if (!File.Exists(outputAbsolutePath) || new FileInfo(outputAbsolutePath).Length == 0)
                {
                    Debug.LogWarning($"[Varco3D] Texture write failed or produced an empty file: {outputAbsolutePath}");
                    continue;
                }

                string assetPath = NormalizeAssetPath($"{textureAssetFolder}/{fileName}");
                textureAssetPaths.Add(assetPath);
                textureFilesWritten++;
                Debug.Log($"[Varco3D] USDZ texture extracted: {assetPath} ({new FileInfo(outputAbsolutePath).Length:N0} bytes)");
            }
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach (string texturePath in textureAssetPaths)
        {
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath) == null)
                Debug.LogWarning($"[Varco3D] Texture exists on disk but Unity did not import it as Texture2D: {texturePath}");
        }

        Debug.Log($"[Varco3D] USDZ package scan: texture entries={textureEntriesFound}, files written={textureFilesWritten}, folder={textureAssetFolder}");

        if (textureEntriesFound > 0 && textureFilesWritten == 0)
            throw new IOException("Texture entries were found in the USDZ, but none could be written.");

        return usdaText;
    }

    private static void ExtractUsdZTexturesOnly(string usdzAssetPath)
    {
        string absoluteUsdZPath = AssetPathToAbsolutePath(usdzAssetPath);
        if (!File.Exists(absoluteUsdZPath))
        {
            Debug.LogError($"[Varco3D] USDZ not found: {absoluteUsdZPath}");
            return;
        }

        string modelName = SanitizeFileName(Path.GetFileNameWithoutExtension(usdzAssetPath));
        string rootFolder = $"{DefaultOutputFolder}/{modelName}";
        string textureFolder = $"{rootFolder}/Textures";
        EnsureAssetFolderRecursive(textureFolder);

        try
        {
            ExtractUsdTextAndTextures(absoluteUsdZPath, textureFolder, out List<string> texturePaths);
            ConfigureExtractedTextureImporters(texturePaths);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EditorUtility.DisplayDialog("VARCO Texture Extract", $"Extracted {texturePaths.Count} texture(s).\n\n{textureFolder}", "OK");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("VARCO Texture Extract", "Texture extraction failed. Check Console.", "OK");
        }
    }

    private static UsdSkelAnimationData ParseSkelAnimation(string usdaText)
    {
        string skelBlock = ExtractPrimBlock(usdaText, "def SkelAnimation");
        if (string.IsNullOrEmpty(skelBlock))
            throw new InvalidDataException("def SkelAnimation was not found.");

        var data = new UsdSkelAnimationData
        {
            FramesPerSecond = ParseMetadataFloat(usdaText, "framesPerSecond", 30f),
            TimeCodesPerSecond = ParseMetadataFloat(usdaText, "timeCodesPerSecond", 30f)
        };

        string jointsArray = ExtractArrayAssignment(skelBlock, "uniform token[] joints");
        foreach (Match match in QuotedTokenRegex.Matches(jointsArray ?? string.Empty))
            data.Joints.Add(match.Groups[1].Value);

        ParseQuaternionSamples(ExtractTimeSamplesBlock(skelBlock, "quatf[] rotations.timeSamples"), data.Rotations);
        ParseVector3Samples(ExtractTimeSamplesBlock(skelBlock, "float3[] translations.timeSamples"), data.Translations);
        ParseVector3Samples(ExtractTimeSamplesBlock(skelBlock, "float3[] scales.timeSamples"), data.Scales);

        var frameNumbers = new SortedSet<float>();
        foreach (float frame in data.Rotations.Keys) frameNumbers.Add(frame);
        foreach (float frame in data.Translations.Keys) frameNumbers.Add(frame);
        foreach (float frame in data.Scales.Keys) frameNumbers.Add(frame);

        data.FrameNumbers.AddRange(frameNumbers);
        if (data.FrameNumbers.Count > 0)
        {
            data.StartFrame = data.FrameNumbers[0];
            data.EndFrame = data.FrameNumbers[data.FrameNumbers.Count - 1];
        }

        return data;
    }

    private static AnimationClip CopyImportedUsdAnimationClip(
        string usdzAssetPath,
        GameObject source,
        string clipAssetPath,
        out int boundJointCount)
    {
        boundJointCount = 0;
        AnimationClip sourceClip = AssetDatabase.LoadAllAssetsAtPath(usdzAssetPath)
            .OfType<AnimationClip>()
            .Where(candidate => !candidate.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.length)
            .FirstOrDefault();

        if (sourceClip == null)
            return null;

        EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(sourceClip);
        if (bindings.Length == 0)
            return null;

        HashSet<string> hierarchyPaths = new HashSet<string>(
            source.GetComponentsInChildren<Transform>(true)
                .Select(transform => AnimationUtility.CalculateTransformPath(transform, source.transform)),
            StringComparer.OrdinalIgnoreCase);

        string[] animatedPaths = bindings
            .Where(binding => binding.type == typeof(Transform))
            .Select(binding => NormalizeHierarchyPath(binding.path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        boundJointCount = animatedPaths.Count(path => hierarchyPaths.Contains(path));

        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(clipAssetPath) != null)
            AssetDatabase.DeleteAsset(clipAssetPath);

        var copiedClip = new AnimationClip();
        EditorUtility.CopySerialized(sourceClip, copiedClip);
        copiedClip.name = Path.GetFileNameWithoutExtension(clipAssetPath);
        copiedClip.legacy = true;
        copiedClip.EnsureQuaternionContinuity();
        AssetDatabase.CreateAsset(copiedClip, clipAssetPath);
        AssetDatabase.SaveAssets();

        Debug.Log(
            $"[Varco3D] Preserved Unity-imported USDZ clip '{sourceClip.name}' instead of rebuilding rotations. " +
            $"Transform paths matched: {boundJointCount}/{animatedPaths.Length}.");
        return copiedClip;
    }

    private static AnimationClip CreateRootHeightCleanClip(
        AnimationClip originalClip,
        GameObject source,
        string cleanClipAssetPath)
    {
        if (originalClip == null || source == null)
            return null;

        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(cleanClipAssetPath) != null)
            AssetDatabase.DeleteAsset(cleanClipAssetPath);

        var cleanClip = new AnimationClip();
        EditorUtility.CopySerialized(originalClip, cleanClip);
        cleanClip.name = Path.GetFileNameWithoutExtension(cleanClipAssetPath);
        cleanClip.legacy = true;

        string rootPath = FindLikelyRootMotionPath(source.transform);
        if (string.IsNullOrEmpty(rootPath))
        {
            Debug.LogWarning(
                "[Varco3D] Retarget clean clip could not resolve a Root bone. " +
                "The original vertical curve was preserved.");
        }
        else
        {
            Transform rootBone = source.transform.Find(rootPath);
            float referenceHeight = rootBone != null ? rootBone.localPosition.y : 0f;
            EditorCurveBinding rootYBinding = AnimationUtility.GetCurveBindings(cleanClip)
                .FirstOrDefault(binding =>
                    binding.type == typeof(Transform) &&
                    string.Equals(binding.path, rootPath, StringComparison.OrdinalIgnoreCase) &&
                    binding.propertyName == "m_LocalPosition.y");

            AnimationCurve sourceCurve = string.IsNullOrEmpty(rootYBinding.propertyName)
                ? null
                : AnimationUtility.GetEditorCurve(cleanClip, rootYBinding);
            if (sourceCurve != null && sourceCurve.length > 0)
            {
                Keyframe[] keys = sourceCurve.keys;
                for (int i = 0; i < keys.Length; i++)
                {
                    keys[i].value = referenceHeight;
                    keys[i].inTangent = 0f;
                    keys[i].outTangent = 0f;
                }

                sourceCurve.keys = keys;
                AnimationUtility.SetEditorCurve(cleanClip, rootYBinding, sourceCurve);
                Debug.Log(
                    $"[Varco3D] Retarget clean clip created. Root Y was stabilized at {referenceHeight:0.#####} " +
                    $"on '{rootPath}'. Hips/leg motion and the original clip were preserved.");
            }
            else
            {
                Debug.LogWarning($"[Varco3D] Root Y curve was not found on '{rootPath}'. No vertical curve was changed.");
            }
        }

        BlendLoopBoundary(cleanClip);
        cleanClip.EnsureQuaternionContinuity();
        AssetDatabase.CreateAsset(cleanClip, cleanClipAssetPath);
        AssetDatabase.SaveAssets();
        SetClipLoopTime(cleanClipAssetPath, true);
        return cleanClip;
    }

    private static void BlendLoopBoundary(AnimationClip clip)
    {
        if (clip == null || clip.length <= 0f)
            return;

        float blendDuration = Mathf.Min(0.20f, clip.length * 0.12f);
        float blendStart = Mathf.Max(0f, clip.length - blendDuration);
        int adjustedCurves = 0;

        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (binding.type != typeof(Transform) ||
                (!binding.propertyName.StartsWith("m_LocalPosition", StringComparison.Ordinal) &&
                 !binding.propertyName.StartsWith("m_LocalRotation", StringComparison.Ordinal)))
            {
                continue;
            }

            AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null || curve.length < 2)
                continue;

            Keyframe[] keys = curve.keys;
            float firstValue = keys[0].value;
            bool changed = false;
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i].time < blendStart)
                    continue;

                float t = Mathf.InverseLerp(blendStart, clip.length, keys[i].time);
                float smooth = t * t * (3f - 2f * t);
                keys[i].value = Mathf.Lerp(keys[i].value, firstValue, smooth);
                changed = true;
            }

            int last = keys.Length - 1;
            keys[last].value = firstValue;
            keys[last].inTangent = keys[0].inTangent;
            keys[last].outTangent = keys[0].outTangent;
            curve.keys = keys;
            AnimationUtility.SetEditorCurve(clip, binding, curve);
            if (changed)
                adjustedCurves++;
        }

        Debug.Log(
            $"[Varco3D] Retarget clean loop blend applied to {adjustedCurves} transform curves " +
            $"over the final {blendDuration:0.###} seconds. Original clip remains unchanged.");
    }

    private static void SetClipLoopTime(string clipAssetPath, bool loopTime)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipAssetPath);
        if (clip == null)
            return;

        var serializedClip = new SerializedObject(clip);
        SerializedProperty settings = serializedClip.FindProperty("m_AnimationClipSettings");
        SerializedProperty loopProperty = settings?.FindPropertyRelative("m_LoopTime");
        if (loopProperty == null)
            return;

        loopProperty.boolValue = loopTime;
        serializedClip.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(clip);
        AssetDatabase.SaveAssets();
    }

    private static AnimationClip BuildAnimationClip(
        UsdSkelAnimationData data,
        GameObject source,
        string clipAssetPath,
        out int boundJointCount)
    {
        boundJointCount = 0;

        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(clipAssetPath) != null)
            AssetDatabase.DeleteAsset(clipAssetPath);

        var clip = new AnimationClip
        {
            name = Path.GetFileNameWithoutExtension(clipAssetPath),
            frameRate = Mathf.Max(1f, data.FramesPerSecond),
            legacy = true
        };

        Transform[] transforms = source.GetComponentsInChildren<Transform>(true);
        Dictionary<string, List<Transform>> byName = transforms
            .GroupBy(t => t.name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        for (int jointIndex = 0; jointIndex < data.Joints.Count; jointIndex++)
        {
            string usdJointPath = data.Joints[jointIndex];
            Transform bone = FindBoneForUsdJoint(source.transform, transforms, byName, usdJointPath);
            if (bone == null)
            {
                Debug.LogWarning($"[Varco3D] Bone not found for USD joint: {usdJointPath}");
                continue;
            }

            string path = AnimationUtility.CalculateTransformPath(bone, source.transform);
            boundJointCount++;

            var px = new AnimationCurve();
            var py = new AnimationCurve();
            var pz = new AnimationCurve();
            var rx = new AnimationCurve();
            var ry = new AnimationCurve();
            var rz = new AnimationCurve();
            var rw = new AnimationCurve();
            var sx = new AnimationCurve();
            var sy = new AnimationCurve();
            var sz = new AnimationCurve();

            bool hasPosition = false;
            bool hasRotation = false;
            bool hasScale = false;
            Quaternion? previousQuaternion = null;

            foreach (float frame in data.FrameNumbers)
            {
                float time = (frame - data.StartFrame) / Mathf.Max(1f, data.TimeCodesPerSecond);

                if (data.Translations.TryGetValue(frame, out List<Vector3> translations) && jointIndex < translations.Count)
                {
                    Vector3 p = UsdPositionToUnity(translations[jointIndex]);
                    px.AddKey(time, p.x);
                    py.AddKey(time, p.y);
                    pz.AddKey(time, p.z);
                    hasPosition = true;
                }

                if (data.Rotations.TryGetValue(frame, out List<Quaternion> rotations) && jointIndex < rotations.Count)
                {
                    Quaternion q = UsdRotationToUnity(rotations[jointIndex]);
                    if (previousQuaternion.HasValue && Quaternion.Dot(previousQuaternion.Value, q) < 0f)
                        q = new Quaternion(-q.x, -q.y, -q.z, -q.w);

                    previousQuaternion = q;
                    rx.AddKey(time, q.x);
                    ry.AddKey(time, q.y);
                    rz.AddKey(time, q.z);
                    rw.AddKey(time, q.w);
                    hasRotation = true;
                }

                if (data.Scales.TryGetValue(frame, out List<Vector3> scales) && jointIndex < scales.Count)
                {
                    Vector3 s = scales[jointIndex];
                    sx.AddKey(time, s.x);
                    sy.AddKey(time, s.y);
                    sz.AddKey(time, s.z);
                    hasScale = true;
                }
            }

            if (hasPosition)
            {
                SetTransformCurve(clip, path, "m_LocalPosition.x", px);
                SetTransformCurve(clip, path, "m_LocalPosition.y", py);
                SetTransformCurve(clip, path, "m_LocalPosition.z", pz);
            }

            if (hasRotation)
            {
                SetTransformCurve(clip, path, "m_LocalRotation.x", rx);
                SetTransformCurve(clip, path, "m_LocalRotation.y", ry);
                SetTransformCurve(clip, path, "m_LocalRotation.z", rz);
                SetTransformCurve(clip, path, "m_LocalRotation.w", rw);
            }

            if (hasScale)
            {
                SetTransformCurve(clip, path, "m_LocalScale.x", sx);
                SetTransformCurve(clip, path, "m_LocalScale.y", sy);
                SetTransformCurve(clip, path, "m_LocalScale.z", sz);
            }
        }

        if (boundJointCount == 0)
        {
            Debug.LogError("[Varco3D] No USD animation joints matched the selected character hierarchy.");
            return null;
        }

        clip.EnsureQuaternionContinuity();
        AssetDatabase.CreateAsset(clip, clipAssetPath);
        AssetDatabase.SaveAssets();
        return clip;
    }

    private static void SetTransformCurve(AnimationClip clip, string path, string property, AnimationCurve curve)
    {
        AnimationUtility.SetEditorCurve(
            clip,
            EditorCurveBinding.FloatCurve(path, typeof(Transform), property),
            curve);
    }

    // ---------------------------------------------------------------------
    // USD PARSING
    // ---------------------------------------------------------------------

    private static string ExtractPrimBlock(string text, string primDeclaration)
    {
        int declarationIndex = text.IndexOf(primDeclaration, StringComparison.Ordinal);
        if (declarationIndex < 0)
            return null;

        int openBrace = text.IndexOf('{', declarationIndex);
        if (openBrace < 0)
            return null;

        int closeBrace = FindMatchingBrace(text, openBrace, '{', '}');
        return closeBrace > openBrace ? text.Substring(openBrace + 1, closeBrace - openBrace - 1) : null;
    }

    private static string ExtractArrayAssignment(string block, string attributeName)
    {
        int attributeIndex = block.IndexOf(attributeName, StringComparison.Ordinal);
        if (attributeIndex < 0)
            return null;

        int equals = block.IndexOf('=', attributeIndex);
        int open = block.IndexOf('[', equals);
        if (equals < 0 || open < 0)
            return null;

        int close = FindMatchingBrace(block, open, '[', ']');
        return close > open ? block.Substring(open + 1, close - open - 1) : null;
    }

    private static string ExtractTimeSamplesBlock(string block, string attributeName)
    {
        int attributeIndex = block.IndexOf(attributeName, StringComparison.Ordinal);
        if (attributeIndex < 0)
            return null;

        int equals = block.IndexOf('=', attributeIndex);
        int open = block.IndexOf('{', equals);
        if (equals < 0 || open < 0)
            return null;

        int close = FindMatchingBrace(block, open, '{', '}');
        return close > open ? block.Substring(open + 1, close - open - 1) : null;
    }

    private static int FindMatchingBrace(string text, int openIndex, char openChar, char closeChar)
    {
        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = openIndex; i < text.Length; i++)
        {
            char c = text[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                    inString = false;

                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == openChar) depth++;
            else if (c == closeChar)
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    private static void ParseQuaternionSamples(string block, Dictionary<float, List<Quaternion>> target)
    {
        if (string.IsNullOrWhiteSpace(block))
            return;

        foreach (string entry in SplitTimeSampleEntries(block))
        {
            if (!TryParseFrameAndValue(entry, out float frame, out string value))
                continue;

            var list = new List<Quaternion>();
            foreach (Match tuple in TupleRegex.Matches(value))
            {
                float[] v = ParseFloatTuple(tuple.Groups[1].Value);
                if (v.Length < 4)
                    continue;

                // USD quatf tuple order: (real, i, j, k) => Unity Quaternion(x, y, z, w)
                list.Add(new Quaternion(v[1], v[2], v[3], v[0]));
            }

            target[NormalizeTimeCode(frame)] = list;
        }
    }

    private static void ParseVector3Samples(string block, Dictionary<float, List<Vector3>> target)
    {
        if (string.IsNullOrWhiteSpace(block))
            return;

        foreach (string entry in SplitTimeSampleEntries(block))
        {
            if (!TryParseFrameAndValue(entry, out float frame, out string value))
                continue;

            var list = new List<Vector3>();
            foreach (Match tuple in TupleRegex.Matches(value))
            {
                float[] v = ParseFloatTuple(tuple.Groups[1].Value);
                if (v.Length < 3)
                    continue;

                list.Add(new Vector3(v[0], v[1], v[2]));
            }

            target[NormalizeTimeCode(frame)] = list;
        }
    }

    private static float NormalizeTimeCode(float frame)
    {
        return Mathf.Round(frame * 10000f) / 10000f;
    }

    private static IEnumerable<string> SplitTimeSampleEntries(string block)
    {
        var current = new StringBuilder();
        int squareDepth = 0;
        int parenDepth = 0;

        for (int i = 0; i < block.Length; i++)
        {
            char c = block[i];
            current.Append(c);

            if (c == '[') squareDepth++;
            else if (c == ']') squareDepth--;
            else if (c == '(') parenDepth++;
            else if (c == ')') parenDepth--;

            if (c == ',' && squareDepth == 0 && parenDepth == 0)
            {
                string entry = current.ToString().Trim().TrimEnd(',');
                current.Length = 0;
                if (!string.IsNullOrWhiteSpace(entry))
                    yield return entry;
            }
        }

        string finalEntry = current.ToString().Trim().TrimEnd(',');
        if (!string.IsNullOrWhiteSpace(finalEntry))
            yield return finalEntry;
    }

    private static bool TryParseFrameAndValue(string entry, out float frame, out string value)
    {
        frame = 0f;
        value = null;

        int colon = entry.IndexOf(':');
        if (colon <= 0)
            return false;

        if (!float.TryParse(entry.Substring(0, colon).Trim(), NumberStyles.Float, Invariant, out frame))
            return false;

        value = entry.Substring(colon + 1).Trim();
        return true;
    }

    private static float[] ParseFloatTuple(string tuple)
    {
        string[] parts = tuple.Split(',');
        var values = new List<float>(parts.Length);

        foreach (string part in parts)
        {
            if (float.TryParse(part.Trim(), NumberStyles.Float, Invariant, out float value))
                values.Add(value);
        }

        return values.ToArray();
    }

    private static float ParseMetadataFloat(string text, string key, float fallback)
    {
        Match match = Regex.Match(
            text,
            @"\b" + Regex.Escape(key) + @"\s*=\s*([-+0-9.eE]+)",
            RegexOptions.CultureInvariant);

        return match.Success && float.TryParse(match.Groups[1].Value, NumberStyles.Float, Invariant, out float value)
            ? value
            : fallback;
    }

    // ---------------------------------------------------------------------
    // COORDINATES / BONE MATCHING
    // ---------------------------------------------------------------------

    private static Vector3 UsdPositionToUnity(Vector3 usd)
    {
        // VARCO USDZ is Y-up, right-handed. Unity is Y-up, left-handed.
        return new Vector3(usd.x, usd.y, -usd.z);
    }

    private static Quaternion UsdRotationToUnity(Quaternion usd)
    {
        // Reflection across Z: R_unity = S * R_usd * S, S = diag(1,1,-1)
        Quaternion unity = new Quaternion(-usd.x, -usd.y, usd.z, usd.w);
        return NormalizeQuaternion(unity);
    }

    private static Quaternion NormalizeQuaternion(Quaternion q)
    {
        float mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        if (mag <= 0.000001f)
            return Quaternion.identity;

        float inv = 1f / mag;
        return new Quaternion(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
    }

    private static Transform FindBoneForUsdJoint(
        Transform sourceRoot,
        Transform[] transforms,
        Dictionary<string, List<Transform>> byName,
        string usdJointPath)
    {
        string normalizedUsdPath = NormalizeHierarchyPath(usdJointPath);

        // First: exact/suffix path match.
        foreach (Transform t in transforms)
        {
            string actual = NormalizeHierarchyPath(AnimationUtility.CalculateTransformPath(t, sourceRoot));
            if (string.Equals(actual, normalizedUsdPath, StringComparison.OrdinalIgnoreCase) ||
                actual.EndsWith("/" + normalizedUsdPath, StringComparison.OrdinalIgnoreCase) ||
                normalizedUsdPath.EndsWith("/" + actual, StringComparison.OrdinalIgnoreCase))
            {
                return t;
            }
        }

        // Fallback: leaf bone name. VARCO humanoid bone names are normally unique.
        string leaf = normalizedUsdPath.Split('/').LastOrDefault();
        if (!string.IsNullOrEmpty(leaf) && byName.TryGetValue(leaf, out List<Transform> matches) && matches.Count > 0)
        {
            if (matches.Count == 1)
                return matches[0];

            return matches.OrderByDescending(t => PathSuffixScore(
                NormalizeHierarchyPath(AnimationUtility.CalculateTransformPath(t, sourceRoot)),
                normalizedUsdPath)).First();
        }

        return null;
    }

    private static int PathSuffixScore(string a, string b)
    {
        string[] aa = a.Split('/');
        string[] bb = b.Split('/');
        int score = 0;
        int ai = aa.Length - 1;
        int bi = bb.Length - 1;

        while (ai >= 0 && bi >= 0 && string.Equals(aa[ai], bb[bi], StringComparison.OrdinalIgnoreCase))
        {
            score++;
            ai--;
            bi--;
        }

        return score;
    }

    private static string NormalizeHierarchyPath(string path)
    {
        return (path ?? string.Empty).Replace('\\', '/').Trim('/');
    }

    private static void LogSkeletonHierarchy(GameObject source, UsdSkelAnimationData animationData)
    {
        if (source == null)
            return;

        Transform[] transforms = source.GetComponentsInChildren<Transform>(true);
        var lines = new List<string>
        {
            $"[Varco3D] Skeleton hierarchy: {transforms.Length} transforms, {animationData.Joints.Count} USD joints"
        };

        foreach (Transform transform in transforms)
        {
            string path = AnimationUtility.CalculateTransformPath(transform, source.transform);
            string parent = transform.parent != null ? transform.parent.name : "<ROOT>";
            lines.Add(
                $"{path} | Parent={parent} | LocalPosition={transform.localPosition:F5} | " +
                $"LocalRotation={transform.localRotation.eulerAngles:F3} | LocalScale={transform.localScale:F5}");
        }

        Debug.Log(string.Join("\n", lines));
    }

    private static void AnalyzeAnimationClip(AnimationClip clip, Transform sourceRoot, string label)
    {
        if (clip == null)
            return;

        EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
        string rootPath = sourceRoot != null ? FindLikelyRootMotionPath(sourceRoot) : string.Empty;
        string hipsPath = sourceRoot != null ? FindLikelyHipsPath(sourceRoot) : string.Empty;
        var candidatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { string.Empty };
        if (!string.IsNullOrEmpty(rootPath)) candidatePaths.Add(rootPath);
        if (!string.IsNullOrEmpty(hipsPath)) candidatePaths.Add(hipsPath);

        var diagnosticLines = new List<string>
        {
            $"[Varco3D] Animation diagnostics - {label}",
            $"Length={clip.length:0.#####}s, FrameRate={clip.frameRate:0.###}, Curves={bindings.Length}, LoopTime={clip.isLooping}",
            $"Likely Root Path={(string.IsNullOrEmpty(rootPath) ? "<clip root>" : rootPath)}, Hips Path={(string.IsNullOrEmpty(hipsPath) ? "not resolved" : hipsPath)}"
        };

        foreach (EditorCurveBinding binding in bindings.Where(binding =>
                     binding.type == typeof(Transform) &&
                     (candidatePaths.Contains(binding.path) || IsRootOrHipsPath(binding.path)) &&
                     (binding.propertyName.StartsWith("m_LocalPosition", StringComparison.Ordinal) ||
                      binding.propertyName.StartsWith("m_LocalRotation", StringComparison.Ordinal))))
        {
            AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null || curve.length == 0)
                continue;

            Keyframe first = curve.keys[0];
            Keyframe last = curve.keys[curve.length - 1];
            float delta = last.value - first.value;
            diagnosticLines.Add(
                $"{binding.path}/{binding.propertyName}: first={first.value:0.#####}, last={last.value:0.#####}, delta={delta:0.#####}");

            if (binding.propertyName == "m_LocalPosition.y" && Mathf.Abs(delta) > 0.001f)
                diagnosticLines.Add("  WARNING: Position Y changes across the clip. Compare Root Motion OFF/ON before changing curves.");
            if (Mathf.Abs(delta) > 0.01f)
                diagnosticLines.Add("  LOOP CHECK: First/last values differ; Loop Pose alone may not remove the boundary discontinuity.");
        }

        Debug.Log(string.Join("\n", diagnosticLines));
    }

    private static string FindLikelyRootMotionPath(Transform root)
    {
        Transform candidate = root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => NormalizeBoneName(t.name) == "root");
        return candidate != null ? AnimationUtility.CalculateTransformPath(candidate, root) : string.Empty;
    }

    private static string FindLikelyHipsPath(Transform root)
    {
        Transform candidate = root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => HumanoidBoneAliases[HumanBodyBones.Hips].Contains(NormalizeBoneName(t.name)));
        return candidate != null ? AnimationUtility.CalculateTransformPath(candidate, root) : string.Empty;
    }

    private static bool IsRootOrHipsPath(string path)
    {
        string leaf = NormalizeBoneName((path ?? string.Empty).Split('/').LastOrDefault());
        return leaf == "root" || HumanoidBoneAliases[HumanBodyBones.Hips].Contains(leaf);
    }

    private static void RestoreReferencePoseForExport(GameObject exportRoot, GameObject referenceRoot)
    {
        if (exportRoot == null || referenceRoot == null)
            return;

        Transform[] references = referenceRoot.GetComponentsInChildren<Transform>(true);
        var referenceByPath = references.ToDictionary(
            transform => NormalizeHierarchyPath(AnimationUtility.CalculateTransformPath(transform, referenceRoot.transform)),
            transform => transform,
            StringComparer.OrdinalIgnoreCase);

        int restored = 0;
        foreach (Transform target in exportRoot.GetComponentsInChildren<Transform>(true))
        {
            string path = NormalizeHierarchyPath(AnimationUtility.CalculateTransformPath(target, exportRoot.transform));
            if (!referenceByPath.TryGetValue(path, out Transform reference))
                continue;

            target.localPosition = reference.localPosition;
            target.localRotation = reference.localRotation;
            target.localScale = reference.localScale;
            restored++;
        }

        exportRoot.transform.localPosition = Vector3.zero;
        exportRoot.transform.localRotation = Quaternion.identity;
        exportRoot.transform.localScale = Vector3.one;
        Debug.Log($"[Varco3D] Restored USDZ reference pose before FBX export: {restored}/{references.Length} transforms.");
    }

    private static bool HasManualFacingCorrection(GameObject source)
    {
        if (source == null)
            return false;

        if (IsApproximatelyFacingBackward(source.transform.localEulerAngles.y))
            return true;

        Transform mesh = source.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(transform => NormalizeBoneName(transform.name).StartsWith("mesh", StringComparison.Ordinal));
        Transform skeleton = source.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(transform => NormalizeBoneName(transform.name) == "skel");

        return mesh != null && skeleton != null &&
               IsApproximatelyFacingBackward(mesh.localEulerAngles.y) &&
               IsApproximatelyFacingBackward(skeleton.localEulerAngles.y);
    }

    private static bool IsApproximatelyFacingBackward(float yDegrees)
    {
        return Mathf.Abs(Mathf.DeltaAngle(yDegrees, 180f)) <= 2f;
    }

    // ---------------------------------------------------------------------
    // TEXTURES / MATERIALS
    // ---------------------------------------------------------------------

    private static void ConfigureExtractedTextureImporters(IEnumerable<string> texturePaths)
    {
        foreach (string texturePath in texturePaths)
        {
            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null)
                continue;

            TextureRole? role = DetectTextureRole(texturePath);
            bool isNormal = role == TextureRole.Normal;
            bool isBase = role == TextureRole.BaseColor;

            importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = isBase;
            importer.SaveAndReimport();
        }
    }

    private static Material CreateOrUpdateUrpMaterial(string modelName, string materialFolder, List<string> texturePaths)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogWarning("[Varco3D] URP Lit shader was not found. FBX/animation export will continue without generated URP material.");
            return null;
        }

        string matPath = $"{materialFolder}/{modelName}_Mat.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, matPath);
        }
        else
        {
            mat.shader = shader;
        }

        Texture2D baseMap = LoadTextureByRole(texturePaths, TextureRole.BaseColor);
        Texture2D normalMap = LoadTextureByRole(texturePaths, TextureRole.Normal);
        Texture2D maskMap = LoadTextureByRole(texturePaths, TextureRole.Mask);

        ApplyTextures(mat, baseMap, normalMap, maskMap);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }

    private static Texture2D LoadTextureByRole(IEnumerable<string> texturePaths, TextureRole role)
    {
        foreach (string path in texturePaths)
        {
            if (DetectTextureRole(path) == role)
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        return null;
    }

    private static void ApplyTextures(Material mat, Texture2D baseMap, Texture2D normalMap, Texture2D maskMap)
    {
        if (mat == null)
            return;

        if (baseMap != null && mat.HasProperty("_BaseMap"))
            mat.SetTexture("_BaseMap", baseMap);

        if (normalMap != null && mat.HasProperty("_BumpMap"))
        {
            mat.SetTexture("_BumpMap", normalMap);
            mat.EnableKeyword("_NORMALMAP");
        }

        if (maskMap == null)
            return;

        if (mat.HasProperty("_MaskMap"))
            mat.SetTexture("_MaskMap", maskMap);

        if (mat.HasProperty("_MetallicGlossMap"))
        {
            mat.SetTexture("_MetallicGlossMap", maskMap);
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
        }

        if (mat.HasProperty("_OcclusionMap"))
        {
            mat.SetTexture("_OcclusionMap", maskMap);
            mat.EnableKeyword("_OCCLUSIONMAP");
        }
    }

    private static void AssignMaterial(GameObject root, Material material)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                renderer.sharedMaterial = material;
                continue;
            }

            for (int i = 0; i < materials.Length; i++)
                materials[i] = material;

            renderer.sharedMaterials = materials;
        }
    }

    private static bool IsTextureEntry(string entryName)
    {
        string ext = Path.GetExtension(entryName).ToLowerInvariant();
        return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tga" || ext == ".exr" || ext == ".tif" || ext == ".tiff";
    }

    // ---------------------------------------------------------------------
    // FBX POST PROCESS
    // ---------------------------------------------------------------------

    private static void ProcessFbx(string fbxPath, Material preferredMaterial)
    {
        string baseFolder = NormalizeAssetPath(Path.GetDirectoryName(fbxPath));
        string modelName = Path.GetFileNameWithoutExtension(fbxPath);
        string matFolder = $"{baseFolder}/Materials";
        string texFolder = $"{baseFolder}/Textures";
        string prefabFolder = $"{baseFolder}/Prefabs";

        EnsureAssetFolderRecursive(matFolder);
        EnsureAssetFolderRecursive(texFolder);
        EnsureAssetFolderRecursive(prefabFolder);

        ExtractEmbeddedTextures(fbxPath, texFolder);
        AssetDatabase.Refresh();

        List<string> extractedTexturePaths = AssetDatabase.FindAssets("t:Texture2D", new[] { texFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .ToList();
        extractedTexturePaths = NormalizeTextureAssetNames(modelName, extractedTexturePaths);
        ConfigureExtractedTextureImporters(extractedTexturePaths);

        Material mat = preferredMaterial;
        if (mat == null)
        {
            Texture2D baseMap = LoadTextureByRole(extractedTexturePaths, TextureRole.BaseColor);
            Texture2D normalMap = LoadTextureByRole(extractedTexturePaths, TextureRole.Normal);
            Texture2D maskMap = LoadTextureByRole(extractedTexturePaths, TextureRole.Mask);

            string matPath = $"{matFolder}/{modelName}_Mat.mat";
            mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader != null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, matPath);
                }
            }

            if (mat != null)
            {
                ApplyTextures(mat, baseMap, normalMap, maskMap);
                EditorUtility.SetDirty(mat);
            }
        }

        GameObject fbxObj = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (fbxObj == null)
        {
            Debug.LogError("[Varco3D] FBX load failed.");
            return;
        }

        string prefabPath = $"{prefabFolder}/{modelName}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
            AssetDatabase.DeleteAsset(prefabPath);

        GameObject instance = PrefabUtility.InstantiatePrefab(fbxObj) as GameObject;
        if (instance == null)
            instance = UnityEngine.Object.Instantiate(fbxObj);

        try
        {
            if (mat != null)
                AssignMaterial(instance, mat);

            foreach (Animator animator in instance.GetComponentsInChildren<Animator>(true))
                animator.applyRootMotion = false;

            PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }

        AssetDatabase.SaveAssets();
    }

    private static Texture2D FindSourceTexture(string texFolder, TextureRole role)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { texFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (DetectTextureRole(path) != role)
                continue;

            ConfigureTextureImporter(path, role);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        return null;
    }

    private static List<string> NormalizeTextureAssetNames(string modelName, IEnumerable<string> texturePaths)
    {
        var normalizedPaths = new List<string>();

        foreach (string sourcePath in texturePaths.Where(path => !string.IsNullOrEmpty(path)).Distinct())
        {
            TextureRole? role = DetectTextureRole(sourcePath);
            if (!role.HasValue)
            {
                normalizedPaths.Add(sourcePath);
                continue;
            }

            string suffix = role == TextureRole.BaseColor ? "_Base" : role == TextureRole.Normal ? "_N" : "_Mask";
            string extension = Path.GetExtension(sourcePath);
            string folder = NormalizeAssetPath(Path.GetDirectoryName(sourcePath));
            string targetPath = NormalizeAssetPath($"{folder}/{modelName}{suffix}{extension}");

            if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                normalizedPaths.Add(targetPath);
                continue;
            }

            string targetAbsolutePath = AssetPathToAbsolutePath(targetPath);
            if (File.Exists(targetAbsolutePath))
            {
                File.Copy(AssetPathToAbsolutePath(sourcePath), targetAbsolutePath, true);
                AssetDatabase.DeleteAsset(sourcePath);
                AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }
            else
            {
                string moveError = AssetDatabase.MoveAsset(sourcePath, targetPath);
                if (!string.IsNullOrEmpty(moveError))
                {
                    Debug.LogWarning($"[Varco3D] Texture rename failed: {sourcePath} -> {targetPath}. {moveError}");
                    normalizedPaths.Add(sourcePath);
                    continue;
                }
            }

            normalizedPaths.Add(targetPath);
            Debug.Log($"[Varco3D] Texture normalized: {sourcePath} -> {targetPath}");
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        return normalizedPaths.Distinct().ToList();
    }

    private static TextureRole? DetectTextureRole(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        if (name.Contains("normal") || name.EndsWith("_n"))
            return TextureRole.Normal;
        if (name == "orm" || name.Contains("_orm") || name.Contains("orm_") || name.Contains("mask") || name.EndsWith("_mask"))
            return TextureRole.Mask;
        if (name.Contains("base_color") || name.Contains("basecolor") || name.Contains("albedo") || name.Contains("diffuse") || name.EndsWith("_base"))
            return TextureRole.BaseColor;
        return null;
    }

    private static void ConfigureTextureImporter(string texturePath, TextureRole role)
    {
        TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer == null)
            return;

        importer.textureType = role == TextureRole.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = role == TextureRole.BaseColor;
        importer.SaveAndReimport();
    }

    private static void ExtractEmbeddedTextures(string fbxPath, string texFolder)
    {
        ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null)
            return;

        try
        {
            importer.ExtractTextures(texFolder);
            importer.SaveAndReimport();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Varco3D] FBX embedded texture extraction skipped: {ex.Message}");
        }
    }

    private static HumanoidReport ConfigureFbxImporter(string fbxPath)
    {
        ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null)
            return new HumanoidReport();

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        Dictionary<HumanBodyBones, string> mapping = BuildHumanoidMapping(model);
        var report = new HumanoidReport
        {
            SkeletonFound = model != null && model.GetComponentsInChildren<Transform>(true).Length > 1,
            AnimationFound = GetImportedAnimationClips(fbxPath).Length > 0,
            MappedCount = RequiredHumanoidBones.Count(mapping.ContainsKey)
        };

        LogHumanoidMapping(mapping);

        bool hasRequiredMapping = RequiredHumanoidBones.All(mapping.ContainsKey);
        if (hasRequiredMapping)
        {
            HumanDescription description = importer.humanDescription;
            description.human = mapping.Select(pair => new HumanBone
            {
                humanName = HumanTrait.BoneName[(int)pair.Key],
                boneName = pair.Value,
                limit = new HumanLimit { useDefaultValues = true }
            }).ToArray();
            description.upperArmTwist = 0.5f;
            description.lowerArmTwist = 0.5f;
            description.upperLegTwist = 0.5f;
            description.lowerLegTwist = 0.5f;
            description.armStretch = 0.05f;
            description.legStretch = 0.05f;
            description.feetSpacing = 0f;
            description.hasTranslationDoF = false;

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.humanDescription = description;
        }
        else
        {
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            Debug.LogWarning(
                "[Varco3D] Humanoid import was not forced because one or more required bones were not mapped. " +
                "The FBX remains Generic so the working skeleton/animation is preserved. Review the mapping log before editing aliases.");
        }

        importer.optimizeGameObjects = false;

        importer.importAnimation = true;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.importBlendShapes = true;
        importer.importCameras = false;
        importer.importLights = false;
        importer.SaveAndReimport();
        return report;
    }

    private static HumanoidReport ValidateImportedHumanoid(string fbxPath, HumanoidReport report)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        Avatar avatar = model != null ? model.GetComponent<Animator>()?.avatar : null;
        if (avatar == null && model != null)
            avatar = model.GetComponentsInChildren<Animator>(true).Select(a => a.avatar).FirstOrDefault(a => a != null);

        report.SkeletonFound = model != null && model.GetComponentsInChildren<Transform>(true).Length > 1;
        report.AnimationFound = GetImportedAnimationClips(fbxPath).Length > 0;
        report.AvatarValid = avatar != null && avatar.isValid;
        report.AvatarHuman = avatar != null && avatar.isHuman;
        report.RetargetReady = report.SkeletonFound && report.AnimationFound &&
                               report.MappedCount == RequiredHumanoidBones.Length &&
                               report.AvatarValid && report.AvatarHuman;

        Debug.Log(
            "[Varco3D] Retarget readiness\n" +
            $"Skeleton Found: {report.SkeletonFound}\n" +
            $"Animation Found: {report.AnimationFound}\n" +
            $"Humanoid Mapping: {report.MappedCount}/{RequiredHumanoidBones.Length}\n" +
            $"Avatar Valid: {report.AvatarValid}\n" +
            $"Avatar Human: {report.AvatarHuman}\n" +
            $"Retarget Ready: {report.RetargetReady}");
        return report;
    }

    private static Dictionary<HumanBodyBones, string> BuildHumanoidMapping(GameObject model)
    {
        var result = new Dictionary<HumanBodyBones, string>();
        if (model == null)
            return result;

        Transform[] transforms = model.GetComponentsInChildren<Transform>(true);
        var used = new HashSet<Transform>();
        foreach (KeyValuePair<HumanBodyBones, string[]> entry in HumanoidBoneAliases)
        {
            Transform match = FindBestHumanoidBone(transforms, entry.Key, entry.Value, used);
            if (match == null)
                continue;

            result[entry.Key] = match.name;
            used.Add(match);
        }

        return result;
    }

    private static Transform FindBestHumanoidBone(
        IEnumerable<Transform> transforms,
        HumanBodyBones humanBone,
        IEnumerable<string> aliases,
        HashSet<Transform> used)
    {
        string[] normalizedAliases = aliases.Select(NormalizeBoneName).ToArray();
        bool wantsLeft = humanBone.ToString().StartsWith("Left", StringComparison.Ordinal);
        bool wantsRight = humanBone.ToString().StartsWith("Right", StringComparison.Ordinal);

        return transforms
            .Where(t => t != null && !used.Contains(t))
            .Select(t => new { Transform = t, Name = NormalizeBoneName(t.name) })
            .Select(candidate => new
            {
                candidate.Transform,
                Score = ScoreBoneName(candidate.Name, normalizedAliases, wantsLeft, wantsRight)
            })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => GetTransformDepth(candidate.Transform))
            .Select(candidate => candidate.Transform)
            .FirstOrDefault();
    }

    private static int ScoreBoneName(string candidate, IEnumerable<string> aliases, bool wantsLeft, bool wantsRight)
    {
        int sideScore = 0;
        if (wantsLeft)
        {
            if (candidate.Contains("right") || candidate.StartsWith("r")) return 0;
            if (candidate.Contains("left") || candidate.StartsWith("l") || candidate.EndsWith("l")) sideScore = 20;
        }
        else if (wantsRight)
        {
            if (candidate.Contains("left") || candidate.StartsWith("l")) return 0;
            if (candidate.Contains("right") || candidate.StartsWith("r") || candidate.EndsWith("r")) sideScore = 20;
        }

        int best = 0;
        foreach (string alias in aliases)
        {
            if (candidate == alias) best = Mathf.Max(best, 100);
            else if (candidate.EndsWith(alias, StringComparison.Ordinal)) best = Mathf.Max(best, 80);
            else if (candidate.Contains(alias)) best = Mathf.Max(best, 60);
        }

        return best + (best > 0 ? sideScore : 0);
    }

    private static string NormalizeBoneName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        string withoutNamespace = value.Split(':').Last();
        return Regex.Replace(withoutNamespace, "[^a-zA-Z0-9]", string.Empty).ToLowerInvariant();
    }

    private static int GetTransformDepth(Transform transform)
    {
        int depth = 0;
        while (transform != null && transform.parent != null)
        {
            depth++;
            transform = transform.parent;
        }
        return depth;
    }

    private static void LogHumanoidMapping(Dictionary<HumanBodyBones, string> mapping)
    {
        var lines = new List<string> { "[Varco3D] Humanoid bone mapping (bone names are preserved):" };
        foreach (HumanBodyBones bone in HumanoidBoneAliases.Keys)
            lines.Add($"{bone}: {(mapping.TryGetValue(bone, out string mapped) ? mapped : "MISSING")}");
        Debug.Log(string.Join("\n", lines));
    }

    private static AnimationClip[] GetImportedAnimationClips(string fbxPath)
    {
        return AssetDatabase.LoadAllAssetsAtPath(fbxPath)
            .OfType<AnimationClip>()
            .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    // ---------------------------------------------------------------------
    // UNITY / PATH HELPERS
    // ---------------------------------------------------------------------

    private static bool TryFindFbxExporter(out MethodInfo exportMethod)
    {
        Type exporterType = AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(assembly => assembly.GetType(FbxExporterTypeName, false))
            .FirstOrDefault(type => type != null);

        exportMethod = exporterType?.GetMethod(
            "ExportObject",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(string), typeof(UnityEngine.Object) },
            null);

        return exporterType != null && exportMethod != null;
    }

    private static string FindBestUsdZAssetForGameObject(GameObject source)
    {
        if (source == null)
            return null;

        string sourceName = NormalizeCharacterName(source.name);
        var candidates = AssetDatabase.GetAllAssetPaths()
            .Where(IsUsdZPath)
            .Select(path => new
            {
                Path = path,
                Name = NormalizeCharacterName(Path.GetFileNameWithoutExtension(path))
            })
            .ToList();

        var exact = candidates.FirstOrDefault(c => string.Equals(c.Name, sourceName, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return exact.Path;

        var partial = candidates.FirstOrDefault(c =>
            sourceName.IndexOf(c.Name, StringComparison.OrdinalIgnoreCase) >= 0 ||
            c.Name.IndexOf(sourceName, StringComparison.OrdinalIgnoreCase) >= 0);

        return partial?.Path;
    }

    private static GameObject FindSceneCharacterForUsdZ(string usdzAssetPath)
    {
        string targetName = NormalizeCharacterName(Path.GetFileNameWithoutExtension(usdzAssetPath));
        return Resources.FindObjectsOfTypeAll<GameObject>()
            .Where(go => go.scene.IsValid() && go.transform.parent == null)
            .FirstOrDefault(go => string.Equals(NormalizeCharacterName(go.name), targetName, StringComparison.OrdinalIgnoreCase));
    }

    // The overlay is commonly clicked while mesh_0 or skel is selected.
    // Always export the complete scene character so the skinned mesh, skeleton,
    // reference pose and animation bindings remain in the same hierarchy.
    private static GameObject FindCharacterRoot(GameObject selected)
    {
        if (selected == null)
            return null;

        Transform current = selected.transform;
        while (current.parent != null)
            current = current.parent;

        return current.gameObject;
    }

    private static string NormalizeCharacterName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string result = value.Trim();
        result = Regex.Replace(result, @"\s*\(Clone\)$", string.Empty, RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"\s*\(\d+\)$", string.Empty, RegexOptions.IgnoreCase);
        return result.Trim();
    }

    private static string PickOrImportUsdZ()
    {
        string absolutePath = EditorUtility.OpenFilePanel("Select VARCO USDZ", Application.dataPath, "usdz");
        if (string.IsNullOrEmpty(absolutePath))
            return null;

        string existingAssetPath = AbsolutePathToAssetPath(absolutePath);
        if (!string.IsNullOrEmpty(existingAssetPath))
        {
            AssetDatabase.ImportAsset(existingAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            return existingAssetPath;
        }

        const string importFolder = "Assets/VARCO3DImports/_USDZ_Source";
        EnsureAssetFolderRecursive(importFolder);
        string fileName = SanitizeFileName(Path.GetFileName(absolutePath));
        string destinationAssetPath = AssetDatabase.GenerateUniqueAssetPath($"{importFolder}/{fileName}");
        File.Copy(absolutePath, AssetPathToAbsolutePath(destinationAssetPath), false);
        AssetDatabase.ImportAsset(destinationAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        Debug.Log($"[Varco3D] External USDZ copied into project: {destinationAssetPath}");
        return destinationAssetPath;
    }

    private static GameObject ResolveGameObjectFromAsset(string assetPath)
    {
        GameObject main = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (main != null)
            return main;

        return AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<GameObject>().FirstOrDefault();
    }

    private static string AssetPathToAbsolutePath(string assetPath)
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrEmpty(projectRoot))
            throw new InvalidOperationException("Unity project root could not be resolved.");

        return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
    }

    private static string AbsolutePathToAssetPath(string absolutePath)
    {
        string normalizedAbsolute = Path.GetFullPath(absolutePath).Replace('\\', '/');
        string normalizedAssets = Path.GetFullPath(Application.dataPath).Replace('\\', '/');

        if (!normalizedAbsolute.StartsWith(normalizedAssets + "/", StringComparison.OrdinalIgnoreCase))
            return null;

        return "Assets" + normalizedAbsolute.Substring(normalizedAssets.Length);
    }

    private static void EnsureAssetFolderRecursive(string assetFolder)
    {
        assetFolder = NormalizeAssetPath(assetFolder);
        if (AssetDatabase.IsValidFolder(assetFolder))
            return;

        string[] parts = assetFolder.Split('/');
        if (parts.Length == 0 || parts[0] != "Assets")
            throw new ArgumentException("Folder must be inside Assets.", nameof(assetFolder));

        string current = "Assets";
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static string NormalizeAssetPath(string path)
    {
        return (path ?? string.Empty).Replace('\\', '/');
    }

    private static string SanitizeFileName(string fileName)
    {
        foreach (char invalidChar in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalidChar, '_');

        return string.IsNullOrWhiteSpace(fileName) ? "VARCO_Export" : fileName.Trim();
    }

    private static bool IsUsdZPath(string path)
    {
        return !string.IsNullOrEmpty(path) && path.EndsWith(".usdz", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFbxPath(string path)
    {
        return !string.IsNullOrEmpty(path) && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);
    }

    private enum TextureRole
    {
        BaseColor,
        Normal,
        Mask
    }

    private sealed class HumanoidReport
    {
        public bool SkeletonFound;
        public bool AnimationFound;
        public int MappedCount;
        public bool AvatarValid;
        public bool AvatarHuman;
        public bool RetargetReady;
    }

    private sealed class UsdSkelAnimationData
    {
        public readonly List<string> Joints = new List<string>();
        public readonly Dictionary<float, List<Quaternion>> Rotations = new Dictionary<float, List<Quaternion>>();
        public readonly Dictionary<float, List<Vector3>> Translations = new Dictionary<float, List<Vector3>>();
        public readonly Dictionary<float, List<Vector3>> Scales = new Dictionary<float, List<Vector3>>();
        public readonly List<float> FrameNumbers = new List<float>();

        public float FramesPerSecond = 30f;
        public float TimeCodesPerSecond = 30f;
        public float StartFrame;
        public float EndFrame;
    }
}
