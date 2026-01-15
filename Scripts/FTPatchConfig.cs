// FTPatchConfig.cs
#if UNITY_EDITOR
using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using Debug = UnityEngine.Debug;

[CreateAssetMenu(fileName = "NewFTPatchConfig", menuName = "Pawlygon/FaceTracking Patch Config")]
public class FTPatchConfig : ScriptableObject
{
    [Header("Identification")]
    public string avatarDisplayName = "Avatar Name";
    public string avatarVersion = "V1.0";

    [Header("Output Configuration")]
    [Tooltip("Optional: Prefabs to instantiate in the test scene after successful patching.")]
    public List<GameObject> patchedPrefabs = new List<GameObject>();
    public string patcherVersion;

    [Header("Original Prefab Reference")]
    public GameObject originalModelPrefab;

    [Header("Patch Output Configuration")]
    [Tooltip("Direct path to the folder where the patched FBX file will be created.")]
    public string outputPath;
    
    [Header("Diff Files")]
    [Tooltip("The diff file for patching the FBX model.")]
    public UnityEngine.Object fbxDiffFile;
    
    [Tooltip("The diff file for patching the FBX meta file.")]
    public UnityEngine.Object metaDiffFile;

    [Header("Package Requirements")]
    [Tooltip("Optional per-configuration package requirements. These will be checked in addition to the global PackageRules.")]
    public List<PackageRequirement> configSpecificPackages = new List<PackageRequirement>();

    /// <summary>
    /// Gets the expected path where the patched FBX file will be created.
    /// </summary>
    /// <returns>The full path to the expected FBX file, or null if configuration is incomplete.</returns>
    public string GetExpectedFBXPath()
    {
        if (string.IsNullOrEmpty(outputPath) || originalModelPrefab == null)
            return null;

        string baseName = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(originalModelPrefab)).Replace(" ", "_");
        return Path.Combine(outputPath, baseName + " FT.fbx");
    }

    /// <summary>
    /// Gets the asset path of the FBX diff file.
    /// </summary>
    /// <returns>The asset path of the FBX diff file, or null if not assigned.</returns>
    public string GetFbxDiffPath()
    {
        if (fbxDiffFile == null)
            return null;
            
        return AssetDatabase.GetAssetPath(fbxDiffFile);
    }
    
    /// <summary>
    /// Gets the asset path of the meta diff file.
    /// </summary>
    /// <returns>The asset path of the meta diff file, or null if not assigned.</returns>
    public string GetMetaDiffPath()
    {
        if (metaDiffFile == null)
            return null;
            
        return AssetDatabase.GetAssetPath(metaDiffFile);
    }

    /// <summary>
    /// Gets the base name derived from the original model prefab (with spaces replaced by underscores).
    /// </summary>
    /// <returns>The base name for file naming, or empty string if no prefab is assigned.</returns>

    /// <summary>
    /// Checks if the configuration has all required fields populated and valid.
    /// </summary>
    /// <returns>True if the configuration is complete and valid for patching.</returns>
    public bool IsValidForPatching()
    {
        // Check basic requirements
        if (originalModelPrefab == null) return false;
        if (string.IsNullOrEmpty(outputPath)) return false;
        if (fbxDiffFile == null) return false;
        if (metaDiffFile == null) return false;
        
        // Check if diff file assets exist
        string fbxDiffPath = GetFbxDiffPath();
        string metaDiffPath = GetMetaDiffPath();
        
        if (string.IsNullOrEmpty(fbxDiffPath) || string.IsNullOrEmpty(metaDiffPath)) return false;
        if (!File.Exists(fbxDiffPath) || !File.Exists(metaDiffPath)) return false;
        
        return true;
    }

    /// <summary>
    /// Gets a description of what is missing from the configuration.
    /// </summary>
    /// <returns>A string describing missing requirements, or null if configuration is valid.</returns>
    public string GetValidationMessage()
    {
        if (originalModelPrefab == null) return "Original Model Prefab is required";
        if (string.IsNullOrEmpty(outputPath)) return "Output Path is required";
        if (fbxDiffFile == null) return "FBX Diff File is required";
        if (metaDiffFile == null) return "Meta Diff File is required";
        
        string fbxDiffPath = GetFbxDiffPath();
        string metaDiffPath = GetMetaDiffPath();
        
        if (string.IsNullOrEmpty(fbxDiffPath)) return "FBX Diff File asset path is invalid";
        if (string.IsNullOrEmpty(metaDiffPath)) return "Meta Diff File asset path is invalid";
        if (!File.Exists(fbxDiffPath)) return "FBX Diff File does not exist";
        if (!File.Exists(metaDiffPath)) return "Meta Diff File does not exist";
        
        return null; // Configuration is valid
    }

    public string GetBaseName()
    {
        if (originalModelPrefab == null)
            return "";

        return Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(originalModelPrefab)).Replace(" ", "_");
    }

    /// <summary>
    /// Gets all package requirements for this configuration, combining global rules with config-specific requirements.
    /// </summary>
    /// <param name="globalRules">Global package rules to merge with config-specific rules</param>
    /// <returns>Combined list of package requirements</returns>
    public List<PackageRequirement> GetAllPackageRequirements(PackageRules globalRules = null)
    {
        var allRequirements = new List<PackageRequirement>();
        
        // Add global rules first
        if (globalRules != null && globalRules.packageRequirements != null)
        {
            allRequirements.AddRange(globalRules.packageRequirements);
        }
        
        // Add config-specific requirements
        if (configSpecificPackages != null)
        {
            // Check for duplicates and merge or override as needed
            foreach (var configPackage in configSpecificPackages)
            {
                var existingIndex = allRequirements.FindIndex(r => r.packageName == configPackage.packageName);
                if (existingIndex >= 0)
                {
                    // Config-specific requirement overrides global requirement
                    allRequirements[existingIndex] = configPackage;
                }
                else
                {
                    // Add new config-specific requirement
                    allRequirements.Add(configPackage);
                }
            }
        }
        
        return allRequirements;
    }
}
#endif

