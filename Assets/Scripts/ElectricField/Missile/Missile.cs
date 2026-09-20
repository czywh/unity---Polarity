using UnityEngine;

/// <summary>
/// ROBOT-06 Missile projectile: flies straight forward; after travelling travelDistance or hitting an obstacle,
/// it deploys a timed electric field at its current position (affects player gravity), then destroys itself.
/// Parameters are injected by MissileLauncher on spawn.
///
/// Field visuals: if fieldVfxPrefab is injected, the deployed field uses that VFX (timed fields fade and shrink out);
/// if empty, falls back to the debug sphere (colored with fieldColor).
/// </summary>
public class Missile : MonoBehaviour
{
    [HideInInspector] public float speed = 20f;
    [HideInInspector] public float travelDistance = 10f;
    [HideInInspector] public LayerMask obstacleMask = ~0;

    // Deployed field config
    [HideInInspector] public float fieldRadius = 4f;
    [HideInInspector] public float holdDuration = 8f;
    [HideInInspector] public float fadeDuration = 4f;
    [HideInInspector] public Color fieldColor = new Color(0.85f, 0.4f, 0.95f, 0.28f);

    // Field visual VFX (injected by MissileLauncher; uses the debug sphere if empty)
    [HideInInspector] public GameObject fieldVfxPrefab;
    [HideInInspector] public float fieldVfxDesignRadius = 3.5f;
    [HideInInspector] public bool autoScaleFieldVfx = true;

    private Vector3 startPos;
    private bool expanded;

    private void Start()
    {
        startPos = transform.position;
    }

    private void Update()
    {
        if (expanded) return;

        float step = speed * Time.deltaTime;

        // Hit an obstacle → deploy early at the hit point
        if (Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, step,
                            obstacleMask, QueryTriggerInteraction.Ignore))
        {
            transform.position = hit.point;
            Expand();
            return;
        }

        transform.position += transform.forward * step;

        if (Vector3.Distance(startPos, transform.position) >= travelDistance)
            Expand();
    }

    private void Expand()
    {
        if (expanded) return;
        expanded = true;

        // Create inactive first, configure, then activate -- ensures color/properties are set by ElectricField.OnEnable
        var go = new GameObject("MissileField");
        go.SetActive(false);
        go.transform.position = transform.position;

        var field = go.AddComponent<ElectricField>();
        field.radius = fieldRadius;
        field.affectsPlayerGravity = true;             // Missile fields make the player float
        field.SetLifetime(holdDuration, fadeDuration); // Hold + fade, then disappear on its own

        if (fieldVfxPrefab != null)
        {
            // Use the VFX prefab as the field visual (timed fields shrink out via shrinkOnFade)
            field.SetVisualPrefab(fieldVfxPrefab);
            field.prefabDesignRadius = fieldVfxDesignRadius;
            field.autoScaleToRadius = autoScaleFieldVfx;
            field.showDebugSphere = false;
        }
        else
        {
            // Fallback: debug sphere, colored with fieldColor
            field.showDebugSphere = true;
            field.debugColor = fieldColor;
        }

        go.SetActive(true);

        Destroy(gameObject);
    }
}