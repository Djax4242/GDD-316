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
    // The current position of the tip of the leg irrespective of the raycast point
    private Vector3 _currentPos;
    public bool isFootGrounded = true;
    
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
    [SerializeField] private AnimationCurve stepCurveHorizontal;
    [SerializeField] private AnimationCurve stepCurveVertical;
    [SerializeField] private float stepHeight;
    
    
    
    [Space(3)]
    [Header("=== DEBUG ===")] 
    [SerializeField] private float gizmoSphereSize;

    

    private void Awake()
    {
        // Start with the position of the leg being wherever the target was put before play mode started
        _currentPos = legTarget.position;
    }

    private void Update()
    {
        legTarget.position = _currentPos;
        
    }

    public bool CheckForStep()
    {
        // Constantly try to move the leg target to the stored position of the tip so the leg doesn't move when the body moves. 

        if (!isFootGrounded) return false;
        
        if (Physics.Raycast(raycastPosition.position, Vector3.down, out RaycastHit hitInfo, maxRaycastDistance, ~invalidStepLayers))
        {
            if ((_currentPos - hitInfo.point).sqrMagnitude >= minDistanceToStep * minDistanceToStep)
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
        Vector3 originalPosition = _currentPos;
        
        // Fallback so the foot stays put if the raycast misses
        Vector3 target = originalPosition;

        while (elapsedTime < stepDuration)
        {
            elapsedTime += Time.deltaTime;

            float n = Mathf.Clamp01(elapsedTime / stepDuration);
            float t = stepCurveHorizontal.Evaluate(n);

            // Aim past the home point in the direction the foot is travelling
            Vector3 stepDirection = Vector3.ProjectOnPlane(raycastPosition.position - originalPosition, Vector3.up).normalized;
            Vector3 rayOrigin = raycastPosition.position + stepDirection * (minDistanceToStep * stepOvershoot);

            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hitInfo, maxRaycastDistance, ~invalidStepLayers))
                target = hitInfo.point;
                
            Vector3 pos = Vector3.Lerp(originalPosition, target, t);
            
            float legLift = stepCurveVertical.Evaluate(n) * stepHeight;
            pos += Vector3.up * legLift;

            _currentPos = pos;
            // Set the target here too so the IK doesn't lag a frame behind
            legTarget.position = pos;
            
            yield return null;
        }

        // Make sure the foot ends exactly on the ground, even if the curves don't end perfectly
        _currentPos = target;
        legTarget.position = target;
        isFootGrounded = true;
    }
    
    

#if UNITY_EDITOR

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(raycastPosition.position, gizmoSphereSize);
        
        if (Physics.Raycast(raycastPosition.position, Vector3.down, out RaycastHit hitInfo, maxRaycastDistance, ~invalidStepLayers))
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawSphere(hitInfo.point, gizmoSphereSize);
        }
        
    }

#endif
}