using System.Linq;
using UnityEngine;

public class LargeWalkerLegController : MonoBehaviour
{
    [SerializeField] private WalkerLeg[] legs;
    [Tooltip("Leg indices in the order they step, e.g. front-left, back-right, front-right, back-left")]
    [SerializeField] private int[] stepOrder = { 0, 3, 1, 2 };

    private int _next;

    private void Update()
    {
        // Wait until every foot is planted
        if (legs.Any(leg => !leg.isFootGrounded)) return;

        // Only the leg whose turn it is may step
        if (legs[stepOrder[_next]].CheckForStep())
            _next = (_next + 1) % stepOrder.Length;
    }
}