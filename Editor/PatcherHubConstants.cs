// PatcherHubConstants.cs
public static class PatcherHubConstants
{
    public const string TOOL_VERSION = "v1.0.4";
    public const string MENU_PATH = "Tools/!Pawlygon/Patcher Hub";
    public const string WINDOW_TITLE = "Patcher Hub";
    
    // GUI Layout
    public const float MIN_WINDOW_WIDTH = 500f;
    public const float MIN_WINDOW_HEIGHT = 750f;
    public const float LOGO_SIZE = 64f;
    public const float BUTTON_HEIGHT = 36f;
    public const float MESSAGE_BOX_HEIGHT = 30f;
    
    // Paths
    public const string LOGO_PATH = "Assets/!Pawlygon/PatcherHub/Editor/Resources/pawlygon_logo.png";
    public const string PATCHER_BASE_PATH = "!Pawlygon/PatcherHub/hdiff/hpatchz";
    
    // Platform-specific executables
    public const string WINDOWS_PATCHER = "Windows/hpatchz.exe";
    public const string MAC_PATCHER = "Mac/hpatchz";
    public const string LINUX_PATCHER = "Linux/hpatchz";
    
    // Messages
    public const string NO_CONFIG_MESSAGE = "No valid patch configuration found.";
    public const string VALIDATING_PACKAGES_MESSAGE = "Validating packages...";
    public const string MISSING_PREFAB_MESSAGE = "Original Prefab is missing. Patch cannot proceed.";
    public const string WARNING_PACKAGES_MESSAGE = "Some recommended packages are missing or outdated. Patch is allowed, but the avatar will require those packages.";
    
    // Dialog messages
    public const string WARNING_DIALOG_TITLE = "Warning: Unmet Package Requirements";
    public const string WARNING_DIALOG_MESSAGE = "Some recommended packages are missing or outdated. Are you sure you want to continue?";
    public const string REPLACE_FOLDER_TITLE = "Replace Existing Folder";
    public const string PATCH_COMPLETE_TITLE = "Patch Complete";
    public const string PATCH_COMPLETE_MESSAGE = "The patch completed successfully. Do you want to open the scene to test the avatar?";
    
    // Icons
    public const string PLAY_ICON = "d_PlayButton";
    public const string ERROR_ICON = "console.erroricon";
    public const string WARNING_ICON = "console.warnicon";
    public const string INFO_ICON = "console.infoicon";
    
    // URLs
    public const string WEBSITE_URL = "https://www.pawlygon.net";
    public const string TWITTER_URL = "https://x.com/Pawlygon_studio";
    public const string YOUTUBE_URL = "https://www.youtube.com/@Pawlygon";
    public const string DISCORD_URL = "https://discord.com/invite/pZew3JGpjb";
    
    // VCC Integration
    public const string VCC_EXECUTABLE = "CreatorCompanion.exe";
    public const string VCC_API_URL = "https://localhost:5477/api/"; // VCC HTTPS API endpoint
    
    // UI Timing and Delays
    public const int PACKAGE_INSTALL_DELAY_MS = 500;
    public const int PACKAGE_REFRESH_DELAY_MS = 3000;
    
    // UI Layout Constants
    public const int FIELD_LABEL_WIDTH = 100;
    public const int BUTTON_SMALL_WIDTH = 80;
    public const int BUTTON_MEDIUM_WIDTH = 120;
    public const int BUTTON_LARGE_WIDTH = 200;
    public const int BUTTON_BULK_WIDTH = 300;
    public const int CONFIG_SELECTION_LABEL_WIDTH = 150;
    
    // Button labels
    public const string INSTALL_PACKAGE_BUTTON = "Install via VCC";
    public const string UPDATE_PACKAGE_BUTTON = "Update via VCC";
}
