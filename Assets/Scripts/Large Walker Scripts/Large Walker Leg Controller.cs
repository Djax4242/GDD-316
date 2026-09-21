using System;
using System.Linq;
using Microsoft.Win32.SafeHandles;
using UnityEngine;
using UnityEngine.AI;

public class LargeWalkerLegController : MonoBehaviour
{
    /// <summary>
    ///
    ///     Handles the order in which the legs are allowed to walk
    /// 
    /// </summary>
    
    
    
    [Header("--- References ---")]
    [Tooltip("Scripts that handle IK and lerping the target")]
    [SerializeField] private WalkerLeg[] legs;
    [Tooltip("Tips of each leg so we can calculate the bodies rotation")]
    [SerializeField] private Transform frontLeftTip;
    [SerializeField] private Transform frontRightTip;
    [SerializeField] private Transform backRightTip;
    [SerializeField] private Transform backLeftTip;
    [SerializeField] private NavMeshAgent walkerNavmeshAgent;
    
    [Space]
    [Header("--- Leg Settings ---")]
    [Tooltip("Order in which the legs walk")]
    [SerializeField] private int[] stepOrder = { 0, 3, 1, 2 };
    private int _next;
    
    [Space]
    [Header("--- Body Settings ---")]
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
        _restLocalRotation = body.transform.localRotation;
    }

    private void Update()
    {
        OrderLegs();
        RotateBody();
    }

    private void RotateBody()
    {
        Vector3 frontLeftToBackRight = backRightTip.position - frontLeftTip.position;
        Vector3 frontRightToBackLeft = backLeftTip.position - frontRightTip.position;
        Vector3 averageTipNormal = -Vector3.Cross(frontRightToBackLeft, frontLeftToBackRight).normalized;

        Vector3 localNormal = body.transform.parent.InverseTransformDirection(averageTipNormal);
        body.transform.localRotation = Quaternion.FromToRotation(Vector3.up, localNormal);
    }

    private void OrderLegs()
    {
        // Wait until every foot is planted. If any leg is not grounded, this returns true.
        if (legs.Any(leg => !leg.isFootGrounded)) return;

        // Only the leg whose turn it is may step
        // Which leg's turn is it? Look up its index in the step order
        int legIndex = stepOrder[_next];
        
        // Get that leg
        WalkerLeg currentLeg = legs[legIndex];

        // Ask the leg to step. CheckForStep returns true if the leg has started a step.
        bool didStep = currentLeg.CheckForStep();
        if (!didStep) return;
        
        // Move the turn to the next position in the order
        _next += 1;

        // If we've gone past the end of the order, wrap back to the start
        if (_next >= stepOrder.Length) _next = 0;
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