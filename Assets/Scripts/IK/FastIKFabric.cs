using System;
using UnityEngine;

public class FastIKFabric : MonoBehaviour
{
    /// <summary>
    ///
    ///     Solves IK using FABRIK
    /// 
    /// </summary>
    
    
    [Header("--- References ---")]
    [Tooltip("The target of the IK chain")]
    [SerializeField] private Transform target;
    // [Tooltip("The pole target of the IK chain so the chain knows which way to bend")]
    // [SerializeField] private Transform poleTarget;
    
    [Header("--- FABRIK Settings ---")]
    [Tooltip("Chain length of bones starting from the last bone in the chain")]
    [SerializeField] private int chainLength;
    // [Tooltip("How many times the IK solver will go back and forth through the chain. Higher means more precise but slower to run")]
    // [SerializeField] private int iterations;
    // [Tooltip("Distance for when the solver stops")]
    // [SerializeField] private float distanceToStop;
    // [Tooltip("Strength of going back to the starting position")]
    // [SerializeField] private float snapBackStrength;

    [Tooltip("The length of each bone")]
    [SerializeField] private float[] boneLengths;
    [Tooltip("The total length of the chain")]
    [SerializeField] private float completeChainLength;
    [Tooltip("Than transform component of each bone")]
    [SerializeField] private Transform[] bonesInChain;
    [SerializeField] private Vector3[] bonePositions;

    
    
    private void Awake()
    {
        Init();
    }

    private void Init()
    {
        // Create our arrays with length of the chain + 1
        // We do this because the chain length is 1 less than the amount of bones in the chain because the last bone is a leaf bone and has no deformation.
        
        bonesInChain = new Transform[chainLength + 1];
        bonePositions = new Vector3[chainLength + 1];
        // Not sure why, but the tutorial says to + 1 this even though there are a chainLength number of boneLengths
        boneLengths = new float[chainLength + 1];

        completeChainLength = 0;
        
        // We put the script on the first bone in the chain, so its the first bones transform.
        Transform currentBone = this.transform;
        
        // Starting at the root bone (the one that doesnt move), which is our current currentBone, we loop through the chain using the length of the chain + 1;
        for (int i = 0; i < bonesInChain.Length; i++)
        {
            // Add the current transform in the loop to the bones array
            // Use I so we build the array with the full chain. 
            
            // So the first bone in this array will be the root bone (the one that doesnt move)
            bonesInChain[i] = currentBone;

            // If its not the root bone
            if (i > 0)
            {
                boneLengths[i - 1] = (bonesInChain[i].position - bonesInChain[i - 1].position).magnitude;
                completeChainLength += boneLengths[i - 1];

                print(boneLengths[i - 1]);
            }
            
            currentBone = currentBone.parent;
        }
    }
}
