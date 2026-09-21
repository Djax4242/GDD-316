using System;
using UnityEngine;
using UnityEngine.AI;

public class LargeWalkerAI : MonoBehaviour
{
    [SerializeField] private NavMeshAgent walkerNavmeshAgent;
    [SerializeField] private GameObject player;

    private void Awake()
    {
        player = FindAnyObjectByType<PlayerMovement>().gameObject;
    }

    private void Update()
    {
        walkerNavmeshAgent.SetDestination(player.transform.position);
    }
}
