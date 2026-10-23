using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sits on the Ground object (needs its collider to be non-trigger). Anything
/// rigidbody-backed that physically touches the ground — struck-target cubes
/// after they fall, stray bullets, whatever else gets knocked to the floor —
/// is destroyed 2 seconds (cleanupDelay) after its FIRST contact. Deliberately
/// tag-agnostic: debris gets swept the same way regardless of what it belongs to.
///
/// Only the colliding piece is destroyed — the empty spawn-instance shell of a
/// struck target is SpawnManager's responsibility (its livetime timer still
/// holds and removes the root). Destroying an object that other cleanup already
/// removed (e.g. the scene clear on a state change) is a safe no-op, so the
/// pending schedule can never double-destroy anything.
/// </summary>
public class DebrisCleanupManager : MonoBehaviour
{
    [Tooltip("Seconds after first ground contact before the object is destroyed.")]
    public float cleanupDelay = 2f;

    readonly HashSet<GameObject> scheduled = new HashSet<GameObject>();

    void OnCollisionEnter(Collision other)
    {
        // Schedule only once per object; repeat contacts while it rolls or rests
        // must not reset or duplicate the 2-second timer.
        if (scheduled.Add(other.gameObject))
            Destroy(other.gameObject, cleanupDelay);
    }
}