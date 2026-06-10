using System.IO;
using System.Text;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

public sealed class QuestAndroidManifestPostprocessor : IPostGenerateGradleAndroidProject
{
    private const string AndroidXmlNamespace = "http://schemas.android.com/apk/res/android";

    public int callbackOrder => 1000;

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        string mainManifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
        string xrManifestPath = Path.Combine(path, "xrmanifest.androidlib", "AndroidManifest.xml");

        bool sanitizedMain = SanitizeManifest(mainManifestPath, true);
        bool sanitizedXrLibrary = SanitizeManifest(xrManifestPath, false);

        if (sanitizedMain || sanitizedXrLibrary)
            Debug.Log("[QuestAndroidManifestPostprocessor] Quest manifest sanitized: eye tracking removed, hand tracking declared.");
    }

    private static bool SanitizeManifest(string manifestPath, bool ensureHandTracking)
    {
        if (!File.Exists(manifestPath))
            return false;

        XmlDocument document = new XmlDocument();
        document.Load(manifestPath);

        XmlElement manifest = document.DocumentElement;
        if (manifest == null)
        {
            Debug.LogWarning($"[QuestAndroidManifestPostprocessor] AndroidManifest.xml has no manifest root: {manifestPath}");
            return false;
        }

        RemoveElementsWithAndroidName(manifest, "uses-feature", "oculus.software.eye_tracking");
        RemoveElementsWithAndroidName(manifest, "uses-permission", "com.oculus.permission.EYE_TRACKING");
        RemoveElementsWithAndroidName(manifest, "uses-permission", "android.permission.EYE_TRACKING");
        RemoveElementsWithAndroidName(manifest, "uses-permission", "android.permission.EYE_TRACKING_FINE");

        if (ensureHandTracking)
        {
            EnsureUsesFeature(document, manifest, "android.hardware.vr.headtracking", "true", "1");
            EnsureUsesFeature(document, manifest, "oculus.software.handtracking", "false");
            EnsureQuestVrLaunchMetadata(document, manifest);
        }

        Save(document, manifestPath);
        return true;
    }

    private static void RemoveElementsWithAndroidName(XmlElement parent, string elementName, string androidName)
    {
        XmlNodeList nodes = parent.GetElementsByTagName(elementName);

        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            if (nodes[i] is not XmlElement element)
                continue;

            string name = element.GetAttribute("name", AndroidXmlNamespace);
            if (name != androidName)
                continue;

            element.ParentNode?.RemoveChild(element);
        }
    }

    private static void EnsureUsesFeature(XmlDocument document, XmlElement manifest, string featureName, string required, string version = null)
    {
        XmlElement feature = FindElementWithAndroidName(manifest, "uses-feature", featureName);

        if (feature == null)
        {
            feature = document.CreateElement("uses-feature");
            feature.SetAttribute("name", AndroidXmlNamespace, featureName);
            manifest.AppendChild(feature);
        }

        feature.SetAttribute("required", AndroidXmlNamespace, required);

        if (!string.IsNullOrEmpty(version))
            feature.SetAttribute("version", AndroidXmlNamespace, version);
    }

    private static XmlElement FindElementWithAndroidName(XmlElement parent, string elementName, string androidName)
    {
        XmlNodeList nodes = parent.GetElementsByTagName(elementName);

        foreach (XmlNode node in nodes)
        {
            if (node is not XmlElement element)
                continue;

            if (element.GetAttribute("name", AndroidXmlNamespace) == androidName)
                return element;
        }

        return null;
    }

    private static void EnsureQuestVrLaunchMetadata(XmlDocument document, XmlElement manifest)
    {
        XmlElement application = manifest["application"];
        if (application == null)
            return;

        XmlElement activity = FindUnityActivity(application);
        if (activity == null)
            return;

        XmlElement intentFilter = activity["intent-filter"];
        if (intentFilter == null)
        {
            intentFilter = document.CreateElement("intent-filter");
            activity.AppendChild(intentFilter);
        }

        EnsureIntentCategory(document, intentFilter, "android.intent.category.LAUNCHER");
        EnsureIntentCategory(document, intentFilter, "com.oculus.intent.category.VR");
        EnsureIntentAction(document, intentFilter, "android.intent.action.MAIN");
        EnsureActivityMetaData(document, activity, "unityplayer.UnityActivity", "true");
        EnsureActivityMetaData(document, activity, "com.samsung.android.vr.application.mode", "vr_only");
    }

    private static XmlElement FindUnityActivity(XmlElement application)
    {
        XmlNodeList activities = application.GetElementsByTagName("activity");

        foreach (XmlNode node in activities)
        {
            if (node is not XmlElement activity)
                continue;

            string name = activity.GetAttribute("name", AndroidXmlNamespace);
            if (name == "com.unity3d.player.UnityPlayerActivity" || name.EndsWith(".UnityPlayerActivity"))
                return activity;
        }

        return activities.Count > 0 ? activities[0] as XmlElement : null;
    }

    private static void EnsureIntentCategory(XmlDocument document, XmlElement intentFilter, string categoryName)
    {
        if (HasChildWithAndroidName(intentFilter, "category", categoryName))
            return;

        XmlElement category = document.CreateElement("category");
        category.SetAttribute("name", AndroidXmlNamespace, categoryName);
        intentFilter.AppendChild(category);
    }

    private static void EnsureIntentAction(XmlDocument document, XmlElement intentFilter, string actionName)
    {
        if (HasChildWithAndroidName(intentFilter, "action", actionName))
            return;

        XmlElement action = document.CreateElement("action");
        action.SetAttribute("name", AndroidXmlNamespace, actionName);
        intentFilter.AppendChild(action);
    }

    private static bool HasChildWithAndroidName(XmlElement parent, string elementName, string androidName)
    {
        XmlNodeList nodes = parent.GetElementsByTagName(elementName);

        foreach (XmlNode node in nodes)
        {
            if (node is XmlElement element && element.GetAttribute("name", AndroidXmlNamespace) == androidName)
                return true;
        }

        return false;
    }

    private static void EnsureActivityMetaData(XmlDocument document, XmlElement activity, string name, string value)
    {
        XmlNodeList nodes = activity.GetElementsByTagName("meta-data");

        foreach (XmlNode node in nodes)
        {
            if (node is not XmlElement metaData)
                continue;

            if (metaData.GetAttribute("name", AndroidXmlNamespace) != name)
                continue;

            metaData.SetAttribute("value", AndroidXmlNamespace, value);
            return;
        }

        XmlElement newMetaData = document.CreateElement("meta-data");
        newMetaData.SetAttribute("name", AndroidXmlNamespace, name);
        newMetaData.SetAttribute("value", AndroidXmlNamespace, value);
        activity.AppendChild(newMetaData);
    }

    private static void Save(XmlDocument document, string path)
    {
        using XmlTextWriter writer = new XmlTextWriter(path, new UTF8Encoding(false))
        {
            Formatting = Formatting.Indented
        };

        document.Save(writer);
    }
}
