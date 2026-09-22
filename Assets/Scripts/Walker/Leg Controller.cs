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
    
    [Space]
    [Header("--- Leg Settings ---")]
    [Tooltip("Order in which the legs walk")]
    [SerializeField] private int[] stepOrder = { 0, 3, 1, 2 };
    [Tooltip("A leg stretched this far past its own step threshold is allowed to skip the line and step first.")]
    [SerializeField] private float urgentStretch = 0.75f;
    // The next leg to be screened for urgency
    private int _next;
    
    [Space]
    [Header("--- Body Settings ---")]
    [Tooltip("Tilt the body when the walker takes a step")]
    [SerializeField] private bool tiltBody;
    [Tooltip("Need this so we can rotate the body to make the walk look more natural and have weight")]
    [SerializeField] private GameObject body;
    // The initial offset the body has
    private Quaternion _restLocalRotation;
    
    
    
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
        Vector3 averageTipNormal = -Vector3.Cross(frontRightToBackLeft, frontLeftToBackRight).normalized;

        // Convert world normal to local relative to the bodies parent. Then rotate the body to face the normal. 
        Vector3 localNormal = body.transform.parent.InverseTransformDirection(averageTipNormal);
        body.transform.localRotation = Quaternion.FromToRotation(Vector3.up, localNormal) * _restLocalRotation;
    }

    private void OrderLegs()
    {
        // Wait until every foot is planted. If any leg is not grounded, this returns true.
        if (legScripts.Any(leg => !leg.isFootGrounded)) return;
        
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