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
        private int selectedConfigIndex = 0;
        private FTPatchConfig selectedConfig;
        private PackageRules packageRules;
        private List<VersionError> versionErrors;
        private bool requirementsChecked = false;

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
            selectedConfigIndex = Mathf.Clamp(selectedConfigIndex, 0, patchConfigs.Count - 1);
            selectedConfig = patchConfigs[selectedConfigIndex];
        }
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
        DrawSelectedConfigInfo();
        DrawPatchButton();
        DrawPatchResult(currentPatchResult);
        DrawVersionErrorsWithLinks(versionErrors, requirementsChecked);
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
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Selected Configuration:", styles?.BoldLabel ?? EditorStyles.boldLabel, GUILayout.Width(PatcherHubConstants.CONFIG_SELECTION_LABEL_WIDTH));

        if (patchConfigs.Count > 1)
        {
            string[] configNames = patchConfigs.ConvertAll(c => c.avatarDisplayName).ToArray();
            int newSelectedIndex = EditorGUILayout.Popup(selectedConfigIndex, configNames);
            if (newSelectedIndex != selectedConfigIndex)
            {
                selectedConfigIndex = newSelectedIndex;
                selectedConfig = patchConfigs[selectedConfigIndex];
                currentPatchResult = PatchResult.None;
                
                // Simply reload package rules for the new configuration without forcing Unity refresh
                LoadPackageRules();
            }
        }
        else
        {
            EditorGUILayout.LabelField(selectedConfig.avatarDisplayName ?? "<Unnamed>");
        }

        EditorGUILayout.EndHorizontal();
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

    private void DrawPatchButton()
    {
        bool configIsValid = selectedConfig.IsValidForPatching();

        // Allow patching as long as requirements have been checked and config is valid
        // Package issues will display warnings/confirmations but will not block patching
        bool allowPatch = requirementsChecked && configIsValid;

        GUILayout.Space(16);

        DrawPatchButtonUI(configIsValid, allowPatch);
        GUILayout.Space(8);

        DrawPatchButtonMessages(configIsValid, allowPatch);
    }

    private void DrawPatchButtonUI(bool configIsValid, bool allowPatch)
    {
        Texture icon = EditorGUIUtility.IconContent(PatcherHubConstants.PLAY_ICON).image;
        GUIContent content = new GUIContent($"  Patch Avatar: {selectedConfig.avatarDisplayName}", icon);

        using (new EditorGUI.DisabledScope(!configIsValid || !allowPatch))
        {
            if (GUILayout.Button(content, styles?.Button ?? GUI.skin.button, GUILayout.ExpandWidth(true)))
            {
                HandlePatchButtonClick();
            }
        }
    }

    private void HandlePatchButtonClick()
    {
        bool hasPackageIssues = versionErrors != null && versionErrors.Count > 0;
        if (hasPackageIssues)
        {
            bool proceed = UIMessageHelper.ShowConfirmationDialog(
                PatcherHubConstants.WARNING_DIALOG_TITLE,
                PatcherHubConstants.WARNING_DIALOG_MESSAGE,
                "Yes, Continue",
                "Cancel"
            );

            if (!proceed) return;
        }

        ApplyPatch(selectedConfig);
    }

    private void DrawPatchButtonMessages(bool configIsValid, bool allowPatch)
    {
        if (!configIsValid)
        {
            string validationMessage = selectedConfig.GetValidationMessage();
            EditorGUILayout.HelpBox($"Configuration incomplete: {validationMessage}", MessageType.Error);
        }
        else if (requirementsChecked)
        {
            bool hasPackageIssues = versionErrors != null && versionErrors.Count > 0;
            if (hasPackageIssues)
            {
                DrawCustomMessage(PatcherHubConstants.WARNING_PACKAGES_MESSAGE, MessageType.Warning);
            }
        }
        else if (!requirementsChecked)
        {
            EditorGUILayout.HelpBox(PatcherHubConstants.VALIDATING_PACKAGES_MESSAGE, MessageType.Info);
        }
    }



    private void DrawVersionErrorsWithLinks(List<VersionError> versionErrors, bool requirementsChecked)
    {
        if (!requirementsChecked || versionErrors == null || versionErrors.Count == 0) return;

        // Show overall progress if packages are being checked
        if (isCheckingPackageAvailability && totalPackagesToCheck > 0)
        {
            EditorGUILayout.Space();
            
            // Create a more visually appealing progress section
            GUIStyle progressBoxStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(8, 8, 6, 6),
                margin = new RectOffset(0, 0, 2, 6)
            };
            
            EditorGUILayout.BeginVertical(progressBoxStyle);
            
            GUILayout.BeginHorizontal();
            
            // Animated loading icon
            string loadingIcon = GetLoadingIcon();
            GUILayout.Label($"{loadingIcon} Checking package availability...", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{packagesBeingChecked}/{totalPackagesToCheck}", EditorStyles.miniLabel);
            GUILayout.EndHorizontal();
            
            // Progress bar
            Rect progressRect = GUILayoutUtility.GetRect(0, 4, GUILayout.ExpandWidth(true));
            EditorGUI.ProgressBar(progressRect, (float)packagesBeingChecked / totalPackagesToCheck, "");
            
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.Space();

        foreach (var error in versionErrors)
        {
            // Determine the icon based on MessageType and repository status
            string iconName = error.messageType switch
            {
                MessageType.Error => PatcherHubConstants.ERROR_ICON,
                MessageType.Warning => PatcherHubConstants.WARNING_ICON,
                MessageType.Info => PatcherHubConstants.INFO_ICON,
                _ => null // None: no icon
            };

            // Override icon if package is not in repository
            if (!string.IsNullOrEmpty(error.packageName) && 
                packageStatusCache.TryGetValue(error.packageName, out PackageStatus status) && 
                status == PackageStatus.NotInRepository)
            {
                iconName = PatcherHubConstants.REPO_MISSING_ICON; // Use a specific icon for repo-missing packages
            }

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

            // Button container - always align to the right
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();

            // Show either VCC install button OR VCC URL button
            if (!string.IsNullOrEmpty(error.packageName))
            {
                // Check if package is being installed or during bulk install
                bool isInstalling = packageInstallingStates.ContainsKey(error.packageName) && packageInstallingStates[error.packageName];
                // Check if package is being loaded
                bool isLoadingVCC = IsPackageLoadingVCC(error.packageName);
                
                if (isInstalling || isBulkInstalling)
                {
                    // Show installing indicator
                    GUI.enabled = false;
                    string statusText = isBulkInstalling ? "Bulk Installing..." : "Installing...";
                    GUILayout.Button(statusText, buttonStyle, GUILayout.Width(PatcherHubConstants.BUTTON_MEDIUM_WIDTH));
                    GUI.enabled = true;
                }
                else if (isLoadingVCC)
                {
                    // Show loading indicator
                    GUI.enabled = false;
                    GUILayout.Button(PatcherHubConstants.CHECKING_STATUS, buttonStyle, GUILayout.Width(100));
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
                            TryAutoInstallPackageViaVCC(error.packageName, error.isMissingPackage);
                        }
                    }
                    else if (packageNotInRepo)
                    {
                        // Show status as a label instead of button
                        GUIStyle statusLabelStyle = new GUIStyle(EditorStyles.helpBox)
                        {
                            fontSize = PatcherHubConstants.STATUS_FONT_SIZE,
                            fontStyle = FontStyle.Bold,
                            alignment = TextAnchor.MiddleCenter,
                            normal = { 
                                textColor = PatcherHubConstants.WARNING_COLOR
                            },
                            fixedHeight = PatcherHubConstants.STATUS_LABEL_HEIGHT,
                            margin = new RectOffset(PatcherHubConstants.SPACE_SMALL, PatcherHubConstants.SPACE_SMALL, PatcherHubConstants.SPACE_SMALL, PatcherHubConstants.SPACE_SMALL),
                            padding = new RectOffset(PatcherHubConstants.SPACE_MEDIUM, PatcherHubConstants.SPACE_MEDIUM, PatcherHubConstants.SPACE_TINY, PatcherHubConstants.SPACE_TINY)
                        };
                        GUILayout.Label(PatcherHubConstants.NOT_IN_VCC_LABEL, statusLabelStyle, GUILayout.Width(PatcherHubConstants.STATUS_LABEL_WIDTH));
                        
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
                // No package name but have VCC URL - show VCC button
                if (GUILayout.Button(PatcherHubConstants.VIEW_IN_VCC_BUTTON, buttonStyle, GUILayout.Width(100)))
                {
                    VCCIntegration.OpenVCCUrl(error.vccURL);
                }
            }

            GUILayout.EndHorizontal();

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            GUILayout.Space(4);
        }
        
        // Add bulk install/update button if there are multiple VCC-manageable packages
        DrawBulkInstallButton();
    }

    /// <summary>
    /// Draws a bulk install/update button for all VCC-manageable packages
    /// </summary>
    private void DrawBulkInstallButton()
    {
        if (!requirementsChecked || versionErrors == null || versionErrors.Count == 0)
            return;
        
        // Verify VCC availability and identify packages manageable through VCC
        if (!(vccAvailable ?? false))
            return;
        
        // Get all packages that can be managed via VCC
        var vccManageablePackages = versionErrors
            .Where(error => !string.IsNullOrEmpty(error.packageName) && 
                           IsPackageAvailableViaVCC(error.packageName) &&
                           !(packageInstallingStates.ContainsKey(error.packageName) && packageInstallingStates[error.packageName]))
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
        if (!(vccAvailable ?? true) && versionErrors != null && versionErrors.Count > 0)
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
                DrawCustomMessage("Failed to patch the FBX .meta file. It must be identical to the original version imported from the avatar.", MessageType.Error);
                break;
            case PatchResult.FbxPatchFailed:
                DrawCustomMessage("Failed to patch the FBX file. Ensure the file has not been modified and matches the original import exactly.", MessageType.Error);
                break;
            case PatchResult.Success:
                DrawCustomMessage("✅ Patch completed successfully.", MessageType.Info);
                break;
        }
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
        if (promptToOpenScene && selectedConfig != null && selectedConfig.configuredScene != null)
        {
            promptToOpenScene = false;
            string scenePath = AssetDatabase.GetAssetPath(selectedConfig.configuredScene);
            if (!string.IsNullOrEmpty(scenePath) &&
                EditorUtility.DisplayDialog(PatcherHubConstants.PATCH_COMPLETE_TITLE, PatcherHubConstants.PATCH_COMPLETE_MESSAGE, "Yes", "No"))
            {
                EditorSceneManager.OpenScene(scenePath);
            }
        }
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

        // Verify package requirements exist (global or configuration-specific)
        var hasGlobalRules = packageRules?.packageRequirements?.Count > 0;
        var hasConfigRules = selectedConfig?.configSpecificPackages?.Count > 0;

        if (!hasGlobalRules && !hasConfigRules)
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

        // Get combined package requirements (global + config-specific)
        var allRequirements = selectedConfig?.GetAllPackageRequirements(packageRules) 
                            ?? packageRules?.packageRequirements 
                            ?? new List<PackageRequirement>();

        // Check each package requirement
        foreach (var req in allRequirements)
        {
            var found = request.Result.FirstOrDefault(p => p.name == req.packageName);
            bool missing = found == null;
            bool badVersion = !missing && !CompareVersions(found.version, req.minVersion);

            // Configure error details
            req.missingError.vccURL = req.vccURL;
            req.missingError.packageName = req.packageName;
            req.missingError.isMissingPackage = true;
            
            req.versionError.vccURL = req.vccURL;
            req.versionError.packageName = req.packageName;
            req.versionError.isMissingPackage = false;

            if (missing) 
                versionErrors.Add(req.missingError);
            else if (badVersion) 
                versionErrors.Add(req.versionError);
        }

        // Check package availability via VCC API if available
        if (vccAvailable ?? false)
        {
            var packagesToCheck = versionErrors
                .Where(error => !string.IsNullOrEmpty(error.packageName) && 
                               !packageStatusCache.ContainsKey(error.packageName))
                .Select(error => error.packageName)
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
    /// Attempts to automatically install or update a package using VCC HTTP API.
    /// </summary>
    /// <param name="packageName">Name of the package to install/update</param>
    /// <param name="isMissingPackage">True if package is missing, false if updating</param>
    private void TryAutoInstallPackageViaVCC(string packageName, bool isMissingPackage)
    {
        string action = isMissingPackage ? "install" : "update";
        
        bool userConfirmed = EditorUtility.DisplayDialog(
            $"Auto-{action} Package",
            $"This will attempt to {action} the package '{packageName}' using VCC HTTP API.\n\n" +
            "Make sure VRChat Creator Companion is running and this project is managed by VCC.\n\n" +
            $"Do you want to proceed with the {action}?",
            "Yes", "Cancel"
        );

        if (!userConfirmed) return;
        
        // Start async installation without blocking UI
        _ = TryInstallPackageViaVCCAsync(packageName, isMissingPackage);
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
    /// Refreshes package validation by clearing cache and reloading rules.
    /// </summary>
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

        // Trigger async check
        EditorApplication.delayCall += () =>
        {
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    bool isAvailable = await System.Threading.Tasks.Task.Run(() => 
                        VCCIntegration.CheckPackageAvailability(packageName));

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

    #region Patch Operations

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

            if (Directory.Exists(outputFolder))
            {
                if (EditorUtility.DisplayDialog(PatcherHubConstants.REPLACE_FOLDER_TITLE, $"The folder '{outputFolder}' already exists. Do you want to delete and recreate it?", "Yes", "No"))
                {
                    Directory.Delete(outputFolder, true);
                    AssetDatabase.Refresh();
                }
                else return;
            }

            Directory.CreateDirectory(outputFolder);
            AssetDatabase.Refresh();

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
