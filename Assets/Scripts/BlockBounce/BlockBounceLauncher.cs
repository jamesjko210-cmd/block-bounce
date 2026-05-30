// BlockBounceLauncher.cs — entry point. On Play, shows a menu to pick the
// 2D version (BlockBounceGame) or the 3D version (BlockBounceGame3D).
// Both share the same engine (BBCore.cs); only the rendering differs.

using UnityEngine;

public class BlockBounceLauncher : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<BlockBounceLauncher>() != null) return;
        if (FindFirstObjectByType<BlockBounceGame>() != null) return;
        if (FindFirstObjectByType<BlockBounceGame3D>() != null) return;
        new GameObject("BlockBounceLauncher").AddComponent<BlockBounceLauncher>();
    }

    bool launched;
    GUIStyle stTitle, stSub, stBtn;

    void OnGUI()
    {
        if (launched) return;

        if (stTitle == null)
        {
            stTitle = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.17f, 0.12f, 0.30f) } };
            stSub   = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.44f, 0.39f, 0.56f) } };
            stBtn   = new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = FontStyle.Bold };
        }

        // soft background so the menu reads on any scene
        var c = GUI.color; GUI.color = new Color(1f, 0.96f, 0.93f, 1f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = c;

        float cx = Screen.width / 2f, cy = Screen.height / 2f;
        GUI.Label(new Rect(cx - 300, cy - 150, 600, 60), "Block Bounce", stTitle);
        GUI.Label(new Rect(cx - 300, cy - 92, 600, 24), "Choose a version", stSub);

        if (GUI.Button(new Rect(cx - 230, cy - 30, 210, 90), "▶  Play 2D", stBtn))
        {
            new GameObject("BlockBounceGame").AddComponent<BlockBounceGame>();
            launched = true;
        }
        if (GUI.Button(new Rect(cx + 20, cy - 30, 210, 90), "▣  Play 3D", stBtn))
        {
            new GameObject("BlockBounceGame3D").AddComponent<BlockBounceGame3D>();
            launched = true;
        }

        GUI.Label(new Rect(cx - 300, cy + 80, 600, 24), "Same game, two renderers. Stop & re-Play to switch.", stSub);
    }
}
