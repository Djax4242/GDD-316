using System;
using System.Collections;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    /// <summary>
    ///
    ///     First person player controller
    /// 
    /// </summary>
    
    
    [Header("--- References ---")]
    [Tooltip("Used top get cam relative input")]
    [SerializeField] private Transform mainCameraTransform;
    [Tooltip("Rb on the player")]
    [SerializeField] private Rigidbody playerRb;
    [Tooltip("Gets input")]
    [SerializeField] private PlayerInput playerInput;
    
    [Space]
    [Header("--- Movement Settings ---")]
    [Tooltip("Base grounded move speed of the player")]
    [SerializeField] private float baseMoveSpeed;
    [Tooltip("Base speed gets multiplied by this when the player is trying to change direction")]
    [SerializeField] private float counterAccelerationMultiplier;
    [Tooltip("When the player stops moving, apply this amount of counter movement to slow them down")]
    [SerializeField] private float groundedFriction;
    [Tooltip("Friction applied when the player isn't grounded")]
    [SerializeField] private float airBorneFriction;
    [Tooltip("Top speed using linearveloctity magnitutde")]
    [SerializeField] private float topSpeed;
    private float moveSpeed;
    
    [Space]
    [Header("--- Jump Settings ---")]
    [Tooltip("How high the player jumps")]
    [SerializeField] private float jumpForce;
    [Tooltip("Max jumps the player has")]
    [SerializeField] private int maxJumps;
    [Tooltip("Extra gravity gets applied at the end")]
    [SerializeField] private float gravity;
    [Tooltip("Cooldown between jumps")]
    [SerializeField] private float jumpCooldown;
    [Tooltip("Grounded check wont use these layers")] 
    [SerializeField] private LayerMask invalidGroundedLayer;
    [SerializeField] private Vector3 spherecastOffset;
    [SerializeField] private float spherecastRadius;
    [SerializeField] private float spherecastDistance;
    private bool isGrounded = true;
    private int currentJumps;
    private bool canJump = true;

    // The forces below were tuned at Unity's default 50 Hz physics. Scaling by this constant instead of
    // Time.deltaTime keeps them the same when the physics rate changes (the ML walker runs physics at 1 kHz).
    private const float TunedPhysicsStep = 0.02f;
    
    
    
    private void Awake()
    {
        playerRb = GetComponent<Rigidbody>();
        playerInput = GetComponent<PlayerInput>();
    }

    private void FixedUpdate()
    {
        Movement();
        Friction();
        
        CheckForJump();
        CheckForGrounded();
    }

    private void Movement()
    {
        // Get input and set input to zero when above top speed
        Vector2 moveInput = playerInput.GetMoveInput();
        
        // Rotate the player rigidbody to face the cameras forward direction.
        // We flatten the Y to make sure the rigidbody doesn't tilt up and down
        Vector3 desiredForward = mainCameraTransform.forward;
        desiredForward.y = 0;
        Quaternion lookRotation = Quaternion.LookRotation(desiredForward);
        playerRb.MoveRotation(lookRotation);
        
        // After the rb is rotated, we use its foward and right to calulate an rb relative input
        Vector3 rbRelativeInput = (moveInput.x * playerRb.transform.right) + (moveInput.y * playerRb.transform.forward);
        
        // Move the player using the rb relative input
        playerRb.AddForce(rbRelativeInput * (baseMoveSpeed * TunedPhysicsStep));

        // Clamp the players movement so they dont exceed the max speed
        Vector2 movementPlane = new Vector2(playerRb.linearVelocity.x, playerRb.linearVelocity.z);
        movementPlane = Vector2.ClampMagnitude(movementPlane, topSpeed);
        playerRb.linearVelocity = new Vector3(movementPlane.x, playerRb.linearVelocity.y, movementPlane.y);

        // Extra gravity
        playerRb.AddForce(Vector3.down * gravity, ForceMode.Acceleration);
    }

    private void Friction()
    {
        // Friction that gets applied when the player stops moving to stop them from sliding around

        if (!isGrounded)
        {
            Vector3 flattenedVelocity = new Vector3(playerRb.linearVelocity.x, 0, playerRb.linearVelocity.z);
            playerRb.AddForce(-flattenedVelocity * (airBorneFriction * TunedPhysicsStep));
        }
        else
        {
            Vector2 moveInput = playerInput.GetMoveInput();
            if (moveInput.sqrMagnitude < 0.1f)
            {
                Vector3 flattenedVelocity = new Vector3(playerRb.linearVelocity.x, 0, playerRb.linearVelocity.z);
                playerRb.AddForce(-flattenedVelocity * (groundedFriction * TunedPhysicsStep));
            }
        }
    }

    private void CheckForJump()
    {
        if (!playerInput.GetJumpInput()) return;
        if (!canJump) return;
        
        currentJumps--;
        currentJumps = Mathf.Clamp(currentJumps, 0, maxJumps);
            
        if (currentJumps != 0)
        {
            playerRb.linearVelocity = new Vector3(playerRb.linearVelocity.x, 0f, playerRb.linearVelocity.z);

            // One-off push, as an impulse so its strength does not depend on the physics step
            playerRb.AddForce(Vector3.up * (jumpForce * TunedPhysicsStep), ForceMode.Impulse);
            canJump = false;

            StartCoroutine(JumpCooldown());
        }
    }

    private void CheckForGrounded()
    {
        if (Physics.SphereCast(playerRb.transform.position + spherecastOffset, spherecastRadius, Vector3.down, out RaycastHit hit, spherecastDistance, ~invalidGroundedLayer))
        {
            isGrounded = true;
            currentJumps = maxJumps; 
        }
        else
        {
            isGrounded = false;
        }
    }

    private IEnumerator JumpCooldown()
    {
        yield return new WaitForSeconds(jumpCooldown);
        canJump = true;
    }

    
    
#if UNITY_EDITOR
    
    [Space]
    [Header("=== DEBUG SETTINGS ===")]
    [SerializeField] private bool showGizmos;
    
    private void OnDrawGizmos()
    {
        if (!showGizmos) return;
        if (!Application.isPlaying) return;
        
        Vector2 moveInput = playerInput.GetMoveInput();
        Vector3 rbRelativeInput = (moveInput.x * playerRb.transform.right) + (moveInput.y * playerRb.transform.forward);
        
        Gizmos.color = Color.red;
        Vector3 spherePosition = playerRb.transform.position + spherecastOffset + new Vector3(0, -spherecastDistance, 0);
        Gizmos.DrawSphere(spherePosition, spherecastRadius);
        
        Gizmos.color = Color.blue;
        Gizmos.DrawRay(playerRb.position, playerRb.linearVelocity);

        Gizmos.color = Color.orangeRed;
        Gizmos.DrawRay(playerRb.position, rbRelativeInput);
    }
#endif
}
