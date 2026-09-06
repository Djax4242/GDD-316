using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerAnimationController : MonoBehaviour
{
    /// <summary>
    ///
    ///     Handles the animations of the player
    /// 
    /// </summary>
    
    
    
    private static readonly int Speed = Animator.StringToHash("MoveAnimSpeed");
    private static readonly int Injury = Animator.StringToHash("Injury");
    
    [Header("--- References ---")] 
    [SerializeField] private PlayerInput playerInput;
    
    [Space]
    [Header("--- Animation Settings ---")]
    [Tooltip("How fast the players movement animates")]
    [SerializeField] private float animChangeSpeed;
    [Tooltip("Target value of when the player is injured")]
    [SerializeField] private float targetInjuryValue;
    [Tooltip("How long the injury animation lasts")]
    [SerializeField] private float injuryAnimationDuration;
    // Pete's animator
    private Animator _animator;
    // Target of the animation move speed (not the speed of the player)
    private float _targetAnimSpeed;


    private void Awake()
    {
        _animator = GetComponent<Animator>();
    }

    private void Update()
    {
        AnimatePlayer();
    }

    private void AnimatePlayer()
    {
        // Get input
        Vector2 inputVector = playerInput.GetMoveInput();

        // If the player is holding input, increase the target move anim speed. If not, decrease it.
        if (inputVector.magnitude != 0f) _targetAnimSpeed += animChangeSpeed * Time.deltaTime;
        else _targetAnimSpeed -= animChangeSpeed * Time.deltaTime;
        
        // If the player is sprinting, increase the target anim value past its walk speed cap so the sprint animation plays.
        if (playerInput.GetSprintInput()) _targetAnimSpeed = Mathf.Clamp(_targetAnimSpeed, 0, 1f);
        else _targetAnimSpeed = Mathf.Clamp(_targetAnimSpeed, 0, 0.5f);
        
        // Lerp the animations
        _animator.SetFloat(Speed, Mathf.Lerp(_animator.GetFloat(Speed), _targetAnimSpeed, Time.deltaTime));

        //todo DEBUG
        if (Keyboard.current.iKey.wasPressedThisFrame) _animator.SetFloat(Injury, targetInjuryValue);

        // If the player has an injury, lerp their injury value down
        if (_animator.GetFloat(Injury) > 0)
        {
            float newInjuryValue = Mathf.MoveTowards(_animator.GetFloat(Injury), 0, injuryAnimationDuration * Time.deltaTime);
            _animator.SetFloat(Injury, newInjuryValue);
        }
    }
}
