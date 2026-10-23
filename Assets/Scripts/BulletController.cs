using UnityEngine;

/// <summary>
/// Attach to the Bullet prefab. The bullet's life timer: a bullet — a miss, or
/// one still travelling its timed impact path — dies after maxLifetime seconds
/// if nothing removed it first. In the raycast system the scheduled deaths are:
/// ShotController destroys the visual at the computed impact time, the shell
/// detonates via TargetController.Detonate, and ground hits are swept by
/// DebrisCleanupManager. Unity's Destroy on an already-destroyed object is a
/// silent no-op, so these paths can never conflict or throw.
/// </summary>
public class BulletController : MonoBehaviour
{
    [Tooltip("Seconds until a bullet is destroyed if it has not reached a target (and the ground sweep hasn't taken it first).")]
    public float maxLifetime = 10f;

    void Start()
    {
        Destroy(gameObject, maxLifetime);
    }
}