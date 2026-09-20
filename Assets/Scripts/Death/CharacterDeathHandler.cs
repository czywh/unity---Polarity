using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Orchestrates character death / respawn. Attach to the root of both player and robot.
/// Flow: Die() -> disable this character's control / freeze physics -> play dissolve -> wait respawnDelay seconds
///       -> teleport to this character's own respawn point -> reappear -> hand back to CharacterSwitcher to decide control.
///
/// Key point: on respawn it does [not enable control itself]; it calls characterSwitcher.RefreshControlState(),
/// letting the switcher decide who can move based on the "current character". So if you switch characters while dead, respawning won't wrongly re-enable this one.
/// </summary>
[RequireComponent(typeof(CharacterId))]
public class CharacterDeathHandler : MonoBehaviour
{
    [Header("References")]
    public DissolvingController dissolvingController;
    [Tooltip("Character switcher. When set, control is decided centrally by the switcher (recommended)")]
    public CharacterSwitcher characterSwitcher;

    [Header("Respawn Flow")]
    [Tooltip("Wait time from end of dissolve to respawn (the 1 second in the spec)")]
    public float respawnDelay = 1f;
    [Tooltip("Whether to play the \"reappear\" animation after respawn (1->0)")]
    public bool appearOnRespawn = true;

    [Header("Control Scripts to Disable on Death (fallback when no CharacterSwitcher)")]
    public MonoBehaviour[] componentsToDisable;

    [Header("Event Callbacks")]
    public UnityEvent onDeath;
    public UnityEvent onRespawn;

    // For CharacterSwitcher to check: a dying character should not have control enabled
    public bool IsDying => isDying;

    private CharacterId id;
    private Rigidbody rb;
    private CharacterController cc;
    private bool originalKinematic;
    private bool isDying;

    void Awake()
    {
        id = GetComponent<CharacterId>();
        rb = GetComponent<Rigidbody>();
        cc = GetComponent<CharacterController>();
        if (rb != null) originalKinematic = rb.isKinematic;

        // Auto-find the switcher in the scene if not assigned, so a missing reference doesn't make respawn take the wrong branch and re-enable control
        if (characterSwitcher == null)
            characterSwitcher = FindObjectOfType<CharacterSwitcher>();

        // Register back with the switcher, so the two death handler slots don't need manual assignment either
        if (characterSwitcher != null)
            characterSwitcher.RegisterDeathHandler(id.characterType == CharacterType.Player, this);
    }

    void Start()
    {
        // If there's no respawn point yet at game start, fall back to the initial spawn position
        if (RespawnManager.Instance != null &&
            !RespawnManager.Instance.TryGetRespawnPoint(id.characterType, out _))
        {
            RespawnManager.Instance.SetRespawnPoint(id.characterType, transform.position, transform.rotation);
        }
    }

    /// <summary>Trigger death. Called by DeathZone, or can be called manually.</summary>
    public void Die()
    {
        if (isDying) return;
        isDying = true;          // Set dying state first so SuppressControl disables this character
        StartCoroutine(DeathSequence());
    }

    IEnumerator DeathSequence()
    {
        onDeath?.Invoke();
        SuppressControl();
        FreezePhysics(true);

        // 1) Play dissolve and wait for it to finish
        bool dissolveDone = (dissolvingController == null);
        if (dissolvingController != null)
            dissolvingController.Dissolve(() => dissolveDone = true);
        while (!dissolveDone) yield return null;

        // 2) Wait
        yield return new WaitForSeconds(respawnDelay);

        // 3) Respawn
        Respawn();
    }

    void Respawn()
    {
        Vector3 pos = transform.position;
        Quaternion rot = transform.rotation;

        if (RespawnManager.Instance != null &&
            RespawnManager.Instance.TryGetRespawnPoint(id.characterType, out var data))
        {
            pos = data.position;
            rot = data.rotation;
        }
        else
        {
            Debug.LogWarning($"[CharacterDeathHandler] {id.characterType} has no usable respawn point, respawning in place.");
        }

        TeleportTo(pos, rot);

        if (appearOnRespawn && dissolvingController != null)
            dissolvingController.Appear(FinishRespawn);
        else
        {
            dissolvingController?.SetDissolveAmount(0f);
            FinishRespawn();
        }
    }

    void FinishRespawn()
    {
        FreezePhysics(false);
        isDying = false;          // Clear dying state first so restoring control takes effect
        RestoreControl();
        onRespawn?.Invoke();
    }

    // -- Control: always delegated to the switcher; fallback list only when there is no switcher --
    void SuppressControl()
    {
        if (characterSwitcher != null) characterSwitcher.RefreshControlState(); // Disables this character while dying
        else SetControlEnabled(false);
    }

    void RestoreControl()
    {
        if (characterSwitcher != null) characterSwitcher.RefreshControlState(); // Re-decide based on current character
        else SetControlEnabled(true);
    }

    void SetControlEnabled(bool on)
    {
        if (componentsToDisable != null)
            foreach (var c in componentsToDisable)
                if (c != null) c.enabled = on;
    }

    // -- Physics --
    void FreezePhysics(bool freeze)
    {
        if (rb == null) return;
        if (freeze) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        rb.isKinematic = freeze ? true : originalKinematic;
    }

    void TeleportTo(Vector3 pos, Quaternion rot)
    {
        // CharacterController locks the transform; must disable it before teleporting
        if (cc != null) cc.enabled = false;
        if (rb != null) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; } // Renamed to linearVelocity in Unity 6
        transform.SetPositionAndRotation(pos, rot);
        Physics.SyncTransforms();
        if (cc != null) cc.enabled = true;
    }
}