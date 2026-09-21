using System;
using System.Collections;
using UnityEngine;

public class WalkerLeg : MonoBehaviour
{
    /// <summary>
    ///
    ///     Handles procedural walking of 1 leg
    /// 
    /// </summary>
    
    
    
    [Header("--- References ---")]
    [Tooltip("The IK target for this leg")]
    [SerializeField] private Transform legTarget;
    [Tooltip("Point at which the downwards raycast fires to check where to step")]
    [SerializeField] private Transform raycastPosition;
    
    [Space]
    [Header("--- Leg Settings ---")]
    [Tooltip("How far down the leg is willing to step")]
    [SerializeField] private float maxRaycastDistance;
    [Tooltip("Leg will not step on these layers")]
    [SerializeField] private LayerMask invalidStepLayers;
    [Tooltip("The leg must be further than this distance to the raycast target to take a step")]
    [SerializeField] private float minDistanceToStep;
    [Tooltip("How far past the home point the foot lands, as a fraction of minDistanceToStep. Keep below 1 or the leg will re-step immediately")]
    [SerializeField, Range(0f, 0.9f)] private float stepOvershoot = 0.6f;
    [Tooltip("How long the leg takes to step")]
    [SerializeField] private float stepDuration;
    [Tooltip("The horizontal movement of the leg")]
    [SerializeField] private AnimationCurve stepCurveHorizontal;
    [Tooltip("The vertical movement of the leg. Does not control how high the leg steps")]
    [SerializeField] private AnimationCurve stepCurveVertical;
    [Tooltip("How high the leg steps")]
    [SerializeField] private float stepHeight;
    // The current position of the tip of the leg irrespective of the raycast point
    private Vector3 CurrentPos { get; set; }
    [HideInInspector] public bool isFootGrounded = true;
    
    
    
    [Space(3)]
    [Header("=== DEBUG ===")]
    [Tooltip("Show where the raycast originates from")]
    [SerializeField] private bool showRaycastOrigin;
    [Tooltip("Show the point that the legs raycast hits")]
    [SerializeField] private bool showRaycastHitPosition;
    [Tooltip("Show the normal of the downwards raycast for this leg")]
    [SerializeField] private bool showRaycastNormal;
    [SerializeField] private float gizmoSphereSize;
    [SerializeField] private float gizmoLength;
    
    

    private void Awake()
    {
        // Start with the position of the leg being wherever the target was put before play mode started
        CurrentPos = legTarget.position;
    }

    private void Update()
    {
        // Constantly try to move the leg target to the stored position of the tip so the leg doesn't move when the body moves. 
        legTarget.position = CurrentPos;
    }

    public bool CheckForStep()
    {
        // No need to check for a step opportunity if the foot is already moving
        if (!isFootGrounded) return false;
        
        // If the foot is far enough away from the raycast position, try to initiate a step
        if (Physics.Raycast(raycastPosition.position, Vector3.down, out RaycastHit hitInfo, maxRaycastDistance, ~invalidStepLayers))
        {
            if ((CurrentPos - hitInfo.point).sqrMagnitude >= minDistanceToStep * minDistanceToStep)
            {
                StartCoroutine(TakeStep());
                return true;
            }
        }

        return false;
    }

    private IEnumerator TakeStep()
    {
        isFootGrounded = false;
        float elapsedTime = 0;
        Vector3 originalPosition = CurrentPos;
        
        // Fallback so the foot stays put if the raycast misses
        Vector3 target = originalPosition;

        while (elapsedTime < stepDuration)
        {
            elapsedTime += Time.deltaTime;

            float n = Mathf.Clamp01(elapsedTime / stepDuration);
            float t = stepCurveHorizontal.Evaluate(n);
            
            // todo make a gizmo of the offset raycast origin
            
            // Aim past the ray origin in the direction the foot is traveling. 
            Vector3 stepDirection = (raycastPosition.position - originalPosition).normalized;
            Vector3 offsetRayOrigin = raycastPosition.position + stepDirection * (minDistanceToStep * stepOvershoot);

            if (Physics.Raycast(offsetRayOrigin, Vector3.down, out RaycastHit hitInfo, maxRaycastDistance, ~invalidStepLayers)) target = hitInfo.point;
            
            //todo instead of getting the average normal of all surfaces the legs hit, get the normal created by the square created by the four leg positions
                
            Vector3 pos = Vector3.Lerp(originalPosition, target, t);
            
            // Lift the leg off the ground
            float legLift = stepCurveVertical.Evaluate(n) * stepHeight;
            pos += Vector3.up * legLift;

            CurrentPos = pos;
            // Set the target here too so the IK doesn't lag a frame behind
            legTarget.position = pos;
            
            yield return null;
        }

        // Make sure the foot ends exactly on the ground, even if the curves don't end perfectly
        CurrentPos = target;
        legTarget.position = target;
        isFootGrounded = true;
    }
    
    

#if UNITY_EDITOR

    private void OnDrawGizmos()
    {
        if (showRaycastOrigin)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(raycastPosition.position, gizmoSphereSize); 
        }
        
        if (Physics.Raycast(raycastPosition.position, Vector3.down, out RaycastHit hitInfo, maxRaycastDistance, ~invalidStepLayers))
        {
            if (showRaycastHitPosition)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawSphere(hitInfo.point, gizmoSphereSize);
            }
            
            if (showRaycastNormal)
            {
                Gizmos.color = Color.orangeRed;
                Gizmos.DrawRay(hitInfo.point, hitInfo.normal * gizmoLength);
            }
        }
    }

#endif
}