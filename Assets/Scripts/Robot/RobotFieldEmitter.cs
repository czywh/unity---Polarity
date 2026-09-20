using UnityEngine;

/// <summary>
/// ROBOT-04  Electric field emitter: attach to the robot; implements IFieldEmitter.
/// Pressing E in robot view -> ToggleField(): spawns / destroys an instant electric field centered on the robot.
///
/// Energy link (CORE-06):
///  - Checks energy before opening the field; refuses to open when empty;
///  - Drains energy continuously while the field is open (even after switching to the player body, the field stays and keeps draining);
///  - Energy depleted -> field closes automatically.
///
/// Field visuals (optional): assign fieldVfxPrefab below (e.g. shield particles),
/// passed in via ElectricField.SetVisualPrefab when the field is spawned; falls back to the debug sphere if left empty.
/// </summary>
public class RobotFieldEmitter : MonoBehaviour, IFieldEmitter
{
    [Header("Field Parameters")]
    public float fieldRadius = 3f;
    [Tooltip("Field center; uses the robot itself if left empty")]
    public Transform fieldOrigin;
    [Tooltip("Optional custom field prefab; generated at runtime if left empty")]
    public ElectricField fieldPrefab;

    [Header("Energy Consumption")]
    [Tooltip("Energy consumed per second while the field is open")]
    public float drainPerSecond = 10f;

    [Header("Field Visual VFX")]
    [Tooltip("Energy-field VFX prefab for the field (e.g. shield particles). Uses the debug sphere below if left empty")]
    public GameObject fieldVfxPrefab;
    [Tooltip("Design radius of the VFX prefab. shield's Start Size=7 -> enter 3.5 first, then fine-tune against the wireframe sphere")]
    public float vfxDesignRadius = 2.1f;
    [Tooltip("Automatically scale the VFX to fieldRadius")]
    public bool autoScaleVfx = true;

    [Header("Debug Visualization")]
    public bool showDebugSphere = true;
    [Tooltip("Field material; uses a semi-transparent debug material if left empty")]
    public Material debugMaterial;

    private ElectricField activeField;
    private EnergySystem energy;
    public bool IsActive => activeField != null;

    private void Awake()
    {
        energy = GetComponent<EnergySystem>();
    }

    // Called by RobotController when E is pressed
    public void ToggleField()
    {
        if (activeField != null) Close();
        else Open();
    }

    private void Open()
    {
        // Can't open without energy
        if (energy != null && energy.IsEmpty)
        {
            Debug.Log("[Field] Out of energy, cannot open the field", this);
            return;
        }

        Transform origin = fieldOrigin != null ? fieldOrigin : transform;

        if (fieldPrefab != null)
        {
            activeField = Instantiate(fieldPrefab, origin.position, Quaternion.identity, origin);
        }
        else
        {
            var go = new GameObject("ElectricField (Instant)");
            go.transform.SetParent(origin, false);
            go.transform.localPosition = Vector3.zero;
            activeField = go.AddComponent<ElectricField>();
        }

        activeField.radius = fieldRadius;
        activeField.showDebugSphere = showDebugSphere;
        if (debugMaterial != null) activeField.debugMaterial = debugMaterial;

        // Only override field visuals if a VFX is assigned; if empty, respect the field prefab's own settings / fall back to the debug sphere
        if (fieldVfxPrefab != null)
        {
            activeField.SetVisualPrefab(fieldVfxPrefab);
            activeField.prefabDesignRadius = vfxDesignRadius;
            activeField.autoScaleToRadius = autoScaleVfx;
        }
    }

    private void Update()
    {
        // Drain energy continuously while the field is open; close automatically when depleted
        if (activeField != null && energy != null)
        {
            energy.Drain(drainPerSecond * Time.deltaTime);
            if (energy.IsEmpty) Close();
        }
    }

    private void Close()
    {
        if (activeField != null) Destroy(activeField.gameObject);
        activeField = null;
    }
}