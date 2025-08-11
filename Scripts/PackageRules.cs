#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[Serializable]
public class VersionError
{
    public string message;
    public MessageType messageType = MessageType.Error;

    [NonSerialized]
    public string vccURL;
    
    [NonSerialized]
    public string packageName; // Add package name for VPM CLI integration
    
    [NonSerialized]
    public bool isMissingPackage; // True if package is missing, false if just version issue
}

[Serializable]
public class PackageRequirement
{
    public string packageName;
    public string minVersion = "Any";
    public VersionError missingError;
    public VersionError versionError;

    [Tooltip("Optional VCC link to open if this package is missing or invalid")]
    public string vccURL;
}

[CreateAssetMenu(fileName = "PackageRules.asset", menuName = "Pawlygon/Patcher/Package Rules")]
public class PackageRules : ScriptableObject
{
    public List<PackageRequirement> packageRequirements;
}
#endif

