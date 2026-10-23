using UnityEngine;
using UnityEngine.InputSystem; // AttitudeSensor — this project uses the new Input System only

/// <summary>
/// Gyro-driven FPS rig with yaw/pitch separation. The camera lives under an
/// empty Player parent; the device's real-world yaw (turning about the vertical
/// axis) writes only the PLAYER's Y rotation, while the real-world pitch
/// (tilting about transform.right) writes only the CAMERA's local pitch, and
/// the device's roll (leaning sideways) writes only the CAMERA's local roll —
/// the Player's rotation stays purely horizontal and upright at all times.
///
/// The decomposition runs on the CALIBRATED rotation (calib x current — the
/// same rotation the single-quaternion version wrote to the camera), never on
/// the raw converted attitude: the portrait fix alone points that forward
/// straight down, and consuming it raw inverted the pitch.
///
/// Calibrated at boot: the first valid sensor sample anchors the device to the
/// pose saved in the scene (player's canonical yaw + camera's canonical local
/// pitch). Recalibrate() re-solves the calibration from the next fresh sample —
/// the rig snaps back to the canonical pose and the world visibly re-centers on
/// the direction the phone points at press time (MENU Calibrate button). Does
/// not change the game state.
///
/// Portrait orientation. Attach to the camera; the Player resolves from
/// transform.parent when the inspector field is left empty.
/// Does nothing in the editor (no sensor) — the scene rig pose stays intact.
/// </summary>
public class GyroCameraController : MonoBehaviour
{
    [Tooltip("The rig root that receives YAW. Empty = this camera's parent.")]
    public Transform player;

    [Tooltip("Orientation fix so 'phone upright in portrait' maps to looking straight ahead.")]
    static readonly Quaternion PortraitFix = Quaternion.Euler(90f, 0f, 0f);

    [Tooltip("Smoothing time in seconds. 0 = raw sensor values.")]
    [Range(0f, 0.25f)]
    public float smoothTime = 0.05f;

    [Tooltip("Seconds to wait after enabling the sensor before the boot sample is taken " +
             "(avoids calibrating from the sensor's default value before it delivers real data).")]
    public float calibrationDelay = 0.15f;

    const float MaxPitch = 85f; // sane tilt clamp so the rig can't flip over its own head

    AttitudeSensor attitudeSensor;
    bool sensorActive;
    bool calibrated;
    float enabledAt;

    float canonicalYaw;   // player's yaw as saved by the scene
    float canonicalPitch; // camera's local pitch as saved by the scene
    Quaternion calib;     // maps any device attitude onto its intended world rotation
    float appliedYaw;     // smoothed angles actually written to the rig
    float appliedPitch;
    float appliedRoll;

    /// <summary>
    /// Fired when a recalibration re-solves the mapping (boot calibration
    /// included); fires from Update at the calibrationDelay guard's release —
    /// the smoothing has had ~calibrationDelay to settle, so listeners can
    /// re-anchor to the near-final rig pose. (Spawn cone re-base.)
    /// </summary>
    public event System.Action OnRecalibrated;

    void Start()
    {
        // Unity caps mobile builds at 30fps when Application.targetFrameRate is unset.
        // Match the physical display refresh rate (60/90/120 depending on the device setting).
        Application.targetFrameRate = Mathf.RoundToInt(Screen.currentResolution.refreshRate);

        if (player == null && transform.parent != null)
            player = transform.parent;
        if (player == null)
            Debug.LogWarning("[GyroCameraController] No Player parent — yaw will be applied to the camera only.");

        // Canonical pose = what the scene placed the rig at.
        if (player != null)
            canonicalYaw = player.eulerAngles.y;
        canonicalPitch = transform.localEulerAngles.x;
        if (canonicalPitch > 180f) canonicalPitch -= 360f; // wrap to +/-180
        appliedYaw = canonicalYaw;
        appliedPitch = canonicalPitch;
        appliedRoll = 0f; // the scene camera is saved upright

        attitudeSensor = AttitudeSensor.current;
        if (attitudeSensor != null)
        {
            InputSystem.EnableDevice(attitudeSensor);
            sensorActive = true;
            enabledAt = Time.time;
        }
        else
        {
            Debug.LogWarning("[GyroCameraController] No attitude/gyro sensor on this device or platform.");
        }
    }

    /// <summary>
    /// Re-anchor (MENU Calibrate handler): the next fresh device sample re-solves
    /// the calibration quaternion, zeroing the deltas and snapping the rig back
    /// to the canonical scene pose — the world re-centers around the direction
    /// the phone points at press time. Does not change the game state.
    /// </summary>
    public void Recalibrate()
    {
        calibrated = false;
        enabledAt = Time.time;
    }

    void Update()
    {
        if (!sensorActive) return;

        Quaternion current = PortraitFix * GyroToUnity(attitudeSensor.attitude.ReadValue());

        if (!calibrated)
        {
            if (Time.time - enabledAt < calibrationDelay) return; // let real samples arrive
            calib = ScenePose() * Quaternion.Inverse(current);
            calibrated = true;
            OnRecalibrated?.Invoke(); // listeners (spawn cone) re-anchor to the ~settled pose
        }

        // The intended camera rotation for this attitude — the same rotation the
        // single-quaternion version applied directly (proven correct, so
        // decomposing THIS yields correct yaw and pitch signs).
        Quaternion worldTarget = calib * current;

        // Split it: heading from the horizontal plane, tilt from the vertical
        // component, and the residual lean of the frame's right axis = roll.
        // The pitch clamp (±85°) also guarantees fwd is never parallel to the
        // world up, so the upright-right reference below stays well-defined.
        Vector3 forward = worldTarget * Vector3.forward;
        Vector3 rolledRight = worldTarget * Vector3.right;
        float targetYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;              // about world up
        float targetPitch = -Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg; // about transform.right
        targetPitch = Mathf.Clamp(targetPitch, -MaxPitch, MaxPitch);
        Vector3 uprightRight = Vector3.Cross(Vector3.up, forward).normalized;
        float targetRoll = Vector3.SignedAngle(uprightRight, rolledRight, forward);       // roll about the look axis

        appliedYaw = SmoothAngle(appliedYaw, targetYaw);
        appliedPitch = SmoothAngle(appliedPitch, targetPitch);
        appliedRoll = SmoothAngle(appliedRoll, targetRoll);

        if (player != null)
            player.rotation = Quaternion.Euler(0f, appliedYaw, 0f); // yaw only — stays upright
        transform.localRotation = Quaternion.Euler(appliedPitch, 0f, appliedRoll); // pitch + roll, parent untouched
    }

    // The canonical rig pose exactly as the scene placed it.
    Quaternion ScenePose() =>
        Quaternion.Euler(0f, canonicalYaw, 0f) * Quaternion.Euler(canonicalPitch, 0f, 0f);

    float SmoothAngle(float current, float target)
    {
        if (smoothTime <= 0f) return target;
        float t = 1f - Mathf.Exp(-Time.deltaTime / smoothTime);
        return Mathf.LerpAngle(current, target, t);
    }

    // Attitude sensors are right-handed; Unity is left-handed — flip z and w.
    static Quaternion GyroToUnity(Quaternion q) => new Quaternion(q.x, q.y, -q.z, -q.w);
}
