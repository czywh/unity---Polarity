using UnityEngine;

/// <summary>
/// Orbit follow camera: combines
///   - HorizontalCamera's "smooth target follow + SetTarget focus switching"
///   - FreeLookCamera's "mouse orbit (horizontal + vertical) + scroll-wheel zoom + occlusion avoidance"
///
/// Attach to Main Camera; replaces HorizontalCamera.
/// Character switching is still done by CharacterSwitcher calling SetTarget (interface unchanged).
/// </summary>
public class OrbitFollowCamera : MonoBehaviour
{
    [Header("Follow Target")]
    public Transform target;
    [Tooltip("Look-at offset relative to the target (usually raised to chest/head height)")]
    public Vector3 targetOffset = new Vector3(0f, 1.5f, 0f);
    [Tooltip("Focus follow smoothing: how fast the camera slides to the new target when switching characters")]
    public float followSmooth = 8f;

    [Header("Mouse Orbit")]
    [Tooltip("Checked = orbit only while dragging with right mouse held (recommended, avoids conflict with cursor aiming); unchecked = orbit on mouse move (FreeLook style)")]
    public bool holdRightMouseToRotate = true;
    public float xSpeed = 200f;     // Horizontal sensitivity
    public float ySpeed = 120f;     // Vertical sensitivity
    public float yMinLimit = -20f;  // Min pitch
    public float yMaxLimit = 70f;   // Max pitch
    public bool invertY = false;
    [Range(0f, 0.3f)] public float rotationSmoothTime = 0.08f;

    [Header("Distance / Zoom")]
    public float distance = 6f;
    public float minDistance = 3f;
    public float maxDistance = 12f;
    public float zoomSpeed = 2f;

    [Header("Occlusion Avoidance")]
    [Tooltip("When there's an obstacle between the camera and the character, pull the camera in front of it to avoid clipping through walls")]
    public bool avoidOcclusion = true;
    [Tooltip("Occlusion check layers; ideally only check environment/terrain layers, not character layers")]
    public LayerMask occlusionMask = ~0;
    public float occlusionPadding = 0.2f;

    [Header("Aim Mode (TPS, controlled by RobotAimController)")]
    public bool aimMode = false;
    [Tooltip("Distance the camera sits behind the character while aiming (closer)")]
    public float aimDistance = 3f;
    [Tooltip("On entering aim, first turn smoothly behind the character; within this angle counts as arrived (then zoom in)")]
    public float aimAlignAngle = 3f;
    [Tooltip("Max time for the aim-entry turn (fallback, prevents never arriving)")]
    public float aimEnterMaxTime = 0.8f;

    [Header("Initial View at Start (your desired viewpoint)")]
    [Tooltip("Checked: at Start use the initial yaw/pitch/distance below instead of reading the camera's current Transform angles")]
    public bool useInitialView = false;
    [Tooltip("Initial horizontal heading (= Rotation Y of your desired viewpoint)")]
    public float initialYaw = 180f;
    [Tooltip("Initial pitch (= Rotation X of your desired viewpoint)")]
    public float initialPitch = 10.84f;
    [Tooltip("Initial distance (how far the camera is from the character)")]
    public float initialDistance = 6f;

    // -- Internal state --
    private float yaw, pitch;
    private float currentDistance, desiredDistance, distVel;
    private Quaternion currentRot, desiredRot;
    private Vector3 smoothedFocus;
    private bool focusInit;
    private bool aimEntering;
    private float aimEnterTimer;

    private void Start()
    {
        if (useInitialView)
        {
            yaw = initialYaw;
            pitch = initialPitch;
            desiredDistance = distance = initialDistance;
        }
        else
        {
            Vector3 e = transform.eulerAngles;
            yaw = e.y;
            pitch = e.x;
            desiredDistance = distance;
        }
        currentRot = desiredRot = Quaternion.Euler(pitch, yaw, 0f);
        currentDistance = desiredDistance;

        if (target != null)
        {
            smoothedFocus = FocusPoint();
            focusInit = true;
        }
    }

    private void LateUpdate()
    {
        if (target == null) return;
        HandleZoom();
        HandleRotation();
        UpdatePosition();
    }

    private Vector3 FocusPoint() => target.position + targetOffset;

    private void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.0001f)
            desiredDistance = Mathf.Clamp(desiredDistance - scroll * zoomSpeed, minDistance, maxDistance);
    }

    private void HandleRotation()
    {
        bool canRotate = !holdRightMouseToRotate || Input.GetMouseButton(1);
        if (canRotate)
        {
            float mx = Input.GetAxis("Mouse X");
            float my = Input.GetAxis("Mouse Y");
            yaw += mx * xSpeed * 0.02f;
            pitch -= my * ySpeed * 0.02f * (invertY ? -1f : 1f);
            pitch = Mathf.Clamp(pitch, yMinLimit, yMaxLimit);
        }

        desiredRot = Quaternion.Euler(pitch, yaw, 0f);

        // Frame-rate independent smoothing (larger rotationSmoothTime = softer)
        float t = rotationSmoothTime > 0f ? 1f - Mathf.Exp(-Time.deltaTime / rotationSmoothTime) : 1f;
        currentRot = Quaternion.Slerp(currentRot, desiredRot, t);

        // Entering aim: first turn smoothly behind the character; the entry phase ends only once arrived (or timed out)
        if (aimEntering)
        {
            aimEnterTimer += Time.deltaTime;
            if (Quaternion.Angle(currentRot, desiredRot) <= aimAlignAngle || aimEnterTimer >= aimEnterMaxTime)
                aimEntering = false;
        }
    }

    private void UpdatePosition()
    {
        // Entering aim: first turn behind (keeping the original distance), then zoom in to aimDistance once arrived
        float distTarget = (aimMode && !aimEntering) ? aimDistance : desiredDistance;
        currentDistance = Mathf.SmoothDamp(currentDistance, distTarget, ref distVel, rotationSmoothTime + 0.05f);

        // Focus smoothing: camera slides over when switching targets (the HorizontalCamera feel)
        Vector3 focusTarget = FocusPoint();
        if (!focusInit) { smoothedFocus = focusTarget; focusInit = true; }
        float ft = 1f - Mathf.Exp(-followSmooth * Time.deltaTime);
        smoothedFocus = Vector3.Lerp(smoothedFocus, focusTarget, ft);

        // Orbit positioning: focus - rotation x forward x distance (the FreeLook math)
        Vector3 desiredPos = smoothedFocus - currentRot * Vector3.forward * currentDistance;

        // Occlusion avoidance: ray from focus toward the camera; if it hits an obstacle, pull the camera in front of it
        if (avoidOcclusion)
        {
            Vector3 dir = desiredPos - smoothedFocus;
            float dist = dir.magnitude;
            if (dist > 0.001f &&
                Physics.Raycast(smoothedFocus, dir.normalized, out RaycastHit hit, dist,
                                occlusionMask, QueryTriggerInteraction.Ignore))
            {
                desiredPos = hit.point + hit.normal * occlusionPadding;
            }
        }

        transform.rotation = currentRot;
        transform.position = desiredPos;
    }

    /// <summary>
    /// Switch the follow target (called by CharacterSwitcher).
    /// instant=true snaps immediately (initialization); false slides smoothly to the new target (character switch).
    /// </summary>
    public void SetTarget(Transform newTarget, bool instant = false)
    {
        target = newTarget;
        if (target == null) return;

        if (instant)
        {
            smoothedFocus = FocusPoint();
            focusInit = true;
            currentRot = desiredRot;
            currentDistance = desiredDistance;
            transform.rotation = currentRot;
            transform.position = smoothedFocus - currentRot * Vector3.forward * currentDistance;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (target == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(target.position + targetOffset, 0.25f);
    }

    /// Camera horizontal heading (projected onto the ground) -- used so the robot faces the crosshair direction while aiming
    public Vector3 AimForward
    {
        get
        {
            Vector3 f = currentRot * Vector3.forward;
            f.y = 0f;
            return f.sqrMagnitude > 0.0001f ? f.normalized : transform.forward;
        }
    }

    /// Whether the aim turn has arrived (only then zoom in and let the character face with the camera)
    public bool AimReady => aimMode && !aimEntering;

    /// Enter aim: set the target rotation behind the character and let the camera Slerp there smoothly (no snapping); zooms in automatically once arrived
    public void BeginAim(float behindYaw)
    {
        yaw = behindYaw;
        desiredRot = Quaternion.Euler(pitch, yaw, 0f);   // Only set the target; currentRot turns there smoothly
        aimMode = true;
        aimEntering = true;
        aimEnterTimer = 0f;
    }

    /// Exit aim
    public void EndAim()
    {
        aimMode = false;
        aimEntering = false;
    }
}