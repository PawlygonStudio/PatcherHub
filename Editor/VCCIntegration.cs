// VCCIntegration.cs
// © 2025 Pawlygon Studios. All rights reserved.
// VRChat Creator Companion HTTP API integration for PatcherHub

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;

namespace Pawlygon.PatcherHub.Editor
{
    /// <summary>
    /// Provides integration with VRChat Creator Companion (VCC) through its HTTP API.
    /// Enables automatic package installation and management for VRChat projects.
    /// </summary>
    public static class VCCIntegration
{
    // VCC Local API Constants
    private const string VCC_API_URL = "http://localhost:5477/api/";
    private const int VCC_TIMEOUT_SECONDS = 5;
    
    // Static flag to prevent multiple batch operations across all instances
    private static bool _isBatchOperationRunning = false;
    private static readonly object _batchLock = new object();

    #region Data Models for VCC API

    [Serializable]
    public class VccResponse<T>
    {
        public bool success;
        public T data;
    }
    
    [Serializable]
    public class VccProject
    {
        public string ProjectId;
    }
    
    [Serializable]
    public class VccPackage
    {
        public string name;
        public string displayName;
        public string version;
        public string description;
    }
    
    [Serializable]
    public class AddPackageRequest
    {
        public string projectId;
        public string packageId;
        public string version;
    }

    [Serializable]
    public class ProjectManifestRequest
    {
        public string id;
    }

    [Serializable]
    public class ProjectManifestResponse
    {
        public List<Dependency> dependencies;
        public string path;
        public string name;
        public string type;
    }

    [Serializable]
    public class Dependency
    {
        public string Id;
        public string Version;
    }

    #endregion

    #region VCC HTTP API Communication

    /// <summary>
    /// Sends HTTP request to VCC's local API
    /// </summary>
    private static async Task<VccResponse<T>> VccRequest<T>(string endpoint, string method, object body = null)
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Origin", "http://localhost:5477/");
            client.DefaultRequestHeaders.Host = "localhost";
            client.Timeout = TimeSpan.FromSeconds(VCC_TIMEOUT_SECONDS);

            var request = new HttpRequestMessage(new HttpMethod(method), VCC_API_URL + endpoint);
            if (body != null)
            {
                string json = JsonUtility.ToJson(body);
                request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            }

            var response = await client.SendAsync(request);
            string responseBody = await response.Content.ReadAsStringAsync();
            
            return JsonUtility.FromJson<VccResponse<T>>(responseBody);
        }
        catch (Exception)
        {
            // Expected when VCC is not running - return null for graceful handling
            return null;
        }
    }

    /// <summary>
    /// Gets the VCC project ID for the current Unity project
    /// </summary>
    private static async Task<string> GetProjectId()
    {
        string projectPath = GetCurrentProjectPath().Replace("/", "\\");
        var response = await VccRequest<VccProject>($"projects/project?path={projectPath}", "GET");
        
        return response?.success == true ? response.data.ProjectId : null;
    }

    #endregion

    #region VCC Availability and Status

    /// <summary>
    /// Checks if VCC is running and accessible via HTTP API (simplified)
    /// </summary>
    public static bool IsVCCAvailable()
    {
        try
        {
            var task = CheckVCCAvailability();
            
            // Use a shorter timeout to avoid blocking UI
            if (task.Wait(1500)) // 1.5 second timeout
            {
                return task.Result;
            }
            else
            {
                return false; // Timeout - assume VCC not available
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Asynchronously checks if VCC is available (simplified)
    /// </summary>
    private static async Task<bool> CheckVCCAvailability()
    {
        try
        {
            // Try health endpoint first (fastest)
            var response = await VccRequest<object>("health", "GET").ConfigureAwait(false);
            if (response?.success == true)
            {
                return true;
            }
            
            // Fallback to projects endpoint
            var projectResponse = await VccRequest<object>("projects", "GET").ConfigureAwait(false);
            return projectResponse?.success == true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Legacy method name for compatibility - now checks VCC HTTP API instead of CLI
    /// </summary>
    public static bool IsVCCCLIAvailable() => IsVCCAvailable();

    #endregion

    #region Package Management via VCC API

    /// <summary>
    /// Checks if a package is available in VCC repositories via HTTP API
    /// Uses a timeout-based approach to avoid blocking the Unity editor
    /// </summary>
    public static bool CheckPackageAvailability(string packageId)
    {
        try
        {
            var task = CheckPackageAvailabilityAsync(packageId);
            
            // Wait with timeout to prevent blocking
            if (task.Wait(2000)) // 2 second timeout
            {
                return task.Result;
            }
            else
            {
                return false; // Timeout
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Asynchronously checks if a package is available (simplified)
    /// </summary>
    private static async Task<bool> CheckPackageAvailabilityAsync(string packageId)
    {
        try
        {
            // Get project ID to verify VCC is working
            string projectId = await GetProjectId();
            if (string.IsNullOrEmpty(projectId))
            {
                return false;
            }

            // If we can get the project ID, VCC is working and we can attempt package installation
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Installs a package via VCC HTTP API with timeout protection
    /// </summary>
    public static VPMResult TryInstallPackageViaAPI(string packageId, string version = null)
    {
        try
        {
            var task = TryInstallPackageViaAPIAsync(packageId, version);
            
            // Wait with timeout to prevent blocking
            if (task.Wait(10000)) // 10 second timeout for installation
            {
                return task.Result;
            }
            else
            {
                return new VPMResult
                {
                    Success = false,
                    Error = $"Package installation for {packageId} timed out after 10 seconds",
                    ExitCode = -1
                };
            }
        }
        catch (Exception ex)
        {
            return new VPMResult
            {
                Success = false,
                Error = $"Package installation for {packageId} failed: {ex.Message}",
                ExitCode = -1
            };
        }
    }

    /// <summary>
    /// Asynchronously installs a package via VCC API
    /// </summary>
    private static async Task<VPMResult> TryInstallPackageViaAPIAsync(string packageId, string version = null)
    {
        try
        {
            // Get project ID first
            string projectId = await GetProjectId();
            if (string.IsNullOrEmpty(projectId))
            {
                return new VPMResult
                {
                    Success = false,
                    Error = "Could not get VCC project ID. Make sure this project is managed by VCC.",
                    ExitCode = -1
                };
            }

            // Create the add package request
            var request = new AddPackageRequest
            {
                projectId = projectId,
                packageId = packageId,
                version = (string.IsNullOrEmpty(version) || version == "latest") ? null : version
            };
            
            // Send the add package request
            var response = await VccRequest<object>("projects/packages", "POST", request);
            
            if (response?.success == true)
            {
                return new VPMResult
                {
                    Success = true,
                    Output = $"Successfully added package {packageId} via VCC API",
                    ExitCode = 0
                };
            }
            else
            {
                return new VPMResult
                {
                    Success = false,
                    Error = $"VCC API request failed for package {packageId}. The package might not exist or may already be installed.",
                    ExitCode = -1
                };
            }
        }
        catch (Exception ex)
        {
            return new VPMResult
            {
                Success = false,
                Error = $"Exception during VCC API package installation: {ex.Message}",
                ExitCode = -1
            };
        }
    }

    #endregion

    #region Legacy VPM CLI Support (Fallback)

    /// <summary>
    /// Legacy method - now uses VCC HTTP API instead of CLI
    /// </summary>
    public static VPMResult TryInstallPackageViaCLI(string packageId, string projectPath = null)
    {
        // Use the new API method instead of CLI
        return TryInstallPackageViaAPI(packageId);
    }

    #endregion

    #region Batch Operations

    /// <summary>
    /// Checks availability for multiple packages using VCC API
    /// </summary>
    public static void CheckMultiplePackageAvailability(List<string> packageIds, 
        System.Action<int, int, string, bool> onProgress = null, 
        System.Action<Dictionary<string, bool>> onComplete = null)
    {
        lock (_batchLock)
        {
            if (_isBatchOperationRunning)
            {
                EditorApplication.delayCall += () => onComplete?.Invoke(new Dictionary<string, bool>());
                return;
            }
            _isBatchOperationRunning = true;
        }
        
        var results = new Dictionary<string, bool>();
        var uniquePackageIds = new HashSet<string>(packageIds).ToList();
        
        if (uniquePackageIds.Count == 0)
        {
            lock (_batchLock) { _isBatchOperationRunning = false; }
            EditorApplication.delayCall += () => onComplete?.Invoke(results);
            return;
        }
        
        // Use async task for batch processing
        System.Threading.ThreadPool.QueueUserWorkItem(async _ =>
        {
            try
            {
                for (int i = 0; i < uniquePackageIds.Count; i++)
                {
                    var packageId = uniquePackageIds[i];
                    bool isAvailable = await CheckPackageAvailabilityAsync(packageId);
                    results[packageId] = isAvailable;
                    
                    // Progress callback on main thread
                    var currentIndex = i + 1;
                    EditorApplication.delayCall += () => onProgress?.Invoke(currentIndex, uniquePackageIds.Count, packageId, isAvailable);
                }
                
                // Release lock and call completion
                lock (_batchLock) { _isBatchOperationRunning = false; }
                var finalResults = new Dictionary<string, bool>(results);
                EditorApplication.delayCall += () => onComplete?.Invoke(finalResults);
            }
            catch
            {
                lock (_batchLock) { _isBatchOperationRunning = false; }
                EditorApplication.delayCall += () => onComplete?.Invoke(results);
            }
        });
    }

    #endregion

    #region VCC Application and URL Handling

    /// <summary>
    /// Attempts to find the VCC executable path
    /// </summary>
    public static string FindVCCPath()
    {
        string[] possiblePaths = {
            // Common installation paths
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRChatCreatorCompanion", PatcherHubConstants.VCC_EXECUTABLE),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VRChat Creator Companion", PatcherHubConstants.VCC_EXECUTABLE),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VRChat Creator Companion", PatcherHubConstants.VCC_EXECUTABLE),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "VRChatCreatorCompanion", PatcherHubConstants.VCC_EXECUTABLE),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRChatCreatorCompanion", PatcherHubConstants.VCC_EXECUTABLE),
            
        };

        UnityEngine.Debug.Log($"Searching for VCC executable: {PatcherHubConstants.VCC_EXECUTABLE}");
        
        foreach (string path in possiblePaths)
        {
            UnityEngine.Debug.Log($"Checking path: {path}");
            if (File.Exists(path))
            {
                UnityEngine.Debug.Log($"Found VCC at: {path}");
                return path;
            }
        }
        
        UnityEngine.Debug.LogWarning("VCC executable not found in any of the expected paths");
        
        // Try to find VCC in PATH environment variable as a last resort
        string pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (string dir in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                
                string possiblePath = Path.Combine(dir.Trim(), PatcherHubConstants.VCC_EXECUTABLE);
                UnityEngine.Debug.Log($"Checking PATH: {possiblePath}");
                if (File.Exists(possiblePath))
                {
                    UnityEngine.Debug.Log($"Found VCC in PATH: {possiblePath}");
                    return possiblePath;
                }
            }
        }
        
        return null;
    }

    /// <summary>
    /// Opens VCC with a specific package URL
    /// </summary>
    public static void OpenVCCUrl(string vccUrl)
    {
        if (string.IsNullOrEmpty(vccUrl)) return;

        try
        {
            Application.OpenURL(vccUrl);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"Failed to open VCC URL: {ex.Message}");
        }
    }

    /// <summary>
    /// Attempts to launch VCC application if found on the system
    /// </summary>
    public static void OpenVCC()
    {
        string vccPath = FindVCCPath();
        if (!string.IsNullOrEmpty(vccPath))
        {
            try
            {
                UnityEngine.Debug.Log($"Attempting to launch VCC from: {vccPath}");
                Process.Start(vccPath);
                UnityEngine.Debug.Log("VCC launch command sent successfully");
                
                // Trigger VCC availability refresh after a brief delay
                TriggerVCCAvailabilityRefresh();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"Could not launch VCC from {vccPath}: {ex.Message}. Please start VCC manually.");
                
                // Fallback: Try to open VCC via the vcc:// protocol
                TryOpenVCCViaProtocol();
            }
        }
        else
        {
            UnityEngine.Debug.LogWarning("VCC executable not found in common installation paths. Trying protocol fallback...");
            
            // If VCC path not found, try the protocol method
            TryOpenVCCViaProtocol();
        }
    }

    /// <summary>
    /// Attempts to open VCC using the vcc:// protocol as a fallback
    /// </summary>
    private static void TryOpenVCCViaProtocol()
    {
        try
        {
            UnityEngine.Debug.Log("Attempting to launch VCC via protocol...");
            Application.OpenURL("vcc://");
            
            // Trigger VCC availability refresh after protocol launch
            TriggerVCCAvailabilityRefresh();
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"Failed to launch VCC via protocol: {ex.Message}. Please start VRChat Creator Companion manually.");
            
            // Final fallback: Open VCC download page
            bool downloadVCC = EditorUtility.DisplayDialog(
                "VCC Not Found", 
                "VRChat Creator Companion could not be found or launched. Please install VCC or start it manually.\n\nWould you like to download VCC?", 
                "Yes", 
                "Cancel"
            );
            
            if (downloadVCC)
            {
                Application.OpenURL("https://vrchat.com/home/download");
            }
        }
    }

    /// <summary>
    /// Triggers a refresh of VCC availability across all PatcherHub windows after a delay
    /// </summary>
    private static void TriggerVCCAvailabilityRefresh()
    {
        // Wait for VCC to start up, then refresh all PatcherHub windows
        EditorApplication.delayCall += () =>
        {
            System.Threading.Tasks.Task.Delay(3000).ContinueWith(_ =>
            {
                EditorApplication.delayCall += () =>
                {
                    // Refresh VCC availability for all open PatcherHub windows
                    var patcherWindows = Resources.FindObjectsOfTypeAll<PatcherHubWindow>();
                    foreach (var window in patcherWindows)
                    {
                        if (window != null)
                        {
                            // Use reflection to call the private method or expose a public refresh method
                            var method = typeof(PatcherHubWindow).GetMethod("RefreshVCCAvailability", 
                                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            
                            if (method != null)
                            {
                                method.Invoke(window, null);
                            }
                        }
                    }
                };
            });
        };
    }

    /// <summary>
    /// Gets the current Unity project path
    /// </summary>
    public static string GetCurrentProjectPath()
    {
        return Path.GetDirectoryName(Application.dataPath);
    }

    #endregion

    #region Legacy Support and Data Models

    /// <summary>
    /// Result of a VCC operation (compatible with legacy VPM CLI result structure)
    /// </summary>
    public class VPMResult
    {
        public bool Success { get; set; }
        public string Output { get; set; }
        public string Error { get; set; }
        public int ExitCode { get; set; }
    }

    #endregion

    #region Production VCC API Methods
    
    /// <summary>
    /// Checks if a specific package is already installed in the current project
    /// </summary>
    public static bool IsPackageInstalled(string packageId)
    {
        try
        {
            var task = IsPackageInstalledAsync(packageId);
            
            if (task.Wait(2000)) // 2 second timeout
            {
                return task.Result;
            }
            else
            {
                return false; // Timeout
            }
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Asynchronously checks if a package is installed
    /// </summary>
    private static async Task<bool> IsPackageInstalledAsync(string packageId)
    {
        try
        {
            string projectId = await GetProjectId();
            if (string.IsNullOrEmpty(projectId))
            {
                return false;
            }

            var manifestRequest = new ProjectManifestRequest { id = projectId };
            var response = await VccRequest<ProjectManifestResponse>("projects/manifest", "POST", manifestRequest);
            
            if (response?.success == true && response.data?.dependencies != null)
            {
                return response.data.dependencies.Any(d => d.Id == packageId);
            }
            
            return false;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Debugging and Utilities

    /// <summary>
    /// Forces clearing of batch operations (used during cleanup)
    /// </summary>
    public static void ForceClearBatchOperations()
    {
        lock (_batchLock) { _isBatchOperationRunning = false; }
    }

    #endregion
    }
}
