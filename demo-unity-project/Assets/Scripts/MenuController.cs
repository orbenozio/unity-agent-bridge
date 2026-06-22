using UnityEngine;
using UnityEngine.UI;

// Wires the menu buttons to a log message at runtime, so a click proves the
// whole loop works end to end. Attach this to the MainMenu (Canvas) GameObject;
// it finds the buttons by name and adds onClick listeners when Play starts.
public class MenuController : MonoBehaviour
{
    private static readonly string[] Buttons =
    {
        "PlayButton", "SettingsButton", "QuitButton", "CreditsButton",
    };

    private void Start()
    {
        foreach (var name in Buttons)
            Wire(name);
    }

    private void Wire(string name)
    {
        var go = GameObject.Find(name);
        if (go == null)
        {
            Debug.LogWarning($"[Menu] button not found: {name}");
            return;
        }

        var button = go.GetComponent<Button>();
        if (button == null)
        {
            Debug.LogWarning($"[Menu] no Button component on {name}");
            return;
        }

        button.onClick.AddListener(() => Debug.Log($"[Menu] {name} clicked"));
    }
}
