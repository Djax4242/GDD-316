using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class WalkerLeg : MonoBehaviour
{
    /// <summary>
    ///
    ///     Handles procedural walking of 1 leg
    /// 
    /// </summary>
    
    
    
    [Header("--- References ---")]
    [Tooltip("The IK target for this leg")]
    [SerializeField] private Transform legIkTarget;
    [Tooltip("Point at which the downwards raycast fires to check where to step")]
    [SerializeField] private Transform raycastPosition;
    [Tooltip("Need this to check to see if the leg is stretching")]
    [SerializeField] private Transform legTip;
    [Space]
    [SerializeField] private AudioClip[] stepClips;
    [SerializeField] private AudioSource legSource;
    [SerializeField] private NavMeshAgent walkerNavmeshAgent;
    
    
    [Space]
    [Header("--- Leg Settings ---")]
    [Tooltip("How far down the leg is willing to step")]
    [SerializeField] private float maxStepRaycastDistance;
    [Tooltip("Leg will not step on these layers")]
    [SerializeField] private LayerMask invalidStepLayers;
    [Tooltip("The leg must be further than this distance to the raycast target to take a step")]
    [SerializeField] private float minDistanceToStep;
    [Tooltip("How far past the home point the foot lands, as a fraction of minDistanceToStep. Keep below 1 or the leg will re-step immediately")]
    [SerializeField, Range(0f, 0.9f)] private float stepOvershoot = 0.6f;
    [Tooltip("Min time the leg takes to step. Based on speed")]
    [SerializeField] private float minStepDuration;
    [SerializeField] private float maxStepDuration;
    [Tooltip("The horizontal movement of the leg")]
    [SerializeField] private AnimationCurve stepCurveHorizontal;
    [Tooltip("The vertical movement of the leg. Does not control how high the leg steps")]
    [SerializeField] private AnimationCurve stepCurveVertical;
    [Tooltip("How high the leg steps")]
    [SerializeField] private float stepHeight;
    // The current position of the tip of the leg irrespective of the raycast point
    private Vector3 _currentTipPos;
    [HideInInspector] public bool isFootGrounded = true;
    // Where this leg wants to plant its foot right now, refreshed once per frame
    private Vector3 _stepTarget;
    // Does the leg have a step target?
    private bool _hasStepTarget;
    /// <summary>
    ///     How badly this leg needs to step, measured in multiples of minDistanceToStep.
    ///     Below 0 the leg is still inside its threshold and must not step, 0 means it has just
    ///     reached the threshold, 1 means it is stretched a whole extra threshold past it.
    ///     The controller uses this to decide who gets the next step instead of using the step order.
    /// </summary>
    public float StepUrgency { get; private set; } = -1f;
    private float _stepDuration;
    
    
    
    [Space(3)]
    [Header("=== DEBUG ===")]
    [Tooltip("Show where the raycast originates from")]
    [SerializeField] private bool showRaycast;
    [Tooltip("Show the point that the legs raycast hits")]
    [SerializeField] private bool showRaycastHitPosition;
    [Tooltip("Show the normal of the downwards raycast for this leg")]
    [SerializeField] private bool showRaycastNormal;
    [SerializeField] private float gizmoSphereSize;
    [SerializeField] private float gizmoLength;
    
    

    private void Awake()
    {
        // Start with the position of the leg being wherever the target was put before play mode started
        _currentTipPos = legIkTarget.position;
    }

    private void Update()
    {
        // Constantly try to move the leg target to the stored position of the tip so the leg doesn't move when the body moves.
        legIkTarget.position = _currentTipPos;

        UpdateStepUrgency();
    }
    
    private void UpdateStepUrgency()
    {
        // Fire the step raycast once a frame and work out how stretched the leg is, so the
        // controller can compare every leg against every other leg on the same frame to see which leg needs to step the most

        // First checks to see if we have a valid step target. This could be false if the walker was near an edge and the raycast fails.
        _hasStepTarget = Physics.Raycast(raycastPosition.position, Vector3.down, out RaycastHit hitInfo, maxStepRaycastDistance, ~invalidStepLayers);
        
        // 
        if (_hasStepTarget)
        {
            _stepTarget = hitInfo.point;
        }
        else
        {
            // Nowhere valid to step, so fall back to where the foot already is
            _stepTarget = _currentTipPos;
        }

        // A leg that is mid step, or that has nowhere valid to put its foot, is not a candidate so we exit early and give it a low step urgency. 
        if (!isFootGrounded || !_hasStepTarget)
        {
            StepUrgency = -1f;
            return;
        }

        // Calculate a step urgency based on how far away the tip of the leg is to where it wants to go. 
        float distanceToTarget = Vector3.Distance(_currentTipPos, _stepTarget);
        StepUrgency = (distanceToTarget - minDistanceToStep) / minDistanceToStep;
    }

    public bool CheckForStep()
    {
        // No need to check for a step opportunity if the foot is already moving
        if (!isFootGrounded) return false;

        // The foot is still close enough to where it wants to be, so leave it planted
        if (StepUrgency < 0f) return false;

        StartCoroutine(TakeStep());
        return true;
    }

    private IEnumerator TakeStep()
    {
        float lerpValue = walkerNavmeshAgent.velocity.magnitude / walkerNavmeshAgent.speed;
        _stepDuration = Mathf.Lerp(maxStepDuration, minStepDuration, lerpValue);
        
        isFootGrounded = false;
        StepUrgency = -1f;
        float elapsedTime = 0;
        Vector3 originalPosition = _currentTipPos;
        
        // Fallback so the foot stays put if the raycast misses
        Vector3 target = originalPosition;

        while (elapsedTime < _stepDuration)
        {
            elapsedTime += Time.deltaTime;

            float n = Mathf.Clamp01(elapsedTime / _stepDuration);
            float t = stepCurveHorizontal.Evaluate(n);

            // todo make a gizmo of the offset raycast origin

            // Aim past the ray origin in the direction the foot is traveling.
            Vector3 stepDirection = (raycastPosition.position - originalPosition).normalized;
            Vector3 offsetRayOrigin = raycastPosition.position + stepDirection * (minDistanceToStep * stepOvershoot);

            if (Physics.Raycast(offsetRayOrigin, Vector3.down, out RaycastHit hitInfo, maxStepRaycastDistance, ~invalidStepLayers)) target = hitInfo.point;

            Vector3 pos = Vector3.Lerp(originalPosition, target, t);
            
            // Lift the leg off the ground
            float legLift = stepCurveVertical.Evaluate(n) * stepHeight;
            pos += Vector3.up * legLift;

            _currentTipPos = pos;
            // Set the target here too so the IK doesn't lag a frame behind
            legIkTarget.position = pos;
            
            yield return null;
        }

        // Make sure the foot ends exactly on the ground, even if the curves don't end perfectly
        
        _currentTipPos = target;
        if (legSource != null) legSource.PlayOneShot(stepClips[UnityEngine.Random.Range(0, stepClips.Length)]);
        legIkTarget.position = target;
        isFootGrounded = true;
    }
    
    

#if UNITY_EDITOR

    private void OnDrawGizmos()
    {
        if (showRaycast)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(raycastPosition.position, gizmoSphereSize); 
            Gizmos.DrawRay(raycastPosition.position, Vector3.down * maxStepRaycastDistance); 
        }
        
        if (Physics.Raycast(raycastPosition.position, Vector3.down, out RaycastHit hitInfo, maxStepRaycastDistance, ~invalidStepLayers))
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
        
        Gizmos.DrawSphere(_currentTipPos, gizmoSphereSize);
    }

#endif
}