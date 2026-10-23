using System.Collections;
using UnityEngine;

/// <summary>
/// Target hit reaction. Attach to the CubeStack root — the same object that
/// carries the isTrigger BoxCollider (the raycast-visible shell). Strikes are
/// raycast-driven now: ShotController casts from the player and, when its ray
/// lands here, schedules Detonate(delay) with the computed bullet flight time.
/// No physics contact or trigger entry destroys the target anymore — the
/// bullet is purely the visual of the flight.
///
/// Inspector values:
///   explosionForce  - impulse magnitude per cube (AddExplosionForce, falloff
///                     by distance from the center).
///   explosionRadius - how far the blast reaches; must cover the whole body,
///                     or AddExplosionForce applies force to nothing.
///
/// Audio: PlayDestructAudio() plays the AudioSource on this same object
/// (fetched automatically, no inspector wiring); it runs when the target is
/// destroyed by a bullet.
///
/// Struck lifecycle: the trigger box itself is destroyed at the strike (no
/// invisible ghost zone eats later bullets), and this empty parent root
/// self-destructs once every cube has been swept off the floor by
/// DebrisCleanupManager. SpawnManager's livetime timer and ClearTargets remain
/// the fallbacks for un-struck targets and state changes.
/// </summary>
public class TargetController : MonoBehaviour
{
    [Header("Explosion (impulse on each cube)")]
    [Tooltip("Impulse magnitude pushed on every cube from the cube-center, falling off with distance.")]
    [Range(0f, 100f)]
    public float explosionForce = 10f;

    [Tooltip("Blast range in meters. 0.1 minimum — AddExplosionForce applies no force outside the radius.")]
    [Range(0.1f, 20f)]
    public float explosionRadius = 2f;

    [SerializeField] GameObject minimapCanvas;

    Rigidbody[] cubes;
    bool struck; // set on the first bullet strike; gates the empty-shell cleanup watch
    AudioSource destructAudio; // same-GameObject source for the destruction sound

    /// <summary>Broadcast to all listeners each time a bullet's strike fires this trigger (+1-score moments).</summary>
    public static event System.Action TargetStruck;

    /// <summary>True once this target has been struck by at least one bullet (used to suppress livetime-expiry life-loss).</summary>
    public bool IsStruck => struck;

    void Awake()
    {
        // The stack's cube bodies (root and StackBody carry no Rigidbody, so
        // this collects exactly the 64 cubes regardless of hierarchy depth).
        cubes = GetComponentsInChildren<Rigidbody>();

        // Default state: cubes hover exactly where the prefab places them —
        // kinematic (immune to forces) and gravity off (prefab default).
        for (int i = 0; i < cubes.Length; i++)
            cubes[i].isKinematic = true;

        if (cubes.Length == 0)
            Debug.LogWarning("[TargetController] No cube rigidbodies found under " + name);

        // The destruct audio lives on this very GameObject.
        destructAudio = GetComponent<AudioSource>();
        if (destructAudio == null)
            Debug.LogWarning("[TargetController] No AudioSource on this object — PlayDestructAudio will be silent.");
    }

    /// <summary>
    /// Scheduled by ShotController when its ray lands on this shell. After
    /// <paramref name="delay"/> (the computed bullet flight time): the cubes go
    /// dynamic + gravity + blast, the score fires, the minimap marker is taken
    /// down, and the shell trigger destroys itself immediately — the parent
    /// root waits for the cubes' sweep (the Update watch). One detonation per
    /// target; repeat schedules are ignored.
    /// </summary>
    public void Detonate(float delay)
    {
        if (struck) return;
        StartCoroutine(DetonateAfter(Mathf.Max(0.01f, delay)));
    }

    IEnumerator DetonateAfter(float delay)
    {
        yield return new WaitForSeconds(delay);

        struck = true;
        TargetStruck?.Invoke(); // ScoreManager: +1 (the ray-activated trigger)
        Explode();
        PlayDestructAudio(); // destroyed by the bullet: the destruct sound goes here

        if (minimapCanvas != null)
            Destroy(minimapCanvas); // the struck target's map marker goes with the strike

        BoxCollider box = GetComponent<BoxCollider>();
        if (box != null)
            Destroy(box); // the shell dies at the impact moment, not after the sweep
    }

    void Explode()
    {
        Vector3 center = AverageCubePosition();

        for (int i = 0; i < cubes.Length; i++)
        {
            Rigidbody cube = cubes[i];
            if (cube == null)
                continue; // already swept by DebrisCleanupManager or cleared with the instance
            cube.useGravity = true;   // struck target is allowed to fall...
            cube.isKinematic = false; // ...only once it's dynamic: kinematic bodies ignore forces,
                                      // so uncheck this in the same tick, then blast it outward (impulse).
            cube.AddExplosionForce(explosionForce, center, explosionRadius, 0f, ForceMode.Impulse);
        }
    }

    void Update()
    {
        if (!struck) return;

        // The empty parent shell dies once every cube has been destroyed by other
        // cleanup (DebrisCleanupManager sweeps each ~2 s after floor contact).
        foreach (Rigidbody cube in cubes)
            if (cube != null)
                return; // still debris in flight or awaiting sweep

        Destroy(gameObject); // all cubes gone — take the hollow root down too
    }

    /// <summary>
    /// Plays this target's destruction sound from the AudioSource attached to
    /// the same GameObject (fetched automatically in Start; no inspector link).
    /// Called when the target is destroyed by a bullet.
    /// </summary>
    public void PlayDestructAudio()
    {
        if (destructAudio != null)
            destructAudio.Play();
    }

    Vector3 AverageCubePosition()
    {
        if (cubes == null || cubes.Length == 0)
            return transform.position;

        Vector3 sum = Vector3.zero;
        int alive = 0;
        for (int i = 0; i < cubes.Length; i++)
        {
            Rigidbody cube = cubes[i];
            if (cube == null)
                continue; // destroyed since Awake: swept by DebrisCleanupManager or cleared with the scene
            sum += cube.position;
            alive++;
        }

        return alive > 0 ? sum / alive : transform.position;
    }
}
