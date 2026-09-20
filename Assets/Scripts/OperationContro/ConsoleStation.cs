using UnityEngine;

/// <summary>
/// PROP  Console (with debug logging): when the player is near and focused, press activateKey (default F) to enter "Operation Mode".
/// Inherits InteractableBase (default PlayerOnly). Each console carries its own camera position params.
///
/// Debug: with verboseLog checked, focus changes / F presses are logged, to help figure out "why pressing F didn't enter Operation Mode".
/// </summary>
public class ConsoleStation : InteractableBase
{
    [Header("Console Activation")]
    [Tooltip("Press this key when near and focused to enter Operation Mode")]
    public KeyCode activateKey = KeyCode.F;

    [Header("Operation Mode Camera Position")]
    [Tooltip("Camera anchor: drag in an empty object placed at the desired camera position/angle; empty = use the coordinates below")]
    public Transform cameraAnchor;
    public Vector3 cameraPosition = new Vector3(-107.53f, 75.81f, -93.7f);
    public Vector3 cameraEuler = new Vector3(55.952f, 90f, -0.2f);
    public float orthographicSize = 33f;

    [Header("Objects Controllable by This Console")]
    [Tooltip("ConsoleOperables listed on the panel after entering Operation Mode.\nEmpty = all ConsoleOperables in the scene (sorted by name)")]
    public ConsoleOperable[] operables;

    [Header("References (empty = auto-find)")]
    [SerializeField] private OperationModeController operationMode;

    [Header("Debug")]
    public bool verboseLog = true;
    [Header("Debug (runtime read-only)")]
    [SerializeField] private bool focusedReadout;

    private void Reset()
    {
        access = InteractAccess.PlayerOnly;
        interactVerb = "Operate";
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        if (operationMode == null) operationMode = FindFirstObjectByType<OperationModeController>();
        if (operationMode == null)
            Debug.LogWarning("[Console] No OperationModeController found in scene! Pressing F can't enter Operation Mode.", this);
    }

    private void Update()
    {
        // Log once when focus state changes
        if (IsFocused != focusedReadout)
        {
            focusedReadout = IsFocused;
            if (verboseLog)
                Debug.Log($"[Console] {name} focused = {IsFocused}" +
                          (IsFocused && CurrentInteractor != null ? $", interactor={CurrentInteractor.name}({CurrentInteractor.type})" : ""), this);
        }

        if (!IsFocused) return;

        if (CurrentInteractor == null)
        {
            if (verboseLog) Debug.Log("[Console] Focused but CurrentInteractor is null", this);
            return;
        }
        if (CurrentInteractor.type != InteractorType.Player)
        {
            if (verboseLog) Debug.Log($"[Console] Focuser is not the player (it is {CurrentInteractor.type}); ignoring F", this);
            return;
        }

        if (Input.GetKeyDown(activateKey))
        {
            if (verboseLog) Debug.Log($"[Console] Pressed {activateKey} → requesting to enter Operation Mode", this);
            if (operationMode != null) operationMode.Enter(this);
            else Debug.LogWarning("[Console] operationMode is null; cannot enter", this);
        }
    }

    /// Objects controlled by this console (empty slots removed; falls back to a full scene scan if not configured)
    public ConsoleOperable[] GetOperables()
    {
        if (operables != null && operables.Length > 0)
        {
            var list = new System.Collections.Generic.List<ConsoleOperable>(operables.Length);
            foreach (var o in operables) if (o != null) list.Add(o);
            if (list.Count > 0) return list.ToArray();
        }
        var all = FindObjectsByType<ConsoleOperable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        System.Array.Sort(all, (a, b) => string.CompareOrdinal(a.name, b.name));
        return all;
    }

    /// Provides this console's camera position (uses the anchor if set, otherwise the coordinate fields)
    public void GetCameraPose(out Vector3 pos, out Quaternion rot, out float size)
    {
        if (cameraAnchor != null)
        {
            pos = cameraAnchor.position;
            rot = cameraAnchor.rotation;
        }
        else
        {
            pos = cameraPosition;
            rot = Quaternion.Euler(cameraEuler);
        }
        size = orthographicSize;
    }
}