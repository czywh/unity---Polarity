using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// LASER  Energy Relay (replaces the old LaserRedirector). Three behaviours:
///
///  1. Attraction: every LaserTower whose position lies inside this relay's attraction box (attractBoxSize, 40x40x40 by
///     default, centred on the relay) turns toward the relay and fires its beams at it (LaserTower.autoAimRelays).
///  2. Electric field: while a laser beam is hitting the relay (LaserBarrier calls Hit()), the relay opens an electric field
///     that LOOKS like the robot's (VFX prefab / debug sphere copied from the scene's RobotFieldEmitter when matchRobotField
///     is on) but has its OWN radius: fieldRadius on this component is never overwritten, so every relay can be tuned
///     independently of the robot and of other relays.
///     The field closes sustainTime seconds after the last hit.
///  3. Robot recharge: while the robot stands inside this relay's open field its energy is held at maximum
///     (keepRobotFullInField), so the robot can keep its own field running for free next to a powered relay.
///  4. Pickup: it is an InteractableBase. Walk up to it and the prompt "Press [F] to pick up Energy Relay" appears;
///     press F and the relay is carried by the player (child of the player, colliders off, field off, towers ignore it).
///     While carried the prompt reads "Press [F] to place Energy Relay"; pressing F again drops it in front of the player,
///     snapped to the ground, and everything returns to normal. (While carrying, the player's Interactor ignores other
///     interactables, so F can't accidentally trigger a console at the same time.)
///
/// Keep SpinY on the same object for the idle spin; the visual spin does not affect any of the logic above.
/// The relay's collider must be on a layer that LaserBarrier.blockMask includes (Default is fine) so beams can hit it.
/// </summary>
[DisallowMultipleComponent]
public class EnergyRelay : InteractableBase
{
    public const string DisplayName = "Energy Relay";

    // -- Global registry (for LaserTower to find relays) --
    private static readonly List<EnergyRelay> relays = new List<EnergyRelay>();
    public static IReadOnlyList<EnergyRelay> All => relays;
    /// The relay currently carried by the player (null if none). Read by PlayerAimController / InteractionPrompt.
    public static EnergyRelay CarriedRelay { get; private set; }

    [Header("Attraction")]
    [Tooltip("Size of the box (world units, centred on the relay) inside which laser towers aim at this relay")]
    public Vector3 attractBoxSize = new Vector3(40f, 40f, 40f);
    [Tooltip("Where the towers aim their beams. Empty = the centre of the relay's model (renderer bounds), i.e. its middle, not the pivot at the base")]
    public Transform aimPoint;
    [Tooltip("Extra offset (relay local space) added to the automatic aim point, to fine-tune where beams land")]
    public Vector3 aimLocalOffset = Vector3.zero;

    [Header("Electric field (opened while a laser hits the relay)")]
    [Tooltip("Copy the LOOK of the robot's field (VFX prefab, design radius, debug sphere / material) from the scene's RobotFieldEmitter at start.\nThe radius is NOT copied -- Field Radius below is always this relay's own value")]
    public bool matchRobotField = true;
    [Tooltip("This relay's own field radius (independent of the robot and of other relays). Editable live in Play Mode")]
    public float fieldRadius = 4f;
    [Tooltip("Optional custom field prefab; generated at runtime if left empty")]
    public ElectricField fieldPrefab;
    [Tooltip("Field VFX prefab (the robot's shield particles). Falls back to the debug sphere if empty")]
    public GameObject fieldVfxPrefab;
    public float vfxDesignRadius = 2.1f;
    public bool autoScaleVfx = true;
    public bool showDebugSphere = true;
    public Material debugMaterial;
    [Tooltip("Field centre offset from the relay (local). Use it to lift the field to the relay's core")]
    public Vector3 fieldLocalOffset = Vector3.zero;
    [Tooltip("After the last laser hit, keep the field open for this long (seconds) -- bridges the gap between physics ticks")]
    public float sustainTime = 0.2f;

    [Header("Robot recharge")]
    [Tooltip("While the robot is inside this relay's open field, its EnergySystem is held at maximum every frame")]
    public bool keepRobotFullInField = true;

    [Header("Pickup / place")]
    [Tooltip("Prompt shown when the player can pick it up")]
    public string pickupPrompt = "Press [F] to pick up " + DisplayName;
    [Tooltip("Prompt shown while carried (press the place key again to put it down)")]
    public string placePrompt = "Press [F] to place " + DisplayName;
    [Tooltip("Key used to place the relay while carried (same key as pick-up: press F again). Works in normal and Operation Mode")]
    public KeyCode placeKey = KeyCode.F;
    [Tooltip("Optional anchor under the player (e.g. a hand bone). Empty = the player root with the offsets below")]
    public Transform carryAnchor;
    public Vector3 carryLocalPosition = new Vector3(0.45f, 0.9f, 0.5f);
    public Vector3 carryLocalEuler = Vector3.zero;
    public float carryScale = 0.35f;
    [Tooltip("How far in front of the player it is placed")]
    public float placeDistance = 1.5f;
    [Tooltip("Ground layers used to snap the placed relay to the floor")]
    public LayerMask groundMask = ~0;
    [Tooltip("Max drop when snapping to the ground; if nothing is found the relay keeps the player's height")]
    public float snapDownDistance = 4f;

    [Header("Editor gizmo (Scene view)")]
    [Tooltip("Always draw the energy field sphere in the Scene view (not only when selected), so its size can be checked while editing")]
    public bool alwaysShowFieldGizmo = true;
    [Tooltip("Also draw a translucent filled sphere")]
    public bool fillFieldGizmo = true;
    public Color fieldGizmoColor = new Color(0.3f, 0.8f, 1f, 1f);

    [Header("Debug (runtime read-only)")]
    [SerializeField] private bool carriedReadout;
    [SerializeField] private bool fieldOpenReadout;
    [SerializeField] private bool beingHitReadout;
    [SerializeField] private bool robotInFieldReadout;
    public bool verboseLog = false;

    public bool IsCarried => CarriedRelay == this;
    /// Placed in the world (not carried) -> towers may aim at it
    public bool IsPlaced => !IsCarried && isActiveAndEnabled;
    public bool IsFieldOpen => activeField != null;
    public Vector3 AimPosition => aimPoint != null
        ? aimPoint.position
        : transform.TransformPoint(AutoAimLocal + aimLocalOffset);

    private Vector3 autoAimLocal;   // model centre in local space, measured once in Awake
    // In the editor nothing calls Awake, so measure on the fly -- the gizmo then follows model / scale changes live
    private Vector3 AutoAimLocal => Application.isPlaying ? autoAimLocal : ComputeModelCentreLocal();

    private ElectricField activeField;
    private float hitUntil = -1f;
    private RobotFieldEmitter robot;
    private EnergySystem robotEnergy;
    private Collider robotCollider;
    private int pickedUpFrame = -1;   // the F press that picked it up must not also place it in the same frame
    private int placedFrame = -1;     // ...and the F press that placed it must not pick it straight back up (Interactor may run after us)
    private bool placeArmed;          // becomes true once F has been released after pick-up; only then can F place it

    // carry state
    private Transform carrier;
    private Transform originalParent;
    private Vector3 originalScale;
    private Collider[] colliders;
    private readonly List<bool> colliderStates = new List<bool>();

    protected override void OnEnable()
    {
        base.OnEnable();
        if (!relays.Contains(this)) relays.Add(this);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        relays.Remove(this);
        CloseField();
        if (CarriedRelay == this) CarriedRelay = null;
    }

    private void Awake()
    {
        interactVerb = pickupPrompt;
        access = InteractAccess.PlayerOnly;
        originalScale = transform.localScale;
        colliders = GetComponentsInChildren<Collider>(true);
        autoAimLocal = ComputeModelCentreLocal();
    }

    // Centre of the relay's renderer bounds, in local space (so it follows the relay when moved / spun / picked up)
    private Vector3 ComputeModelCentreLocal()
    {
        bool has = false; Bounds b = default;
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r is ParticleSystemRenderer) continue;
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        if (!has)
        {
            foreach (var c in GetComponentsInChildren<Collider>(true))
            {
                if (c == null) continue;
                if (!has) { b = c.bounds; has = true; } else b.Encapsulate(c.bounds);
            }
        }
        return has ? transform.InverseTransformPoint(b.center) : Vector3.zero;
    }

    private void Start()
    {
        robot = FindFirstObjectByType<RobotFieldEmitter>(FindObjectsInactive.Include);
        if (robot != null)
        {
            robotEnergy = robot.GetComponent<EnergySystem>();
            robotCollider = robot.GetComponent<CharacterController>();
            if (robotCollider == null) robotCollider = robot.GetComponentInChildren<Collider>();
        }

        if (matchRobotField)
        {
            if (robot != null)
            {
                // Visuals only -- fieldRadius stays this relay's own value
                fieldPrefab = robot.fieldPrefab;
                fieldVfxPrefab = robot.fieldVfxPrefab;
                vfxDesignRadius = robot.vfxDesignRadius;
                autoScaleVfx = robot.autoScaleVfx;
                showDebugSphere = robot.showDebugSphere;
                if (robot.debugMaterial != null) debugMaterial = robot.debugMaterial;
            }
        }
    }

    private void Update()
    {
        // -- Field driven by laser hits --
        bool hit = Time.time <= hitUntil;
        beingHitReadout = hit;
        if (hit && !IsCarried) OpenField();
        else if (!hit || IsCarried) CloseField();

        // -- Placing while carried: press the same key (F) again --
        if (IsCarried)
        {
            if (interactVerb != placePrompt) interactVerb = placePrompt;

            // Arm only after the pick-up key has been released, so the same press can never both pick up and place
            if (!placeArmed)
            {
                if (Time.frameCount != pickedUpFrame && !Input.GetKey(placeKey)) placeArmed = true;
            }
            else if (Input.GetKeyDown(placeKey))
            {
                Place();
            }
        }

        carriedReadout = IsCarried;
        fieldOpenReadout = IsFieldOpen;
    }

    // Runs after RobotFieldEmitter.Update has drained this frame, so the robot's bar never dips below full while inside
    protected override void LateUpdate()
    {
        base.LateUpdate();
        KeepRobotFull();
    }

    private void KeepRobotFull()
    {
        bool inside = false;
        if (keepRobotFullInField && activeField != null && robot != null && robotEnergy != null && robot.isActiveAndEnabled)
        {
            Vector3 c = activeField.transform.position;
            float r = activeField.radius;
            Vector3 p = robotCollider != null ? robotCollider.ClosestPoint(c) : robot.transform.position;
            inside = (p - c).sqrMagnitude <= r * r;
            if (inside && robotEnergy.CurrentEnergy < robotEnergy.MaxEnergy)
                robotEnergy.Recharge(robotEnergy.MaxEnergy);   // Recharge clamps at max
        }
        robotInFieldReadout = inside;
    }

    // ------------------------------------------------------------------
    //  Laser hit -> field
    // ------------------------------------------------------------------

    /// <summary>Called by LaserBarrier every physics tick while a beam hits this relay.</summary>
    public void Hit(Vector3 incomingDir)
    {
        hitUntil = Time.time + sustainTime;
    }

    private void OpenField()
    {
        if (activeField != null) return;
        if (fieldPrefab != null)
        {
            activeField = Instantiate(fieldPrefab, transform.TransformPoint(fieldLocalOffset), Quaternion.identity, transform);
        }
        else
        {
            var go = new GameObject("ElectricField (Relay)");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = fieldLocalOffset;
            activeField = go.AddComponent<ElectricField>();
        }
        activeField.radius = fieldRadius;
        activeField.showDebugSphere = showDebugSphere;
        if (debugMaterial != null) activeField.debugMaterial = debugMaterial;
        if (fieldVfxPrefab != null)
        {
            activeField.SetVisualPrefab(fieldVfxPrefab);
            activeField.prefabDesignRadius = vfxDesignRadius;
            activeField.autoScaleToRadius = autoScaleVfx;
        }
        Log("field open");
    }

    private void CloseField()
    {
        if (activeField == null) return;
        Destroy(activeField.gameObject);
        activeField = null;
        Log("field closed");
    }

    // ------------------------------------------------------------------
    //  Attraction helper (used by LaserTower)
    // ------------------------------------------------------------------

    /// Whether a world point lies inside this relay's attraction box
    public bool Attracts(Vector3 worldPoint)
    {
        if (!IsPlaced) return false;
        Vector3 d = worldPoint - transform.position;
        return Mathf.Abs(d.x) <= attractBoxSize.x * 0.5f
            && Mathf.Abs(d.y) <= attractBoxSize.y * 0.5f
            && Mathf.Abs(d.z) <= attractBoxSize.z * 0.5f;
    }

    /// Nearest placed relay whose attraction box contains the point; null if none
    public static EnergyRelay FindAttracting(Vector3 worldPoint)
    {
        EnergyRelay best = null; float bestD = float.MaxValue;
        for (int i = 0; i < relays.Count; i++)
        {
            var r = relays[i];
            if (r == null || !r.Attracts(worldPoint)) continue;
            float d = (r.transform.position - worldPoint).sqrMagnitude;
            if (d < bestD) { bestD = d; best = r; }
        }
        return best;
    }

    // ------------------------------------------------------------------
    //  Pickup / place
    // ------------------------------------------------------------------

    public override void OnInteract(Interactor interactor)
    {
        if (interactor == null || interactor.type != InteractorType.Player) return;
        if (CarriedRelay != null) return;   // already carrying one
        if (Time.frameCount == placedFrame) return;   // just placed this frame by the same key press
        PickUp(interactor.transform);
    }

    public void PickUp(Transform player)
    {
        if (IsCarried || player == null) return;
        carrier = player;
        CarriedRelay = this;
        pickedUpFrame = Time.frameCount;
        placeArmed = false;

        // leave aim mode if the player was aiming (hands are busy with the relay)
        var aim = player.GetComponent<PlayerAimController>();
        if (aim != null) aim.ExitAim();

        CloseField();
        hitUntil = -1f;
        interactable = false;               // nobody can focus it while carried
        SetColliders(false);

        originalParent = transform.parent;
        Transform anchor = carryAnchor != null ? carryAnchor : player;
        transform.SetParent(anchor, false);
        transform.localPosition = carryLocalPosition;
        transform.localRotation = Quaternion.Euler(carryLocalEuler);
        transform.localScale = originalScale * carryScale;
        Debug.Log($"[EnergyRelay] {name}: picked up by {player.name} (press [{placeKey}] again to place)", this);
    }

    public void Place()
    {
        if (!IsCarried) return;
        Transform player = carrier;

        transform.SetParent(originalParent, true);
        transform.localScale = originalScale;
        transform.rotation = Quaternion.identity;

        Vector3 pos = player.position + player.forward * placeDistance;
        // snap to ground: cast down from a bit above the target point
        if (Physics.Raycast(pos + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 1.5f + snapDownDistance, groundMask, QueryTriggerInteraction.Ignore)
            && !hit.transform.IsChildOf(player))
            pos = hit.point;
        transform.position = pos;

        SetColliders(true);
        interactable = true;
        interactVerb = pickupPrompt;
        CarriedRelay = null;
        carrier = null;
        placedFrame = Time.frameCount;
        Physics.SyncTransforms();
        Debug.Log($"[EnergyRelay] {name}: placed at {pos}", this);
    }

    private void SetColliders(bool on)
    {
        if (colliders == null) return;
        if (on)
        {
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i] != null) colliders[i].enabled = i < colliderStates.Count ? colliderStates[i] : true;
        }
        else
        {
            colliderStates.Clear();
            for (int i = 0; i < colliders.Length; i++)
            {
                colliderStates.Add(colliders[i] != null && colliders[i].enabled);
                if (colliders[i] != null) colliders[i].enabled = false;
            }
        }
    }

    private void Log(string msg)
    {
        if (verboseLog) Debug.Log($"[EnergyRelay] {name}: {msg}", this);
    }

    // ------------------------------------------------------------------
    //  Editor gizmos
    // ------------------------------------------------------------------

#if UNITY_EDITOR
    // Any Inspector change on this component (or on the robot emitter) must repaint the Scene view,
    // otherwise the gizmo keeps showing the previous values until the view happens to redraw.
    private void OnValidate()
    {
        if (fieldRadius < 0f) fieldRadius = 0f;
        autoAimLocal = ComputeModelCentreLocal();

        // Play Mode: push the edited values into the field that is already open, so tweaking is live
        if (Application.isPlaying && activeField != null)
        {
            activeField.radius = fieldRadius;
            activeField.transform.localPosition = fieldLocalOffset;
            activeField.prefabDesignRadius = vfxDesignRadius;
            activeField.autoScaleToRadius = autoScaleVfx;
            activeField.showDebugSphere = showDebugSphere;
        }

        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null) UnityEditor.SceneView.RepaintAll();
        };
    }
#endif

    /// <summary>
    /// Inspector context menu (the three dots on the component header): copy the scene robot's field settings, INCLUDING
    /// its radius, into this relay -- an explicit one-off sync for when you want a relay to start from the robot's numbers.
    /// </summary>
    [ContextMenu("Copy field settings from robot (incl. radius)")]
    private void CopyFieldSettingsFromRobot()
    {
        var robot = FindFirstObjectByType<RobotFieldEmitter>(FindObjectsInactive.Include);
        if (robot == null) { Debug.LogWarning("[EnergyRelay] No RobotFieldEmitter found in the scene", this); return; }
        fieldRadius = robot.fieldRadius;
        fieldPrefab = robot.fieldPrefab;
        fieldVfxPrefab = robot.fieldVfxPrefab;
        vfxDesignRadius = robot.vfxDesignRadius;
        autoScaleVfx = robot.autoScaleVfx;
        showDebugSphere = robot.showDebugSphere;
        if (robot.debugMaterial != null) debugMaterial = robot.debugMaterial;
        Debug.Log($"[EnergyRelay] {name}: copied field settings from {robot.name} (radius {fieldRadius})", this);
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.SceneView.RepaintAll();
#endif
    }

    private void DrawFieldGizmo(bool selected)
    {
        Vector3 c = transform.TransformPoint(fieldLocalOffset);
        float r = fieldRadius;   // always this relay's own radius
        Color col = fieldGizmoColor;

        if (fillFieldGizmo)
        {
            col.a = selected ? 0.18f : 0.08f;
            Gizmos.color = col;
            Gizmos.DrawSphere(c, r);
        }
        col.a = selected ? 1f : 0.6f;
        Gizmos.color = col;
        Gizmos.DrawWireSphere(c, r);

        // Equator + diameter lines: make the radius readable from the 2.5D side view
        Gizmos.DrawLine(c + Vector3.left * r, c + Vector3.right * r);
        Gizmos.DrawLine(c + Vector3.down * r, c + Vector3.up * r);
        Gizmos.DrawLine(c + Vector3.back * r, c + Vector3.forward * r);

#if UNITY_EDITOR
        UnityEditor.Handles.color = col;
        UnityEditor.Handles.Label(c + Vector3.up * (r + 0.3f), $"{name}  field r = {r:0.##}");
#endif
    }

    private void OnDrawGizmos()
    {
        if (!alwaysShowFieldGizmo) return;
#if UNITY_EDITOR
        if (UnityEditor.Selection.Contains(gameObject)) return;   // drawn by OnDrawGizmosSelected instead
#endif
        if (Application.isPlaying && IsCarried) return;             // no field while carried
        DrawFieldGizmo(false);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.8f);
        Gizmos.DrawWireCube(transform.position, attractBoxSize);
        DrawFieldGizmo(true);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(AimPosition, 0.15f);
    }
}
