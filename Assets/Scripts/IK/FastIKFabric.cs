using UnityEngine;

public class FastIKFabric : MonoBehaviour
{
    /// <summary>
    ///
    ///     Solves IK using the FABRIK algorithm
    /// 
    /// </summary>
    
    
    [Header("--- References ---")]
    [SerializeField] private Transform target;
    
    [Space]
    [Header("--- FABRIK Settings ---")]
    [Tooltip("How many bones above this one are part of the chain. So 2 chainLength would be this bone + its two parents")]
    [SerializeField] private int chainLength = 2;
    [Tooltip("How many times FABRIK will run forwards and backwards to solve for IK")]
    [SerializeField] private int iterations = 10;
    [Tooltip("If the distance between the target and the tip is less than this, abandon solving")]
    [SerializeField] private float delta = 0.001f;
    [Tooltip("If you want a snake like creature, ignore the backwards step of FARBIK")]
    [SerializeField] private bool ignoreForwardsStep;
    
    [Space]
    [Header("--- Chain Information ---")]
    [Tooltip("joints[0] is the root bone (not the tip) that never moves. Its the top of the chain. This also means that joints[chainLength] is this object. " +
             "This object is always the tip of the chain or the bottom of the chain in the hierarchy")]
    [SerializeField] private Transform[] joints;
    [Tooltip("Scratch space for the solve, in world coordinates. FABRIK needs to slide joints " +
             "around as free-floating points, which it can't do on real bones because moving a " +
             "parent drags its children along. Once solved, we only read the directions between " +
             "these points to build bone rotations — the positions themselves are never applied.")]
    [SerializeField] private Vector3[] positions;
    [Tooltip("This is just an array of the lengths from each bone to the next. lengths[i] is the distance between joints[i] and joints[i + 1]. " +
             "The array is one shorter than joints because the final joint (the leaf being teleported to the target) has no bone leaving it. 3 points makes 2 bones")]
    [SerializeField] private float[] lengths;
    [Tooltip("The direction to the child, stored in the bone's own space")]
    [SerializeField] private Vector3[] localChildDir;
    [Tooltip("The total length of the chain")]
    [SerializeField] private float totalLength;
    
    
    
    void Awake()
    {
        // We use chainLength + 1 because of the joint bone disconnect. Remember, there will always be 1 more joint than there are bones since the last
        // joint counts but doesnt have a parent. And we do that because these two arrays give us information about each joint, not bone. 
        joints = new Transform[chainLength + 1];
        
        // Scratch array
        positions = new Vector3[chainLength + 1];
        
        // We only need chain length here because these two arrays give us information about the bones themselves. 
        lengths = new float[chainLength];
        localChildDir = new Vector3[chainLength];

        // Walk up the hierarchy, filling the array backwards so joints[0] ends up at the top.
        // use i >= 0 so we get all the joints in chain length (because we include zero which is basically our chainLength + 1)
        Transform current = transform;
        for (int i = chainLength; i >= 0; i--)
        {
            // If the loop tries to access an object past the chain, exit and throw an error. 
            if (current == null)
            {
                Debug.LogError("Chain is longer than the parent hierarchy.", this);
                enabled = false;
                return;
            }
            
            // Add the current joint to the array, then add its parent, and so on until we've built joints[].
            joints[i] = current;
            current = current.parent;
        }

        // Record the rest pose: how long each bone is, and where each bone points.
        
        // Again, we dont need chainLength + 1 OR to use i >= 0 because these arrays we are building give us information about the BONES, not the joints
        for (int i = 0; i < chainLength; i++)
        {
            // Get the direction from the current joint to the current joints parent.
            // Again, we use chainLength because we are dealing with amount of bones.
            Vector3 toChild = joints[i + 1].position - joints[i].position;
            
            // Build the length from each joint to its parent. 
            lengths[i] = toChild.magnitude;
            
            // Add the distance between each joint and its parent to the total length of the chain.
            totalLength += lengths[i];
            
            // Build the local direction to each joints child. 
            localChildDir[i] = Quaternion.Inverse(joints[i].rotation) * toChild;
        }
    }

    void LateUpdate()
    {
        if (target == null) return;

        // Copy the current world positions into our scratch array so we dont use each joints local position in the hierarchy
        for (int i = 0; i < joints.Length; i++)
        {
            positions[i] = joints[i].position;
        }

        // Cache positions
        Vector3 rootPos = positions[0];
        Vector3 targetPos = target.position;
        int tipIndex = chainLength;

        // Check to see if the target and root are further than the total length of the chain. 
        if ((targetPos - rootPos).sqrMagnitude >= totalLength * totalLength && !ignoreForwardsStep)
        {
            Vector3 dirToTarget = (targetPos - rootPos).normalized;

            // Start the loop at one because joint[0] is the root joint that wont move. We dont need to include it in this loop.
            // So for a chain length of 2, this loop will run twice. 
            // So we essentially visit each joint from one after the root
            for (int i = 1; i <= tipIndex; i++)
            {
                // Get the previous joints position and length
                Vector3 previousJoint = positions[i - 1];
                float boneLength = lengths[i - 1];
                
                // Turns a pure normalized direction into a world position offset. 
                // dirToTarget has a length of 1 and points towards the target, so multiplying by bone length 
                // Gives us an actual offset. 
                Vector3 offset = dirToTarget * boneLength;

                // Finally, construct our positions array to then be rotated below using our offset. 
                positions[i] = previousJoint + offset;
            }
        }
        else
        {
            // IF the target is within the total length of the chain, then we need to solve using FABRIK. 
            
            for (int it = 0; it < iterations; it++)
            {
                // BACKWARDS 
                
                // Teleport tip to target
                positions[tipIndex] = targetPos;
                
                // Walk down the chain starting from the joint just below the tip all the way to the root. 
                // Start at tipIndex - 1 because the tip was already placed at the target. 
                // End at zero because we still want to move the root. The forward will put it back once it drifts off
                
                // So with a chain length of 2, this loop will run twice. 
                for (int i = tipIndex - 1; i >= 0; i--)
                {
                    // Get the position of the child joint.
                    Vector3 childJoint = positions[i + 1];
                    float boneLength = lengths[i];

                    // Calculate the direction from the child joint to this joint
                    Vector3 dirToThisJoint = (positions[i] - childJoint).normalized;
                    
                    // Tehn using that normalized direction, calculate the offset needed
                    Vector3 offset = dirToThisJoint * boneLength;

                    // The simply build the array using our offset position relative to the joints child. 
                    positions[i] = childJoint + offset;
                }

                // FORWARDS

                if (!ignoreForwardsStep)
                {
                    // Teleport the root joint to the root position
                    positions[0] = rootPos;
                    
                    // Walk back up the chain to recalculate positions starting at the joint just after the root. 
                    // Start at 1 because the root was already teleported above. 
                    for (int i = 1; i <= tipIndex; i++)
                    {
                        // The joint one step closer to the root. 
                        Vector3 parentJoint = positions[i - 1];
                        float boneLength = lengths[i - 1];

                        // Calculate our offset using the direction to the parent joint from the current position.
                        Vector3 dirToThisJoint = (positions[i] - parentJoint).normalized;
                        // Then calculate the offset needed. 
                        Vector3 offset = dirToThisJoint * boneLength;

                        positions[i] = parentJoint + offset;
                    }

                }

                // Finally, this checks to see if the tip is already close enough to the target. If it is, we can end the loop early since we've already
                // achieved the desired position
                if ((positions[tipIndex] - targetPos).sqrMagnitude < delta * delta) break;
            }
        }

        // Snake mode: the root was allowed to drift during the backwards pass, so actually move it.
        // Without this, positions[0] is thrown away, the root stays pinned, and we only get normal IK.
        if (ignoreForwardsStep)
        {
            joints[0].position = positions[0];
        }

        
// Turn the solved points back into bone rotations.
        for (int i = 0; i < chainLength; i++)
        {
            // Get the current direction to the next joint using this joint and the direction to its child. 
            Vector3 currentDir = joints[i].rotation * localChildDir[i];
            // Get the desired direction of the joint by getting the direction from our current joint to its child. 
            Vector3 desiredDir = positions[i + 1] - positions[i];
            
            // Finally, set the rotation of our joints
            joints[i].rotation = Quaternion.FromToRotation(currentDir, desiredDir) * joints[i].rotation;
        }
    }
}