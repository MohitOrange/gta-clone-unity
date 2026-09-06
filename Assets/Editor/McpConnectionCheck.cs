using UnityEngine;
using UnityEditor;

/// <summary>
/// Prints a connection message to the Unity Console.
/// Runs automatically after every domain reload (script compile / editor start),
/// and can be fired manually from Tools > MCP > Print Connection Message.
/// </summary>
[InitializeOnLoad]
public static class McpConnectionCheck
{
    static McpConnectionCheck()
    {
        // Delay so the Console is ready and the log isn't swallowed by the reload.
        EditorApplication.delayCall += PrintConnectionMessage;
    }

    [MenuItem("Tools/MCP/Print Connection Message")]
    public static void PrintConnectionMessage()
    {
        Debug.Log(
            "[MCP] CONNECTED - Unity <-> Claude bridge is alive.\n" +
            "Unity version : " + Application.unityVersion + "\n" +
            "Project       : " + Application.productName + "\n" +
            "Platform      : " + Application.platform + "\n" +
            "Time          : " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        );
    }
}
