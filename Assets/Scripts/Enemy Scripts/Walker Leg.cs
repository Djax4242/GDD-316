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
    [Tooltip("How far ahead of itself the foot lands, in SECONDS of hip movement. This helps turning look good and when the walker is faster than its legs can handle")]
    [SerializeField] private float stepAheadScalar;
    [Tooltip("Caps the lead as a fraction of minDistanceToStep. Keep below 1 or the foot lands already far enough from its home point to qualify for another step, and immediately takes a second tiny one")]
    [SerializeField, Range(0f, 1f)] private float maxLeadFraction;
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
    // How fast this leg's home point is moving through the world. Because the raycast origin is parented
    // under the body, its motion already contains the walker's forward travel AND the sweep it picks up
    // from the body turning, so one subtraction covers both without having to separate them.
    private Vector3 _hipVelocity;
    private Vector3 _previousHipPosition;



    [Space(3)]
    [Header("=== DEBUG ===")]
    [Tooltip("Show where the raycast originates from")]
    [SerializeField] private bool showRaycast;
    [Tooltip("Show the point that the legs raycast hits")]
    [SerializeField] private bool showRaycastHitPosition;
    [Tooltip("Show the normal of the downwards raycast for this leg")]
    [SerializeField] private bool showRaycastNormal;
    [Tooltip("Shows the direction this leg's home point is travelling, which is what the step offset now follows")]
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

        _previousHipPosition = raycastOriginPosition.position;
    }

    private void Update()
    {
        // Constantly try to move the leg target to the stored position of the tip so the leg doesn't move when the body moves.
        legIkTarget.position = _currentTipPos;

        UpdateHipVelocity();
        UpdateStepUrgency();
    }

    // How far ahead of the home point to land the foot. Capped so the foot can never land far enough from
    // home to immediately qualify for another step, which would make the leg hop twice in a row.
    private Vector3 GetStepLead()
    {
        return Vector3.ClampMagnitude(_hipVelocity * stepAheadScalar, minDistanceToStep * maxLeadFraction);
    }

    // Track how fast this leg's home point is travelling by comparing it against where it was last frame.
    // Moving forwards and turning on the spot both show up here as the same thing, a world space velocity,
    // so nothing downstream has to care which of the two is happening.
    private void UpdateHipVelocity()
    {
        _hipVelocity = (raycastOriginPosition.position - _previousHipPosition) / Time.deltaTime;
        _previousHipPosition = raycastOriginPosition.position;
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

    /// <param name="ignoreUrgency">
    ///     Step even if this foot hasn't drifted far enough to want to. The diagonal pair gait uses this to
    ///     move both legs of a pair together, since the two almost never cross their own thresholds on the
    ///     same frame and the pair would otherwise break apart into single steps.
    /// </param>
    public bool CheckForStep(bool ignoreUrgency = false)
    {
        // No need to check for a step opportunity if the foot is already moving. This has to be _isStepping, not
        // isFootGrounded, or a leg past nextStepThreshold would start a second TakeStep on top of the one still
        // running. Both coroutines would then fight over _currentTipPos and the foot would snap.
        if (_isStepping) return false;

        // Nowhere valid to put the foot, so stay planted even when forced
        if (!_hasStepTarget) return false;

        // The foot is still close enough to where it wants to be, so leave it planted
        if (!ignoreUrgency && StepUrgency < 0f) return false;

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

            // Land the foot ahead of the home point by however far the hip will travel in the next
            // stepAheadScalar seconds. The old version took its direction from how far the foot had
            // already drifted, which only points the right way when the walker is moving in a straight
            // line. During a turn that drift vector points back across the body and threw the target
            // out past the walker.

            // Only the flat part of the lead is used. The vertical part is the body climbing a slope or
            // bobbing, and adding it to a ground position would lift the foot off the ground rather than
            // move it along the ground. The height comes from the raycast below instead.
            Vector3 lead = GetStepLead();
            lead.y = 0f;

            // Fire the ray at the spot the foot is actually heading for, not at the home point. On a slope
            // the ground under that spot sits at a different height, and reusing the height from under the
            // home point would bury the foot going uphill and float it going downhill.
            // Start above the home point too, because when climbing, the ground ahead is often higher than
            // the hip is right now, and a ray starting at hip height would begin underneath it and miss.
            Vector3 leadRayOrigin = raycastOriginPosition.position + lead + Vector3.up * stepHeight;

            if (Physics.Raycast(leadRayOrigin, Vector3.down, out RaycastHit leadHit, maxStepRaycastDistance + stepHeight, ~invalidStepLayers))
            {
                footTarget = leadHit.point;
            }
            else if (Physics.Raycast(raycastOriginPosition.position, Vector3.down, out RaycastHit homeHit, maxStepRaycastDistance, ~invalidStepLayers))
            {
                // Nothing under the led to spot, so it is over a ledge or a gap. Give up the lead for this
                // frame and put the foot down under the home point instead of out over the drop.
                footTarget = homeHit.point;
            }
            
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
        // Nothing to draw from until the references are hooked up, and without this the whole method throws
        // every repaint while the prefab is still being set up
        if (raycastOriginPosition == null) return;

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

            // Which way the home point is sweeping. Straight ahead when the walker is driving forwards, and
            // tangential when it is turning on the spot, without this having to know which is happening.
            if (showDirectionToStep && _hipVelocity.sqrMagnitude > 0.0001f)
            {
                Gizmos.color = Color.deepPink;
                Gizmos.DrawRay(hitInfo.point, _hipVelocity.normalized * gizmoLength);
            }

            // Where the foot would actually land right now. The gap between this and the blue hit point is the
            // whole lead, so this is the one to watch while tuning stepAheadScalar. Uses the same flattened
            // lead and the same re grounding ray as the real step, so on a slope it sits on the ground ahead
            // rather than floating out at the home point's height.
            if (showOffsetFootTarget)
            {
                Vector3 lead = GetStepLead();
                lead.y = 0f;
                Vector3 leadRayOrigin = raycastOriginPosition.position + lead + Vector3.up * stepHeight;

                if (Physics.Raycast(leadRayOrigin, Vector3.down, out RaycastHit leadHit, maxStepRaycastDistance + stepHeight, ~invalidStepLayers))
                {
                    Gizmos.color = Color.purple;
                    Gizmos.DrawSphere(leadHit.point, gizmoSphereSize);
                    Gizmos.DrawLine(hitInfo.point, leadHit.point);
                }
                else
                {
                    // Over a ledge, so the real step would fall back to the home point. Drawn in a different
                    // colour so it is obvious in the scene view when that is happening.
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawSphere(hitInfo.point, gizmoSphereSize);
                }
            }
        }

        // These two track runtime state, so out of play mode they would both just sit at the world origin
        if (!Application.isPlaying) return;

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
