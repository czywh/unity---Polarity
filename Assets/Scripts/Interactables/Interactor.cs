using UnityEngine;

/// <summary>
/// Interactor: attach to the player / robot. Every frame, from all interactables, picks the nearest one that is
/// "in range + aimed at by the camera + permission matches" as the focus, and triggers interaction on key press.
///
/// Control arbitration: actively asks CharacterSwitcher "am I the character currently being controlled?".
///   - Not the current character -> immediately clears focus and stops working
///   - ExternallyFrozen (Operation Mode, etc.) -> also stops working
/// So even if you forget to add this component to CharacterSwitcher's control script array, you won't get
/// "after switching to the player, the robot's Interactor still lights up interaction prompts on the shared camera".
/// Still recommended to add it to the array as usual (double safety, and it freezes movement and other scripts together).
/// </summary>
public class Interactor : MonoBehaviour
{
    [Header("Identity")]
    [Tooltip("Who this interactor represents; decides which RobotOnly / PlayerOnly objects it can use")]
    public InteractorType type = InteractorType.Player;

    [Header("Control (auto-finds the scene's CharacterSwitcher if left empty)")]
    [Tooltip("Control authority. If not found, falls back to only checking this component's enabled state")]
    [SerializeField] private CharacterSwitcher switcher;

    [Header("Detection")]
    [Tooltip("Distance reference point; uses this object if left empty")]
    public Transform origin;
    [Tooltip("Camera used for the aim check; uses Camera.main if left empty")]
    public Camera cam;
    [Tooltip("Max distance required to interact (character to object)")]
    public float interactRange = 3f;
    [Tooltip("Whether the camera must be aimed at the object")]
    public bool requireCameraAim = true;
    [Range(0f, 90f)]
    [Tooltip("Max angle between camera forward and the object direction; larger is more lenient")]
    public float aimAngle = 35f;

    [Header("Key Interaction")]
    public KeyCode interactKey = KeyCode.F;

    [Header("Debug (runtime read-only)")]
    [Tooltip("Whether this interactor currently holds control (= is the controlled character and not externally frozen)")]
    [SerializeField] private bool hasControlReadout;
    [SerializeField] private string currentReadout;

    /// Currently focused object (null if none)
    public InteractableBase Current { get; private set; }

    /// <summary>Whether it holds control: the currently controlled character, and not externally frozen</summary>
    public bool HasControl
    {
        get
        {
            if (!isActiveAndEnabled) return false;
            if (switcher == null) return true;               // No arbiter: fall back to old behavior
            if (switcher.ExternallyFrozen) return false;     // Operation Mode, etc.: everyone stops

            return type == InteractorType.Player
                ? switcher.Current == CharacterSwitcher.Character.Player
                : switcher.Current == CharacterSwitcher.Character.Robot;
        }
    }

    private void Awake()
    {
        if (switcher == null) switcher = FindFirstObjectByType<CharacterSwitcher>();
    }

    private void Update()
    {
        hasControlReadout = HasControl;

        // No control -> actively clear focus (otherwise the prompt UI would read a stale Current)
        if (!hasControlReadout)
        {
            ClearCurrent();
            currentReadout = "(no control)";
            return;
        }

        // Hands full: while the player carries an Energy Relay, F means "place the relay" (handled by EnergyRelay),
        // so no other interactable gets focused or triggered
        if (type == InteractorType.Player && EnergyRelay.CarriedRelay != null)
        {
            ClearCurrent();
            currentReadout = "(carrying " + EnergyRelay.DisplayName + ")";
            return;
        }

        InteractableBase best = FindBest();

        if (best != Current)
        {
            if (Current != null) Current.OnFocusExit(this);
            Current = best;
            if (Current != null) Current.OnFocusEnter(this);
        }

        currentReadout = Current != null ? Current.name : "(none)";

        if (Current != null && Input.GetKeyDown(interactKey))
            Current.OnInteract(this);
    }

    private InteractableBase FindBest()
    {
        Transform o = origin != null ? origin : transform;
        Camera c = cam != null ? cam : Camera.main;

        InteractableBase best = null;
        float bestDist = float.MaxValue;

        var list = InteractableBase.All;
        for (int i = 0; i < list.Count; i++)
        {
            var it = list[i];
            if (it == null || !it.CanBeUsedBy(type)) continue;

            Vector3 p = it.InteractTransform.position;
            float dist = Vector3.Distance(o.position, p);
            if (dist > interactRange) continue;

            // Camera aim check: angle between camera forward and the "camera -> object" direction must not exceed aimAngle
            if (requireCameraAim && c != null)
            {
                Vector3 toObj = p - c.transform.position;
                if (toObj.sqrMagnitude > 0.0001f &&
                    Vector3.Angle(c.transform.forward, toObj.normalized) > aimAngle)
                    continue;
            }

            if (dist < bestDist) { bestDist = dist; best = it; }
        }

        return best;
    }

    private void ClearCurrent()
    {
        if (Current == null) return;
        Current.OnFocusExit(this);
        Current = null;
    }

    private void OnDisable()
    {
        // When disabled (e.g. switched away from the character), actively release the current focus
        ClearCurrent();
        hasControlReadout = false;
    }

    private void OnDrawGizmosSelected()
    {
        Transform o = origin != null ? origin : transform;
        Gizmos.color = new Color(0.3f, 1f, 0.6f, 0.5f);
        Gizmos.DrawWireSphere(o.position, interactRange);
    }
}