using System;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    /// <summary>
    ///
    ///     Handles the players movement
    /// 
    /// </summary>
    
    
    [Header("--- References ---")]
    [SerializeField] private Rigidbody playerRb;
    [SerializeField] private Transform cinemachineTransform;
    [SerializeField] private Transform playerVisual;
    [SerializeField] private PlayerInput playerInput;
    
    [Space]
    [Header("--- Movement Settings ---")]
    [Tooltip("Speed when walking")]
    [SerializeField] private float walkSpeed;
    [Tooltip("Max speed when walking")]
    [SerializeField] private float maxWalkSpeed;
    [Tooltip("How fast the players visual rotates to face their movement direction")]
    [SerializeField] private float visualRotationSpeed;
    
    
    
    private void FixedUpdate()
    {
        Movement();
    }
    
    private void Update()
    {
        RotateVisual();
    }

    private void RotateVisual()
    {
        Vector2 moveInput = playerInput.GetMoveInput();
        
        Vector3 camRelativeInput = (cinemachineTransform.right * moveInput.x) + (cinemachineTransform.forward * moveInput.y);
        camRelativeInput.y = 0;
        
        // if (camRelativeInput.sqrMagnitude > Mathf.Abs(0.01f))
        Quaternion lookRotation = Quaternion.LookRotation(camRelativeInput);
        Quaternion targetRotation = Quaternion.RotateTowards(playerVisual.transform.rotation, lookRotation, visualRotationSpeed);

        playerVisual.transform.rotation = targetRotation;
        
        // hi
    }
    
    private void Movement()
    {
        Vector2 moveInput = playerInput.GetMoveInput();
        Vector3 camRelativeInput = (cinemachineTransform.right * moveInput.x) + (cinemachineTransform.forward * moveInput.y);

        if (playerRb.linearVelocity.magnitude > maxWalkSpeed)
        {
            camRelativeInput = Vector3.zero;
        }
        
        playerRb.AddForce(camRelativeInput * walkSpeed);
    }
}
