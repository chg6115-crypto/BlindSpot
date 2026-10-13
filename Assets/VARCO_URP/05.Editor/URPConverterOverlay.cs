#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

[Overlay(typeof(SceneView), "URP Converter", true)]
public class URPConverterOverlay : Overlay
{
    private const string UrpLitShaderName = "Universal Render Pipeline/Lit";
    private const string Incorrect2DLitShaderName = "Universal Render Pipeline/2D/Mesh2D-Lit-Default";
    private const float DefaultMetallic = 0.5f;

    public override VisualElement CreatePanelContent()
    {
        var root = new VisualElement();
        root.style.flexDirection = FlexDirection.Row;
        root.style.paddingLeft = 6;
        root.style.paddingRight = 6;

        var convertButton = new Button(ConvertSelectedMaterials)
        {
            text = "Convert to URP",
            tooltip = "Convert selected Built-in materials to URP/Lit"
        };

        convertButton.style.paddingLeft = 6;
        convertButton.style.paddingRight = 6;
        convertButton.style.marginRight = 6;
        convertButton.style.backgroundColor = new Color(0.2f, 0.6f, 1f);
        convertButton.style.color = Color.white;

        root.Add(convertButton);

        return root;
    }

    private static void ConvertSelectedMaterials()
    {
        Material[] selectedMaterials = Selection.GetFiltered<Material>(SelectionMode.DeepAssets);
        if (selectedMaterials == null || selectedMaterials.Length == 0)
        {
            Debug.LogWarning("[URP Converter] Project 창에서 Built-in 머터리얼 또는 머터리얼 폴더를 선택하세요.");
            return;
        }

        Shader urpLitShader = Shader.Find(UrpLitShaderName);
        if (urpLitShader == null)
        {
            Debug.LogError($"[URP Converter] 셰이더를 찾을 수 없습니다: {UrpLitShaderName}");
            return;
        }

        int convertedCount = 0;
        int skippedCount = 0;
        var processed = new HashSet<Material>();

        foreach (Material material in selectedMaterials)
        {
            if (material == null || !processed.Add(material))
                continue;

            if (!CanConvert(material, urpLitShader))
            {
                skippedCount++;
                Debug.LogWarning(
                    $"[URP Converter] 지원하지 않는 셰이더라 건너뜁니다: {material.shader.name}",
                    material);
                continue;
            }

            MaterialSnapshot snapshot = MaterialSnapshot.Capture(material);
            Undo.RecordObject(material, "Convert Material to URP/Lit");
            material.shader = urpLitShader;
            snapshot.ApplyToUrpLit(material);
            ApplyRequestedDefaults(material);
            EditorUtility.SetDirty(material);
            convertedCount++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            $"[URP Converter] 완료: {convertedCount}개 URP/Lit 변환 및 설정, " +
            $"{skippedCount}개 건너뜀. Metallic={DefaultMetallic}, Emission=ON/Black.");
    }

    private static bool CanConvert(Material material, Shader urpLitShader)
    {
        if (material.shader == null)
            return false;

        if (material.shader == urpLitShader)
            return true;

        string shaderName = material.shader.name;
        if (shaderName == Incorrect2DLitShaderName)
            return true;

        string shaderAssetPath = AssetDatabase.GetAssetPath(material.shader);
        return string.IsNullOrEmpty(shaderAssetPath) ||
               shaderAssetPath == "Resources/unity_builtin_extra";
    }

    private static void ApplyRequestedDefaults(Material material)
    {
        material.SetFloat("_WorkflowMode", 1f);
        material.SetFloat("_Metallic", DefaultMetallic);

        // 변환 전 설정과 관계없이 Emission은 항상 ON, 색상은 Black으로 통일한다.
        material.SetColor("_EmissionColor", Color.black);
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        material.EnableKeyword("_EMISSION");
    }

    private struct MaterialSnapshot
    {
        public Texture baseMap;
        public Vector2 baseMapScale;
        public Vector2 baseMapOffset;
        public Color baseColor;
        public Texture normalMap;
        public float normalScale;
        public Texture metallicMap;
        public float smoothness;
        public Texture occlusionMap;
        public float occlusionStrength;
        public Texture heightMap;
        public float heightScale;
        public Texture emissionMap;
        public float alphaCutoff;

        public static MaterialSnapshot Capture(Material material)
        {
            string baseMapProperty = material.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
            string baseColorProperty = material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";

            return new MaterialSnapshot
            {
                baseMap = GetTexture(material, baseMapProperty),
                baseMapScale = GetTextureScale(material, baseMapProperty),
                baseMapOffset = GetTextureOffset(material, baseMapProperty),
                baseColor = GetColor(material, baseColorProperty, Color.white),
                normalMap = GetTexture(material, "_BumpMap") ?? GetTexture(material, "_NormalMap"),
                normalScale = GetFloat(material, "_BumpScale", 1f),
                metallicMap = GetTexture(material, "_MetallicGlossMap"),
                smoothness = material.HasProperty("_Smoothness")
                    ? material.GetFloat("_Smoothness")
                    : GetFloat(material, "_Glossiness", 0.5f),
                occlusionMap = GetTexture(material, "_OcclusionMap"),
                occlusionStrength = GetFloat(material, "_OcclusionStrength", 1f),
                heightMap = GetTexture(material, "_ParallaxMap"),
                heightScale = GetFloat(material, "_Parallax", 0.005f),
                emissionMap = GetTexture(material, "_EmissionMap"),
                alphaCutoff = GetFloat(material, "_Cutoff", 0.5f)
            };
        }

        public void ApplyToUrpLit(Material material)
        {
            material.SetTexture("_BaseMap", baseMap);
            material.SetTextureScale("_BaseMap", baseMapScale);
            material.SetTextureOffset("_BaseMap", baseMapOffset);
            material.SetColor("_BaseColor", baseColor);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Cutoff", alphaCutoff);

            material.SetTexture("_BumpMap", normalMap);
            material.SetFloat("_BumpScale", normalScale);
            SetKeyword(material, "_NORMALMAP", normalMap != null);

            material.SetTexture("_MetallicGlossMap", metallicMap);
            SetKeyword(material, "_METALLICSPECGLOSSMAP", metallicMap != null);

            material.SetTexture("_OcclusionMap", occlusionMap);
            material.SetFloat("_OcclusionStrength", occlusionStrength);
            SetKeyword(material, "_OCCLUSIONMAP", occlusionMap != null);

            material.SetTexture("_ParallaxMap", heightMap);
            material.SetFloat("_Parallax", heightScale);
            SetKeyword(material, "_PARALLAXMAP", heightMap != null);

            material.SetTexture("_EmissionMap", emissionMap);
        }

        private static Texture GetTexture(Material material, string propertyName)
        {
            return material.HasProperty(propertyName) ? material.GetTexture(propertyName) : null;
        }

        private static float GetFloat(Material material, string propertyName, float fallback)
        {
            return material.HasProperty(propertyName) ? material.GetFloat(propertyName) : fallback;
        }

        private static Color GetColor(Material material, string propertyName, Color fallback)
        {
            return material.HasProperty(propertyName) ? material.GetColor(propertyName) : fallback;
        }

        private static Vector2 GetTextureScale(Material material, string propertyName)
        {
            return material.HasProperty(propertyName) ? material.GetTextureScale(propertyName) : Vector2.one;
        }

        private static Vector2 GetTextureOffset(Material material, string propertyName)
        {
            return material.HasProperty(propertyName) ? material.GetTextureOffset(propertyName) : Vector2.zero;
        }

        private static void SetKeyword(Material material, string keyword, bool enabled)
        {
            if (enabled)
                material.EnableKeyword(keyword);
            else
                material.DisableKeyword(keyword);
        }
    }
}
#endif

