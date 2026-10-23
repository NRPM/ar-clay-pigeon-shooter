using System;
using UnityEngine;

/// <summary>
/// Top-level game flow: MENU -> GAMEPLAY <-> PAUSE -> END, with restart back to
/// GAMEPLAY.
/// MENU is set when the app first opens. UI code shows/hides panels by
/// subscribing to OnStateChanged and calls the public transition methods from
/// button handlers:
///   StartGame()    - MENU start button -> GAMEPLAY
///   PauseGame()    - gameplay pause button -> PAUSE
///   ResumeGame()   - pause resume button -> GAMEPLAY
///   EndGame()      - gameplay code calls this when the round ends -> END
///   RestartGame()  - END restart button -> GAMEPLAY
/// Invalid transitions are rejected with a warning (e.g. RestartGame from MENU),
/// which also catches button wiring mistakes early.
/// </summary>
public enum GameState
{
    MENU,
    GAMEPLAY,
    PAUSE,
    END
}

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    /// <summary>Fired on every state change; argument is the new state.</summary>
    public event Action<GameState> OnStateChanged;

    /// <summary>State the app is currently in.</summary>
    public GameState CurrentState { get; private set; } = GameState.MENU;

    private void Awake()
    {
        // Singleton: ensure exactly one live instance (first one wins).
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject); // survives scene reloads/restarts

        // First app open always starts in MENU.
        SetState(GameState.MENU);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>MENU start button handler: enters GAMEPLAY.</summary>
    public void StartGame()
    {
        if (CurrentState != GameState.MENU)
        {
            Debug.LogWarning($"[GameManager] StartGame called from {CurrentState}, expected MENU — ignored.");
            return;
        }

        SetState(GameState.GAMEPLAY);
    }

    /// <summary>Called by gameplay code when the round ends: enters END.</summary>
    public void EndGame()
    {
        if (CurrentState != GameState.GAMEPLAY)
        {
            Debug.LogWarning($"[GameManager] EndGame called from {CurrentState}, expected GAMEPLAY — ignored.");
            return;
        }

        SetState(GameState.END);
    }

    /// <summary>Gameplay pause button handler: enters PAUSE.</summary>
    public void PauseGame()
    {
        if (CurrentState != GameState.GAMEPLAY)
        {
            Debug.LogWarning($"[GameManager] PauseGame called from {CurrentState}, expected GAMEPLAY — ignored.");
            return;
        }

        SetState(GameState.PAUSE);
    }

    /// <summary>Pause resume button handler: back into GAMEPLAY.</summary>
    public void ResumeGame()
    {
        if (CurrentState != GameState.PAUSE)
        {
            Debug.LogWarning($"[GameManager] ResumeGame called from {CurrentState}, expected PAUSE — ignored.");
            return;
        }

        SetState(GameState.GAMEPLAY);
    }

    /// <summary>END restart button handler: back into GAMEPLAY.</summary>
    public void RestartGame()
    {
        if (CurrentState != GameState.END)
        {
            Debug.LogWarning($"[GameManager] RestartGame called from {CurrentState}, expected END — ignored.");
            return;
        }

        SetState(GameState.GAMEPLAY);
    }

    private void SetState(GameState state)
    {
        if (CurrentState == state)
            return; // MENU is the initial value; invoking on Awake would be a no-op echo

        CurrentState = state;
        OnStateChanged?.Invoke(state);
    }
}