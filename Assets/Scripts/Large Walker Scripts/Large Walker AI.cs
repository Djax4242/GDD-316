using System;
using UnityEngine;
using UnityEngine.AI;

public class LargeWalkerAI : MonoBehaviour
{
    [SerializeField] private NavMeshAgent walkerNavmeshAgent;
    [SerializeField] private Transform target;
    

    private void Update()
    {
        walkerNavmeshAgent.SetDestination(target.position);
    }
}
