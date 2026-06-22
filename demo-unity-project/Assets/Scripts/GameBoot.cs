using UnityEngine;

// A tiny "boot" that runs when the game enters Play Mode. It contains an INTENTIONAL
// NullReferenceException for the live debugging demo: ask Claude to "read the console
// and fix it". Claude sees the stack trace via read_console / run_playmode and applies
// the minimal fix (a null check). See docs/webinar-script.md, section 3.
public static class GameBoot
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        // There is no "Player" in the empty demo scene, so Find returns null.
        var player = GameObject.Find("Player");

        // BUG: dereferencing a null GameObject throws NullReferenceException.
        // FIX (live): if (player == null) { Debug.LogWarning("No Player in scene"); return; }
        Debug.Log("Spawning player at " + player.transform.position);
    }
}
