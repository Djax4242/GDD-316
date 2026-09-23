using System;
using UnityEngine;

public class LookAtPlayer : MonoBehaviour
{
    [SerializeField] private float rotationSpeed;
    private Transform _playerTransform;
    
    private void Awake()
    {
        _playerTransform = FindAnyObjectByType<PlayerMovement>().gameObject.transform;
    }

    private void Update()
    {
        Vector3 directionToPlayer = transform.position - _playerTransform.position;
        Quaternion targetRotation = Quaternion.LookRotation(-directionToPlayer);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
    }
}
