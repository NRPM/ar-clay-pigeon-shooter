using UnityEngine;

/// <summary>
/// View layer: shows/hides one panel per game state. Manages UI updates only —
/// button handlers live on the components they act on (GameManager for state
/// transitions, GyroCameraController for recalibration), so wire buttons in
/// the inspector directly:
///   start     -> GameManager.StartGame()
///   pause     -> GameManager.PauseGame()
///   resume    -> GameManager.ResumeGame()
///   restart   -> GameManager.RestartGame()
///   calibrate -> GyroCameraController.Recalibrate()   (menu, pause and end panels)
/// On boot (MENU) only menuPanel (Calibrate + Start) is visible; gameplay only
/// shows the pause button; PAUSE shows Resume + Recalibrate; END shows
/// Restart + Recalibrate.
/// Panels can be left unassigned while the UI is still being built — the
/// manager simply skips them.
/// </summary>
public class UIManager : MonoBehaviour
{
    [Header("State panels (empty = skipped while the UI is being built)")]
    [Tooltip("Shown only in MENU: Calibrate + Start buttons.")]
    public GameObject menuPanel;

    [Tooltip("Shown only in GAMEPLAY: the pause button (HUD).")]
    public GameObject gameplayPanel;

    [Tooltip("Shown only in PAUSE: Resume + Recalibrate buttons.")]
    public GameObject pausePanel;

    [Tooltip("Shown only in END: Restart + Recalibrate buttons.")]
    public GameObject endPanel;

    void Start()
    {
        // Safe by Start-time: GameManager.Awake runs before any Start().
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged += ApplyState;

        ApplyState(GameManager.Instance != null
            ? GameManager.Instance.CurrentState
            : GameState.MENU); // no GameManager present: default to the menu layout
    }

    void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged -= ApplyState;
    }

    void ApplyState(GameState state)
    {
        Set(menuPanel, state == GameState.MENU);
        Set(gameplayPanel, state == GameState.GAMEPLAY);
        Set(pausePanel, state == GameState.PAUSE);
        Set(endPanel, state == GameState.END);
    }

    void Set(GameObject panel, bool active)
    {
        if (panel != null && panel.activeSelf != active)
            panel.SetActive(active);
    }
}