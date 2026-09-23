using System;
using System.Linq;
using Microsoft.Win32.SafeHandles;
using UnityEngine;
using UnityEngine.AI;

public class LegController : MonoBehaviour
{
    /// <summary>
    ///
    ///     Handles the order in which the legs are allowed to walk
    ///     Also handles tilting the main body
    /// 
    /// </summary>
    
    
    
    [Header("--- References ---")]
    [Tooltip("Scripts that handle IK and lerping the target")]
    [SerializeField] private WalkerLeg[] legScripts;
    [Tooltip("Need these tips so we can calculate the tilt of the body")]
    [SerializeField] private Transform frontLeftTip;
    [SerializeField] private Transform frontRightTip;
    [SerializeField] private Transform backRightTip;
    [SerializeField] private Transform backLeftTip;
    [Tooltip("Need this so we can rotate the body to make the walk look more natural and have weight")]
    [SerializeField] private GameObject body;
    
    [Space]
    [Header("--- Leg Settings ---")]
    [Tooltip("SingleLeg walks one leg at a time, so a full cycle takes four steps. DiagonalPairs steps opposite corners together. Diagonal movement is better for fast enemies")]
    [SerializeField] private Gait gait = Gait.SingleLeg;
    [Tooltip("Order in which the legs walk. Only used by the SingleLeg gait")]
    [SerializeField] private int[] stepOrder = { 0, 3, 1, 2 };
    [Tooltip("First diagonal pair, as indices into Leg Scripts. Only used by the DiagonalPairs gait")]
    [SerializeField] private int[] firstDiagonalPair = { 0, 2 };
    [Tooltip("Second diagonal pair, as indices into Leg Scripts. Only used by the DiagonalPairs gait")]
    [SerializeField] private int[] secondDiagonalPair = { 1, 3 };
    [Tooltip("A leg stretched this far past its own step threshold is allowed to skip the line and step first.")]
    [SerializeField] private float urgentStretch;
    // The next leg to be screened for urgency
    private int _next;
    // The next diagonal pair to be screened for urgency
    private int _nextPair;

    public enum Gait
    {
        // One leg at a time, in stepOrder
        SingleLeg,
        // Opposite corners together, alternating between the two pairs
        DiagonalPairs
    }
    
    [Space]
    [Header("--- Body Settings ---")]
    [Tooltip("Tilt the body when the walker takes a step")]
    [SerializeField] private bool tiltBody;
    [Tooltip("How quickly the body settles into the tilt")]
    [SerializeField] private float tiltSmoothing;
    [Tooltip("Furthest the body may lean away from its rest orientation, in degrees")]
    [SerializeField, Range(0f, 90f)] private float maxTiltAngle = 25f;
    // The initial offset the body has
    private Quaternion _restLocalRotation;
    // The tilt the body is easing toward. Held between frames so a frame with an unusable normal can keep
    // the last good tilt instead of snapping somewhere random.
    private Quaternion _targetLocalRotation;
    
    
    
    [Space(3)]
    [Header("=== DEBUG ===")]  
    [Tooltip("Show green spheres at the leg tips")]
    [SerializeField] private bool showLegTips;
    [Tooltip("Show diagonal lines from each leg")]
    [SerializeField] private bool showLegDiagonals;
    [Tooltip("Show the normal created by the positions of each leg tip")]
    [SerializeField] private bool showLegNormal;
    [SerializeField] private float gizmoSphereSize;
    [SerializeField] private float gizmoLength;


    private void Awake()
    {
        // Generate a rotation offset that the body uses when it spins. The initial offset the body has
        _restLocalRotation = body.transform.localRotation;
        _targetLocalRotation = _restLocalRotation;
    }

    private void Update()
    {
        OrderLegs();
        if(tiltBody) TiltBody();
    }

    private void TiltBody()
    {
        // Get the diagonal directions from the front and back legs to compute an average normal direction that the legs positions create.
        Vector3 frontLeftToBackRight = backRightTip.position - frontLeftTip.position;
        Vector3 frontRightToBackLeft = backLeftTip.position - frontRightTip.position;
        Vector3 tipCross = -Vector3.Cross(frontRightToBackLeft, frontLeftToBackRight);

        // When the two diagonals come close to lining up, which happens whenever the feet collapse toward
        // each other or two of them end up in nearly the same place, the cross product falls to almost
        // nothing. Normalising that gives a meaningless direction and the body snaps somewhere random.
        // Skip the frame and keep the last good tilt instead. This is the actual bug out.
        if (tipCross.sqrMagnitude > 0.0001f)
        {
            Vector3 averageTipNormal = tipCross.normalized;

            // Which way this normal points depends on the winding of the four tips, and that can invert when
            // the feet cross over each other. When it does, the normal points down through the floor and the
            // body rolls upside down. Force it back to the same side as the walker's own up.
            if (Vector3.Dot(averageTipNormal, body.transform.parent.up) < 0f) averageTipNormal = -averageTipNormal;

            // Convert world normal to local relative to the bodies parent. Then rotate the body to face the normal.
            Vector3 localNormal = body.transform.parent.InverseTransformDirection(averageTipNormal);
            Quaternion desiredLocalRotation = Quaternion.FromToRotation(Vector3.up, localNormal) * _restLocalRotation;

            // Even a perfectly valid normal can be steep enough to look broken, so cap how far from rest the
            // body is ever allowed to lean.
            _targetLocalRotation = Quaternion.RotateTowards(_restLocalRotation, desiredLocalRotation, maxTiltAngle);
        }

        // Ease toward the target rather than snapping to it. The exponential keeps the smoothing feeling the
        // same whatever the frame rate, which a plain Slerp by deltaTime would not.
        float smoothing = 1f - Mathf.Exp(-tiltSmoothing * Time.deltaTime);
        body.transform.localRotation = Quaternion.Slerp(body.transform.localRotation, _targetLocalRotation, smoothing);
    }

    private void OrderLegs()
    {
        // Wait until every foot is planted. If any leg is not grounded, this returns true.
        if (legScripts.Any(leg => !leg.isFootGrounded)) return;

        if (gait == Gait.DiagonalPairs)
        {
            OrderDiagonalPairs();
            return;
        }

        // This is the slot that wins this current frame. -1 is the nothing found exit below. A real slot is always greater than 0. Used to pick a leg in the walk order. 
        int chosenSlot = -1;

        // Loops four times
        for (int i = 0; i < stepOrder.Length; i++)
        {
            int slot = (_next + i) % stepOrder.Length;
            
            // This leg has an urgency below zero, re-run the loop.
            if (legScripts[stepOrder[slot]].StepUrgency < 0f) continue;

            chosenSlot = slot;
            break;
        }

        // Nobody needs to step this frame
        if (chosenSlot < 0) return;

        // A leg that is badly stretched jumps the queue, otherwise it keeps stretching while
        // the legs ahead of it take tiny steps.
        for (int slot = 0; slot < stepOrder.Length; slot++)
        {
            float urgency = legScripts[stepOrder[slot]].StepUrgency;
            if (urgency < urgentStretch) continue;
            if (urgency <= legScripts[stepOrder[chosenSlot]].StepUrgency) continue;

            chosenSlot = slot;
        }

        // Ask the leg to step. CheckForStep returns true if the leg has started a step.
        bool didStep = legScripts[stepOrder[chosenSlot]].CheckForStep();
        if (!didStep) return;

        // The turn resumes from whoever follows the leg that just stepped, so the gait keeps
        // cycling in the authored order.
        _next = (chosenSlot + 1) % stepOrder.Length;
    }

    // Same idea as OrderLegs, but the queue holds two diagonal pairs instead of four single legs. Because a
    // full cycle is two steps rather than four, each leg gets its turn twice as often, which is what lets the
    // walker keep its feet under it at higher speeds and turn rates.
    private void OrderDiagonalPairs()
    {
        // The pair that wins this frame. -1 is the nothing found exit below.
        int chosenPair = -1;

        // Loops twice
        for (int i = 0; i < 2; i++)
        {
            int pair = (_nextPair + i) % 2;

            // Neither leg in this pair needs to move, re-run the loop.
            if (GetPairUrgency(pair) < 0f) continue;

            chosenPair = pair;
            break;
        }

        // Nobody needs to step this frame
        if (chosenPair < 0) return;

        // A badly stretched pair jumps the queue, exactly as a single leg does in the other gait.
        for (int pair = 0; pair < 2; pair++)
        {
            float urgency = GetPairUrgency(pair);
            if (urgency < urgentStretch) continue;
            if (urgency <= GetPairUrgency(chosenPair)) continue;

            chosenPair = pair;
        }

        // Step both legs in the pair, forcing the one that doesn't think it needs to move. The two legs are
        // at different places and last stepped at different times, so they practically never pass their own
        // thresholds on the same frame. Letting the pair mate opt out collapses this straight back into a
        // one leg at a time gait, which is the whole thing this mode exists to avoid.
        bool didStep = false;
        foreach (int legIndex in GetPair(chosenPair))
        {
            if (legIndex < 0 || legIndex >= legScripts.Length) continue;
            if (legScripts[legIndex].CheckForStep(true)) didStep = true;
        }

        if (!didStep) return;

        // Hand the turn to the other pair
        _nextPair = (chosenPair + 1) % 2;
    }

    // A pair is as urgent as its most desperate leg, so one badly stretched leg is enough to move the pair
    private float GetPairUrgency(int pair)
    {
        float highest = -1f;

        foreach (int legIndex in GetPair(pair))
        {
            if (legIndex < 0 || legIndex >= legScripts.Length) continue;

            float urgency = legScripts[legIndex].StepUrgency;
            if (urgency > highest) highest = urgency;
        }

        return highest;
    }

    private int[] GetPair(int pair)
    {
        return pair == 0 ? firstDiagonalPair : secondDiagonalPair;
    }

    
    
#if UNITY_EDITOR
    
    private void OnDrawGizmos()
    {
        if (showLegTips)
        {
            Gizmos.color = Color.chartreuse;
    
            Gizmos.DrawSphere(frontLeftTip.position, gizmoSphereSize);
            Gizmos.DrawSphere(frontRightTip.position, gizmoSphereSize);
            Gizmos.DrawSphere(backRightTip.position, gizmoSphereSize);
            Gizmos.DrawSphere(backLeftTip.position, gizmoSphereSize);
        }

        Gizmos.color = Color.orangeRed;
        Vector3 frontLeftToBackRight = backRightTip.position - frontLeftTip.position;
        Vector3 frontRightToBackLeft = backLeftTip.position - frontRightTip.position;
        Vector3 averageTipNormal = -Vector3.Cross(frontRightToBackLeft, frontLeftToBackRight).normalized;
        
        if (showLegDiagonals)
        {
            Gizmos.DrawRay(frontLeftTip.position, frontLeftToBackRight);
            Gizmos.DrawRay(frontRightTip.position, frontRightToBackLeft);
        }

        if (showLegNormal)
        {
            Gizmos.DrawRay(transform.position, averageTipNormal * gizmoLength);   
        }
    }

#endif
}