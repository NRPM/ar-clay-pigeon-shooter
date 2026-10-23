using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem; // Pointer — this project uses the new Input System only

/// <summary>
/// Fires the bullet on a tap while in GAMEPLAY. The muzzle is the offset point
/// under the crosshair image (a UI-space position converted to world space at
/// spawnDepth meters along the camera forward).
///
/// Kill logic is raycast-driven and fps-independent: every shot casts an
/// infinite ray from the camera that DOES see trigger colliders; when it lands
/// on a target's shell, the flight time = (shell distance − spawnDepth) /
/// bulletSpeed, TargetController.Detonate(impactTime) is scheduled, and the
/// visual bullet is destroyed as it arrives. Misses (ground, walls, sky, the
/// debris of struck targets) just launch the bullet: the ground sweep and
/// BulletController's timer stay its only deaths.
///
/// Attach to the Main Camera (or any always-active object), then drag in:
///   spawnPoint   - the offset point (child of the crosshair image)
///   bulletPrefab - the Bullet prefab (Rigidbody required)
/// Bullet gravity/size are prefab-side settings, not touched here.
/// </summary>
public class ShotController : MonoBehaviour
{
    [Header("Shooting")]
    [Tooltip("Where bullets appear: the offset point (child of the crosshair image).")]
    public RectTransform spawnPoint;

    [Tooltip("Bullet prefab to launch (needs a Rigidbody).")]
    public GameObject bulletPrefab;

    [Tooltip("Camera the crosshair aims from. Empty = Camera.main.")]
    public Camera aimCamera;

    [Tooltip("Meters from the camera used to convert the UI point into world space (the muzzle's distance).")]
    [Range(0.2f, 3f)]
    public float spawnDepth = 1f;

    [Tooltip("Bullet speed in meters per second.")]
    [Range(1f, 50f)]
    public float bulletSpeed = 15f;

    Camera cam;

    void Awake()
    {
        cam = aimCamera != null ? aimCamera : Camera.main;
    }

    void Update()
    {
        if (GameManager.Instance == null)
        {
            Debug.LogWarning("[ShotController] No GameManager in scene — firing only in GAMEPLAY, so nothing will fire.");
            enabled = false;
            return;
        }
        if (GameManager.Instance.CurrentState != GameState.GAMEPLAY)
            return;

        cam = aimCamera != null ? aimCamera : (cam == null ? Camera.main : cam);
        if (spawnPoint == null || bulletPrefab == null || cam == null)
            return; // references still being wired in the inspector

        Pointer pointer = Pointer.current;
        if (pointer == null || !pointer.press.wasPressedThisFrame)
            return;

        // A tap on the pause button (or any UI) is not a shot.
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(pointer.deviceId))
            return;

        Fire();
    }

    /// <summary>Broadcast on every shot fired (hit or miss) — the limited-ammo spend moments.</summary>
    public static event System.Action ShotFired;

    void Fire()
    {
        // 1) Muzzle: the crosshair offset point's screen position, placed at
        //    spawnDepth meters along the camera forward.
        Vector3 screen = spawnPoint.position; // overlay canvas: x/y are screen pixels
        Vector3 spawnPosition = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, spawnDepth));

        // 2) The kill ray: infinite, from the player (the camera), and it DOES
        //    see trigger colliders — the target's shell trigger is a hit surface.
        //    Whichever surface comes first wins; only a hit carrying a live
        //    (not-yet-struck) TargetController schedules the timed detonation.
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        RaycastHit hit;
        bool hitSomething = Physics.Raycast(ray, out hit, Mathf.Infinity,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);

        TargetController struckTarget = null;
        float flightTime = 0f;
        if (hitSomething)
        {
            var tc = hit.collider.GetComponentInParent<TargetController>();
            if (tc != null && !tc.IsStruck)
            {
                struckTarget = tc;
                // Bullet flight time to the shell; spawnDepth subtracted because
                // the muzzle sits that far down the same line. (Literal
                // "distance/speed" variant = drop the spawnDepth term.)
                flightTime = Mathf.Max(0.01f, (hit.distance - spawnDepth) / bulletSpeed);
            }
        }

        // 3) Aim: toward the hit point, never behind the muzzle; a miss goes
        //    100 m down the line.
        float aimDistance = hitSomething ? Mathf.Max(hit.distance, spawnDepth + 0.05f) : 100f;
        Vector3 aimPoint = ray.origin + ray.direction * aimDistance;

        Vector3 direction = aimPoint - spawnPosition;
        direction = direction.sqrMagnitude < 1e-6f
            ? cam.transform.forward
            : direction.normalized;

        // 4) Launch: straight-line velocity along the aim; the bullet prefab's
        //    gravity setting then curves it or not.
        GameObject bullet = Instantiate(bulletPrefab, spawnPosition, Quaternion.LookRotation(direction));
        if (bullet.TryGetComponent<Rigidbody>(out Rigidbody rb))
            rb.linearVelocity = direction * bulletSpeed;
        else
            Debug.LogWarning("[ShotController] Bullet prefab has no Rigidbody — it will not fly.");

        // 5) Timed detonation: the shell explodes after the computed flight time —
        //    fps-independent — and the visual bullet dies as it arrives. Misses
        //    fly on to their ground sweep / lifetime cleanup. The ammo was spent:
        //    ScoreManager tracks the stock and ends the round at zero.
        ShotFired?.Invoke();
        if (struckTarget != null)
        {
            struckTarget.Detonate(flightTime);
            Destroy(bullet, flightTime);
        }
    }
}