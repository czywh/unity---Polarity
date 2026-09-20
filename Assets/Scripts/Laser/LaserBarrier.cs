using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Laser barrier gameplay logic (separate from the Hovl_Laser visuals): the laser is always on along forward.
///   - Blocked by blockMask (bridge / wall) -> the beam is cut off there; targets behind it are safe.
///   - Hits [player / robot] -> nothing by default (knockbackCharacters = false): characters walk through the beam freely.
///     Turn knockbackCharacters on to restore the old "pushed out of the beam" behaviour.
///   - Electric fields (ElectricField + their VFX / debug sphere) never block the beam: the beam passes straight through a
///     field and hits whatever is inside it (e.g. the Energy Relay that opened the field). See BlockRaycast().
///   - Hits [enemy] -> no knockback, can walk straight through the beam, but continuously loses enemyDrainPerSecond energy per second.
///
/// Division of the two rules: for characters it's a "wall", for enemies it's a "drain".
/// Enemies can walk into the beam at the cost of losing energy; so the player can lure enemies into the laser to drain them dry,
/// instead of the laser simply blocking them in place.
///
/// Attach to the laser tower (same object and orientation as Hovl_Laser). Beam direction = transform.forward.
/// </summary>
public class LaserBarrier : MonoBehaviour
{
    [Header("Beam")]
    public float maxLength = 30f;
    [Tooltip("Beam radius (thickness used to test whether a target is touching it)")]
    public float beamRadius = 0.35f;

    [Header("Layers")]
    [Tooltip("Layers that block / cut off the beam (bridge, wall) -- targets behind them are unaffected")]
    public LayerMask blockMask;
    [Tooltip("Target layers affected by the beam (Player / Robot / Enemy)")]
    public LayerMask knockbackMask;

    [Header("Knockback (player / robot only)")]
    [Tooltip("Push the player / robot out of the beam. OFF by default: characters walk through the beam and are not affected by it")]
    public bool knockbackCharacters = false;
    [Tooltip("Distance each knockback pushes the character away (a small step)")]
    public float knockbackDistance = 0.8f;
    [Tooltip("Minimum interval (seconds) between two knockbacks on the same character, to avoid pushing every frame")]
    public float knockbackCooldown = 0.25f;
    [Tooltip("Whether knockback is pushed smoothly over several frames (otherwise teleports one step)")]
    public bool smooth = true;
    [Tooltip("Smooth push speed (units/sec)")]
    public float smoothSpeed = 12f;

    [Header("Energy Drain (enemies only)")]
    [Tooltip("Energy drained per second while an enemy stays in the beam. Settled continuously by actual contact time, not a lump sum per touch")]
    public float enemyDrainPerSecond = 10f;
    [Tooltip("Checked: kill the enemy outright when the laser drains it to 0 (via EnemyDeath.Kill, with dissolve effect).\nUnchecked: it just shuts down from no energy and can be revived by recharging in the robot's electric field")]
    public bool killWhenDrained = false;

    [Header("Slow (enemies only)")]
    [Tooltip("Enemy movement speed multiplier inside the beam. 0.333 = down to 1/3 of normal speed; 1 = no slow")]
    [Range(0.05f, 1f)]
    public float enemySlowMultiplier = 1f / 3f;
    [Tooltip("How long (seconds) the slow lingers after leaving the beam. Gives some slack so speed doesn't flicker at the beam's edge")]
    public float slowLinger = 0.25f;

    [Header("Energy Drain (electric objects: drain pylons, etc.)")]
    [Tooltip("Energy drained per second when the beam hits an object with ElectricEntity. 0 = doesn't affect objects.\nNote: the object's layer must be in blockMask so the beam can hit it and be cut off")]
    public float entityDrainPerSecond = 30f;
    [Tooltip("Checked: only drain energy pylons (EnergyPylon).\nUnchecked: any ElectricEntity that isn't externallyDriven gets drained")]
    public bool onlyDrainPylons = true;

    [Header("Owner")]
    [Tooltip("The character that fired this beam (set by PlayerLaserGun). The owner and everything under it are never knocked back by their own beam")]
    public Transform owner;

    [Header("Debug")]
    public bool drawGizmo = true;
    [Tooltip("Print knockback hit logs (energy drain is continuous per frame, not logged; see the read-only fields below)")]
    public bool verboseLog = false;
    [Tooltip("Number of enemies being drained by the beam this frame")]
    [SerializeField] private int drainingCountReadout;
    [Tooltip("Electric object being drained by the beam this frame")]
    [SerializeField] private string entityDrainReadout = "(none)";

    private readonly Dictionary<int, float> cooldowns = new Dictionary<int, float>();
    private readonly Dictionary<Transform, Vector3> pendingPush = new Dictionary<Transform, Vector3>();
    private static readonly Collider[] buffer = new Collider[16];
    private static readonly RaycastHit[] rayBuffer = new RaycastHit[64];

    /// <summary>
    /// The beam's block raycast: nearest collider on blockMask along forward, skipping triggers, anything under an
    /// ElectricField (field volumes / VFX never block a beam), the Ignore Raycast layer and the owner's own hierarchy.
    /// Shared by the gameplay tick (FixedUpdate) and the Hovl visual (Hovl_LaserDemo), so what you see is what hits.
    /// </summary>
    public bool BlockRaycast(out RaycastHit hit)
    {
        return BlockRaycast(transform.position, transform.forward, maxLength, out hit);
    }

    public bool BlockRaycast(Vector3 origin, Vector3 dir, float length, out RaycastHit hit)
    {
        int mask = blockMask.value & ~(1 << 2);   // never let "Ignore Raycast" block the beam
        int n = Physics.RaycastNonAlloc(origin, dir, rayBuffer, length, mask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        int bestIdx = -1;
        for (int i = 0; i < n; i++)
        {
            var h = rayBuffer[i];
            if (h.distance >= best) continue;
            Transform t = h.collider.transform;
            if (owner != null && (t == owner || t.IsChildOf(owner))) continue;         // own body / weapon
            if (t.IsChildOf(transform)) continue;                                       // own beam effects
            if (h.collider.GetComponentInParent<ElectricField>() != null) continue;     // electric field volume / VFX
            best = h.distance;
            bestIdx = i;
        }
        if (bestIdx < 0) { hit = default; return false; }
        hit = rayBuffer[bestIdx];
        return true;
    }

    private void FixedUpdate()
    {
        Vector3 origin = transform.position;
        Vector3 dir = transform.forward;

        // (1) Length of the beam until cut off by the block layer
        float length = maxLength;
        entityDrainReadout = "(none)";
        if (BlockRaycast(origin, dir, maxLength, out RaycastHit blockHit))
        {
            length = blockHit.distance;
            // Hit a deflector -> activate it and fire a new beam from the new angle
            var redirector = blockHit.collider.GetComponentInParent<LaserRedirector>();
            if (redirector != null) redirector.Hit(dir);

            // Hit an Energy Relay -> it opens its electric field while the beam stays on it
            var relay = blockHit.collider.GetComponentInParent<EnergyRelay>();
            if (relay != null) relay.Hit(dir);

            // Hit an electric object (drain pylon, etc.) -> drain continuously
            var entity = blockHit.collider.GetComponentInParent<ElectricEntity>();
            if (entity != null) DrainEntity(entity);
            else entityDrainReadout = "(none)";
        }

        // (2) Find targets within the capsule from origin to the cut-off point
        Vector3 end = origin + dir * length;
        int count = Physics.OverlapCapsuleNonAlloc(origin, end, beamRadius, buffer, knockbackMask, QueryTriggerInteraction.Ignore);

        int draining = 0;

        for (int i = 0; i < count; i++)
        {
            var col = buffer[i];
            if (col == null) continue;

            // Resolve to "the target that was hit" itself: prefer the object holding its own CharacterController,
            // not transform.root (several enemies may share one parent, so root would point at the wrong object)
            Transform target = ResolveTarget(col);
            if (target == null) continue;
            if (owner != null && (target == owner || target.IsChildOf(owner))) continue;   // own beam

            if (IsEnemy(target))
            {
                // -- Enemy: no knockback, continuous drain + slow --
                if (DrainEnemy(target)) draining++;
                SlowEnemy(target);
                continue;
            }

            // -- Player / robot: no drain; knockback only if enabled (off by default -> they pass through freely) --
            if (!knockbackCharacters) continue;

            int id = target.GetInstanceID();
            if (cooldowns.TryGetValue(id, out float until) && Time.time < until) continue;
            cooldowns[id] = Time.time + knockbackCooldown;

            if (verboseLog) Debug.Log($"[LaserBarrier] Knockback {target.name}", target);

            // Knockback direction: perpendicular to the beam, from the beam line toward the character (push it back to its own side)
            Vector3 toP = target.position - origin;
            Vector3 along = Vector3.Project(toP, dir);
            Vector3 perp = toP - along;
            perp.y = 0f;
            Vector3 pushDir = perp.sqrMagnitude > 0.0001f ? perp.normalized
                                                          : Vector3.Cross(dir, Vector3.up).normalized;

            ApplyPush(target, pushDir * knockbackDistance);
        }

        drainingCountReadout = draining;

        if (smooth && pendingPush.Count > 0) TickPending();
    }

    private static bool IsEnemy(Transform t)
    {
        return t.GetComponentInParent<EnemyChaser>() != null
            || t.GetComponentInParent<EnemyDeath>() != null;
    }

    /// Continuously drain an enemy. Returns whether energy was actually deducted (for debug counting)
    private bool DrainEnemy(Transform enemy)
    {
        if (enemyDrainPerSecond <= 0f) return false;

        var es = enemy.GetComponentInParent<EnergySystem>();
        if (es == null) es = enemy.GetComponentInChildren<EnergySystem>();
        if (es == null) return false;

        if (!es.HasEnergy) return false;   // Already empty, no need to deduct again

        // FixedUpdate is a fixed step; multiply by fixedDeltaTime to get a true "N points per second"
        es.Drain(enemyDrainPerSecond * Time.fixedDeltaTime);

        // Drained by the laser -> optionally kill outright (via EnemyDeath, with dissolve effect)
        if (killWhenDrained && es.IsEmpty)
        {
            var death = enemy.GetComponentInParent<EnemyDeath>();
            if (death != null) death.Kill();
        }
        return true;
    }

    /// Continuously drain an electric object (drain pylon, etc.). Uses DrainExternal to bypass the energyMode restriction --
    /// drain pylons are ChargeOnly (the player can't drain them), but the laser as environmental damage can
    private void DrainEntity(ElectricEntity entity)
    {
        if (entityDrainPerSecond <= 0f) { entityDrainReadout = "(none)"; return; }
        if (onlyDrainPylons && !(entity is EnergyPylon)) { entityDrainReadout = "(none)"; return; }

        entity.DrainExternal(entityDrainPerSecond * Time.fixedDeltaTime);
        entityDrainReadout = $"{entity.name}  {entity.CurrentEnergy:0}/{entity.MaxEnergy:0}";
    }

    /// Slow the enemy. Renewed every FixedUpdate; the enemy returns to normal speed slowLinger seconds after leaving the beam
    private void SlowEnemy(Transform enemy)
    {
        if (enemySlowMultiplier >= 1f) return;

        var chaser = enemy.GetComponentInParent<EnemyChaser>();
        if (chaser == null) return;

        chaser.ApplySlow(enemySlowMultiplier, slowLinger);
    }

    private void ApplyPush(Transform character, Vector3 push)
    {
        if (!smooth) { MoveCharacter(character, push); return; }
        pendingPush.TryGetValue(character, out Vector3 cur);
        pendingPush[character] = cur + push;
    }

    private void TickPending()
    {
        var keys = new List<Transform>(pendingPush.Keys);
        foreach (var t in keys)
        {
            if (t == null) { pendingPush.Remove(t); continue; }
            Vector3 remain = pendingPush[t];
            float step = smoothSpeed * Time.deltaTime;
            Vector3 move = Vector3.ClampMagnitude(remain, step);

            MoveCharacter(t, move);

            remain -= move;
            if (remain.sqrMagnitude < 0.0001f) pendingPush.Remove(t);
            else pendingPush[t] = remain;
        }
    }

    private static void MoveCharacter(Transform t, Vector3 delta)
    {
        var cc = t.GetComponent<CharacterController>();
        if (cc != null) cc.Move(delta);
        else t.position += delta;
    }

    // Resolve from the hit collider to "the target itself", avoiding the shared parent's root
    private static Transform ResolveTarget(Collider col)
    {
        var cc = col.GetComponentInParent<CharacterController>();
        if (cc != null) return cc.transform;
        var chaser = col.GetComponentInParent<EnemyChaser>();
        if (chaser != null) return chaser.transform;
        return col.transform;   // Fallback: the collider itself
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmo) return;
        Vector3 origin = transform.position;
        Vector3 end = origin + transform.forward * maxLength;
        Gizmos.color = Color.red;
        Gizmos.DrawLine(origin, end);
        Gizmos.color = new Color(1f, 0.5f, 0f, 1f);
        Gizmos.DrawWireSphere(origin, beamRadius);
        Gizmos.DrawWireSphere(end, beamRadius);
    }
}