// PatcherHubConstants.cs
public static class PatcherHubConstants
{
    public const string TOOL_VERSION = "v1.1.0";
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
    public const string REPO_MISSING_ICON = "d_console.warnicon.sml";
    public const string REFRESH_ICON = "refresh";
    public const string PACKAGE_ICON = "Package Manager";
    public const string CLOUD_ICON = "CloudConnect";
    public const string DOWNLOAD_ICON = "Download-Available";
    
    // URLs
    public const string WEBSITE_URL = "https://www.pawlygon.net";
    public const string TWITTER_URL = "https://x.com/Pawlygon_studio";
    public const string YOUTUBE_URL = "https://www.youtube.com/@Pawlygon";
    public const string DISCORD_URL = "https://discord.com/invite/pZew3JGpjb";
    
    // VCC Integration
    public const string VCC_EXECUTABLE = "CreatorCompanion.exe";
    public const string VCC_API_URL = "http://localhost:5477/api/"; // VCC HTTP API endpoint
    
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
    public const string INSTALL_PACKAGE_BUTTON = "Install (VCC)";
    public const string UPDATE_PACKAGE_BUTTON = "Update (VCC)";

    // UI Text and Messages
    public const string HEADER_TITLE = "Pawlygon Patcher Hub";
    public const string HEADER_SUBTITLE = "Tool to apply face tracking patch files to FBX models.";
    public const string NO_CONFIGS_TITLE = "No Patch Configurations Found";
    public const string CREATE_CONFIG_BUTTON = "Create New Patch Config";
    public const string REFRESH_BUTTON = "Refresh";
    public const string VIEW_IN_VCC_BUTTON = "VCC Website";
    public const string ADD_VIA_VCC_BUTTON = "Add to VCC";
    public const string CHECKING_STATUS = "Checking...";
    public const string OPEN_VCC_BUTTON = "🚀 Open VCC";
    public const string VIEW_DOCS_BUTTON = "📖 View Documentation";
    public const string REFRESH_AVAILABILITY_BUTTON = "🔄 Refresh Package Availability";
    public const string NOT_IN_REPO_BUTTON = "❌ Not in Repositories";
    public const string NOT_IN_VCC_LABEL = "⚠️ Missing from VCC";
    public const string ADD_REPO_BUTTON = "📂 Add Repository";
    public const string REPO_STATUS_TITLE = "📦 Package Repository Status";
    public const string MISSING_FROM_REPOS = "Missing from Repositories";
    
    // Font Sizes
    public const int HEADER_FONT_SIZE = 16;
    public const int FOOTER_FONT_SIZE = 10;
    public const int BUTTON_FONT_SIZE = 13;
    public const int MESSAGE_FONT_SIZE = 12;
    public const int LINK_FONT_SIZE = 12;
    public const int STATUS_FONT_SIZE = 11;
    public const int CREDIT_FONT_SIZE = 9;
    
    // Spacing Constants
    public const int SPACE_TINY = 4;
    public const int SPACE_SMALL = 5;
    public const int SPACE_MEDIUM = 8;
    public const int SPACE_LARGE = 10;
    public const int SPACE_SECTION = 16;
    public const int SPACE_HEADER = 20;
    
    // UI Colors (as static readonly for better performance with Color objects)
    public static readonly UnityEngine.Color LINK_COLOR_NORMAL = new UnityEngine.Color(0.3f, 0.5f, 1f);
    public static readonly UnityEngine.Color LINK_COLOR_FLAT = new UnityEngine.Color(0.4f, 0.7f, 1f);
    public static readonly UnityEngine.Color LINK_COLOR_HOVER = new UnityEngine.Color(0.6f, 0.9f, 1f);
    public static readonly UnityEngine.Color WARNING_COLOR = new UnityEngine.Color(0.9f, 0.7f, 0.4f, 1f);
    
    // UI Dimensions
    public const int STATUS_LABEL_WIDTH = 140;
    public const int STATUS_LABEL_HEIGHT = 24;
    public const int ADD_VCC_BUTTON_WIDTH = 110;
    
    // Timeout Values
    public const int VCC_AVAILABILITY_TIMEOUT_MS = 1500;
    public const int VCC_REQUEST_TIMEOUT_SECONDS = 3;
    public const int PACKAGE_CHECK_TIMEOUT_MS = 5000;
    
    // Asset Search Patterns
    public const string FTPATCH_CONFIG_SEARCH = "t:FTPatchConfig";
    public const string PACKAGE_RULES_SEARCH = "t:PackageRules";
}
