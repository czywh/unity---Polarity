using UnityEngine;

/// <summary>
/// PROP-02  Movable charging dock: inherits ChargingDock (keeps charging ability),
/// and additionally supports "player focus + press interact" to slide between two nodes A / B.
///
/// Interaction split:
///   - Player focuses + presses interact -> toggle A<->B and slide (charging dock movement puzzle)
///   - Robot focuses                    -> charges as usual (inherited from ChargingDock)
///
/// Default access = Both, so the player can focus it to move it and the robot can focus it to charge.
/// </summary>
public class ChargingDockMovable : ChargingDock
{
    [Header("Move Nodes (player presses interact to toggle between two points)")]
    [Tooltip("Node A (empty object placed at the first position)")]
    public Transform waypointA;
    [Tooltip("Node B (empty object placed at the second position)")]
    public Transform waypointB;
    public float moveSpeed = 3f;
    [Tooltip("Lock while moving, ignoring further presses (avoids reversing halfway)")]
    public bool lockWhileMoving = true;
    [Tooltip("Snap to point A at start")]
    public bool snapToAOnStart = true;

    [Header("Debug (read-only at runtime)")]
    [SerializeField] private int targetIndex;   // 0 = A, 1 = B
    [SerializeField] private bool moving;

    private Vector3 PointA => waypointA != null ? waypointA.position : transform.position;
    private Vector3 PointB => waypointB != null ? waypointB.position : transform.position;
    private Vector3 TargetPos => targetIndex == 0 ? PointA : PointB;

    protected override void Reset()
    {
        // Movable charging dock: player moves it + robot charges from it, both need to be able to focus it
        access = InteractAccess.Both;
    }

    private void Start()
    {
        if (snapToAOnStart && waypointA != null)
            transform.position = PointA;
        targetIndex = 0;
    }

    protected override void Update()
    {
        base.Update();   // Keep charging logic (charges while the robot focuses it)

        // Slide toward the current target node
        Vector3 dest = TargetPos;
        if (Vector3.Distance(transform.position, dest) > 0.001f)
        {
            transform.position = Vector3.MoveTowards(transform.position, dest, moveSpeed * Time.deltaTime);
            moving = true;
        }
        else
        {
            moving = false;
        }
    }

    public override void OnInteract(Interactor interactor)
    {
        // Player presses key -> switch target node and slide
        if (interactor != null && interactor.type == InteractorType.Player)
        {
            if (lockWhileMoving && moving) return;   // Ignore while moving
            targetIndex = 1 - targetIndex;           // 0 <-> 1
            return;
        }

        // Others (robot) -> handled by the base class (useful in key-press charging mode)
        base.OnInteract(interactor);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 a = waypointA != null ? waypointA.position : transform.position;
        Vector3 b = waypointB != null ? waypointB.position : transform.position;
        Gizmos.color = Color.green; Gizmos.DrawWireSphere(a, 0.3f);
        Gizmos.color = Color.cyan;  Gizmos.DrawWireSphere(b, 0.3f);
        Gizmos.color = Color.yellow; Gizmos.DrawLine(a, b);
    }
}