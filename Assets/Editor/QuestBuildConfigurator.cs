using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR.Features;

public static class QuestBuildConfigurator
{
    private const string OpenXRLoaderTypeName = "UnityEngine.XR.OpenXR.OpenXRLoader";
    private const string SettingsAssetPath = "Assets/XR/Settings/XRGeneralSettingsPerBuildTarget.asset";
    private const string QuestBuildOutputPath = "Builds/Quest/XR_course_project_Quest.apk";

    private static readonly string[] QuestBuildScenes =
    {
        "Assets/Scenes/Test_lobby.unity",
        "Assets/Scenes/Test_classroom.unity",
        "Assets/Scenes/Ocean.unity",
        "Assets/Scenes/Space.unity",
    };

    private static readonly string[] QuestFeatureIds =
    {
        "com.unity.openxr.feature.metaquest",
        "com.unity.openxr.feature.input.oculustouch",
        "com.unity.openxr.feature.input.metaquestpro",
        "com.unity.openxr.feature.input.metaquestplus",
        "com.unity.openxr.feature.input.handinteraction",
        "com.unity.openxr.feature.input.handinteractionposes",
        "com.unity.openxr.feature.input.palmpose",
    };

    private static readonly string[] DisabledQuestFeatureIds =
    {
        "com.unity.openxr.feature.input.eyetracking",
        "com.unity.openxr.feature.foveatedrendering",
    };

    [MenuItem("XR Course/Configure PC + Quest Test Build")]
    public static void ConfigurePcQuestTestBuild()
    {
        ConfigureQuestSceneOrder();
        ConfigureXRManagement(BuildTargetGroup.Android);
        EnableQuestOpenXRFeatures(BuildTargetGroup.Android);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[QuestBuildConfigurator] Android OpenXR loader and Quest OpenXR features configured.");
    }

    public static void ConfigureFromCommandLine()
    {
        ConfigurePcQuestTestBuild();
    }

    public static void BuildQuestApkFromCommandLine()
    {
        ConfigurePcQuestTestBuild();

        Directory.CreateDirectory(Path.GetDirectoryName(QuestBuildOutputPath));

        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.development = false;
        EditorUserBuildSettings.allowDebugging = false;
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        string[] scenes = QuestBuildScenes
            .Where(scenePath => File.Exists(scenePath))
            .ToArray();

        BuildReport report = BuildPipeline.BuildPlayer(
            scenes,
            QuestBuildOutputPath,
            BuildTarget.Android,
            BuildOptions.None);

        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new System.Exception($"Quest APK build failed: {report.summary.result}");
        }

        Debug.Log($"[QuestBuildConfigurator] Quest APK built at {QuestBuildOutputPath}. Size={report.summary.totalSize} bytes");
    }

    private static void ConfigureQuestSceneOrder()
    {
        EditorBuildSettings.scenes = QuestBuildScenes
            .Where(scenePath => File.Exists(scenePath))
            .Select(scenePath => new EditorBuildSettingsScene(scenePath, true))
            .ToArray();

        Debug.Log("[QuestBuildConfigurator] Quest scene order configured: Test_lobby -> Test_classroom -> Ocean -> Space.");
    }

    private static void ConfigureXRManagement(BuildTargetGroup targetGroup)
    {
        XRGeneralSettingsPerBuildTarget settings = LoadOrCreateBuildTargetSettings();

        if (!settings.HasSettingsForBuildTarget(targetGroup))
            settings.CreateDefaultSettingsForBuildTarget(targetGroup);

        if (!settings.HasManagerSettingsForBuildTarget(targetGroup))
            settings.CreateDefaultManagerSettingsForBuildTarget(targetGroup);

        XRGeneralSettings generalSettings = settings.SettingsForBuildTarget(targetGroup);
        XRManagerSettings managerSettings = settings.ManagerSettingsForBuildTarget(targetGroup);

        generalSettings.InitManagerOnStart = true;
        managerSettings.automaticLoading = true;
        managerSettings.automaticRunning = true;

        XRPackageMetadataStore.AssignLoader(managerSettings, OpenXRLoaderTypeName, targetGroup);

        EditorUtility.SetDirty(settings);
        EditorUtility.SetDirty(generalSettings);
        EditorUtility.SetDirty(managerSettings);
    }

    private static XRGeneralSettingsPerBuildTarget LoadOrCreateBuildTargetSettings()
    {
        if (EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget settings) && settings != null)
            return settings;

        string[] guids = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget");
        if (guids.Length > 0)
        {
            settings = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(AssetDatabase.GUIDToAssetPath(guids[0]));
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settings, true);
            return settings;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(SettingsAssetPath));
        settings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
        AssetDatabase.CreateAsset(settings, SettingsAssetPath);
        EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settings, true);
        return settings;
    }

    private static void EnableQuestOpenXRFeatures(BuildTargetGroup targetGroup)
    {
        FeatureHelpers.RefreshFeatures(targetGroup);

        foreach (string featureId in QuestFeatureIds)
        {
            OpenXRFeature feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(targetGroup, featureId);
            if (feature == null)
            {
                Debug.LogWarning($"[QuestBuildConfigurator] OpenXR feature not found: {featureId}");
                continue;
            }

            feature.enabled = true;
            EditorUtility.SetDirty(feature);
        }

        foreach (string featureId in DisabledQuestFeatureIds)
        {
            OpenXRFeature feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(targetGroup, featureId);
            if (feature == null)
                continue;

            feature.enabled = false;
            EditorUtility.SetDirty(feature);
        }
    }
}
