using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns target stacks around the player while the game state is GAMEPLAY.
/// Each target appears at a random distance in [minDist, maxDist] meters along
/// a direction inside the spawn cone built around the player's FORWARD AT
/// CAPTURE TIME — the rig pose when the round starts, re-captured whenever the
/// gyro recalibrates → the cone is a FIXED world sector that never chases the
/// live camera rotation:
/// tilted up/down up to +/-angleUD degrees about transform.right, and
/// left/right up to +/-angleLR degrees about transform.up, then clamped so no
/// target ever spawns below groundLevel. Spawns repeat at a random interval
/// in [minInterval, maxInterval] seconds, and every target despawns by itself
/// after livetime seconds if it was not shot before then.
///
/// Run control is state-driven: starts spawning on GameState.GAMEPLAY, stops
/// (and clears any live targets) on MENU/END — this is the SpawnManager's
/// cleanup role from the architecture. StartSpawning()/StopSpawning() also
/// work manually if no GameManager is present.
/// </summary>
public class SpawnManager : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Prefab to spawn as the target (the CubeStack stack).")]
    public GameObject targetPrefab;

    [Tooltip("Transform targets are measured/aimed from. Left empty: falls back to Camera.main.")]
    public Transform player;

    [Header("Spawn area (shell sector around the player)")]
    [Tooltip("Minimum distance from the player, in meters.")]
    [Range(0.5f, 50f)]
    public float minDist = 1.5f;

    [Tooltip("Maximum distance from the player, in meters.")]
    [Range(0.5f, 50f)]
    public float maxDist = 6f;

    [Tooltip("Max up/down tilt in degrees ABOUT transform.right. The cone spans +/- this around the player's forward.")]
    [Range(0f, 90f)]
    public float angleUD = 15f;

    [Tooltip("Max left/right tilt in degrees ABOUT transform.up. The cone spans +/- this around the player's forward.")]
    [Range(0f, 90f)]
    public float angleLR = 30f;

    [Tooltip("Minimum world Y for a spawn point — targets never appear below this height.")]
    public float groundLevel = 0f;

    [Tooltip("Minimum spacing between targets, in meters. A spawn point closer than this to any live target is rejected and a new one is drawn immediately.")]
    public float minTargetSpacing = 1f;

    [Header("Timing")]
    [Tooltip("Minimum seconds between spawns.")]
    public float minInterval = 3f;

    [Tooltip("Maximum seconds between spawns.")]
    public float maxInterval = 6f;

    [Tooltip("Seconds a target stays alive if not shot; after this it despawns (cleanup).")]
    public float livetime = 10f;

    readonly List<GameObject> liveTargets = new List<GameObject>();
    Coroutine spawnRoutine;

    /// <summary>Broadcast when a target's livetime expires without having been shot — the -1-life moments.</summary>
    public static event System.Action TargetExpired;

    GyroCameraController gyro;   // found in Start; re-captures the cone on recalibrate
    Quaternion coneBase;         // player rotation captured at round start / recalibration

    /// <summary>
    /// Freezes the spawn cone's anchor at the player's current rig rotation.
    /// Called when a round starts and whenever the user recalibrates the gyro,
    /// so the cone follows the recalibrated heading rather than the live camera.
    /// </summary>
    public void CaptureConeBase()
    {
        if (player != null)
            coneBase = player.rotation;
    }

    void Start()
    {
        if (player == null)
        {
            Camera cam = Camera.main;
            // Prefer the rig root (the Player): it carries the yaw and sits at
            // the same position as the camera; fall back to the camera itself.
            if (cam != null)
                player = cam.transform.parent != null ? cam.transform.parent : cam.transform;
        }

        // Safe by Start-time: GameManager.Awake runs before any Start().
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged += OnStateChanged;

        // Track the gyro's recalibrations so the cone re-anchors with them.
        gyro = Camera.main != null ? Camera.main.GetComponent<GyroCameraController>() : null;
        if (gyro != null)
            gyro.OnRecalibrated += CaptureConeBase;

        StartSpawning(); // no-op stop if state is not GAMEPLAY — covers mid-game attach
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.GAMEPLAY)
            StopSpawning();
    }

    void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged -= OnStateChanged;
        if (gyro != null)
            gyro.OnRecalibrated -= CaptureConeBase;
    }

    void OnStateChanged(GameState state)
    {
        if (state == GameState.GAMEPLAY) StartSpawning();
        else StopSpawning();
    }

    /// <summary>Begin the random-interval spawn loop.</summary>
    public void StartSpawning()
    {
        if (spawnRoutine != null) return;
        CaptureConeBase(); // cone anchored at the round's initial rig pose
        spawnRoutine = StartCoroutine(SpawnLoop());
    }

    /// <summary>Stop the loop and clear every live target (cleanup).</summary>
    public void StopSpawning()
    {
        if (spawnRoutine != null)
        {
            StopCoroutine(spawnRoutine);
            spawnRoutine = null;
        }
        ClearTargets();
    }

    IEnumerator SpawnLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(minInterval, maxInterval));
            SpawnTarget();
        }
    }

    void SpawnTarget()
    {
        if (targetPrefab == null)
        {
            Debug.LogError("[SpawnManager] targetPrefab not assigned — stopping spawning.");
            StopSpawning();
            return;
        }
        if (player == null)
        {
            Debug.LogError("[SpawnManager] player not assigned and no Camera.main — stopping spawning.");
            StopSpawning();
            return;
        }

        if (!TryFindSpawnPosition(out Vector3 position))
            return; // no valid spot this tick; the next interval retries

        // Face the spawn point's "forward" at the player so the target presents itself.
        Quaternion look = Quaternion.LookRotation(player.position - position);

        GameObject target = Instantiate(targetPrefab, position, look);
        liveTargets.Add(target);
        StartCoroutine(DespawnAfter(target, livetime));
    }

    /// <summary>Retries allowed per spawn tick before skipping the interval.</summary>
    const int MaxSpawnRetries = 48;

    /// <summary>
    /// Draws new random samples inside the spawn cone until one is at least
    /// minTargetSpacing away from every live target (checked after the groundLevel
    /// clamp, on the final spawn point). Bounded: if free space is too tight the
    /// tick is skipped and the next interval retries — an unbounded reroll loop
    /// would hard-freeze the game when the spawn cone collapses to nothing.
    /// </summary>
    bool TryFindSpawnPosition(out Vector3 position)
    {
        for (int attempt = 0; attempt < MaxSpawnRetries; attempt++)
        {
            float dist = Random.Range(minDist, maxDist);
            float pitch = Random.Range(-angleUD, angleUD); // up/down about transform.right
            float yaw = Random.Range(-angleLR, angleLR);   // left/right about transform.up

            // Cone around the FROZEN capture-time pose (coneBase), not the live
            // rotation — the sector stays fixed while the user looks around.
            Quaternion orientation = coneBase
                * Quaternion.AngleAxis(pitch, Vector3.right)
                * Quaternion.AngleAxis(yaw, Vector3.up);

            position = player.position + orientation * Vector3.forward * dist;
            position.y = Mathf.Max(position.y, groundLevel);

            if (IsFarEnough(position))
                return true;
        }

        position = default;
        Debug.LogWarning("[SpawnManager] No spawn point at least " + minTargetSpacing
            + "m from existing targets after " + MaxSpawnRetries + " attempts — skipping this spawn interval.");
        return false;
    }

    bool IsFarEnough(Vector3 position)
    {
        float minSpacingSq = minTargetSpacing * minTargetSpacing;
        for (int i = 0; i < liveTargets.Count; i++)
        {
            GameObject existing = liveTargets[i];
            if (existing == null) continue; // already despawned, still in the list
            if ((existing.transform.position - position).sqrMagnitude < minSpacingSq)
                return false;
        }
        return true;
    }

    IEnumerator DespawnAfter(GameObject target, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (target == null) yield break; // already gone (struck-cleanup ran earlier)
        var tc = target.GetComponent<TargetController>();
        if (tc != null && tc.IsStruck) yield break; // struck late, still sweeping: not an expiry
        liveTargets.Remove(target);
        Destroy(target);
        TargetExpired?.Invoke(); // ScoreManager: -1 life
    }

    void ClearTargets()
    {
        for (int i = 0; i < liveTargets.Count; i++)
            if (liveTargets[i] != null)
                Destroy(liveTargets[i]);
        liveTargets.Clear();
    }
}