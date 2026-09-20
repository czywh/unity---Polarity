using UnityEngine;

/// <summary>
/// ROBOT-05 (Aim) - Robot third-person shooting aim controller.
///
/// Right-click toggles between "Aim / Move" modes:
///  - Aim mode: camera snaps behind the robot, crosshair at screen center, robot facing follows the mouse (= camera facing),
///    left-click fires a missile toward the crosshair.
///  - Move mode: restores the original orbit camera + turning toward movement direction.
/// Only Aim mode can fire. Automatically exits Aim when switching away from the robot.
///
/// Recommended: add to CharacterSwitcher's robotControlScripts so it only runs while controlling the robot.
/// Requirement: camera uses OrbitFollowCamera with holdRightMouseToRotate unchecked (right-click is reserved for aim toggle).
/// </summary>
[RequireComponent(typeof(RobotController))]
public class RobotAimController : MonoBehaviour
{
    [Header("References (auto-fetched if empty)")]
    public OrbitFollowCamera cam;
    [Tooltip("Muzzle / aim direction origin; empty = use self")]
    public Transform muzzle;

    [Header("Aim Ray")]
    [Tooltip("Crosshair hit detection layers; recommended to exclude player / robot")]
    public LayerMask aimMask = ~0;
    public float maxAimDistance = 1000f;

    [Header("Crosshair (optional; empty = use built-in simple crosshair)")]
    public GameObject crosshair;

    private IMissileLauncher launcher;
    private Camera cameraComp;
    private Transform muzzleT;
    private RobotController robot;   // Its enabled state tells whether "the robot is currently being controlled"

    public bool IsAiming { get; private set; }

    private void Awake()
    {
        robot = GetComponent<RobotController>();
        launcher = GetComponent<IMissileLauncher>();
        if (cam == null && Camera.main != null)
            cam = Camera.main.GetComponent<OrbitFollowCamera>();
        cameraComp = cam != null ? cam.GetComponent<Camera>() : Camera.main;
        muzzleT = muzzle != null ? muzzle : transform;
        if (crosshair != null) crosshair.SetActive(false);
    }

    private void Update()
    {
        // Aim / fire only allowed while "controlling the robot"; when switched to the player (RobotController disabled), force exit and ignore input
        if (robot == null || !robot.enabled)
        {
            if (IsAiming) SetAiming(false);
            return;
        }

        // Right-click toggles Aim / Move
        if (Input.GetMouseButtonDown(1)) SetAiming(!IsAiming);

        if (!IsAiming) return;

        // Left-click fires (Aim state only)
        if (Input.GetMouseButtonDown(0)) Fire();
    }

    private void LateUpdate()
    {
        // Character facing follows the camera only after the camera finishes turning (avoids the character swinging during the aim-in turn)
        if (IsAiming && cam != null && cam.AimReady)
        {
            Vector3 f = cam.AimForward;
            if (f.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(f, Vector3.up);
        }
    }

    private void SetAiming(bool on)
    {
        IsAiming = on;

        if (cam != null)
        {
            if (on) cam.BeginAim(transform.eulerAngles.y);   // Smoothly turn to behind, no snapping
            else cam.EndAim();
        }
        if (crosshair != null) crosshair.SetActive(on);

        // While playing as the robot the cursor is always locked and hidden (move / aim both only use the camera, no visible cursor needed)
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Fire()
    {
        if (launcher == null) return;
        launcher.TryFire(ComputeAimDirection());
    }

    // Ray from screen center to get the aim point, then compute fire direction
    private Vector3 ComputeAimDirection()
    {
        if (cameraComp == null) return muzzleT.forward;

        Ray ray = cameraComp.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 aimPoint = Physics.Raycast(ray, out RaycastHit hit, maxAimDistance, aimMask, QueryTriggerInteraction.Ignore)
            ? hit.point
            : ray.GetPoint(maxAimDistance);

        Vector3 dir = aimPoint - muzzleT.position;
        return dir.sqrMagnitude > 0.0001f ? dir.normalized : muzzleT.forward;
    }

    private void OnDisable()
    {
        // Switching away from the robot -> exit Aim state and reset camera, but leave the cursor alone:
        // CharacterSwitcher sets the cursor centrally (player = locked hidden), avoiding two places fighting over it
        if (IsAiming)
        {
            IsAiming = false;
            if (cam != null) cam.EndAim();
            if (crosshair != null) crosshair.SetActive(false);
        }
    }

    // Built-in simple crosshair (used when no crosshair is assigned)
    private void OnGUI()
    {
        if (!IsAiming || crosshair != null) return;

        float cx = Screen.width * 0.5f;
        float cy = Screen.height * 0.5f;
        const float len = 10f, thick = 2f;

        Color old = GUI.color;
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(cx - len, cy - thick * 0.5f, len * 2f, thick), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - thick * 0.5f, cy - len, thick, len * 2f), Texture2D.whiteTexture);
        GUI.color = old;
    }
}