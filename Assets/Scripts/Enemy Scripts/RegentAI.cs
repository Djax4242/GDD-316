using UnityEngine;
using UnityEngine.AI;

public class RegentAI : MonoBehaviour
{
    [SerializeField] private NavMeshAgent walkerNavmeshAgent;
    private GameObject _player;

    private void Awake()
    {
        _player = FindAnyObjectByType<PlayerMovement>().gameObject;
    }

    private void Update()
    {
        walkerNavmeshAgent.SetDestination(_player.transform.position);
    }
}
