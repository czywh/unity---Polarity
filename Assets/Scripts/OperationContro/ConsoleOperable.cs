using UnityEngine;

/// <summary>
/// PROP  Object clickable in Operation Mode: clicking toggles between states A and B with a [smooth transition].
/// Can drive position, rotation, or both:
///   - Position: positionA <-> positionB (e.g. path3's (49,-25.78,-64) <-> (72,-25.78,-64))
///   - Rotation: eulerA <-> eulerB (e.g. path1's (0,0,0) <-> (0,-90,0))
/// A click only switches the target state; movement/rotation approaches it frame by frame in Update ("gradually becomes").
///
/// Trigger: the toggle button on the Operation Mode panel (ConsoleTogglePanel) calls Operate();
/// the old "raycast-click the object" method is still kept behind the OperationModeController.clickToOperate switch.
/// State changes are broadcast via the StateChanged event, and the panel switches its on/off image accordingly.
/// </summary>
[DisallowMultipleComponent]
public class ConsoleOperable : MonoBehaviour, IConsoleOperable
{
    [Header("Position (driven only if checked)")]
    public bool drivePosition = false;
    public bool positionIsLocal = false;   // World space / local space
    public Vector3 positionA;
    public Vector3 positionB;
    [Tooltip("Move speed (units/sec)")]
    public float moveSpeed = 8f;

    [Header("Rotation (driven only if checked)")]
    public bool driveRotation = false;
    public bool rotationIsLocal = true;
    public Vector3 eulerA;
    public Vector3 eulerB;
    [Tooltip("Rotation speed (degrees/sec)")]
    public float rotateSpeed = 180f;

    [Header("Snap to A at Start")]
    public bool snapToAOnStart = true;

    [Header("Console Button (on/off toggle placed next to the object)")]
    [Tooltip("Name shown on the button; empty = object name")]
    public string displayName;
    [Tooltip("Button anchor: an empty child placed where the button should appear.\nAs a child it moves/rotates with this object, so the button follows. Empty = use the local offset below")]
    public Transform buttonAnchor;
    [Tooltip("Used when there is no anchor: local offset relative to this object.\nConverted with TransformPoint, so the anchor follows when the object rotates/moves")]
    public Vector3 buttonLocalOffset = new Vector3(0f, 2f, 0f);

    [Header("Debug (read-only at runtime)")]
    [SerializeField] private int state;   // 0 = A, 1 = B

    /// <summary>Current target state: 0 = A, 1 = B</summary>
    public int State => state;
    /// <summary>Whether in state B (shown as ON on the panel)</summary>
    public bool IsAtB => state == 1;
    /// <summary>Panel display name</summary>
    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

    /// <summary>[Live] world position of the button anchor (changes with the object's current position/rotation). Only used with followTarget</summary>
    public Vector3 ButtonAnchorWorld =>
        buttonAnchor != null ? buttonAnchor.position : transform.TransformPoint(buttonLocalOffset);

    /// <summary>
    /// [Fixed] world position of the button anchor: computed once in Start() after the object snaps to state A, and never changes afterwards.
    /// Wherever the object later moves to B or rotates, the button stays pinned here -- this is the anchor used by default.
    /// </summary>
    public Vector3 ButtonAnchorFixed { get; private set; }
    private bool anchorCached;

    /// <summary>Raised when the target state changes (argument is this). Buttons subscribe to refresh their on/off image</summary>
    public event System.Action<ConsoleOperable> StateChanged;

    private void Start()
    {
        state = 0;
        if (snapToAOnStart) SnapToState(0);
        CacheFixedAnchor();
    }

    /// Records the button's fixed position. Called by default in Start (after snapping to A); call again manually to recompute after changing the anchor
    public void CacheFixedAnchor()
    {
        ButtonAnchorFixed = ButtonAnchorWorld;
        anchorCached = true;
    }

    /// Used by the panel to get the anchor: if Start has not run yet (e.g. just Instantiated), fall back to the live value
    public Vector3 GetButtonAnchor(bool follow) =>
        follow || !anchorCached ? ButtonAnchorWorld : ButtonAnchorFixed;

    // Triggered: switch to the other state (Update transitions smoothly)
    public void Operate()
    {
        SetState(1 - state);
    }

    /// <summary>Set the target state directly (0 = A, 1 = B). No event if unchanged</summary>
    public void SetState(int s)
    {
        s = Mathf.Clamp(s, 0, 1);
        if (s == state) return;
        state = s;
        StateChanged?.Invoke(this);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        bool moved = false;

        if (drivePosition)
        {
            Vector3 tp = state == 0 ? positionA : positionB;
            if (positionIsLocal)
            {
                Vector3 prev = transform.localPosition;
                transform.localPosition = Vector3.MoveTowards(prev, tp, moveSpeed * dt);
                if ((transform.localPosition - prev).sqrMagnitude > 1e-8f) moved = true;
            }
            else
            {
                Vector3 prev = transform.position;
                transform.position = Vector3.MoveTowards(prev, tp, moveSpeed * dt);
                if ((transform.position - prev).sqrMagnitude > 1e-8f) moved = true;
            }
        }

        if (driveRotation)
        {
            Quaternion tr = Quaternion.Euler(state == 0 ? eulerA : eulerB);
            if (rotationIsLocal)
            {
                Quaternion prev = transform.localRotation;
                transform.localRotation = Quaternion.RotateTowards(prev, tr, rotateSpeed * dt);
                if (Quaternion.Angle(prev, transform.localRotation) > 0.001f) moved = true;
            }
            else
            {
                Quaternion prev = transform.rotation;
                transform.rotation = Quaternion.RotateTowards(prev, tr, rotateSpeed * dt);
                if (Quaternion.Angle(prev, transform.rotation) > 0.001f) moved = true;
            }
        }

        // While moving/rotating, tell the grid to enter high-frequency scanning
        if (moved && GridSystem.Instance != null) GridSystem.Instance.NotifyMoving();
    }

    private void SnapToState(int s)
    {
        if (drivePosition)
        {
            Vector3 p = s == 0 ? positionA : positionB;
            if (positionIsLocal) transform.localPosition = p; else transform.position = p;
        }
        if (driveRotation)
        {
            Quaternion r = Quaternion.Euler(s == 0 ? eulerA : eulerB);
            if (rotationIsLocal) transform.localRotation = r; else transform.rotation = r;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (drivePosition)
        {
            Vector3 a = positionIsLocal && transform.parent ? transform.parent.TransformPoint(positionA) : positionA;
            Vector3 b = positionIsLocal && transform.parent ? transform.parent.TransformPoint(positionB) : positionB;
            Gizmos.color = Color.green; Gizmos.DrawWireSphere(a, 0.3f);
            Gizmos.color = Color.cyan;  Gizmos.DrawWireSphere(b, 0.3f);
            Gizmos.color = Color.yellow; Gizmos.DrawLine(a, b);
        }
    }
}