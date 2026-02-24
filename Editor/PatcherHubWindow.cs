// PatcherHubWindow.cs
// © 2025 Pawlygon Studio. All rights reserved.
// Main editor window for PatcherHub - VRChat avatar face tracking patch application tool

using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Diagnostics;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
#endif

namespace Pawlygon.PatcherHub.Editor
{
    /// <summary>
    /// Advanced Unity Editor window for applying face tracking patches to FBX avatar models.
    /// Features integrated VRChat Creator Companion (VCC) package management, automated dependency checking,
    /// and streamlined patch application workflow with professional UI design.
    /// </summary>
    public class PatcherHubWindow : EditorWindow
    {
        #region Private Static Fields

        // Static tracking for all PatcherHub windows
        private static readonly HashSet<PatcherHubWindow> _openWindows = new HashSet<PatcherHubWindow>();
        private static readonly object _windowsLock = new object();

        #endregion

        #region Private Instance Fields

        // Configuration and state
        private List<FTPatchConfig> patchConfigs = new List<FTPatchConfig>();
        private HashSet<int> selectedConfigIndices = new HashSet<int>();
        private FTPatchConfig selectedConfig;
        private PackageRules packageRules;
        private List<VersionError> versionErrors;
        private Dictionary<string, List<VersionError>> configSpecificErrors = new Dictionary<string, List<VersionError>>();
        private bool requirementsChecked = false;
        
        // UI state for multi-select
        private Vector2 configListScrollPosition;
        private Vector2 validationErrorsScrollPosition;

        // UI and styling
        private Texture2D logo;
        private GUIStyleCollection styles;

        // VCC and package management
        private bool? vccAvailable = null;
        private Dictionary<string, PackageStatus> packageStatusCache = new Dictionary<string, PackageStatus>();

        // Loading state tracking
        private Dictionary<string, bool> packageLoadingStates = new Dictionary<string, bool>();
        private Dictionary<string, bool> packageInstallingStates = new Dictionary<string, bool>();
        private bool isCheckingPackageAvailability = false;
        private bool isBulkInstalling = false;
        private bool isCheckingVCCAvailability = false;
        private int packagesBeingChecked = 0;
        private int totalPackagesToCheck = 0;

        // Callback storage for proper cleanup
        private UnityEditor.EditorApplication.CallbackFunction _packageValidationCallback;

        // Patch operation state
        private PatchResult currentPatchResult = PatchResult.None;
        private bool promptToOpenScene = false;
        
        // Bulk patching state
        private bool isPatchingAll = false;
        private List<(FTPatchConfig config, PatchResult result)> bulkPatchResults = new List<(FTPatchConfig, PatchResult)>();
        private List<string> patchedPrefabPaths = new List<string>();
        
        // Diff validation state
        private Dictionary<string, DiffValidationResult> diffValidationResults = new Dictionary<string, DiffValidationResult>();
        
        // Patch warning panel state
        private bool showPatchWarningPanel = false;
        private double countdownStartTime;
        private string patchWarningTitle;
        private string patchWarningMessage;
        private string patchWarningFooter;
        private MessageType patchWarningType;
        private int pendingPatchValidCount;
        
        // UI state
        private bool showCredits = false;

        #endregion

        #region Nested Classes

        /// <summary>
        /// Collection of GUI styles used throughout the window.
        /// </summary>
        private class GUIStyleCollection
        {
            public GUIStyle Header { get; set; }
            public GUIStyle Footer { get; set; }
            public GUIStyle Link { get; set; }
            public GUIStyle BoldLabel { get; set; }
            public GUIStyle Button { get; set; }
            public GUIStyle Message { get; set; }
            public GUIStyle FlatLink { get; set; }
        }

        /// <summary>
        /// Stores the result of diff validation for a single patch configuration.
        /// </summary>
        private class DiffValidationResult
        {
            public DiffValidationStatus fbxStatus = DiffValidationStatus.NotChecked;
            public DiffValidationStatus metaStatus = DiffValidationStatus.NotChecked;
            public string fbxMessage;
            public string metaMessage;
            public string avatarName;

            public bool IsValid => fbxStatus == DiffValidationStatus.Valid && metaStatus == DiffValidationStatus.Valid;
            public bool HasIssues => (fbxStatus != DiffValidationStatus.NotChecked && fbxStatus != DiffValidationStatus.Valid) ||
                                     (metaStatus != DiffValidationStatus.NotChecked && metaStatus != DiffValidationStatus.Valid);
            public bool HasSourceNotFound => fbxStatus == DiffValidationStatus.SourceNotFound || metaStatus == DiffValidationStatus.SourceNotFound;
            public bool HasHashMismatch => fbxStatus == DiffValidationStatus.HashMismatch || metaStatus == DiffValidationStatus.HashMismatch;
        }

        #endregion

        #region Private Enums

        /// <summary>
        /// Represents the result of a patch operation.
        /// </summary>
        private enum PatchResult 
        { 
            None, 
            InvalidFBXPath, 
            MissingDiffFiles, 
            MetaPatchFailed, 
            FbxPatchFailed, 
            Success 
        }

        /// <summary>
        /// Represents the status of a package for VCC operations.
        /// </summary>
        private enum PackageStatus
        {
            Unknown,         // Status not yet determined
            Available,       // Available in VCC repositories for installation
            Installed,       // Already installed in project
            NotInRepository  // Package not found in any VCC repository
        }

        /// <summary>
        /// Represents the result of source file validation against stored hashes.
        /// </summary>
        private enum DiffValidationStatus
        {
            NotChecked,
            Valid,
            HashMismatch,
            SourceNotFound,
            NoHashStored
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Shows the Patcher Hub window.
        /// </summary>
        [MenuItem(PatcherHubConstants.MENU_PATH, false, 0)]
        public static void ShowWindow() => GetWindow<PatcherHubWindow>(PatcherHubConstants.WINDOW_TITLE).Show();

        #endregion

    #region Unity Editor Methods

    /// <summary>
    /// Called when the window is enabled. Initializes window state and registers for tracking.
    /// </summary>
    private void OnEnable()
    {
        // Register this window for tracking
        lock (_windowsLock)
        {
            _openWindows.Add(this);
        }
        
        // Load UI resources and set window constraints immediately
        logo = AssetDatabase.LoadAssetAtPath<Texture2D>(PatcherHubConstants.LOGO_PATH);
        minSize = new Vector2(PatcherHubConstants.MIN_WINDOW_WIDTH, PatcherHubConstants.MIN_WINDOW_HEIGHT);
        
        // Initialize window content for responsive startup
        LoadPatchConfigs();
        LoadPackageRules();
        
        // Start VCC availability check asynchronously
        EditorApplication.delayCall += () =>
        {
            if (this != null)
            {
                _ = CheckVCCAvailabilityInBackground();
            }
        };
    }

    /// <summary>
    /// Called when the window is disabled. Cleans up resources and unregisters tracking.
    /// </summary>
    private void OnDisable()
    {
        // Unregister this window from tracking
        lock (_windowsLock)
        {
            _openWindows.Remove(this);
        }
        
        // Clean up any ongoing operations
        ClearLoadingStates();
        
        // Remove package validation callback to prevent memory leaks
        if (_packageValidationCallback != null)
        {
            EditorApplication.update -= _packageValidationCallback;
            _packageValidationCallback = null;
        }
        
        // Clean up any VCC batch operations
        VCCIntegration.ForceClearBatchOperations();
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Clears all loading states and stops async package checking operations.
    /// </summary>
    private void ClearLoadingStates()
    {
        isCheckingPackageAvailability = false;
        packageLoadingStates.Clear();
        packageInstallingStates.Clear();
        isBulkInstalling = false;
        packagesBeingChecked = 0;
        totalPackagesToCheck = 0;
        
        Repaint();
    }

    /// <summary>
    /// Initializes GUI styles used throughout the window interface.
    /// </summary>
    private void SetupStyles()
    {
        styles = new GUIStyleCollection
        {
            Header = new GUIStyle(EditorStyles.boldLabel) 
            { 
                fontSize = PatcherHubConstants.HEADER_FONT_SIZE, 
                alignment = TextAnchor.MiddleCenter 
            },
            Footer = new GUIStyle(EditorStyles.label) 
            { 
                alignment = TextAnchor.MiddleCenter, 
                fontSize = PatcherHubConstants.FOOTER_FONT_SIZE 
            },
            Link = new GUIStyle(EditorStyles.label) 
            { 
                normal = { textColor = PatcherHubConstants.LINK_COLOR_NORMAL }, 
                alignment = TextAnchor.MiddleCenter 
            },
            BoldLabel = new GUIStyle(EditorStyles.boldLabel),
            Button = CreateButtonStyle(),
            Message = CreateMessageStyle(),
            FlatLink = CreateFlatLinkStyle()
        };
    }

    /// <summary>
    /// Creates the button style used for action buttons throughout the interface.
    /// </summary>
    private GUIStyle CreateButtonStyle()
    {
        return new GUIStyle(GUI.skin.button)
        {
            fontSize = PatcherHubConstants.BUTTON_FONT_SIZE,
            fontStyle = FontStyle.Bold,
            fixedHeight = PatcherHubConstants.BUTTON_HEIGHT,
            alignment = TextAnchor.MiddleCenter,
            margin = new RectOffset(8, 8, 4, 4),
            padding = new RectOffset(8, 8, 6, 6)
        };
    }

    /// <summary>
    /// Creates the message style used for informational text and help messages.
    /// </summary>
    private GUIStyle CreateMessageStyle()
    {
        return new GUIStyle(EditorStyles.label)
        {
            fontSize = PatcherHubConstants.MESSAGE_FONT_SIZE,
            wordWrap = true,
            richText = true,
            alignment = TextAnchor.MiddleLeft
        };
    }

    /// <summary>
    /// Creates the flat link style used for footer links and navigation elements.
    /// </summary>
    private GUIStyle CreateFlatLinkStyle()
    {
        return new GUIStyle(EditorStyles.label)
        {
            fontSize = PatcherHubConstants.LINK_FONT_SIZE,
            fontStyle = FontStyle.Normal,
            normal = { textColor = PatcherHubConstants.LINK_COLOR_FLAT },
            hover = { textColor = PatcherHubConstants.LINK_COLOR_HOVER },
            alignment = TextAnchor.MiddleCenter,
        };
    }

    /// <summary>
    /// Loads all available patch configurations from the project assets.
    /// </summary>
    private void LoadPatchConfigs()
    {
        patchConfigs.Clear();
        
        // Find all FTPatchConfig assets in the project
        foreach (var guid in AssetDatabase.FindAssets(PatcherHubConstants.FTPATCH_CONFIG_SEARCH))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var config = AssetDatabase.LoadAssetAtPath<FTPatchConfig>(path);
            if (config != null) 
            {
                patchConfigs.Add(config);
            }
        }

        // Update selected configuration to ensure valid selection
        if (patchConfigs.Count > 0)
        {
            // Initialize selection: select all configs by default if none selected
            if (selectedConfigIndices.Count == 0)
            {
                for (int i = 0; i < patchConfigs.Count; i++)
                {
                    selectedConfigIndices.Add(i);
                }
            }
            else
            {
                // Remove invalid indices
                selectedConfigIndices.RemoveWhere(i => i >= patchConfigs.Count);
            }
            
            // Set first config as the preview config for package validation
            selectedConfig = patchConfigs[0];
        }

        // Validate source files against stored hashes
        ValidateAllDiffFiles();
    }

    /// <summary>
    /// Renders the editor window GUI.
    /// </summary>
    private void OnGUI()
    {
        // Ensure styles are initialized
        if (styles == null) SetupStyles();
        
        if (patchConfigs == null || selectedConfig == null) LoadPatchConfigs();

        // Always draw header
        DrawHeader();

        // Check if we have configurations
        if (patchConfigs == null || patchConfigs.Count == 0)
        {
            DrawNoConfigurationsUI();
            return;
        }

        DrawConfigSelection();
        DrawDiffValidation();
        DrawPatchButton();
        DrawPatchWarningPanel();
        DrawPatchResult(currentPatchResult);
        DrawGroupedValidationErrors(requirementsChecked);
        DrawVCCTip();

        DrawHorizontalLine();
        DrawFooter();
        TryOpenSceneAfterPatch();
    }

    private void DrawHeader()
    {
        GUILayout.Space(PatcherHubConstants.SPACE_LARGE);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (logo != null) GUILayout.Label(logo, GUILayout.Width(PatcherHubConstants.LOGO_SIZE), GUILayout.Height(PatcherHubConstants.LOGO_SIZE));
        GUILayout.Space(PatcherHubConstants.SPACE_MEDIUM + 4);
        GUILayout.BeginVertical(GUILayout.Height(PatcherHubConstants.LOGO_SIZE));
        GUILayout.FlexibleSpace();
        GUILayout.Label(PatcherHubConstants.HEADER_TITLE, styles?.Header ?? EditorStyles.boldLabel);
        GUILayout.Label(PatcherHubConstants.HEADER_SUBTITLE, EditorStyles.centeredGreyMiniLabel);
        GUILayout.FlexibleSpace();
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        GUILayout.Space(PatcherHubConstants.SPACE_HEADER);
    }

    private void DrawNoConfigurationsUI()
    {
        GUILayout.Space(20);
        
        // Main warning message
        EditorGUILayout.HelpBox(PatcherHubConstants.NO_CONFIGS_TITLE, MessageType.Warning);
        
        GUILayout.Space(10);
        
        // Information box with instructions
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Space(8);
        
        EditorGUILayout.LabelField("Getting Started", EditorStyles.boldLabel);
        GUILayout.Space(5);
        
        EditorGUILayout.LabelField("To use the Patcher Hub, you need to create FT Patch Configuration assets:", EditorStyles.wordWrappedLabel);
        GUILayout.Space(8);
        
        EditorGUILayout.LabelField("1. Right-click in the Project window", EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("2. Navigate to Create > Pawlygon > FaceTracking Patch Config", EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("3. Configure your avatar settings and diff files", EditorStyles.wordWrappedLabel);
        
        GUILayout.Space(10);
        
        // Create button
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        
        if (GUILayout.Button(PatcherHubConstants.CREATE_CONFIG_BUTTON, GUILayout.Height(30), GUILayout.Width(200)))
        {
            CreateNewPatchConfig();
        }
        
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        
        GUILayout.Space(8);
        EditorGUILayout.EndVertical();
        
        GUILayout.Space(20);
        
        // Refresh button
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        
        if (GUILayout.Button(PatcherHubConstants.REFRESH_BUTTON, GUILayout.Width(80)))
        {
            LoadPatchConfigs();
        }
        
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        
        // Add footer to maintain UI consistency
        DrawHorizontalLine();
        DrawFooter();
    }

    private void CreateNewPatchConfig()
    {
        // Use Unity's built-in ScriptableObject creation
        var newConfig = CreateInstance<FTPatchConfig>();
        newConfig.avatarDisplayName = "New Avatar";
        newConfig.avatarVersion = "V1.0";
        
        // Save it to the project
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Patch Configuration",
            "NewFTPatchConfig",
            "asset",
            "Choose where to save the patch configuration"
        );
        
        if (!string.IsNullOrEmpty(path))
        {
            AssetDatabase.CreateAsset(newConfig, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            // Reload configurations and select the new one
            LoadPatchConfigs();
            
            // Focus on the new asset in the project window
            EditorGUIUtility.PingObject(newConfig);
            Selection.activeObject = newConfig;
        }
        else
        {
            DestroyImmediate(newConfig);
        }
    }

    private void DrawConfigSelection()
    {
        EditorGUILayout.LabelField("Avatar Configurations:", styles?.BoldLabel ?? EditorStyles.boldLabel);
        GUILayout.Space(4);
        
        // If only one config, show simple info instead of selection UI
        if (patchConfigs.Count == 1)
        {
            var config = patchConfigs[0];
            
            // Ensure it's selected
            if (!selectedConfigIndices.Contains(0))
            {
                selectedConfigIndices.Add(0);
            }
            
            GUIStyle infoBoxStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(12, 12, 8, 8),
                fontSize = 12
            };
            
            EditorGUILayout.BeginVertical(infoBoxStyle);
            EditorGUILayout.BeginHorizontal();
            
            // Avatar name
            GUIStyle nameStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13
            };
            EditorGUILayout.LabelField(config.avatarDisplayName, nameStyle);
            
            // Show dependency info inline if available
            if (config.requiredDependency != null)
            {
                GUIStyle depStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = config.IsDependencySatisfied() ? new Color(0.4f, 0.8f, 0.4f) : new Color(0.9f, 0.6f, 0.2f) },
                    fontSize = 10
                };
                string depIcon = config.IsDependencySatisfied() ? "✓" : "⚠";
                EditorGUILayout.LabelField($"{depIcon} Requires: {config.requiredDependency.avatarDisplayName}", depStyle);
            }
            
            GUILayout.FlexibleSpace();
            
            DrawConfigValidationIcon(config, fontSize: 16);
            
            EditorGUILayout.EndHorizontal();
            
            // Show version if available
            if (!string.IsNullOrEmpty(config.avatarVersion))
            {
                GUIStyle versionStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = Color.gray }
                };
                EditorGUILayout.LabelField($"Version: {config.avatarVersion}", versionStyle);
            }
            
            EditorGUILayout.EndVertical();
        }
        else
        {
            // Multiple configs - show full selection UI
            // Draw selection controls
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All", GUILayout.Width(80)))
            {
                selectedConfigIndices.Clear();
                for (int i = 0; i < patchConfigs.Count; i++)
                {
                    selectedConfigIndices.Add(i);
                }
                
                // Re-validate packages when selection changes
                requirementsChecked = false;
                showPatchWarningPanel = false;
                LoadPackageRules();
            }
            if (GUILayout.Button("Deselect All", GUILayout.Width(90)))
            {
                selectedConfigIndices.Clear();
                
                // Re-validate packages when selection changes
                requirementsChecked = false;
                showPatchWarningPanel = false;
                LoadPackageRules();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"{selectedConfigIndices.Count} of {patchConfigs.Count} selected", GUILayout.Width(120));
            EditorGUILayout.EndHorizontal();
            
            GUILayout.Space(4);
            
            // Draw scrollable list of configs with checkboxes
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            configListScrollPosition = EditorGUILayout.BeginScrollView(configListScrollPosition, GUILayout.Height(Mathf.Min(patchConfigs.Count * 24, 200)));
            
            for (int i = 0; i < patchConfigs.Count; i++)
            {
                var config = patchConfigs[i];
                EditorGUILayout.BeginHorizontal();
                
                // Checkbox
                bool wasSelected = selectedConfigIndices.Contains(i);
                bool isSelected = EditorGUILayout.ToggleLeft("", wasSelected, GUILayout.Width(20));
                
                if (isSelected != wasSelected)
                {
                    if (isSelected)
                    {
                        selectedConfigIndices.Add(i);
                        
                        // Auto-select required dependency if it exists and is not already selected
                        if (config.requiredDependency != null)
                        {
                            int depIndex = patchConfigs.IndexOf(config.requiredDependency);
                            if (depIndex >= 0 && !selectedConfigIndices.Contains(depIndex))
                            {
                                selectedConfigIndices.Add(depIndex);
                                Debug.Log($"[PatcherHub] Auto-selected dependency '{config.requiredDependency.avatarDisplayName}' for '{config.avatarDisplayName}'");
                            }
                        }
                    }
                    else
                    {
                        selectedConfigIndices.Remove(i);
                        
                        // Check if any other selected config depends on this one
                        bool isDependedUpon = false;
                        foreach (int selectedIndex in selectedConfigIndices.ToList())
                        {
                            if (selectedIndex < patchConfigs.Count)
                            {
                                var selectedCfg = patchConfigs[selectedIndex];
                                if (selectedCfg.requiredDependency == config)
                                {
                                    isDependedUpon = true;
                                    // Also deselect the dependent config
                                    selectedConfigIndices.Remove(selectedIndex);
                                    Debug.Log($"[PatcherHub] Auto-deselected '{selectedCfg.avatarDisplayName}' because its dependency '{config.avatarDisplayName}' was deselected");
                                }
                            }
                        }
                    }
                    
                    // Re-validate packages when selection changes
                    requirementsChecked = false;
                    showPatchWarningPanel = false;
                    LoadPackageRules();
                }
                
                // Config info
                GUIStyle labelStyle = new GUIStyle(EditorStyles.label)
                {
                    fontStyle = wasSelected ? FontStyle.Bold : FontStyle.Normal
                };
                
                // Show avatar name
                EditorGUILayout.LabelField(config.avatarDisplayName, labelStyle);
                
                // Show dependency info inline if available
                if (config.requiredDependency != null)
                {
                    GUIStyle depStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = config.IsDependencySatisfied() ? new Color(0.4f, 0.8f, 0.4f) : new Color(0.9f, 0.6f, 0.2f) },
                        fontSize = 9
                    };
                    string depIcon = config.IsDependencySatisfied() ? "✓" : "⚠";
                    EditorGUILayout.LabelField($"{depIcon} Requires: {config.requiredDependency.avatarDisplayName}", depStyle);
                }
                
                GUILayout.FlexibleSpace();
                
                DrawConfigValidationIcon(config);
                
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        GUILayout.Space(8);
    }

    /// <summary>
    /// Draws the selected configuration information with clean, Unity-native styling.
    /// </summary>
    private void DrawSelectedConfigInfo()
    {
        GUILayout.Space(8);
        
        // Use Unity's built-in HelpBox style for clean containment
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Space(6);
        
        // Clean two-column layout
        DrawConfigInfoField("Avatar Version", selectedConfig.avatarVersion);
        DrawConfigInfoField("Patcher Version", selectedConfig.patcherVersion);
        
        // Special handling for FBX field with validation
        DrawFBXField();
        
        GUILayout.Space(4);
        EditorGUILayout.EndVertical();
        GUILayout.Space(8);
    }

    /// <summary>
    /// Draws a clean configuration info field with proper label-value alignment.
    /// </summary>
    private void DrawConfigInfoField(string label, string value)
    {
        EditorGUILayout.BeginHorizontal();
        
        // Label with consistent width
        GUILayout.Label(label + ":", EditorStyles.label, GUILayout.Width(PatcherHubConstants.FIELD_LABEL_WIDTH));
        
        // Value with proper styling
        if (string.IsNullOrEmpty(value))
        {
            GUIStyle emptyStyle = new GUIStyle(EditorStyles.label)
            {
                fontStyle = FontStyle.Italic,
                normal = { textColor = Color.gray }
            };
            GUILayout.Label("Not specified", emptyStyle);
        }
        else
        {
            GUILayout.Label(value, EditorStyles.label);
        }
        
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// Draws the FBX field with integrated validation status.
    /// </summary>
    private void DrawFBXField()
    {
        EditorGUILayout.BeginHorizontal();
        
        // Label
        GUILayout.Label("Original FBX:", EditorStyles.label, GUILayout.Width(PatcherHubConstants.FIELD_LABEL_WIDTH));
        
        // FBX object field
        EditorGUI.BeginChangeCheck();
        var newPrefab = EditorGUILayout.ObjectField(selectedConfig.originalModelPrefab, typeof(GameObject), false);
        if (EditorGUI.EndChangeCheck())
        {
            // Handle prefab changes if needed (typically read-only in config display)
            // selectedConfig.originalModelPrefab = (GameObject)newPrefab;
        }
        
        // Status indicator
        bool configIsValid = selectedConfig.IsValidForPatching();
        
        DrawStatusIcon(configIsValid);
        
        EditorGUILayout.EndHorizontal();
        
        // Show validation message if configuration is invalid
        if (!configIsValid)
        {
            EditorGUILayout.BeginHorizontal();
            DrawFieldIndent(PatcherHubConstants.FIELD_LABEL_WIDTH);
            
            GUIStyle warningStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(1f, 0.6f, 0f) },
                fontStyle = FontStyle.Italic
            };
            
            string validationMessage = selectedConfig.GetValidationMessage();
            GUILayout.Label(validationMessage, warningStyle);
            EditorGUILayout.EndHorizontal();
        }
    }

    /// <summary>
    /// Draws the diff validation section showing patch compatibility status.
    /// </summary>
    private void DrawDiffValidation()
    {
        if (diffValidationResults == null || diffValidationResults.Count == 0) return;

        // Check if there are any results worth showing
        bool hasAnyIssues = diffValidationResults.Values.Any(r => r.HasIssues);
        bool hasAnyChecked = diffValidationResults.Values.Any(r =>
            r.fbxStatus != DiffValidationStatus.NotChecked || r.metaStatus != DiffValidationStatus.NotChecked);

        if (!hasAnyChecked) return;


        foreach (var kvp in diffValidationResults)
        {
            var result = kvp.Value;
            if (!result.HasIssues && result.fbxStatus == DiffValidationStatus.NotChecked) continue;

            if (result.HasSourceNotFound)
            {
                // Source not found is an error
                if (!string.IsNullOrEmpty(result.fbxMessage))
                    EditorGUILayout.HelpBox(result.fbxMessage, MessageType.Error);
                if (!string.IsNullOrEmpty(result.metaMessage))
                    EditorGUILayout.HelpBox(result.metaMessage, MessageType.Error);
            }
            else if (result.HasHashMismatch)
            {
                // Hash mismatch is a warning
                if (!string.IsNullOrEmpty(result.fbxMessage))
                    EditorGUILayout.HelpBox(result.fbxMessage, MessageType.Warning);
                if (!string.IsNullOrEmpty(result.metaMessage))
                    EditorGUILayout.HelpBox(result.metaMessage, MessageType.Warning);
            }
            else if (result.fbxStatus == DiffValidationStatus.NoHashStored)
            {
                // No hash stored is info-level
                if (!string.IsNullOrEmpty(result.fbxMessage))
                    EditorGUILayout.HelpBox(result.fbxMessage, MessageType.Info);
            }
        }
    }

    /// <summary>
    /// Gets the appropriate icon and tooltip for a config's diff validation status.
    /// </summary>
    private GUIContent GetDiffValidationIcon(FTPatchConfig config)
    {
        if (diffValidationResults.TryGetValue(config.avatarDisplayName, out var result))
        {
            if (result.HasSourceNotFound)
                return new GUIContent("⚠", "Original avatar model not found");
            if (result.HasHashMismatch)
                return new GUIContent("⚠", "Source file has been modified - patch may fail");
            if (result.IsValid)
                return new GUIContent("✓", "Source file validated - ready for patching");
            if (result.fbxStatus == DiffValidationStatus.NoHashStored)
                return new GUIContent("⚙", "No validation hashes stored");
        }
        return null;
    }

    /// <summary>
    /// Draws a validation status icon for a patch configuration, incorporating diff validation.
    /// </summary>
    /// <param name="config">The patch configuration to draw the icon for</param>
    /// <param name="fontSize">Font size for the icon (default 14, use 16 for single-config view)</param>
    private void DrawConfigValidationIcon(FTPatchConfig config, int fontSize = 14)
    {
        bool isValid = config.IsValidForPatching();
        GUIContent diffIcon = GetDiffValidationIcon(config);
        GUIContent statusIcon;
        Color iconColor;

        if (!isValid)
        {
            statusIcon = new GUIContent("✗", config.GetValidationMessage());
            iconColor = Color.red;
        }
        else if (diffIcon != null && diffValidationResults.TryGetValue(config.avatarDisplayName, out var result) && result.HasIssues)
        {
            statusIcon = diffIcon;
            iconColor = result.HasSourceNotFound ? PatcherHubConstants.ERROR_BUTTON_COLOR : PatcherHubConstants.WARNING_COLOR;
        }
        else
        {
            statusIcon = new GUIContent("✓", "Configuration is valid");
            iconColor = Color.green;
        }

        GUIStyle iconStyle = new GUIStyle(EditorStyles.label)
        {
            normal = { textColor = iconColor },
            fontSize = fontSize,
            alignment = TextAnchor.MiddleRight
        };

        EditorGUILayout.LabelField(statusIcon, iconStyle, GUILayout.Width(20));
    }

    private void DrawPatchButton()
    {
        GUILayout.Space(16);

        // Count valid selected configs
        int validSelectedCount = 0;
        foreach (int index in selectedConfigIndices)
        {
            if (index < patchConfigs.Count && patchConfigs[index].IsValidForPatching())
            {
                validSelectedCount++;
            }
        }
        
        bool hasSelection = selectedConfigIndices.Count > 0;
        bool hasValidSelection = validSelectedCount > 0;
        bool allowPatch = requirementsChecked && hasValidSelection;

        // Determine button color based on severity
        GetPackageIssueSeverity(out bool hasErrors, out bool hasWarnings);
        bool hasDiffIssues = diffValidationResults.Values.Any(r => r.HasIssues);
        
        Color originalColor = GUI.backgroundColor;
        if (allowPatch)
        {
            if (hasErrors)
                GUI.backgroundColor = PatcherHubConstants.ERROR_BUTTON_COLOR;
            else if (hasWarnings || hasDiffIssues)
                GUI.backgroundColor = PatcherHubConstants.WARNING_BUTTON_COLOR;
        }

        // Draw the patch button with severity-appropriate icon
        string iconName;
        if (hasErrors)
            iconName = PatcherHubConstants.ERROR_ICON;
        else if (hasWarnings || hasDiffIssues)
            iconName = PatcherHubConstants.WARNING_ICON;
        else
            iconName = PatcherHubConstants.PLAY_ICON;
        
        Texture icon = EditorGUIUtility.IconContent(iconName).image;
        string buttonText = hasSelection 
            ? $"  Patch Selected Avatars" 
            : "  Select Avatars to Patch";
        GUIContent content = new GUIContent(buttonText, icon);

        using (new EditorGUI.DisabledScope(!allowPatch || showPatchWarningPanel))
        {
            if (GUILayout.Button(content, styles?.Button ?? GUI.skin.button, GUILayout.ExpandWidth(true)))
            {
                HandlePatchSelectedButtonClick();
            }
        }
        
        GUI.backgroundColor = originalColor;
        
        GUILayout.Space(8);

        if (!showPatchWarningPanel)
        {
            DrawPatchButtonMessages(hasSelection, hasValidSelection, validSelectedCount);
        }
    }

    private void HandlePatchSelectedButtonClick()
    {
        int validSelectedCount = 0;
        foreach (int index in selectedConfigIndices)
        {
            if (index < patchConfigs.Count && patchConfigs[index].IsValidForPatching())
            {
                validSelectedCount++;
            }
        }
        
        GetPackageIssueSeverity(out bool hasErrors, out bool hasWarnings);
        bool hasDiffIssues = diffValidationResults.Values.Any(r => r.HasIssues);
        bool hasAnyIssues = hasErrors || hasWarnings || hasDiffIssues;
        
        if (hasAnyIssues)
        {
            // Show inline warning panel with countdown instead of popup
            if (hasErrors)
            {
                patchWarningTitle = PatcherHubConstants.ERROR_DIALOG_TITLE;
                patchWarningMessage = PatcherHubConstants.ERROR_DIALOG_MESSAGE;
                patchWarningFooter = PatcherHubConstants.ERROR_DIALOG_FOOTER;
                patchWarningType = MessageType.Error;
            }
            else if (hasWarnings)
            {
                patchWarningTitle = PatcherHubConstants.WARNING_ONLY_DIALOG_TITLE;
                patchWarningMessage = PatcherHubConstants.WARNING_ONLY_DIALOG_MESSAGE;
                patchWarningFooter = PatcherHubConstants.WARNING_ONLY_DIALOG_FOOTER;
                patchWarningType = MessageType.Warning;
            }
            else // hasDiffIssues
            {
                patchWarningTitle = "Warning: Patch Compatibility";
                patchWarningMessage = "One or more source files have compatibility warnings. The avatar(s) should still patch, but may not fully function after uploading in VRChat.";
                patchWarningFooter = "Please fix the warnings listed below, or only continue if you are an advanced user.";
                patchWarningType = MessageType.Warning;
            }
            
            pendingPatchValidCount = validSelectedCount;
            countdownStartTime = EditorApplication.timeSinceStartup;
            showPatchWarningPanel = true;
            return;
        }
        
        // No issues — use a simple confirmation dialog
        bool proceed = EditorUtility.DisplayDialog(
            "Patch Selected Configurations",
            $"This will patch {validSelectedCount} avatar(s) sequentially.\n\n" +
            "After completion, a new scene will be created with all patched prefabs.\n\nContinue?",
            "Yes, Patch Selected",
            "Cancel"
        );

        if (!proceed) return;

        StartPatchingSelected();
    }

    /// <summary>
    /// Begins the bulk patch operation. Called after user confirms via dialog or countdown panel.
    /// </summary>
    private void StartPatchingSelected()
    {
        isPatchingAll = true;
        bulkPatchResults.Clear();
        patchedPrefabPaths.Clear();
        ApplyPatchSelected();
    }

    /// <summary>
    /// Draws an inline warning panel with a countdown timer when package/diff issues exist.
    /// Replaces the popup dialog to ensure users read the warnings before proceeding.
    /// </summary>
    private void DrawPatchWarningPanel()
    {
        if (!showPatchWarningPanel) return;

        double elapsed = EditorApplication.timeSinceStartup - countdownStartTime;
        float remaining = Mathf.Max(0f, PatcherHubConstants.PATCH_COUNTDOWN_SECONDS - (float)elapsed);
        bool countdownComplete = remaining <= 0f;

        // Determine panel border color based on severity
        Color panelColor = patchWarningType == MessageType.Error
            ? PatcherHubConstants.ERROR_BUTTON_COLOR
            : PatcherHubConstants.WARNING_BUTTON_COLOR;

        // Draw colored border
        Rect borderRect = EditorGUILayout.BeginVertical();
        EditorGUI.DrawRect(borderRect, panelColor * 0.35f);

        GUILayout.Space(2);
        Rect innerRect = EditorGUILayout.BeginVertical(new GUIStyle("box")
        {
            padding = new RectOffset(12, 12, 10, 10),
            margin = new RectOffset(2, 2, 0, 0)
        });

        // Title with icon
        string iconStr = patchWarningType == MessageType.Error ? "❌" : "⚠";
        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 13,
            wordWrap = true,
            normal = { textColor = panelColor }
        };
        EditorGUILayout.LabelField($"{iconStr} {patchWarningTitle}", titleStyle);

        GUILayout.Space(6);

        // Warning message body
        GUIStyle messageStyle = new GUIStyle(EditorStyles.label)
        {
            wordWrap = true,
            fontSize = 12,
            richText = true
        };
        EditorGUILayout.LabelField(patchWarningMessage, messageStyle);

        GUILayout.Space(6);

        // Footer instruction
        GUIStyle footerStyle = new GUIStyle(EditorStyles.label)
        {
            wordWrap = true,
            fontSize = 12,
            fontStyle = FontStyle.Bold
        };
        EditorGUILayout.LabelField(patchWarningFooter, footerStyle);

        GUILayout.Space(4);

        // Patch count info
        GUIStyle infoStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            fontStyle = FontStyle.Italic
        };
        // EditorGUILayout.LabelField($"This will patch {pendingPatchValidCount} avatar(s) sequentially.", infoStyle);

        GUILayout.Space(10);

        // Buttons row
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        // Cancel button
        if (GUILayout.Button("Cancel", GUILayout.Width(100), GUILayout.Height(28)))
        {
            showPatchWarningPanel = false;
        }

        GUILayout.Space(8);

        // Continue button with countdown
        Color origBg = GUI.backgroundColor;
        if (countdownComplete)
        {
            GUI.backgroundColor = patchWarningType == MessageType.Error
                ? PatcherHubConstants.ERROR_BUTTON_COLOR
                : PatcherHubConstants.WARNING_BUTTON_COLOR;
        }

        using (new EditorGUI.DisabledScope(!countdownComplete))
        {
            string continueText = countdownComplete
                ? "Continue with Patching"
                : $"Continue in {Mathf.CeilToInt(remaining)}s...";

            if (GUILayout.Button(continueText, GUILayout.Width(200), GUILayout.Height(28)))
            {
                showPatchWarningPanel = false;
                StartPatchingSelected();
            }
        }

        GUI.backgroundColor = origBg;

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(4);
        EditorGUILayout.EndVertical();
        GUILayout.Space(2);
        EditorGUILayout.EndVertical();

        // Keep repainting during countdown
        if (!countdownComplete)
        {
            Repaint();
        }
    }

    private void DrawPatchButtonMessages(bool hasSelection, bool hasValidSelection, int validCount)
    {
        if (!hasSelection)
        {
            EditorGUILayout.HelpBox("No configurations selected. Please select at least one configuration to patch.", MessageType.Info);
        }
        else if (!hasValidSelection)
        {
            EditorGUILayout.HelpBox("No valid configurations selected. All selected configurations have validation errors.", MessageType.Error);
        }
        else if (!requirementsChecked)
        {
            EditorGUILayout.HelpBox(PatcherHubConstants.VALIDATING_PACKAGES_MESSAGE, MessageType.Info);
        }
        else if (requirementsChecked)
        {
            GetPackageIssueSeverity(out bool hasErrors, out bool hasWarnings);
            if (hasErrors)
            {
                DrawCustomMessage(PatcherHubConstants.ERROR_PACKAGES_MESSAGE, MessageType.Error);
            }
            else if (hasWarnings)
            {
                DrawCustomMessage(PatcherHubConstants.WARNING_PACKAGES_MESSAGE, MessageType.Warning);
            }
            else
            {
                bool hasPackageIssues = (versionErrors != null && versionErrors.Count > 0) || 
                                       (configSpecificErrors != null && configSpecificErrors.Count > 0);
                if (hasPackageIssues)
                {
                    DrawCustomMessage(PatcherHubConstants.INFO_PACKAGES_MESSAGE, MessageType.Info);
                }
            }
        }
    }

    private void DrawGroupedValidationErrors(bool requirementsChecked)
    {
        if (!requirementsChecked) return;
        
        bool hasGlobalErrors = versionErrors != null && versionErrors.Count > 0;
        bool hasConfigErrors = configSpecificErrors != null && configSpecificErrors.Count > 0;
        
        if (!hasGlobalErrors && !hasConfigErrors) return;

        // Show overall progress if packages are being checked
        if (isCheckingPackageAvailability && totalPackagesToCheck > 0)
        {
            EditorGUILayout.Space();
            
            GUIStyle progressBoxStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(8, 8, 6, 6),
                margin = new RectOffset(0, 0, 2, 6)
            };
            
            EditorGUILayout.BeginVertical(progressBoxStyle);
            
            GUILayout.BeginHorizontal();
            string loadingIcon = GetLoadingIcon();
            GUILayout.Label($"{loadingIcon} Checking package availability...", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{packagesBeingChecked}/{totalPackagesToCheck}", EditorStyles.miniLabel);
            GUILayout.EndHorizontal();
            
            Rect progressRect = GUILayoutUtility.GetRect(0, 4, GUILayout.ExpandWidth(true));
            EditorGUI.ProgressBar(progressRect, (float)packagesBeingChecked / totalPackagesToCheck, "");
            
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.Space();
        
        // Begin scroll view for validation errors
        validationErrorsScrollPosition = EditorGUILayout.BeginScrollView(validationErrorsScrollPosition, GUILayout.ExpandHeight(true));

        // Draw global configuration errors
        if (hasGlobalErrors)
        {
            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                margin = new RectOffset(0, 0, 8, 4)
            };
            EditorGUILayout.LabelField("Global Configuration", headerStyle);
            
            foreach (var error in versionErrors)
            {
                DrawSingleError(error);
            }
        }

        // Draw divider if we have both global and config-specific errors
        if (hasGlobalErrors && hasConfigErrors)
        {
            EditorGUILayout.Space(8);
            DrawHorizontalLine();
            EditorGUILayout.Space(8);
        }

        // Draw config-specific errors grouped by avatar
        if (hasConfigErrors)
        {
            // Only show avatar headers when there are multiple configs selected
            bool showHeaders = configSpecificErrors.Count > 1;
            
            foreach (var kvp in configSpecificErrors)
            {
                string avatarName = kvp.Key;
                var errors = kvp.Value;
                
                if (showHeaders)
                {
                    GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 13,
                        margin = new RectOffset(0, 0, 8, 4)
                    };
                    EditorGUILayout.LabelField(avatarName, headerStyle);
                }
                
                foreach (var error in errors)
                {
                    DrawSingleError(error);
                }
                
                EditorGUILayout.Space(4);
            }
        }
        
        // Draw Package Repository Status section
        DrawPackageRepositoryStatus();
        
        // Draw bulk install/update button if multiple VCC-manageable packages exist
        DrawBulkInstallButton();
        
        // End scroll view
        EditorGUILayout.EndScrollView();
    }
    
    private void DrawPackageRepositoryStatus()
    {
        // Collect all errors to check repository status
        var allErrors = new List<VersionError>();
        if (versionErrors != null)
            allErrors.AddRange(versionErrors);
        if (configSpecificErrors != null)
        {
            foreach (var configErrorList in configSpecificErrors.Values)
            {
                allErrors.AddRange(configErrorList);
            }
        }
        
        // Get packages that are not in VCC repositories
        var missingFromRepo = allErrors
            .Where(error => !string.IsNullOrEmpty(error.packageName) && 
                           packageStatusCache.TryGetValue(error.packageName, out PackageStatus status) && 
                           status == PackageStatus.NotInRepository)
            .Select(error => error.packageName)
            .Distinct()
            .ToList();
        
        if (missingFromRepo.Count == 0)
            return;
        
        EditorGUILayout.Space(8);
        DrawHorizontalLine();
        EditorGUILayout.Space(8);
        
        GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 13,
            margin = new RectOffset(0, 0, 8, 4)
        };
        EditorGUILayout.LabelField("📦 Package Repository Status", headerStyle);
        
        GUIStyle messageStyle = new GUIStyle(EditorStyles.helpBox)
        {
            fontSize = 11,
            richText = true,
            padding = new RectOffset(12, 12, 8, 8)
        };
        
        string message = $"Some packages are not found in VCC repositories ({missingFromRepo.Count} missing). This means VCC cannot automatically install them. Possible solutions:\n" +
                        "• Add the missing repositories to VCC\n" +
                        "• Install packages manually from their GitHub releases\n" +
                        "• Contact the package authors for VCC repository information";
        
        EditorGUILayout.LabelField(message, messageStyle);
        
        EditorGUILayout.Space(4);
    }

    private void DrawSingleError(VersionError error)
    {
        // Determine the icon based on MessageType and repository status
        string iconName = error.messageType switch
        {
            MessageType.Error => PatcherHubConstants.ERROR_ICON,
            MessageType.Warning => PatcherHubConstants.WARNING_ICON,
            MessageType.Info => PatcherHubConstants.INFO_ICON,
            _ => null
        };

        Texture icon = !string.IsNullOrEmpty(iconName)
            ? EditorGUIUtility.IconContent(iconName).image
            : null;

        GUIStyle messageStyle = new GUIStyle(EditorStyles.label)
        {
            fontSize = 12,
            richText = true,
            alignment = TextAnchor.MiddleLeft
        };

        GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fixedHeight = 24,
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            margin = new RectOffset(6, 6, 6, 6)
        };

        EditorGUILayout.BeginVertical(GUI.skin.box);
        EditorGUILayout.BeginHorizontal();

        if (icon != null)
        {
            GUILayout.Label(icon, GUILayout.Width(30), GUILayout.Height(30));
            GUILayout.Space(4);
        }

        GUILayout.Label(error.message, messageStyle, GUILayout.ExpandWidth(true), GUILayout.Height(30));

        // Button container
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();

        // Show either VCC install button OR VCC URL button
        if (!string.IsNullOrEmpty(error.packageName))
        {
            bool isInstalling = packageInstallingStates.ContainsKey(error.packageName) && packageInstallingStates[error.packageName];
            bool isLoadingVCC = IsPackageLoadingVCC(error.packageName);
            
            if (isInstalling || isBulkInstalling)
            {
                GUI.enabled = false;
                string statusText = isBulkInstalling ? "Bulk Installing..." : "Installing...";
                GUILayout.Button(statusText, buttonStyle, GUILayout.Width(140));
                GUI.enabled = true;
            }
            else if (isLoadingVCC)
            {
                GUI.enabled = false;
                string loadingText = GetLoadingIcon() + " Checking...";
                GUILayout.Button(loadingText, buttonStyle, GUILayout.Width(120));
                GUI.enabled = true;
            }
            else
            {
                // Check if package is available for installation via VCC API
                bool packageAvailableViaVCC = IsPackageAvailableViaVCC(error.packageName);
                
                // Check if we've already determined the package is not in repositories
                bool packageNotInRepo = packageStatusCache.TryGetValue(error.packageName, out PackageStatus packageStatus) && 
                                      packageStatus == PackageStatus.NotInRepository;
                
                if (packageAvailableViaVCC)
                {
                    // Show VCC API install/update button
                    string buttonText = error.isMissingPackage ? PatcherHubConstants.INSTALL_PACKAGE_BUTTON : PatcherHubConstants.UPDATE_PACKAGE_BUTTON;
                    if (GUILayout.Button(buttonText, buttonStyle, GUILayout.Width(PatcherHubConstants.BUTTON_MEDIUM_WIDTH)))
                    {
                        _ = TryInstallPackageViaVCCAsync(error.packageName, error.isMissingPackage);
                    }
                }
                else if (packageNotInRepo)
                {
                    // Determine severity-appropriate color for the "Missing from VCC" label
                    Color labelColor = error.messageType switch
                    {
                        MessageType.Error => PatcherHubConstants.ERROR_BUTTON_COLOR,
                        MessageType.Warning => PatcherHubConstants.WARNING_COLOR,
                        _ => PatcherHubConstants.LINK_COLOR_FLAT
                    };
                    string labelIcon = error.messageType switch
                    {
                        MessageType.Error => "❌ ",
                        MessageType.Warning => "⚠ ",
                        _ => "ℹ "
                    };
                    
                    // Show status as a label instead of button
                    GUIStyle statusLabelStyle = new GUIStyle(EditorStyles.helpBox)
                    {
                        fontSize = PatcherHubConstants.STATUS_FONT_SIZE,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter,
                        normal = { 
                            textColor = labelColor
                        },
                        fixedHeight = PatcherHubConstants.STATUS_LABEL_HEIGHT,
                        margin = new RectOffset(PatcherHubConstants.SPACE_SMALL, PatcherHubConstants.SPACE_SMALL, PatcherHubConstants.SPACE_SMALL, PatcherHubConstants.SPACE_SMALL),
                        padding = new RectOffset(PatcherHubConstants.SPACE_MEDIUM, PatcherHubConstants.SPACE_MEDIUM, PatcherHubConstants.SPACE_TINY, PatcherHubConstants.SPACE_TINY)
                    };
                    GUILayout.Label(labelIcon + PatcherHubConstants.NOT_IN_VCC_LABEL, statusLabelStyle, GUILayout.Width(PatcherHubConstants.STATUS_LABEL_WIDTH));
                    
                    // Also show VCC webpage button if available
                    if (!string.IsNullOrEmpty(error.vccURL))
                    {
                        if (GUILayout.Button(PatcherHubConstants.ADD_VIA_VCC_BUTTON, buttonStyle, GUILayout.Width(PatcherHubConstants.ADD_VCC_BUTTON_WIDTH)))
                        {
                            VCCIntegration.OpenVCCUrl(error.vccURL);
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(error.vccURL))
                {
                    // Show VCC URL button as fallback
                    if (GUILayout.Button(PatcherHubConstants.VIEW_IN_VCC_BUTTON, buttonStyle, GUILayout.Width(100)))
                    {
                        VCCIntegration.OpenVCCUrl(error.vccURL);
                    }
                }
            }
        }
        else if (!string.IsNullOrEmpty(error.vccURL))
        {
            if (GUILayout.Button("Open in VCC", buttonStyle, GUILayout.Width(110)))
            {
                VCCIntegration.OpenVCCUrl(error.vccURL);
            }
        }

        GUILayout.EndHorizontal();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    /// <summary>
    /// Draws a bulk install/update button for all VCC-manageable packages
    /// </summary>
    private void DrawBulkInstallButton()
    {
        bool hasGlobalErrors = versionErrors != null && versionErrors.Count > 0;
        bool hasConfigErrors = configSpecificErrors != null && configSpecificErrors.Count > 0;
        
        if (!requirementsChecked || (!hasGlobalErrors && !hasConfigErrors))
            return;
        
        // Verify VCC availability and identify packages manageable through VCC
        if (!(vccAvailable ?? false))
            return;
        
        // Collect all errors from both global and config-specific
        var allErrors = new List<VersionError>();
        if (hasGlobalErrors)
            allErrors.AddRange(versionErrors);
        if (hasConfigErrors)
        {
            foreach (var configErrorList in configSpecificErrors.Values)
            {
                allErrors.AddRange(configErrorList);
            }
        }
        
        // Get all packages that can be managed via VCC
        var vccManageablePackages = allErrors
            .Where(error => !string.IsNullOrEmpty(error.packageName) && 
                           IsPackageAvailableViaVCC(error.packageName) &&
                           !(packageInstallingStates.ContainsKey(error.packageName) && packageInstallingStates[error.packageName]))
            .GroupBy(error => error.packageName)
            .Select(g => g.First())
            .ToList();
        
        if (vccManageablePackages.Count < 2) // Only show for multiple packages
            return;
        
        EditorGUILayout.Space();
        
        // Create bulk button style
        GUIStyle bulkButtonStyle = new GUIStyle(GUI.skin.button)
        {
            fixedHeight = 32,
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            margin = new RectOffset(8, 8, 6, 6)
        };
        
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        
        if (isBulkInstalling)
        {
            // Show progress during bulk installation
            GUI.enabled = false;
            GUILayout.Button($"Installing All Packages... ({vccManageablePackages.Count} packages)", bulkButtonStyle, GUILayout.Width(300));
            GUI.enabled = true;
        }
        else
        {
            // Show bulk install button
            string buttonText = $"🚀 Install/Update All Packages ({vccManageablePackages.Count})";
            if (GUILayout.Button(buttonText, bulkButtonStyle, GUILayout.Width(300)))
            {
                TryBulkInstallAllPackages(vccManageablePackages);
            }
        }
        
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        
        if (isBulkInstalling)
        {
            EditorGUILayout.Space();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Installing packages automatically via VCC...", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
    }

    private void DrawVCCTip()
    {
        // Show VCC availability loading state if checking
        if (isCheckingVCCAvailability)
        {
            EditorGUILayout.Space();
            
            GUIStyle infoBoxStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(12, 12, 8, 8),
                margin = new RectOffset(0, 0, 4, 4)
            };
            
            EditorGUILayout.BeginVertical(infoBoxStyle);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("🔄 Checking VCC availability...", EditorStyles.label);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            return;
        }

        // Only show tip if VCC is not available AND there are missing packages
        bool hasGlobalErrors = versionErrors != null && versionErrors.Count > 0;
        bool hasConfigErrors = configSpecificErrors != null && configSpecificErrors.Count > 0;
        
        if (!(vccAvailable ?? true) && (hasGlobalErrors || hasConfigErrors))
        {
            EditorGUILayout.Space();
            
            // Create info box style
            GUIStyle infoBoxStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(12, 12, 8, 8),
                margin = new RectOffset(0, 0, 4, 4)
            };
            
            // Create tip content style
            GUIStyle tipTextStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                wordWrap = true,
                richText = true,
                alignment = TextAnchor.MiddleLeft
            };
            
            // Create link button style
            GUIStyle linkButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fontStyle = FontStyle.Normal,
                fixedHeight = 22,
                margin = new RectOffset(0, 0, 4, 0)
            };
            
            EditorGUILayout.BeginVertical(infoBoxStyle);
            
            // Header with icon
            EditorGUILayout.BeginHorizontal();
            Texture infoIcon = EditorGUIUtility.IconContent("console.infoicon").image;
            if (infoIcon != null)
            {
                GUILayout.Label(infoIcon, GUILayout.Width(16), GUILayout.Height(16));
                GUILayout.Space(4);
            }
            GUILayout.Label("<b>💡 Tip: VRChat Creator Companion Required for One-Click Package Management</b>", tipTextStyle);
            EditorGUILayout.EndHorizontal();
            
            GUILayout.Space(4);
            
            // Tip content
            GUILayout.Label(
                "VRChat Creator Companion (VCC) enables one-click package installation directly from this window. " +
                "Make sure VCC is running and this project is managed by VCC for the best experience.\n\n" +
                "<b>To enable one-click installation:</b>\n" +
                "1. Install and run VRChat Creator Companion\n" +
                "2. Make sure this project is added to VCC\n" +
                "3. Keep VCC running while using Patcher Hub\n\n" +
                "<b>Alternative:</b> You can always add packages manually using the 'View in VCC' button.",
                tipTextStyle
            );
            
            GUILayout.Space(6);
            
            // Buttons row
            EditorGUILayout.BeginHorizontal();
            
            // Open VCC button
            if (GUILayout.Button(PatcherHubConstants.OPEN_VCC_BUTTON, linkButtonStyle, GUILayout.Width(PatcherHubConstants.BUTTON_MEDIUM_WIDTH)))
            {
                VCCIntegration.OpenVCC();
                
                // Show immediate feedback to user
                EditorUtility.DisplayProgressBar("Opening VCC", "Attempting to launch VRChat Creator Companion...", 0.5f);
                
                // Clear progress bar after a delay and refresh VCC status
                EditorApplication.delayCall += () =>
                {
                    System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ =>
                    {
                        EditorApplication.delayCall += () =>
                        {
                            EditorUtility.ClearProgressBar();
                        };
                    });
                };
            }
            
            GUILayout.Space(8);
            
            // Documentation link button
            if (GUILayout.Button(PatcherHubConstants.VIEW_DOCS_BUTTON, linkButtonStyle, GUILayout.Width(150)))
            {
                Application.OpenURL("https://vcc.docs.vrchat.com/");
            }
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
        }
        
        // Show refresh section if VCC is available but there are still missing packages
        // Hide during bulk installation to prevent UI flicker
        else if ((vccAvailable ?? false) && versionErrors != null && versionErrors.Count > 0 && !isBulkInstalling)
        {
            // Check if any packages might be missing from VCC repos
            var missingFromVCC = versionErrors.Where(error => 
                !string.IsNullOrEmpty(error.packageName) && 
                !IsPackageAvailableViaVCC(error.packageName) &&
                !IsPackageLoadingVCC(error.packageName)
            ).ToList();
            
            if (missingFromVCC.Count > 0)
            {
                EditorGUILayout.Space();
                
                GUIStyle infoBoxStyle = new GUIStyle(GUI.skin.box)
                {
                    padding = new RectOffset(12, 12, 8, 8),
                    margin = new RectOffset(0, 0, 4, 4)
                };
                
                GUIStyle tipTextStyle = new GUIStyle(EditorStyles.label)
                {
                    fontSize = 11,
                    wordWrap = true,
                    richText = true,
                    alignment = TextAnchor.MiddleLeft
                };
                
                GUIStyle refreshButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 11,
                    fontStyle = FontStyle.Normal,
                    fixedHeight = 24,
                    margin = new RectOffset(0, 0, 4, 0)
                };
                
                EditorGUILayout.BeginVertical(infoBoxStyle);
                
                EditorGUILayout.BeginHorizontal();
                Texture refreshIcon = EditorGUIUtility.IconContent("refresh").image;
                if (refreshIcon != null)
                {
                    GUILayout.Label(refreshIcon, GUILayout.Width(16), GUILayout.Height(16));
                    GUILayout.Space(4);
                }
                GUILayout.Label($"<b>{PatcherHubConstants.REPO_STATUS_TITLE}</b>", tipTextStyle);
                EditorGUILayout.EndHorizontal();
                
                GUILayout.Space(4);
                
                GUILayout.Label(
                    $"Some packages are not found in VCC repositories ({missingFromVCC.Count} missing). " +
                    "This means VCC cannot automatically install them. Possible solutions:\n" +
                    "• Add the missing repositories to VCC\n" +
                    "• Install packages manually from their GitHub releases\n" +
                    "• Contact the package authors for VCC repository information",
                    tipTextStyle
                );
                
                GUILayout.Space(6);
                
                EditorGUILayout.BeginHorizontal();
                
                // Refresh button
                GUI.enabled = !isCheckingPackageAvailability && !isBulkInstalling;
                if (GUILayout.Button(PatcherHubConstants.REFRESH_AVAILABILITY_BUTTON, refreshButtonStyle, GUILayout.Width(200)))
                {
                    RefreshAllPackageAvailability();
                }
                GUI.enabled = true;
                
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
        }
    }


    private void DrawPatchResult(PatchResult result)
    {
        EditorGUILayout.Space();
        switch (result)
        {
            case PatchResult.InvalidFBXPath:
                DrawCustomMessage("FBX path invalid or missing. Ensure your prefab is linked to a valid FBX file.", MessageType.Error);
                break;
            case PatchResult.MissingDiffFiles:
                DrawCustomMessage("Patch files are missing. Make sure the 'patcher/data/DiffFiles' folder exists and contains the required .hdiff files.", MessageType.Error);
                break;
            case PatchResult.MetaPatchFailed:
                DrawCustomMessage(GetPatchFailureDetail("meta"), MessageType.Error);
                break;
            case PatchResult.FbxPatchFailed:
                DrawCustomMessage(GetPatchFailureDetail("FBX"), MessageType.Error);
                break;
            case PatchResult.Success:
                DrawCustomMessage("✅ Patch completed successfully.", MessageType.Info);
                break;
        }
    }

    /// <summary>
    /// Returns a specific failure message based on diff validation results.
    /// If the source file had a hash mismatch, the message indicates modification.
    /// </summary>
    /// <param name="fileType">"FBX" or "meta"</param>
    private string GetPatchFailureDetail(string fileType)
    {
        bool isFbx = fileType == "FBX";

        // Check diff validation results for any selected config with issues
        foreach (var kvp in diffValidationResults)
        {
            var validation = kvp.Value;
            var status = isFbx ? validation.fbxStatus : validation.metaStatus;

            if (status == DiffValidationStatus.HashMismatch)
            {
                return $"Failed to patch the {fileType} file. The original avatar model has been modified and no longer matches the expected file.\nEnsure the file has not been modified and matches the original import exactly.";
            }

            if (status == DiffValidationStatus.SourceNotFound)
            {
                return $"Failed to patch the {fileType} file. The original avatar model could not be found.\nEnsure the original unmodified avatar is imported in the project.";
            }
        }

        // Generic fallback
        return isFbx
            ? "Failed to patch the FBX file. Ensure the file has not been modified and matches the original import exactly."
            : "Failed to patch the FBX .meta file. It must be identical to the original version imported from the avatar.";
    }

    private void DrawFooter()
    {
        GUILayout.FlexibleSpace();
        GUILayout.Space(20);
        
        // Status bar above footer separator
        DrawStatusBar();
        
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

        // Footer with centered branding
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label($"Made with ❤️ by Pawlygon Studio  •  {PatcherHubConstants.TOOL_VERSION}", styles?.Footer ?? EditorStyles.label);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        // Flat link buttons
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        DrawFlatFooterLink("Website", PatcherHubConstants.WEBSITE_URL);
        DrawFlatFooterLink("X (Twitter)", PatcherHubConstants.TWITTER_URL);
        DrawFlatFooterLink("YouTube", PatcherHubConstants.YOUTUBE_URL);
        DrawFlatFooterLink("Discord", PatcherHubConstants.DISCORD_URL);
        
        GUILayout.Space(8);
        DrawCreditsToggleLink();

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        // Expandable credits section
        DrawCreditsSection();

        GUILayout.Space(10);
    }

    /// <summary>
    /// Draws a status bar showing system status like VCC connectivity.
    /// </summary>
    private void DrawStatusBar()
    {
        // Only show status bar if there's something to display
        if (vccAvailable == true)
        {
            GUILayout.Space(4);
            
            // Use built-in Unity styling for better compatibility
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            
            // VCC Status
            GUIStyle statusTextStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.3f, 0.8f, 0.3f) },
                fontSize = 9
            };
            
            GUILayout.Label("✓ VCC Ready", statusTextStyle);
            
            GUILayout.FlexibleSpace();
            
            // You can add more status indicators here in the future
            // Example: GUILayout.Label("📡 Online", statusTextStyle);
            
            EditorGUILayout.EndHorizontal();
        }
    }


    private void DrawFlatFooterLink(string label, string url)
    {
        var linkStyle = styles?.FlatLink ?? EditorStyles.label;
        Rect rect = GUILayoutUtility.GetRect(new GUIContent(label), linkStyle, GUILayout.Width(100), GUILayout.Height(20));
        EditorGUI.LabelField(rect, label, linkStyle);

        EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
        if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
        {
            Application.OpenURL(url);
            Event.current.Use();
        }
    }

    /// <summary>
    /// Draws a clickable credits toggle link in the footer.
    /// </summary>
    private void DrawCreditsToggleLink()
    {
        var linkStyle = styles?.FlatLink ?? EditorStyles.label;
        string toggleText = showCredits ? "Credits ▲" : "Credits ▼";
        Rect rect = GUILayoutUtility.GetRect(new GUIContent(toggleText), linkStyle, GUILayout.Width(80), GUILayout.Height(20));
        EditorGUI.LabelField(rect, toggleText, linkStyle);

        EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
        if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
        {
            showCredits = !showCredits;
            Repaint();
        }
    }

    /// <summary>
    /// Draws the expandable credits section when showCredits is true.
    /// </summary>
    private void DrawCreditsSection()
    {
        if (!showCredits) return;

        GUILayout.Space(8);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        GUILayout.Space(8);

        // Credits container with subtle background
        GUIStyle creditsBoxStyle = new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(12, 12, 8, 8),
            margin = new RectOffset(8, 8, 4, 4)
        };

        GUIStyle creditsTextStyle = new GUIStyle(EditorStyles.label)
        {
            fontSize = 11,
            wordWrap = true,
            alignment = TextAnchor.UpperLeft,
            richText = true
        };

        EditorGUILayout.BeginVertical(creditsBoxStyle);
        
        GUILayout.Label("<b>Special Thanks & Credits</b>", creditsTextStyle);
        GUILayout.Space(6);
        
        // Credits with clickable tool names
        DrawClickableCredit("Hash's EditDistributionTools", "https://github.com/HashEdits/EditDistributionTools", "Inspiration for distribution workflows using binary patching", creditsTextStyle);
        DrawClickableCredit("hpatchz", "https://github.com/sisong/HDiffPatch", "High-performance binary diff/patch library by housisong", creditsTextStyle);
        DrawClickableCredit("ikeiwa VRC Package Verificator", "https://ikeiwa.gumroad.com/l/vrcverificator", "Inspiration behind our package checking feature", creditsTextStyle);
        DrawClickableCredit("VRChat Creator Companion", "https://vcc.docs.vrchat.com/", "Package management integration", creditsTextStyle);
        DrawClickableCredit("Furality SDK", "https://furality.org/", "VCC package import implementation", creditsTextStyle);
        DrawClickableCredit("tkya", null, "Countless hours of technical support to the community", creditsTextStyle);
        DrawClickableCredit("VRChat Community", null, "Feedback, testing, and feature requests", creditsTextStyle);
        
        GUILayout.Space(8);
        GUILayout.Label("<i>Thank you to everyone who helped make PatcherHub possible!</i>", creditsTextStyle);
        
        EditorGUILayout.EndVertical();
    }

    /// <summary>
    /// Helper method to draw a field label with consistent spacing for validation messages.
    /// </summary>
    /// <param name="indentWidth">Width to indent for alignment</param>
    private void DrawFieldIndent(int indentWidth = 100)
    {
        GUILayout.Space(indentWidth); // Align with value column
    }

    /// <summary>
    /// Helper method to draw status icons with consistent styling.
    /// </summary>
    /// <param name="isValid">Whether to show success or error icon</param>
    private void DrawStatusIcon(bool isValid)
    {
        string iconName = isValid ? "TestPassed" : "TestFailed";
        Texture icon = EditorGUIUtility.IconContent(iconName).image;
        if (icon != null)
        {
            GUILayout.Label(icon, GUILayout.Width(16), GUILayout.Height(16));
        }
    }

    /// <summary>
    /// Draws a credit line with the tool name as a clickable link.
    /// </summary>
    /// <param name="toolName">Name of the tool/library</param>
    /// <param name="url">URL to open when clicked (null for non-clickable text)</param>
    /// <param name="description">Description of what the tool provides</param>
    /// <param name="baseStyle">Base style for text</param>
    private void DrawClickableCredit(string toolName, string url, string description, GUIStyle baseStyle)
    {
        EditorGUILayout.BeginHorizontal();
        
        // Bullet point
        GUILayout.Label("• ", baseStyle, GUILayout.Width(12));
        
        // Tool name - clickable if URL provided, bold text if not
        if (!string.IsNullOrEmpty(url))
        {
            // Clickable tool name
            var linkStyle = new GUIStyle(baseStyle)
            {
                normal = { textColor = new Color(0.4f, 0.7f, 1f) },
                hover = { textColor = new Color(0.6f, 0.9f, 1f) },
                fontStyle = FontStyle.Bold
            };
            
            var toolNameContent = new GUIContent(toolName);
            Rect toolNameRect = GUILayoutUtility.GetRect(toolNameContent, linkStyle, GUILayout.ExpandWidth(false));
            
            EditorGUI.LabelField(toolNameRect, toolName, linkStyle);
            EditorGUIUtility.AddCursorRect(toolNameRect, MouseCursor.Link);
            
            if (Event.current.type == EventType.MouseDown && toolNameRect.Contains(Event.current.mousePosition))
            {
                Application.OpenURL(url);
                Event.current.Use();
            }
        }
        else
        {
            // Non-clickable bold text
            var boldStyle = new GUIStyle(baseStyle)
            {
                fontStyle = FontStyle.Bold
            };
            
            GUILayout.Label(toolName, boldStyle, GUILayout.ExpandWidth(false));
        }
        
        // Description - let it use remaining space
        GUILayout.Label($" - {description}", baseStyle);
        
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(2);
    }
    
    private void DrawCustomMessage(string message, MessageType type)
    {
        UIMessageHelper.DrawMessage(message, type, styles?.Message);
    }


    private void DrawHorizontalLine()
    {
        var rect = EditorGUILayout.GetControlRect(false, 1);
        rect.height = 1;
        EditorGUI.DrawRect(rect, new Color(0.3f, 0.3f, 0.3f, 1));
    }

    private void TryOpenSceneAfterPatch()
    {
        if (!promptToOpenScene)
            return;
            
        promptToOpenScene = false;
        
        // Handle bulk patching - create new scene with all patched prefabs
        if (isPatchingAll && patchedPrefabPaths.Count > 0)
        {
            CreateSceneWithPatchedPrefabs();
        }
        // Handle single patch - check if config has prefabs to instantiate
        else if (!isPatchingAll && selectedConfig != null && selectedConfig.patchedPrefabs != null && selectedConfig.patchedPrefabs.Count > 0)
        {
            patchedPrefabPaths.Clear();
            
            foreach (var prefab in selectedConfig.patchedPrefabs)
            {
                if (prefab != null)
                {
                    string prefabPath = AssetDatabase.GetAssetPath(prefab);
                    if (!string.IsNullOrEmpty(prefabPath))
                    {
                        patchedPrefabPaths.Add(prefabPath);
                    }
                }
            }
            
            if (patchedPrefabPaths.Count > 0)
            {
                CreateSceneWithPatchedPrefabs();
            }
        }
    }

    /// <summary>
    /// Creates a new scene and instantiates all patched prefabs into it.
    /// </summary>
    private void CreateSceneWithPatchedPrefabs()
    {
        try
        {
            // Ask user to save current scene if needed
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }
            
            // Create new scene
            var newScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            
            // Instantiate each prefab in a grid layout
            float spacing = 2.5f;
            int columns = Mathf.CeilToInt(Mathf.Sqrt(patchedPrefabPaths.Count));
            
            for (int i = 0; i < patchedPrefabPaths.Count; i++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(patchedPrefabPaths[i]);
                if (prefab != null)
                {
                    int row = i / columns;
                    int col = i % columns;
                    Vector3 position = new Vector3(col * spacing, 0, row * spacing);
                    
                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, newScene);
                    instance.transform.position = position;
                    
                    Debug.Log($"[PatcherHub] Instantiated {prefab.name} at {position}");
                }
                else
                {
                    Debug.LogWarning($"[PatcherHub] Could not load prefab at path: {patchedPrefabPaths[i]}");
                }
            }
            
            // Center camera on the grid
            if (SceneView.lastActiveSceneView != null)
            {
                Vector3 centerPosition = new Vector3((columns - 1) * spacing * 0.5f, 1f, (patchedPrefabPaths.Count / columns) * spacing * 0.5f);
                SceneView.lastActiveSceneView.LookAt(centerPosition);
            }
            
            // Auto-save scene to !Pawlygon/Scenes folder
            string scenesFolder = "Assets/!Pawlygon/Scenes";
            
            // Create directory if it doesn't exist
            if (!System.IO.Directory.Exists(scenesFolder))
            {
                System.IO.Directory.CreateDirectory(scenesFolder);
                AssetDatabase.Refresh();
            }
            
            // Get avatar display names from the results
            List<string> avatarNames = new List<string>();
            foreach (var result in bulkPatchResults.Where(r => r.result == PatchResult.Success))
            {
                if (!string.IsNullOrEmpty(result.config.avatarDisplayName))
                {
                    avatarNames.Add(result.config.avatarDisplayName);
                }
            }
            
            // Generate scene name based on patched avatars
            string sceneName;
            if (avatarNames.Count == 1)
            {
                // Single avatar: {AvatarDisplayName} - Pawlygon VRCFT
                sceneName = $"{avatarNames[0]} - Pawlygon VRCFT";
            }
            else
            {
                // Multiple avatars: Extract common prefix (first word)
                string commonPrefix = GetCommonAvatarPrefix(avatarNames);
                sceneName = $"{commonPrefix} - Pawlygon VRCFT";
            }
            
            string scenePath = $"{scenesFolder}/{sceneName}.unity";
            
            // If file exists, append number to avoid overwriting
            int counter = 1;
            while (System.IO.File.Exists(scenePath))
            {
                scenePath = $"{scenesFolder}/{sceneName} ({counter}).unity";
                counter++;
            }
            
            EditorSceneManager.SaveScene(newScene, scenePath);
            Debug.Log($"[PatcherHub] Created test scene at: {scenePath}");
            
            // Ask if user wants to open the scene
            if (EditorUtility.DisplayDialog(
                "Scene Created", 
                $"Scene created:\n{scenePath}\n\nWould you like to open it now?", 
                "Open Scene",
                "Later"))
            {
                // Use delayCall to avoid GUI layout errors when switching scenes
                string scenePathToOpen = scenePath;
                EditorApplication.delayCall += () =>
                {
                    EditorSceneManager.OpenScene(scenePathToOpen, OpenSceneMode.Single);
                };
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PatcherHub] Failed to create scene with patched prefabs: {ex.Message}");
            EditorUtility.DisplayDialog("Error", $"Failed to create scene: {ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Sorts configs by dependency order - configs with dependencies come after their dependencies.
    /// Uses topological sort to handle dependency chains.
    /// </summary>
    private List<FTPatchConfig> SortConfigsByDependency(List<FTPatchConfig> configs)
    {
        var sorted = new List<FTPatchConfig>();
        var visited = new HashSet<FTPatchConfig>();
        var visiting = new HashSet<FTPatchConfig>();
        
        foreach (var config in configs)
        {
            if (!visited.Contains(config))
            {
                VisitConfig(config, configs, visited, visiting, sorted);
            }
        }
        
        return sorted;
    }
    
    /// <summary>
    /// Recursive helper for topological sort of config dependencies.
    /// </summary>
    private void VisitConfig(FTPatchConfig config, List<FTPatchConfig> allConfigs, 
        HashSet<FTPatchConfig> visited, HashSet<FTPatchConfig> visiting, List<FTPatchConfig> sorted)
    {
        if (visited.Contains(config)) return;
        
        if (visiting.Contains(config))
        {
            // Circular dependency detected - already handled in validation
            Debug.LogWarning($"[PatcherHub] Circular dependency detected for '{config.avatarDisplayName}'");
            return;
        }
        
        visiting.Add(config);
        
        // Visit dependency first (if it exists and is in the selected list)
        if (config.requiredDependency != null && allConfigs.Contains(config.requiredDependency))
        {
            VisitConfig(config.requiredDependency, allConfigs, visited, visiting, sorted);
        }
        
        visiting.Remove(config);
        visited.Add(config);
        sorted.Add(config);
    }

    private string GetCommonAvatarPrefix(List<string> avatarNames)
    {
        if (avatarNames == null || avatarNames.Count == 0)
            return "PatchedAvatars";
        
        // Get first words from all avatar names
        var firstWords = avatarNames.Select(name =>
        {
            // Split by space and take first part
            var parts = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0] : name;
        }).ToList();
        
        // Check if all first words are the same
        var uniqueFirstWords = firstWords.Distinct().ToList();
        if (uniqueFirstWords.Count == 1)
        {
            // All avatars share the same first word
            return uniqueFirstWords[0];
        }
        
        // Different prefixes - use generic name with count
        return $"{avatarNames.Count} Avatars";
    }

    /// <summary>
    /// Loads package rules and initiates package validation for VRChat dependencies.
    /// </summary>
    private void LoadPackageRules()
    {
        versionErrors = new List<VersionError>();
        
        // Reset validation state
        ClearLoadingStates();
        packageStatusCache.Clear();
        requirementsChecked = false;
        
        // Find package rules asset
        packageRules = AssetDatabase.FindAssets($"t:{nameof(PackageRules)}")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<PackageRules>)
            .FirstOrDefault();

        // Verify package requirements exist (global or any selected configuration)
        var hasGlobalRules = packageRules?.packageRequirements?.Count > 0;
        var hasAnyConfigRules = false;
        
        foreach (int index in selectedConfigIndices)
        {
            if (index < patchConfigs.Count && patchConfigs[index].configSpecificPackages?.Count > 0)
            {
                hasAnyConfigRules = true;
                break;
            }
        }

        if (!hasGlobalRules && !hasAnyConfigRules)
        {
            requirementsChecked = true;
            return;
        }

        // Start package validation using Unity's Package Manager API
        var listRequest = UnityEditor.PackageManager.Client.List(true);
        _packageValidationCallback = () => CheckPackageValidation(listRequest);
        EditorApplication.update += _packageValidationCallback;
    }

    /// <summary>
    /// Validates installed packages against requirements and populates version errors.
    /// </summary>
    private void CheckPackageValidation(ListRequest request)
    {
        if (!request.IsCompleted) return;
        
        // Remove callback to prevent multiple executions
        if (_packageValidationCallback != null)
        {
            EditorApplication.update -= _packageValidationCallback;
            _packageValidationCallback = null;
        }

        // Verify window is still valid
        lock (_windowsLock)
        {
            if (!_openWindows.Contains(this) || _openWindows.Count == 0)
            {
                return;
            }
        }

        // Skip if already validated
        if (requirementsChecked)
        {
            return;
        }

        versionErrors = new List<VersionError>();
        configSpecificErrors = new Dictionary<string, List<VersionError>>();

        // First, check global requirements
        if (packageRules?.packageRequirements != null)
        {
            foreach (var req in packageRules.packageRequirements)
            {
                var found = request.Result.FirstOrDefault(p => p.name == req.packageName);
                bool missing = found == null;
                bool badVersion = !missing && !CompareVersions(found.version, req.minVersion);

                if (missing)
                {
                    var error = new VersionError
                    {
                        message = req.missingError.message,
                        messageType = req.missingError.messageType,
                        vccURL = req.vccURL,
                        packageName = req.packageName,
                        isMissingPackage = true
                    };
                    versionErrors.Add(error);
                }
                else if (badVersion)
                {
                    var error = new VersionError
                    {
                        message = req.versionError.message,
                        messageType = req.versionError.messageType,
                        vccURL = req.vccURL,
                        packageName = req.packageName,
                        isMissingPackage = false
                    };
                    versionErrors.Add(error);
                }
            }
        }
        
        // Then check config-specific requirements for each selected config
        foreach (int index in selectedConfigIndices)
        {
            if (index < patchConfigs.Count)
            {
                var config = patchConfigs[index];
                if (config.configSpecificPackages != null && config.configSpecificPackages.Count > 0)
                {
                    var configErrors = new List<VersionError>();
                    
                    foreach (var req in config.configSpecificPackages)
                    {
                        var found = request.Result.FirstOrDefault(p => p.name == req.packageName);
                        bool missing = found == null;
                        bool badVersion = !missing && !CompareVersions(found.version, req.minVersion);

                        if (missing)
                        {
                            var error = new VersionError
                            {
                                message = req.missingError.message,
                                messageType = req.missingError.messageType,
                                vccURL = req.vccURL,
                                packageName = req.packageName,
                                isMissingPackage = true
                            };
                            configErrors.Add(error);
                        }
                        else if (badVersion)
                        {
                            var error = new VersionError
                            {
                                message = req.versionError.message,
                                messageType = req.versionError.messageType,
                                vccURL = req.vccURL,
                                packageName = req.packageName,
                                isMissingPackage = false
                            };
                            configErrors.Add(error);
                        }
                    }
                    
                    if (configErrors.Count > 0)
                    {
                        configSpecificErrors[config.avatarDisplayName] = configErrors;
                    }
                }
            }
        }

        // Collect all errors for VCC availability checking
        var allErrors = new List<VersionError>(versionErrors);
        foreach (var configErrorList in configSpecificErrors.Values)
        {
            allErrors.AddRange(configErrorList);
        }

        // Check package availability via VCC API if available
        if (vccAvailable ?? false)
        {
            var packagesToCheck = allErrors
                .Where(error => !string.IsNullOrEmpty(error.packageName) && 
                               !packageStatusCache.ContainsKey(error.packageName))
                .Select(error => error.packageName)
                .Distinct()
                .ToList();

            if (packagesToCheck.Count > 0)
            {
                StartBatchPackageAvailabilityCheck(packagesToCheck);
            }
        }

        requirementsChecked = true;
        Repaint();
    }

    /// <summary>
    /// Initiates batch package availability checking using VCC integration.
    /// </summary>
    /// <param name="packagesToCheck">List of package names to check for VCC availability</param>
    private void StartBatchPackageAvailabilityCheck(List<string> packagesToCheck)
    {
        // Prevent concurrent batch operations
        if (isCheckingPackageAvailability)
        {
            return;
        }
        
        isCheckingPackageAvailability = true;
        totalPackagesToCheck = packagesToCheck.Count;
        packagesBeingChecked = 0;
        
        // Initialize loading states for UI feedback
        foreach (var packageName in packagesToCheck)
        {
            packageLoadingStates[packageName] = true;
        }
        
        // Start efficient batch availability checking
        VCCIntegration.CheckMultiplePackageAvailability(
            packagesToCheck,
            // Progress callback
            (current, total, packageName, isAvailable) =>
            {
                if (this == null) return;
                
                packagesBeingChecked = current;
                packageStatusCache[packageName] = isAvailable ? PackageStatus.Available : PackageStatus.NotInRepository;
                packageLoadingStates[packageName] = false;
                
                Repaint();
            },
            // Completion callback
            (results) =>
            {
                if (this == null) return;
                
                isCheckingPackageAvailability = false;
                packageLoadingStates.Clear();
                
                Repaint();
            }
        );
        
        Repaint();
    }

    /// <summary>
    /// Triggers VCC package availability check if VCC is available and there are unchecked packages with errors.
    /// Called after VCC availability is confirmed.
    /// </summary>
    private void TriggerVCCPackageAvailabilityCheckIfNeeded()
    {
        // Collect all errors
        var allErrors = new List<VersionError>();
        
        if (versionErrors != null)
        {
            allErrors.AddRange(versionErrors);
        }
        
        if (configSpecificErrors != null)
        {
            foreach (var configErrorList in configSpecificErrors.Values)
            {
                allErrors.AddRange(configErrorList);
            }
        }
        
        if (allErrors.Count == 0) return;
        
        // Find packages that need to be checked
        var packagesToCheck = allErrors
            .Where(error => !string.IsNullOrEmpty(error.packageName) && 
                           !packageStatusCache.ContainsKey(error.packageName))
            .Select(error => error.packageName)
            .Distinct()
            .ToList();

        if (packagesToCheck.Count > 0)
        {
            StartBatchPackageAvailabilityCheck(packagesToCheck);
        }
    }

    /// <summary>
    /// Compares version strings to determine if installed version meets requirements.
    /// </summary>
    /// <param name="installed">Currently installed version</param>
    /// <param name="required">Required minimum version</param>
    /// <returns>True if installed version meets or exceeds requirements</returns>
    private bool CompareVersions(string installed, string required)
    {
        if (string.IsNullOrEmpty(required) || required == "Any") 
            return true;
            
        try
        {
            return new Version(installed) >= new Version(required);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Attempts to install/update all VCC-manageable packages in bulk
    /// </summary>
    /// <param name="vccManageablePackages">List of version errors for packages that can be managed via VCC</param>
    private void TryBulkInstallAllPackages(List<VersionError> vccManageablePackages)
    {
        if (vccManageablePackages.Count == 0) return;
        
        var packageNames = vccManageablePackages.Select(error => error.packageName).ToList();
        var missingCount = vccManageablePackages.Count(error => error.isMissingPackage);
        var updateCount = vccManageablePackages.Count(error => !error.isMissingPackage);
        
        string confirmationMessage = "This will attempt to automatically install and update the following packages via VCC:\n\n";
        
        if (missingCount > 0)
            confirmationMessage += $"📦 Install {missingCount} missing package(s)\n";
        if (updateCount > 0)
            confirmationMessage += $"🔄 Update {updateCount} outdated package(s)\n";
        
        confirmationMessage += "\nPackages:\n" + string.Join("\n", packageNames.Select(name => $"• {name}"));
        confirmationMessage += "\n\nMake sure VRChat Creator Companion is running and this project is managed by VCC.\n\nProceed with bulk installation?";
        
        bool userConfirmed = EditorUtility.DisplayDialog(
            "Bulk Install/Update Packages",
            confirmationMessage,
            "Yes, Install All",
            "Cancel"
        );
        
        if (!userConfirmed) return;
        
        // Start bulk installation
        _ = TryBulkInstallAllPackagesAsync(vccManageablePackages);
    }

    /// <summary>
    /// Asynchronously installs/updates all packages in bulk
    /// </summary>
    private async System.Threading.Tasks.Task TryBulkInstallAllPackagesAsync(List<VersionError> vccManageablePackages)
    {
        isBulkInstalling = true;
        
        // Set all packages to installing state
        EditorApplication.delayCall += () =>
        {
            if (this != null)
            {
                foreach (var error in vccManageablePackages)
                {
                    packageInstallingStates[error.packageName] = true;
                }
                Repaint();
            }
        };
        
        var results = new List<(string packageName, bool success, string error)>();
        
        try
        {
            // Install packages sequentially to avoid overwhelming VCC
            foreach (var error in vccManageablePackages)
            {
                try
                {
                    var vccResult = await System.Threading.Tasks.Task.Run(() => VCCIntegration.TryInstallPackageViaAPI(error.packageName));
                    results.Add((error.packageName, vccResult.Success, vccResult.Error));
                    
                    // Brief delay between installations to prevent VCC overload
                    await System.Threading.Tasks.Task.Delay(PatcherHubConstants.PACKAGE_INSTALL_DELAY_MS);
                }
                catch (System.Exception ex)
                {
                    results.Add((error.packageName, false, ex.Message));
                }
            }
            
            // Update UI on main thread
            EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                
                // Clear all installing states
                foreach (var error in vccManageablePackages)
                {
                    packageInstallingStates[error.packageName] = false;
                }
                isBulkInstalling = false;
                Repaint();
                
                // Show results
                ShowBulkInstallResults(results);
                
                // Force package refresh
                ForceUnityPackageRefresh();
                
                // Refresh package validation after delay
                EditorApplication.delayCall += () =>
                {
                    if (this != null)
                    {
                        System.Threading.Tasks.Task.Delay(PatcherHubConstants.PACKAGE_REFRESH_DELAY_MS).ContinueWith(_ =>
                        {
                            EditorApplication.delayCall += () =>
                            {
                                if (this != null)
                                {
                                    // Refresh validation after package installation
                                    RefreshPackageValidation(forceUnityRefresh: true);
                                }
                            };
                        });
                    }
                };
            };
        }
        catch (System.Exception ex)
        {
            // Handle overall failure
            EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                
                // Clear all installing states
                foreach (var error in vccManageablePackages)
                {
                    packageInstallingStates[error.packageName] = false;
                }
                isBulkInstalling = false;
                Repaint();
                
                EditorUtility.DisplayDialog(
                    "Bulk Installation Failed",
                    $"Bulk package installation failed with error:\n\n{ex.Message}\n\n" +
                    "Please try installing packages individually or check that VCC is running properly.",
                    "OK"
                );
            };
        }
    }

    /// <summary>
    /// Shows the results of bulk package installation
    /// </summary>
    private void ShowBulkInstallResults(List<(string packageName, bool success, string error)> results)
    {
        var successful = results.Where(r => r.success).ToList();
        var failed = results.Where(r => !r.success).ToList();
        
        string message = $"Bulk Package Installation Complete!\n\n";
        
        if (successful.Count > 0)
        {
            message += $"✅ Successfully installed/updated {successful.Count} package(s):\n";
            message += string.Join("\n", successful.Select(r => $"• {r.packageName}"));
            message += "\n\n";
        }
        
        if (failed.Count > 0)
        {
            message += $"❌ Failed to install {failed.Count} package(s):\n";
            message += string.Join("\n", failed.Select(r => $"• {r.packageName}"));
            message += "\n\n";
        }
        
        message += "Unity Package Manager is refreshing to detect new packages...";
        
        if (failed.Count == 0)
        {
            EditorUtility.DisplayDialog("Bulk Installation Successful", message, "OK");
        }
        else
        {
            bool showDetails = EditorUtility.DisplayDialog(
                "Bulk Installation Completed with Issues", 
                message + "\n\nWould you like to see error details?", 
                "Show Details", 
                "OK"
            );
            
            if (showDetails)
            {
                string errorDetails = "Error Details:\n\n";
                errorDetails += string.Join("\n\n", failed.Select(r => $"{r.packageName}:\n{r.error}"));
                
                EditorUtility.DisplayDialog("Installation Error Details", errorDetails, "OK");
            }
        }
    }

    /// <summary>
    /// Asynchronously installs a package via VCC API without blocking Unity
    /// </summary>
    private async System.Threading.Tasks.Task TryInstallPackageViaVCCAsync(string packageName, bool isMissingPackage)
    {
        string action = isMissingPackage ? "install" : "update";
        
        // Set installing state for UI feedback
        EditorApplication.delayCall += () =>
        {
            if (this != null)
            {
                packageInstallingStates[packageName] = true;
                Repaint();
            }
        };
        
        try
        {
            // Run VCC API call in background thread
            var vccResult = await System.Threading.Tasks.Task.Run(() => VCCIntegration.TryInstallPackageViaAPI(packageName));
            
            // Update UI on main thread
            EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                
                // Clear installing state
                packageInstallingStates[packageName] = false;
                Repaint();
                
                if (vccResult.Success)
                {
                    EditorUtility.DisplayDialog(
                        $"Package {action} Successful",
                        $"Package '{packageName}' {action} completed successfully via VCC.\n\n" +
                        "Unity Package Manager is refreshing to detect the new package...",
                        "OK"
                    );
                    
                    // Force Unity to refresh its package database
                    ForceUnityPackageRefresh();
                    
                    // Refresh package validation after allowing time for Unity PM to update
                    EditorApplication.delayCall += () =>
                    {
                        if (this != null)
                        {
                            System.Threading.Tasks.Task.Delay(PatcherHubConstants.PACKAGE_REFRESH_DELAY_MS).ContinueWith(_ =>
                            {
                                EditorApplication.delayCall += () =>
                                {
                                    if (this != null) 
                                    {
                                        // Force complete refresh after package installation
                                        RefreshPackageValidation(forceUnityRefresh: true);
                                    }
                                };
                            });
                        }
                    };
                }
                else
                {
                    HandlePackageInstallError(packageName, action, vccResult);
                }
            };
        }
        catch (System.Exception ex)
        {
            // Handle any async exceptions on main thread
            EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                
                // Clear installing state
                packageInstallingStates[packageName] = false;
                Repaint();
                
                EditorUtility.DisplayDialog(
                    $"Auto-{action} Failed",
                    $"Failed to {action} package '{packageName}' via VCC.\n\n" +
                    $"Error: {ex.Message}\n\n" +
                    "Please try using the 'View in VCC' button instead, or check that VCC is running and this project is managed by VCC.",
                    "OK"
                );
            };
        }
    }

    /// <summary>
    /// Handles errors that occur during package installation attempts.
    /// </summary>
    /// <param name="packageName">Name of the package that failed to install</param>
    /// <param name="action">Action being performed (install/update)</param>
    /// <param name="vccResult">Result from the VCC API operation</param>
    private void HandlePackageInstallError(string packageName, string action, VCCIntegration.VPMResult vccResult)
    {
        string errorDetails = "";
        string additionalHelp = "";
        
        // Parse common VCC error messages
        if (!string.IsNullOrEmpty(vccResult.Error))
        {
            if (vccResult.Error.Contains("Could not get VCC project ID"))
            {
                errorDetails = "This project is not managed by VRChat Creator Companion.";
                additionalHelp = "\n\nTo fix this:\n" +
                               "1. Open VRChat Creator Companion\n" +
                               "2. Add this project to VCC\n" +
                               "3. Make sure VCC is running\n" +
                               "4. Try the installation again\n\n" +
                               "For help with VCC, visit:\nhttps://vcc.docs.vrchat.com/";
            }
            else if (vccResult.Error.Contains("VCC API request failed"))
            {
                errorDetails = "VCC API request failed. VCC might not be running or accessible.";
                additionalHelp = "\n\nTroubleshooting:\n" +
                               "1. Make sure VRChat Creator Companion is running\n" +
                               "2. Check that this project is added to VCC\n" +
                               "3. Restart VCC if needed\n" +
                               "4. Try again after VCC is fully loaded";
            }
            else
            {
                errorDetails = $"VCC Error:\n{vccResult.Error}";
            }
        }
        else if (!string.IsNullOrEmpty(vccResult.Output))
        {
            errorDetails = $"VCC Output:\n{vccResult.Output}";
        }
        else
        {
            errorDetails = "VCC operation failed with no specific error message.";
        }
        
        // Show appropriate error dialog
        if (!string.IsNullOrEmpty(additionalHelp))
        {
            bool openDocs = EditorUtility.DisplayDialog(
                $"Auto-{action} Failed - VCC Issue",
                $"Failed to {action} package '{packageName}' via VCC.\n\n" +
                errorDetails + additionalHelp + "\n\n" +
                "Would you like to open the VRChat Creator Companion documentation?",
                "Open VCC Docs",
                "Cancel"
            );
            
            if (openDocs)
            {
                Application.OpenURL("https://vcc.docs.vrchat.com/");
            }
        }
        else
        {
            EditorUtility.DisplayDialog(
                $"Auto-{action} Failed",
                $"Failed to {action} package '{packageName}' via VCC.\n\n" +
                errorDetails + "\n\n" +
                "Please try using the 'View in VCC' button instead, or check that VCC is running and this project is managed by VCC.",
                "OK"
            );
        }
    }

    /// <summary>
    /// Forces Unity to refresh its Package Manager database to detect newly installed packages
    /// </summary>
    private void ForceUnityPackageRefresh()
    {
        try
        {
            // Force Package Manager to resolve and refresh
            UnityEditor.PackageManager.Client.Resolve();
            
            // Refresh asset database
            AssetDatabase.Refresh();
            
            // Force compilation refresh if needed
            UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
        }
        catch (System.Exception)
        {
            // Package refresh may fail occasionally - not critical for operation
        }
    }

    /// <summary>
    /// Refreshes VCC availability and updates the UI accordingly.
    /// Called when VCC is launched to check if it's now available.
    /// </summary>
    private void RefreshVCCAvailability()
    {
        UnityEngine.Debug.Log("Refreshing VCC availability after launch...");
        
        bool wasAvailable = vccAvailable ?? false;
        
        // Reset VCC availability to trigger a fresh check
        vccAvailable = null;
        
        // Start VCC availability check asynchronously
        _ = CheckVCCAvailabilityInBackground();
        
        // Clear package caches to force fresh checks
        packageStatusCache.Clear();
        packageLoadingStates.Clear();
        
        Debug.Log($"[PatcherHub] VCC availability refresh triggered. Cleared package cache.");
    }

    /// <summary>
    /// Refreshes package availability for all missing packages
    /// </summary>
    private void RefreshAllPackageAvailability()
    {
        if (isCheckingPackageAvailability)
        {
            Debug.LogWarning("[PatcherHub] Package availability check already in progress.");
            return;
        }

        if (isBulkInstalling)
        {
            Debug.LogWarning("[PatcherHub] Cannot refresh during bulk installation.");
            return;
        }

        if (versionErrors == null || versionErrors.Count == 0)
        {
            Debug.Log("[PatcherHub] No version errors to refresh.");
            return;
        }

        Debug.Log("[PatcherHub] Refreshing package availability for all missing packages...");

        // Clear the package status cache to force fresh checks
        packageStatusCache.Clear();
        packageLoadingStates.Clear();

        // Get all packages that need checking
        var packagesToCheck = versionErrors
            .Where(error => !string.IsNullOrEmpty(error.packageName))
            .Select(error => error.packageName)
            .Distinct()
            .ToList();

        if (packagesToCheck.Count == 0)
        {
            Debug.Log("[PatcherHub] No packages to refresh.");
            return;
        }

        // Trigger bulk availability check
        isCheckingPackageAvailability = true;
        totalPackagesToCheck = packagesToCheck.Count;
        packagesBeingChecked = 0;

        Debug.Log($"[PatcherHub] Starting bulk package availability check for {totalPackagesToCheck} packages.");

        VCCIntegration.CheckMultiplePackageAvailability(
            packagesToCheck,
            onProgress: (current, total, packageId, isAvailable) =>
            {
                // Progress callback
                packagesBeingChecked = current;
                packageStatusCache[packageId] = isAvailable ? PackageStatus.Available : PackageStatus.NotInRepository;
                packageLoadingStates[packageId] = false;
                
                Debug.Log($"[PatcherHub] Package availability check progress: {current}/{total} - {packageId}: {isAvailable}");
                Repaint();
            },
            onComplete: (results) =>
            {
                // Completion callback
                isCheckingPackageAvailability = false;
                packagesBeingChecked = 0;
                totalPackagesToCheck = 0;

                foreach (var result in results)
                {
                    packageStatusCache[result.Key] = result.Value ? PackageStatus.Available : PackageStatus.NotInRepository;
                    packageLoadingStates[result.Key] = false;
                }

                Debug.Log($"[PatcherHub] Package availability refresh complete. Found {results.Count(r => r.Value)} available packages out of {results.Count} total.");
                
                // Re-trigger package validation to update version errors and UI
                requirementsChecked = false;
                
                // Restart package validation using Unity's Package Manager API (same as in LoadPackageRules)
                var listRequest = UnityEditor.PackageManager.Client.List(true);
                _packageValidationCallback = () => CheckPackageValidation(listRequest);
                EditorApplication.update += _packageValidationCallback;
                
                Repaint();
            }
        );

        // Mark all packages as loading
        foreach (var packageId in packagesToCheck)
        {
            packageLoadingStates[packageId] = true;
        }

        Repaint();
    }

    /// <summary>
    /// Refreshes package validation by clearing cache and reloading rules.
    /// Use forceUnityRefresh=true only when packages have been installed/removed.
    /// </summary>
    private void RefreshPackageValidation(bool forceUnityRefresh = false)
    {
        requirementsChecked = false;
        versionErrors?.Clear();
        configSpecificErrors?.Clear();
        packageStatusCache.Clear();
        
        // Only force Unity Package Manager refresh when explicitly needed
        if (forceUnityRefresh)
        {
            ForceUnityPackageRefresh();
        }
        
        LoadPackageRules();
        
        // Restart VCC check in background
        _ = CheckVCCAvailabilityInBackground();
    }

    /// <summary>
    /// Checks if a package is available via VCC API (now with proper repository checking)
    /// </summary>
    /// <param name="packageName">The package name to check</param>
    /// <returns>True if package is available via VCC API</returns>
    private bool IsPackageAvailableViaVCC(string packageName)
    {        
        // If VCC is not available, return false
        if (!(vccAvailable ?? false))
        {
            return false;
        }

        // Prevent UI interactions during loading or bulk operations
        if ((packageLoadingStates.ContainsKey(packageName) && packageLoadingStates[packageName]) || isBulkInstalling)
        {
            return false;
        }

        // Check cache first
        if (packageStatusCache.TryGetValue(packageName, out PackageStatus cachedStatus))
        {
            return IsPackageStatusAvailable(cachedStatus);
        }

        // If not cached, trigger async check but return false for now
        // The UI will update once the async check completes
        TriggerPackageAvailabilityCheck(packageName);
        return false;
    }

    /// <summary>
    /// Helper method to determine if a PackageStatus indicates availability
    /// </summary>
    private bool IsPackageStatusAvailable(PackageStatus status)
    {
        return status == PackageStatus.Available || status == PackageStatus.Installed;
    }

    /// <summary>
    /// Triggers an asynchronous package availability check
    /// </summary>
    private void TriggerPackageAvailabilityCheck(string packageName)
    {
        if (packageLoadingStates.ContainsKey(packageName) && packageLoadingStates[packageName])
        {
            return; // Already checking
        }

        packageLoadingStates[packageName] = true;

        // Trigger async check on background thread
        EditorApplication.delayCall += () =>
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    bool isAvailable = VCCIntegration.CheckPackageAvailability(packageName);

                    // Update cache and UI on main thread
                    EditorApplication.delayCall += () =>
                    {
                        packageStatusCache[packageName] = isAvailable ? PackageStatus.Available : PackageStatus.NotInRepository;
                        packageLoadingStates[packageName] = false;
                        Repaint();
                    };
                }
                catch
                {
                    // Handle error on main thread
                    EditorApplication.delayCall += () =>
                    {
                        packageStatusCache[packageName] = PackageStatus.NotInRepository;
                        packageLoadingStates[packageName] = false;
                        Repaint();
                    };
                }
            });
        };
    }

    /// <summary>
    /// Checks if a package is currently being loaded
    /// </summary>
    /// <param name="packageName">The package name to check</param>
    /// <returns>True if package availability is currently being checked</returns>
    private bool IsPackageLoadingVCC(string packageName)
    {
        if (!(vccAvailable ?? false))
        {
            return false;
        }

        return packageLoadingStates.ContainsKey(packageName) && packageLoadingStates[packageName];
    }

    #endregion

    #region Diff Validation

    /// <summary>
    /// Validates source files for all loaded patch configurations by comparing
    /// stored MD5 hashes with the actual file hashes on disk.
    /// </summary>
    private void ValidateAllDiffFiles()
    {
        diffValidationResults.Clear();

        foreach (var config in patchConfigs)
        {
            if (config == null) continue;
            var result = ValidateDiffForConfig(config);
            diffValidationResults[config.avatarDisplayName] = result;
        }
    }

    /// <summary>
    /// Validates source files for a single patch configuration by comparing MD5 hashes.
    /// </summary>
    /// <param name="config">The patch configuration to validate</param>
    /// <returns>Validation result containing status for both FBX and meta files</returns>
    private DiffValidationResult ValidateDiffForConfig(FTPatchConfig config)
    {
        var result = new DiffValidationResult();
        result.avatarName = config.avatarDisplayName;

        // If diff files are missing, skip validation (IsValidForPatching will catch this)
        if (config.fbxDiffFile == null || config.metaDiffFile == null)
        {
            return result;
        }

        // Check if original model prefab exists
        if (config.originalModelPrefab == null)
        {
            result.fbxStatus = DiffValidationStatus.SourceNotFound;
            result.fbxMessage = string.Format(PatcherHubConstants.DIFF_SOURCE_NOT_FOUND, config.avatarDisplayName);
            result.metaStatus = DiffValidationStatus.SourceNotFound;
            result.metaMessage = null; // Only show the message once
            return result;
        }

        string originalFbxPath = AssetDatabase.GetAssetPath(config.originalModelPrefab);
        string fullFbxPath = Path.GetFullPath(originalFbxPath);
        string fullMetaPath = fullFbxPath + ".meta";

        // Check if source files exist on disk
        if (!File.Exists(fullFbxPath))
        {
            string modelName = Path.GetFileNameWithoutExtension(originalFbxPath);
            result.fbxStatus = DiffValidationStatus.SourceNotFound;
            result.fbxMessage = string.Format(PatcherHubConstants.DIFF_SOURCE_NOT_FOUND,
                string.IsNullOrEmpty(modelName) ? config.avatarDisplayName : modelName);
            result.metaStatus = DiffValidationStatus.SourceNotFound;
            result.metaMessage = null;
            return result;
        }

        // Check if hashes are stored in the config
        if (!config.HasHashes)
        {
            result.fbxStatus = DiffValidationStatus.NoHashStored;
            result.fbxMessage = string.Format(PatcherHubConstants.DIFF_NO_HASH, config.avatarDisplayName);
            result.metaStatus = DiffValidationStatus.NoHashStored;
            result.metaMessage = null; // Only show the message once
            return result;
        }

        // Validate FBX hash
        ValidateSingleHash(fullFbxPath, config.expectedFbxHash, "FBX",
            out result.fbxStatus, out result.fbxMessage);

        // Validate meta hash
        ValidateSingleHash(fullMetaPath, config.expectedMetaHash, "meta",
            out result.metaStatus, out result.metaMessage);

        return result;
    }

    /// <summary>
    /// Validates a single source file by comparing its MD5 hash with the expected hash.
    /// </summary>
    /// <param name="filePath">Full path to the source file</param>
    /// <param name="expectedHash">Expected MD5 hash from the config</param>
    /// <param name="label">Label for error messages (e.g., "FBX" or "meta")</param>
    /// <param name="status">Output validation status</param>
    /// <param name="message">Output message (null if valid)</param>
    private void ValidateSingleHash(string filePath, string expectedHash, string label,
        out DiffValidationStatus status, out string message)
    {
        if (string.IsNullOrEmpty(expectedHash))
        {
            status = DiffValidationStatus.NoHashStored;
            message = string.Format(PatcherHubConstants.DIFF_NO_HASH, label);
            return;
        }

        if (!File.Exists(filePath))
        {
            status = DiffValidationStatus.SourceNotFound;
            message = string.Format(PatcherHubConstants.DIFF_SOURCE_NOT_FOUND,
                Path.GetFileNameWithoutExtension(filePath));
            return;
        }

        string actualHash = FTPatchConfig.ComputeMD5(filePath);

        if (actualHash == null)
        {
            status = DiffValidationStatus.NotChecked;
            message = string.Format(PatcherHubConstants.DIFF_EXEC_ERROR, label, "Could not compute file hash");
            return;
        }

        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            status = DiffValidationStatus.HashMismatch;
            message = string.Format(PatcherHubConstants.DIFF_HASH_MISMATCH, label);
            return;
        }

        status = DiffValidationStatus.Valid;
        message = null;
    }

    #endregion

    #region Patch Operations

    /// <summary>
    /// Applies face tracking patches to all selected valid configurations sequentially.
    /// </summary>
    private void ApplyPatchSelected()
    {
        bulkPatchResults.Clear();
        patchedPrefabPaths.Clear();
        
        // Get list of selected configs
        var selectedConfigs = new List<FTPatchConfig>();
        foreach (int index in selectedConfigIndices)
        {
            if (index < patchConfigs.Count)
            {
                selectedConfigs.Add(patchConfigs[index]);
            }
        }
        
        // Sort configs by dependency order (dependencies first)
        selectedConfigs = SortConfigsByDependency(selectedConfigs);
        
        int totalConfigs = selectedConfigs.Count;
        int processedCount = 0;
        
        foreach (var config in selectedConfigs)
        {
            processedCount++;
            
            if (!config.IsValidForPatching())
            {
                bulkPatchResults.Add((config, PatchResult.InvalidFBXPath));
                continue;
            }
            
            // Check if dependency is satisfied (if it has one)
            if (config.requiredDependency != null && !config.IsDependencySatisfied())
            {
                // Check if dependency is in the selected list
                if (!selectedConfigs.Contains(config.requiredDependency))
                {
                    Debug.LogWarning($"[PatcherHub] Skipping '{config.avatarDisplayName}' - required dependency '{config.requiredDependency.avatarDisplayName}' is not selected or patched.");
                    bulkPatchResults.Add((config, PatchResult.MissingDiffFiles)); // Using this as "dependency not satisfied" for now
                    continue;
                }
            }
            
            // Show progress
            EditorUtility.DisplayProgressBar(
                "Patching Avatars",
                $"Patching {config.avatarDisplayName} ({processedCount}/{totalConfigs})...",
                (float)processedCount / totalConfigs
            );
            
            // Apply the patch
            ApplyPatch(config);
            bulkPatchResults.Add((config, currentPatchResult));
            
            // Track successfully patched prefabs
            if (currentPatchResult == PatchResult.Success && config.patchedPrefabs != null)
            {
                foreach (var prefab in config.patchedPrefabs)
                {
                    if (prefab != null)
                    {
                        string prefabPath = AssetDatabase.GetAssetPath(prefab);
                        if (!string.IsNullOrEmpty(prefabPath))
                        {
                            patchedPrefabPaths.Add(prefabPath);
                        }
                    }
                }
            }
        }
        
        EditorUtility.ClearProgressBar();
        
        // Show results and create scene if any patches succeeded
        ShowBulkPatchResults();
        
        if (patchedPrefabPaths.Count > 0)
        {
            promptToOpenScene = true;
        }
    }

    /// <summary>
    /// Shows the results of bulk patch operations in a dialog.
    /// </summary>
    private void ShowBulkPatchResults()
    {
        int successCount = bulkPatchResults.Count(r => r.result == PatchResult.Success);
        int failureCount = bulkPatchResults.Count - successCount;
        
        string message = $"Patch Complete:\n\n";
        message += $"✓ Successfully patched: {successCount}\n";
        
        if (failureCount > 0)
        {
            message += $"✗ Failed: {failureCount}\n\n";
            message += "Failed configurations:\n";
            
            foreach (var result in bulkPatchResults.Where(r => r.result != PatchResult.Success))
            {
                message += $"• {result.config.avatarDisplayName}: {GetPatchResultMessage(result.result)}\n";
            }
        }
        
        if (successCount > 0)
        {
            message += $"\n{successCount} patched prefab(s) will be loaded into a new scene.";
        }
        
        EditorUtility.DisplayDialog("Patch Results", message, "OK");
    }

    /// <summary>
    /// Gets a human-readable message for a patch result.
    /// </summary>
    private string GetPatchResultMessage(PatchResult result)
    {
        return result switch
        {
            PatchResult.Success => "Success",
            PatchResult.InvalidFBXPath => "Invalid configuration",
            PatchResult.MissingDiffFiles => "Missing diff files",
            PatchResult.MetaPatchFailed => "Meta patch failed",
            PatchResult.FbxPatchFailed => "FBX patch failed",
            _ => "Unknown error"
        };
    }

    /// <summary>
    /// Applies face tracking patches to the configured FBX model.
    /// </summary>
    /// <param name="config">Patch configuration containing model and patch details</param>
    private void ApplyPatch(FTPatchConfig config)
    {
        try
        {
            // Get diff file paths directly from the asset references
            string fbxDiffPath = config.GetFbxDiffPath();
            string metaDiffPath = config.GetMetaDiffPath();

            if (string.IsNullOrEmpty(fbxDiffPath) || string.IsNullOrEmpty(metaDiffPath))
            {
                currentPatchResult = PatchResult.MissingDiffFiles;
                return;
            }

            if (!File.Exists(fbxDiffPath) || !File.Exists(metaDiffPath))
            {
                currentPatchResult = PatchResult.MissingDiffFiles;
                return;
            }

            string outputFbxPath = config.GetExpectedFBXPath();
            string outputFolder = Path.GetDirectoryName(outputFbxPath);

            // Check if the specific FBX file already exists
            if (File.Exists(outputFbxPath))
            {
                if (!EditorUtility.DisplayDialog(
                    PatcherHubConstants.REPLACE_FOLDER_TITLE, 
                    $"The file '{Path.GetFileName(outputFbxPath)}' already exists. Do you want to replace it?", 
                    "Yes", 
                    "No"))
                {
                    return;
                }
                
                // Delete only the specific FBX and its meta file
                File.Delete(outputFbxPath);
                if (File.Exists(outputFbxPath + ".meta"))
                {
                    File.Delete(outputFbxPath + ".meta");
                }
                AssetDatabase.Refresh();
            }

            // Create output directory if it doesn't exist
            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
                AssetDatabase.Refresh();
            }

            string originalFbxPath = AssetDatabase.GetAssetPath(config.originalModelPrefab);
            string originalMetaPath = originalFbxPath + ".meta";
            string outputMetaPath = outputFbxPath + ".meta";

            string patchExe = GetPatcherExecutablePath();

#if !UNITY_EDITOR_WIN
            if (!SetExecutablePermission(patchExe))
            {
                currentPatchResult = PatchResult.FbxPatchFailed;
                return;
            }
#endif


            bool metaSuccess = ExecutePatch(patchExe, Path.GetFullPath(originalMetaPath), Path.GetFullPath(metaDiffPath), Path.GetFullPath(outputMetaPath));
            bool fbxSuccess = ExecutePatch(patchExe, Path.GetFullPath(originalFbxPath), Path.GetFullPath(fbxDiffPath), Path.GetFullPath(outputFbxPath));

            currentPatchResult = (!metaSuccess) ? PatchResult.MetaPatchFailed : (!fbxSuccess ? PatchResult.FbxPatchFailed : PatchResult.Success);
            if (currentPatchResult == PatchResult.Success) promptToOpenScene = true;

            AssetDatabase.Refresh();
        }
        catch (DirectoryNotFoundException) { currentPatchResult = PatchResult.MissingDiffFiles; }
        catch (Exception ex) { 
            Debug.LogError("Patch operation failed: " + ex.Message); 
            currentPatchResult = PatchResult.FbxPatchFailed; 
        }
    }

    /// <summary>
    /// Executes the hpatchz tool to apply a diff file to a source file.
    /// </summary>
    /// <param name="exePath">Path to the hpatchz executable</param>
    /// <param name="source">Path to the source file to be patched</param>
    /// <param name="diff">Path to the diff file containing the patches</param>
    /// <param name="output">Path where the patched file should be saved</param>
    /// <returns>True if patching succeeded, false otherwise</returns>
    private bool ExecutePatch(string exePath, string source, string diff, string output)
    {
        try
        {
            if (!File.Exists(exePath))
            {
                Debug.LogError($"Patch executable not found: {exePath}");
                return false;
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"\"{source}\" \"{diff}\" \"{output}\"",
                    WorkingDirectory = Path.GetDirectoryName(exePath),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string outputLog = process.StandardOutput.ReadToEnd();
            string errorLog = process.StandardError.ReadToEnd();
            process.WaitForExit();

            // Log any warnings/errors from the patching process
            if (!string.IsNullOrEmpty(errorLog)) 
                Debug.LogWarning("Patch process warning: " + errorLog);

            return File.Exists(output);
        }
        catch (Exception ex)
        {
            Debug.LogError("Patch execution failed: " + ex.Message);
            return false;
        }
    }
    
    /// <summary>
    /// Sets executable permissions for the patch tool on Unix-based systems (Mac/Linux).
    /// </summary>
    /// <param name="path">Path to the file that needs executable permissions</param>
    /// <returns>True if permission setting succeeded, false otherwise</returns>
    private bool SetExecutablePermission(string path)
    {
        try
        {
            var chmod = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/chmod",
                    Arguments = $"+x \"{path}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            
            chmod.Start();
            chmod.WaitForExit();
            return chmod.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the platform-specific path to the patch executable.
    /// </summary>
    /// <returns>Full path to the appropriate hpatchz executable</returns>
    private string GetPatcherExecutablePath()
    {
        string basePath = Path.Combine(Application.dataPath, PatcherHubConstants.PATCHER_BASE_PATH);
        
        return Application.platform switch
        {
            RuntimePlatform.WindowsEditor => Path.Combine(basePath, PatcherHubConstants.WINDOWS_PATCHER),
            RuntimePlatform.OSXEditor => Path.Combine(basePath, PatcherHubConstants.MAC_PATCHER),
            RuntimePlatform.LinuxEditor => Path.Combine(basePath, PatcherHubConstants.LINUX_PATCHER),
            _ => throw new PlatformNotSupportedException("Unsupported platform for patching")
        };
    }

    /// <summary>
    /// Determines the highest severity level across all package errors.
    /// </summary>
    private void GetPackageIssueSeverity(out bool hasErrors, out bool hasWarnings)
    {
        hasErrors = false;
        hasWarnings = false;
        
        if (versionErrors != null)
        {
            foreach (var error in versionErrors)
            {
                if (error.messageType == MessageType.Error) hasErrors = true;
                else if (error.messageType == MessageType.Warning) hasWarnings = true;
            }
        }
        
        if (configSpecificErrors != null)
        {
            foreach (var kvp in configSpecificErrors)
            {
                foreach (var error in kvp.Value)
                {
                    if (error.messageType == MessageType.Error) hasErrors = true;
                    else if (error.messageType == MessageType.Warning) hasWarnings = true;
                }
            }
        }
    }

    /// <summary>
    /// Gets an animated loading icon based on time
    /// </summary>
    /// <returns>String containing animated loading character</returns>
    private string GetLoadingIcon()
    {
        string[] loadingChars = { "|", "/", "-", "\\" };
        int index = (int)(EditorApplication.timeSinceStartup * 4) % loadingChars.Length;
        return loadingChars[index];
    }

    /// <summary>
    /// Checks VCC availability in background without blocking the UI
    /// </summary>
    private async System.Threading.Tasks.Task CheckVCCAvailabilityInBackground()
    {
        isCheckingVCCAvailability = true;
        Repaint();
        
        try
        {
            // Quick timeout to avoid hanging
            var timeoutTask = System.Threading.Tasks.Task.Delay(2000);
            var vccTask = System.Threading.Tasks.Task.Run(() => VCCIntegration.IsVCCAvailable());
            
            var completedTask = await System.Threading.Tasks.Task.WhenAny(vccTask, timeoutTask);
            
            bool isAvailable = false;
            if (completedTask == vccTask && vccTask.IsCompletedSuccessfully)
            {
                isAvailable = vccTask.Result;
            }
            
            // Update UI on main thread
            EditorApplication.delayCall += () =>
            {
                if (this != null)
                {
                    vccAvailable = isAvailable;
                    isCheckingVCCAvailability = false;
                    
                    // If VCC is available and we have errors but no package status cache, trigger VCC package check
                    if (isAvailable && requirementsChecked)
                    {
                        TriggerVCCPackageAvailabilityCheckIfNeeded();
                    }
                    
                    Repaint();
                }
            };
        }
        catch
        {
            // Handle VCC availability check failure gracefully
            EditorApplication.delayCall += () =>
            {
                if (this != null)
                {
                    vccAvailable = false;
                    isCheckingVCCAvailability = false;
                    Repaint();
                }
            };
        }
    }

    #endregion
    }
}
