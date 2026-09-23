using System;
using UnityEngine;

public class HierarchAI : MonoBehaviour
{
    /// <summary>
    ///
    ///     AI for the Hierarch
    /// 
    /// </summary>
    
    
    [Header("--- References ---")]
    [SerializeField] private Rigidbody hierarchRb;
    [SerializeField] private Transform hierarchFollowTarget;
    [SerializeField] private Transform rotatingTarget;
    private Transform _playerTransform;
    
    [Space]
    [Header("--- Hierarch Settings ---")]
    [SerializeField] private float moveSpeed;
    [SerializeField] private float turnSpeed;
    [SerializeField] private Vector3 offsetFollow;
    [SerializeField] private float targetRotationSpeed;
    [SerializeField] private float targetFrequency;
    [SerializeField] private float targetAmplitude;
    [SerializeField] private float targetYOffset;
    
    
    
    
    [Header("=== DEBUG ===")] 
    [SerializeField] private float gizmoSphereSize;

    
    
    private void Awake()
    {
        _playerTransform = FindAnyObjectByType<PlayerMovement>().transform;
    }

    private void FixedUpdate()
    {
        hierarchRb.linearVelocity = hierarchRb.transform.forward * (moveSpeed * Time.deltaTime);
        
        rotatingTarget.transform.Rotate(0, targetRotationSpeed * Time.deltaTime, 0);
        rotatingTarget.transform.position = _playerTransform.position;

        hierarchFollowTarget.transform.position = new Vector3(hierarchFollowTarget.transform.position.x, 
            (Mathf.Sin(Time.time * targetFrequency * Time.deltaTime) * targetAmplitude * Time.deltaTime) + targetYOffset, 
            hierarchFollowTarget.transform.position.z);
        
        Vector3 directionToPlayer = hierarchFollowTarget.position - hierarchRb.position;
        Quaternion rotationToFacePlayer = Quaternion.LookRotation(directionToPlayer);
        Quaternion smoothedRotation = Quaternion.RotateTowards(hierarchRb.rotation, rotationToFacePlayer, turnSpeed * Time.deltaTime);
        
        hierarchRb.MoveRotation(smoothedRotation);
    }

#if UNITY_EDITOR

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.orangeRed;
        Gizmos.DrawSphere(hierarchFollowTarget.position, gizmoSphereSize);
    }

#endif
}
