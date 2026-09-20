using UnityEngine;

/// <summary>
/// ROBOT-05 Missile launcher: attached to the robot, implements IMissileLauncher.
/// Left click in robot view → RobotController calls TryFire(): fires one missile forward.
///
/// Constraint changed to [energy]: each launch consumes energyCost energy at once; can't fire without enough energy.
/// Shares the same EnergySystem as the field, so all abilities are constrained by energy uniformly.
///
/// Two optional prefabs:
///  - missilePrefab   -- missile body in flight (model / VFX / trail). May have a Missile component or be visual-only.
///  - fieldVfxPrefab  -- field visual VFX spawned when the missile deploys.
/// If both are empty, falls back to a code-generated small sphere + debug sphere.
/// </summary>
public class MissileLauncher : MonoBehaviour, IMissileLauncher
{
    [Header("Firing")]
    [Tooltip("Muzzle; uses this object if empty. Forward = this Transform's forward")]
    public Transform muzzle;
    public float missileSpeed = 20f;
    [Tooltip("Distance flown before deploying into a field")]
    public float travelDistance = 10f;
    [Tooltip("Hitting these layers deploys early; recommend only environment layers, excluding player / robot")]
    public LayerMask obstacleMask = ~0;
    [Tooltip("How many meters ahead of the muzzle to spawn the missile, to avoid hitting the robot itself on spawn")]
    public float spawnForwardOffset = 1f;

    [Header("Prefabs")]
    [Tooltip("Missile body prefab (model / VFX / trail). May have a Missile component or be visual-only; uses a code sphere if empty")]
    public GameObject missilePrefab;

    [Header("Energy Cost")]
    [Tooltip("Energy consumed per missile fired")]
    public float energyCost = 50f;

    [Header("Deployed Field")]
    public float fieldRadius = 4f;
    public float holdDuration = 8f;
    public float fadeDuration = 4f;
    [Tooltip("Debug sphere color (ignored when a field VFX is set)")]
    public Color fieldColor = new Color(0.85f, 0.4f, 0.95f, 0.28f);

    [Header("Field Visual VFX")]
    [Tooltip("VFX prefab for the field (e.g. shield particles). Uses the debug sphere if empty")]
    public GameObject fieldVfxPrefab;
    [Tooltip("Design radius of the field VFX prefab (Start Size=7 → 3.5), then fine-tune against the wireframe")]
    public float fieldVfxDesignRadius = 2.2f;
    [Tooltip("Auto-scale the field VFX to fieldRadius")]
    public bool autoScaleFieldVfx = true;

    private EnergySystem energy;

    private void Awake()
    {
        energy = GetComponent<EnergySystem>();
    }

    /// Whether there is enough energy to fire one
    public bool CanFire => energy == null || energy.CurrentEnergy >= energyCost;

    // Fire straight ahead (used when not aiming)
    public void TryFire()
    {
        Transform m = muzzle != null ? muzzle : transform;
        TryFire(m.forward);
    }

    // Fire in the given direction (crosshair direction while aiming)
    public void TryFire(Vector3 direction)
    {
        if (!CanFire)
        {
            Debug.Log($"[Missile] Not enough energy (need {energyCost}), cannot fire", this);
            return;
        }

        // Drain energy (EnergySystem syncs the fade)
        if (energy != null) energy.Drain(energyCost);

        Transform m = muzzle != null ? muzzle : transform;
        Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : m.forward;
        Vector3 pos = m.position + dir * spawnForwardOffset;

        CreateMissile(pos, dir);
    }

    private void CreateMissile(Vector3 pos, Vector3 dir)
    {
        Quaternion rot = Quaternion.LookRotation(dir);

        GameObject go;
        Missile missile;

        if (missilePrefab != null)
        {
            go = Instantiate(missilePrefab, pos, rot);
            missile = go.GetComponent<Missile>();
            if (missile == null) missile = go.AddComponent<Missile>();  // Works with visual-only prefabs too
        }
        else
        {
            // Fallback: code-generate a small sphere (no collider, no shadows)
            go = new GameObject("Missile");
            go.transform.SetPositionAndRotation(pos, rot);

            var vis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            vis.transform.SetParent(go.transform, false);
            vis.transform.localScale = Vector3.one * 0.3f;
            var col = vis.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var mr = vis.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            missile = go.AddComponent<Missile>();
        }

        // Inject flight parameters
        missile.speed = missileSpeed;
        missile.travelDistance = travelDistance;
        missile.obstacleMask = obstacleMask;

        // Inject field parameters
        missile.fieldRadius = fieldRadius;
        missile.holdDuration = holdDuration;
        missile.fadeDuration = fadeDuration;
        missile.fieldColor = fieldColor;

        // Inject field VFX
        missile.fieldVfxPrefab = fieldVfxPrefab;
        missile.fieldVfxDesignRadius = fieldVfxDesignRadius;
        missile.autoScaleFieldVfx = autoScaleFieldVfx;
    }
}
