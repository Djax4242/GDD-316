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
    [Tooltip("The IK target for this leg. This is the object that the animation rigging package made")]
    [SerializeField] private Transform legIkTarget;
    [Tooltip("Point at which the downwards raycast fires to check where to step")]
    [SerializeField] private Transform raycastOriginPosition;
    [Space]
    [SerializeField] private NavMeshAgent walkerNavmeshAgent;


    [Space]
    [Header("--- Leg Settings ---")]
    [Tooltip("How far down the leg is willing to step")]
    [SerializeField] private float maxStepRaycastDistance;
    [Tooltip("Leg will not step on these layers")]
    [SerializeField] private LayerMask invalidStepLayers;
    [Tooltip("The leg must be further than this distance to the raycast target to take a step")]
    [SerializeField] private float minDistanceToStep;
    [Tooltip("Min time the leg takes to step. Based on speed of navmesh agent")]
    [SerializeField] private float minStepDuration;
    [Tooltip("Max time the leg takes to step. Based on speed of navmesh agent")]
    [SerializeField] private float maxStepDuration;
    [Tooltip("The horizontal movement of the leg")]
    [SerializeField] private AnimationCurve stepCurveHorizontal;
    [Tooltip("The vertical movement of the leg. Does not control how high the leg steps")]
    [SerializeField] private AnimationCurve stepCurveVertical;
    [Tooltip("How high the leg steps")]
    [SerializeField] private float stepHeight;
    [Tooltip("How much the leg should step ahead where its trying to go. This helps turning look good and when the walker is faster than its legs can handle")]
    [SerializeField] private float stepAheadScalar;
    [Tooltip("Percentage of the legs animation where the walker will allow another leg to step")]
    [SerializeField, Range(0f, 1f)] private float nextStepThreshold;
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
    // True for the whole of TakeStep. isFootGrounded gets released early at nextStepThreshold so another leg
    // can start moving, so it can't also be the thing that stops this leg starting a second step on itself.
    private bool _isStepping;
    // The original position of the tip of the foot before it began stepping
    private Vector3 _originalFootPosition;



    [Space(3)]
    [Header("=== DEBUG ===")]
    [Tooltip("Show where the raycast originates from")]
    [SerializeField] private bool showRaycast;
    [Tooltip("Show the point that the legs raycast hits")]
    [SerializeField] private bool showRaycastHitPosition;
    [Tooltip("Show the normal of the downwards raycast for this leg")]
    [SerializeField] private bool showRaycastNormal;
    [Tooltip("Shows the direction from the original position of the foot to its target")]
    [SerializeField] private bool showDirectionToStep;
    [Tooltip("Just use this to make sure that the script is properly tracking the tip position")]
    [SerializeField] private bool showCurrentTipPos;
    [Tooltip("Show the original position of the tip before it stepped")]
    [SerializeField] private bool showOriginalFootPosition;
    [Tooltip("Show the offset foot target based on where the walker is trying to go")]
    [SerializeField] private bool showOffsetFootTarget;
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
        _hasStepTarget = Physics.Raycast(raycastOriginPosition.position, Vector3.down, out RaycastHit hitInfo, maxStepRaycastDistance, ~invalidStepLayers);

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
        // Tests _isStepping rather than isFootGrounded on purpose. Past nextStepThreshold the foot is still in
        // the air but counts as grounded, and measuring the stretch from a mid air tip position reads as a huge
        // urgency and asks the leg to step again while it is already stepping.
        if (_isStepping || !_hasStepTarget)
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
        // No need to check for a step opportunity if the foot is already moving. This has to be _isStepping, not
        // isFootGrounded, or a leg past nextStepThreshold would start a second TakeStep on top of the one still
        // running. Both coroutines would then fight over _currentTipPos and the foot would snap.
        if (_isStepping) return false;

        // The foot is still close enough to where it wants to be, so leave it planted
        if (StepUrgency < 0f) return false;

        StartCoroutine(TakeStep());
        return true;
    }

    private IEnumerator TakeStep()
    {
        // Calculate the time it will take for this step to occur based on the speed of the agent
        float lerpValue = walkerNavmeshAgent.velocity.magnitude / walkerNavmeshAgent.speed;
        _stepDuration = Mathf.Lerp(maxStepDuration, minStepDuration, lerpValue);

        _isStepping = true;
        isFootGrounded = false;
        StepUrgency = -1f;
        float elapsedTime = 0;
        
        // Cache the original position of the tip. 
        _originalFootPosition = _currentTipPos;

        // Fallback so the foot stays put if the raycast misses
        Vector3 footTarget = _originalFootPosition;

        while (elapsedTime < _stepDuration)
        {
            elapsedTime += Time.deltaTime;

            float n = Mathf.Clamp01(elapsedTime / _stepDuration);
            float t = stepCurveHorizontal.Evaluate(n);
            
            if (Physics.Raycast(raycastOriginPosition.position, Vector3.down, out RaycastHit hitInfo, maxStepRaycastDistance, ~invalidStepLayers)) footTarget = hitInfo.point;
            
            // todo Get the point at the end of the direction vector. Using that, we can increase or decrease the direction vector based on how far ahead we want to step. 
            
            Vector3 footPosition = Vector3.Lerp(_originalFootPosition, footTarget, t);

            // Lift the leg off the ground
            float legLift = stepCurveVertical.Evaluate(n) * stepHeight;
            footPosition += Vector3.up * legLift;

            _currentTipPos = footPosition;
            // Set the target here too so the IK doesn't lag a frame behind
            legIkTarget.position = footPosition;

            // Allow other legs to step before this one has landed if nextStepThreshold is below 1
            if (n >= nextStepThreshold && !isFootGrounded)
            {
                isFootGrounded = true;
            }

            yield return null;
        }

        // Make sure the foot ends exactly on the ground, even if the curves don't end perfectly

        _currentTipPos = footTarget;
        legIkTarget.position = footTarget;

        // isFootGrounded is normally already true from nextStepThreshold, but it still needs setting here for
        // the case where that threshold is 1 and the early release never ran.
        isFootGrounded = true;
        _isStepping = false;
    }



#if UNITY_EDITOR

    private void OnDrawGizmos()
    {
        if (showRaycast)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(raycastOriginPosition.position, gizmoSphereSize);
            Gizmos.DrawRay(raycastOriginPosition.position, Vector3.down * maxStepRaycastDistance);
        }

        if (Physics.Raycast(raycastOriginPosition.position, Vector3.down, out RaycastHit hitInfo, maxStepRaycastDistance, ~invalidStepLayers))
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
            
            if (showDirectionToStep)
            {
                Gizmos.color = Color.deepPink;
                Vector3 directionToNextStep = -(_currentTipPos - hitInfo.point).normalized;
                Gizmos.DrawRay(_currentTipPos, directionToNextStep * minDistanceToStep);
            }

            if (showOffsetFootTarget)
            {
                Gizmos.color = Color.purple;
                Vector3 directionToNextStep = -(_currentTipPos - hitInfo.point).normalized;
                Vector3 stepOffsetTarget = _currentTipPos + directionToNextStep * stepAheadScalar;
                Gizmos.DrawSphere(stepOffsetTarget, gizmoSphereSize);
            }
        }

        if (showOriginalFootPosition)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(_originalFootPosition, gizmoSphereSize);
        }
        
        if (showCurrentTipPos)
        {
            Gizmos.color = Color.aquamarine;
            Gizmos.DrawSphere(_currentTipPos, gizmoSphereSize);
        }
    }

#endif
}
