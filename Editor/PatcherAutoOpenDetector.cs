using UnityEditor;

namespace Pawlygon.PatcherHub.Editor
{
    /// <summary>
    /// Automatically opens the Patcher Hub window on the first project import.
    /// Uses project-specific EditorPrefs to ensure the window only opens once per project.
    /// </summary>
    [InitializeOnLoad]
    public static class PatcherAutoOpenDetector
    {
        /// <summary>
        /// Generates a unique key for this project to track first-run status.
        /// </summary>
        /// <returns>Project-specific key for EditorPrefs storage</returns>
        private static string GetAssemblyKey()
        {
            string projectPath = UnityEngine.Application.dataPath;
            string assemblyName = typeof(PatcherAutoOpenDetector).Assembly.GetName().Name;
            return $"PatcherHub_FirstRun_{projectPath.GetHashCode()}_{assemblyName}";
        }

        /// <summary>
        /// Static constructor that runs when the class is first loaded.
        /// Opens the Patcher Hub window automatically on first project import.
        /// </summary>
        static PatcherAutoOpenDetector()
        {
            string assemblyKey = GetAssemblyKey();
            
            if (!EditorPrefs.GetBool(assemblyKey, false))
            {
                EditorApplication.delayCall += () =>
                {
                    PatcherHubWindow.ShowWindow();
                    EditorPrefs.SetBool(assemblyKey, true);
                };
            }
        }
    }
}

