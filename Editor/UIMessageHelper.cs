// UIMessageHelper.cs
using UnityEditor;
using UnityEngine;

public static class UIMessageHelper
{
    public static void DrawMessage(string message, MessageType type, GUIStyle messageStyle = null)
    {
        Texture icon = EditorGUIUtility.IconContent(GetIconName(type)).image;
        
        if (messageStyle == null)
        {
            messageStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                wordWrap = true,
                richText = true,
                alignment = TextAnchor.MiddleLeft
            };
        }

        EditorGUILayout.BeginVertical(GUI.skin.box);
        EditorGUILayout.BeginHorizontal();

        if (icon != null)
        {
            GUILayout.Label(icon, GUILayout.Width(30), GUILayout.Height(30));
            GUILayout.Space(4);
        }

        GUILayout.Label(message, messageStyle, GUILayout.ExpandWidth(true), GUILayout.Height(30));

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
        GUILayout.Space(4);
    }

    private static string GetIconName(MessageType type)
    {
        return type switch
        {
            MessageType.Error => PatcherHubConstants.ERROR_ICON,
            MessageType.Warning => PatcherHubConstants.WARNING_ICON,
            MessageType.Info => PatcherHubConstants.INFO_ICON,
            _ => ""
        };
    }
    
    public static bool ShowConfirmationDialog(string title, string message, string okText = "OK", string cancelText = "Cancel")
    {
        return EditorUtility.DisplayDialog(title, message, okText, cancelText);
    }
}
