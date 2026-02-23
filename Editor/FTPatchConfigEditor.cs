using UnityEngine;
using UnityEditor;
using System.IO;

[CustomEditor(typeof(FTPatchConfig))]
public class FTPatchConfigEditor : Editor
{
    public override void OnInspectorGUI()
    {
        FTPatchConfig config = (FTPatchConfig)target;

        EditorGUI.BeginChangeCheck();

        // Draw default fields except for outputPath
        SerializedProperty iterator = serializedObject.GetIterator();
        bool enterChildren = true;
        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;
            
            if (iterator.propertyPath == "m_Script" || iterator.propertyPath == "outputPath" || iterator.propertyPath == "expectedFbxHash" || iterator.propertyPath == "expectedMetaHash")
                continue;
                
            EditorGUILayout.PropertyField(iterator, true);
        }

        // Custom folder path field with browse button
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("FBX Output Folder", EditorStyles.boldLabel);
        
        EditorGUILayout.BeginHorizontal();
        config.outputPath = EditorGUILayout.TextField("FBX Folder Path", config.outputPath);
        
        if (GUILayout.Button("Browse", GUILayout.Width(60)))
        {
            string initialPath = string.IsNullOrEmpty(config.outputPath) 
                ? Application.dataPath 
                : Path.GetFullPath(config.outputPath);
                
            string selectedPath = EditorUtility.OpenFolderPanel("Select FBX Output Folder", initialPath, "");
            
            if (!string.IsNullOrEmpty(selectedPath))
            {
                // Convert absolute path to relative path if it's within the project
                if (selectedPath.StartsWith(Application.dataPath))
                {
                    config.outputPath = "Assets" + selectedPath.Substring(Application.dataPath.Length);
                }
                else
                {
                    config.outputPath = selectedPath;
                }
            }
        }
        EditorGUILayout.EndHorizontal();

        // Show folder validation
        if (!string.IsNullOrEmpty(config.outputPath))
        {
            if (Directory.Exists(config.outputPath))
            {
                EditorGUILayout.HelpBox("✓ Folder exists", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("⚠ Folder does not exist", MessageType.Warning);
            }
        }

        // Show overall configuration status
        EditorGUILayout.Space();
        
        // Source File Validation section
        EditorGUILayout.LabelField("Source File Validation", EditorStyles.boldLabel);
        
        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(config.originalModelPrefab == null))
        {
            if (GUILayout.Button("Generate Hashes", GUILayout.Width(130)))
            {
                config.GenerateHashes();
            }
        }
        
        if (config.HasHashes)
        {
            if (GUILayout.Button("Clear", GUILayout.Width(50)))
            {
                Undo.RecordObject(config, "Clear Source File Hashes");
                config.expectedFbxHash = "";
                config.expectedMetaHash = "";
                EditorUtility.SetDirty(config);
            }
        }
        EditorGUILayout.EndHorizontal();
        
        if (config.HasHashes)
        {
            EditorGUILayout.HelpBox(
                $"FBX Hash: {config.expectedFbxHash}\nMeta Hash: {config.expectedMetaHash}",
                MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "No validation hashes stored. Click 'Generate Hashes' to enable source file validation in Patcher Hub.",
                MessageType.Warning);
        }
        
        EditorGUILayout.Space();
        
        // Show dependency status
        if (config.requiredDependency != null)
        {
            if (config.HasCircularDependency())
            {
                EditorGUILayout.HelpBox($"⚠ Circular dependency detected with '{config.requiredDependency.avatarDisplayName}'. Remove the dependency reference.", MessageType.Error);
            }
            else if (config.IsDependencySatisfied())
            {
                EditorGUILayout.HelpBox($"✓ Required dependency '{config.requiredDependency.avatarDisplayName}' is already patched", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox($"⚠ Required dependency '{config.requiredDependency.avatarDisplayName}' must be patched first", MessageType.Warning);
            }
        }
        
        if (config.IsValidForPatching())
        {
            EditorGUILayout.HelpBox("✓ Configuration is ready for patching", MessageType.Info);
        }
        else
        {
            string validationMessage = config.GetValidationMessage();
            EditorGUILayout.HelpBox($"⚠ {validationMessage}", MessageType.Warning);
        }

        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(config);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
