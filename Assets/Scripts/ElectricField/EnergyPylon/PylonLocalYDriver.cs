using UnityEngine;

/// <summary>
/// PROP  Maps the energy percentage of an ElectricEntity (usually an EnergyPylon)
/// to the target Transform's [local Y position]:
///   Energy 0    -> target localPosition.y = yAtEmpty
///   Energy full -> target localPosition.y = yAtFull
///   In between  -> linear interpolation by Fraction
///
/// Suited for effects like "pylon energy drives a child (e.g. Disruptor) up/down along Y".
/// X / Z keep the target's original local coordinates.
/// </summary>
[DisallowMultipleComponent]
public class PylonLocalYDriver : MonoBehaviour
{
    [Header("Energy Source (empty = ElectricEntity on this object or a parent)")]
    [SerializeField] private ElectricEntity energySource;

    [Header("Driven Target (empty = this object)")]
    [Tooltip("Child object to raise/lower, e.g. Disruptor")]
    [SerializeField] private Transform target;

    [Header("Local Y Endpoints")]
    [Tooltip("Local Y when energy is 0")]
    public float yAtEmpty = -1.5f;
    [Tooltip("Local Y when energy is full")]
    public float yAtFull = 0.4f;

    [Header("Smoothing (optional)")]
    [Tooltip("Max movement speed per second; 0 = snap directly (energy already changes gradually, so 0 is usually fine)")]
    public float moveSpeed = 0f;

    [Header("Debug (read-only at runtime)")]
    [SerializeField] private float fractionReadout;
    [SerializeField] private float currentY;

    private void Awake()
    {
        if (energySource == null) energySource = GetComponentInParent<ElectricEntity>();
        if (target == null) target = transform;
        if (energySource == null)
            Debug.LogWarning("PylonLocalYDriver: ElectricEntity energy source not found", this);
    }

    private void Start()
    {
        if (energySource != null && target != null)
            SetY(Mathf.Lerp(yAtEmpty, yAtFull, Mathf.Clamp01(energySource.Fraction)), instant: true);
    }

    // LateUpdate: read the latest energy after this frame's charging/discharging
    private void LateUpdate()
    {
        if (energySource == null || target == null) return;

        float f = Mathf.Clamp01(energySource.Fraction);
        fractionReadout = f;

        float targetY = Mathf.Lerp(yAtEmpty, yAtFull, f);
        SetY(targetY, instant: moveSpeed <= 0f);
    }

    private void SetY(float y, bool instant)
    {
        Vector3 lp = target.localPosition;
        float prevY = lp.y;
        lp.y = instant ? y : Mathf.MoveTowards(lp.y, y, moveSpeed * Time.deltaTime);
        target.localPosition = lp;
        currentY = lp.y;

        // Position changed -> tell the grid to enter high-frequency scanning
        if (Mathf.Abs(lp.y - prevY) > 1e-5f && GridSystem.Instance != null)
            GridSystem.Instance.NotifyMoving();
    }
}