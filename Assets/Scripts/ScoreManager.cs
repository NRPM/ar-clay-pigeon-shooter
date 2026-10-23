using TMPro;
using UnityEngine;

/// <summary>
/// Central score/lives/highscore bookkeeping. Listens to the game's event
/// sources and keeps the UI texts in step:
///
/// - Score: incremented by 1 each time a bullet reaches a target (a strike =
///   TargetController's trigger activation, broadcast via TargetController.TargetStruck).
///   Displayed as "Score: <score>".
/// - Lives: starts at 3. Decremented by 1 whenever a target is destroyed without
///   being shot — the livetime expiry, broadcast via SpawnManager.TargetExpired.
///   Displayed as "Lives: <lives>". At 0, the game ends (GameManager.EndGame → END).
/// - High score: one PlayerPrefs integer under the "HighScore" key, written on
///   every END transition when the finished score beats the stored value
///   (starting from 0 when no key exists). Both texts (menu panel + end panel)
///   display the same number as "High Score: <n>".
///
/// - Limited ammo: the shooter spends one bullet per shot (ShotController.ShotFired)
///   and regains 2 per strike. Stock = defaultAmmo (slider) on each fresh round;
///   displayed as "Ammo: <n>" on the Gameplay HUD. Hitting 0 ends the game.
///
/// Reset semantics: score/lives/ammo reset to the fresh-round values when the game
/// enters GAMEPLAY from anything other than PAUSE (start/restart = new round;
/// resume = the same round continues, nothing resets). Texts refresh on every
/// state change and once at boot.
///
/// Text references can be left empty while the UI is still being built; the
/// script skips them safely. Strikes and expiries only count while the state is
/// GAMEPLAY, so stray bullets from an ended run pay/cash in on nothing.
/// </summary>
public class ScoreManager : MonoBehaviour
{
    [Tooltip("Score readout (Gameplay HUD): displays 'Score: <n>'.")]
    public TMP_Text scoreText;

    [Tooltip("Lives readout: displays 'Lives: <n>'.")]
    public TMP_Text livesText;

    [Tooltip("High score display in the MENU panel.")]
    public TMP_Text menuHighScoreText;

    [Tooltip("High score display in the END panel.")]
    public TMP_Text endHighScoreText;

    [Tooltip("Final score display in the END panel (written when the game ends).")]
    public TMP_Text endScoreText;

    [Tooltip("Ammo readout (Gameplay HUD): displays 'Ammo: <n>'.")]
    public TMP_Text ammoText;

    [Tooltip("Bullets in stock when a fresh round begins (each shoot spends one; each target hit pays 2).")]
    [Range(1, 30)]
    public int defaultAmmo = 5;

    const string HighScoreKey = "HighScore";
    const int StartingLives = 3;

    int score;
    int lives = StartingLives;
    int currentAmmo; // restocked to defaultAmmo on every fresh round
    GameState lastState = GameState.MENU; // to distinguish start/restart from resume

    void Start()
    {
        TargetController.TargetStruck += OnTargetStruck;
        SpawnManager.TargetExpired += OnTargetExpired;
        ShotController.ShotFired += OnShotFired;
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged += OnStateChanged;
        currentAmmo = defaultAmmo;
        RefreshScore();
        RefreshLives();
        RefreshAmmo();
        RefreshHighScores();
    }

    void OnDestroy()
    {
        TargetController.TargetStruck -= OnTargetStruck;
        SpawnManager.TargetExpired -= OnTargetExpired;
        ShotController.ShotFired -= OnShotFired;
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged -= OnStateChanged;
    }

    void OnTargetStruck()
    {
        if (GameManager.Instance == null || GameManager.Instance.CurrentState != GameState.GAMEPLAY)
            return; // stray bullets from an ended run pay nothing mid-state-change

        score++;
        currentAmmo += 2; // a successful hit refills two bullets
        RefreshScore();
        RefreshAmmo();
    }

    void OnShotFired()
    {
        currentAmmo--;
        RefreshAmmo();
        if (currentAmmo <= 0 && GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.GAMEPLAY)
            GameManager.Instance.EndGame(); // the last bullet is in the air; the game ends now
    }

    void OnTargetExpired()
    {
        if (GameManager.Instance == null || GameManager.Instance.CurrentState != GameState.GAMEPLAY)
            return;

        lives--;
        RefreshLives();
        if (lives <= 0 && GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.GAMEPLAY)
            GameManager.Instance.EndGame();
    }

    void OnStateChanged(GameState state)
    {
        if (state == GameState.END)
        {
            SaveHighScore();
            if (endScoreText != null)
                endScoreText.SetText($"Score: {score}"); // the run's final tally
        }

        if (state == GameState.GAMEPLAY && lastState != GameState.PAUSE)
        {
            // Fresh round (start or restart), not a resume.
            score = 0;
            lives = StartingLives;
            currentAmmo = defaultAmmo;
        }

        lastState = state;
        RefreshScore();
        RefreshLives();
        RefreshAmmo(); // the ammo text must follow fresh-round restocks (restart shows the default, not the last in-flight value)
        RefreshHighScores();
    }

    void SaveHighScore()
    {
        if (score > PlayerPrefs.GetInt(HighScoreKey, 0))
        {
            PlayerPrefs.SetInt(HighScoreKey, score);
            PlayerPrefs.Save();
        }
    }

    void RefreshScore()
    {
        if (scoreText != null)
            scoreText.SetText($"Score: {score}");
    }

    void RefreshLives()
    {
        if (livesText != null)
            livesText.SetText($"Lives: {lives}");
    }

    void RefreshAmmo()
    {
        if (ammoText != null)
            ammoText.SetText($"Ammo: {currentAmmo}");
    }

    void RefreshHighScores()
    {
        // No key yet (the very first run) counts as a stored high score of 0.
        string display = $"High Score: {PlayerPrefs.GetInt(HighScoreKey, 0)}";

        if (menuHighScoreText != null)
            menuHighScoreText.SetText(display);
        if (endHighScoreText != null)
            endHighScoreText.SetText(display);
    }
}