using System;
using UnityEngine;
using Random = UnityEngine.Random;

public class RiseAndFall : MonoBehaviour
{
    /// <summary>
    ///
    ///     Makes objects rise and fall
    /// 
    /// </summary>
    
    
    [Header("--- Settings ---")] 
    [SerializeField] private float minFrequency;
    [SerializeField] private float maxFrequency;
    [SerializeField] private float amplitude;
    [SerializeField] private float minInitialOffset;
    [SerializeField] private float maxInitialOffset;
    [SerializeField] private bool invert;
    private float _initialYOffset;
    private float _randomFrequency;

    
    
    private void Awake()
    {
        _initialYOffset = transform.position.y + Random.Range(minInitialOffset, maxInitialOffset);
        _randomFrequency = Random.Range(minFrequency, maxFrequency);
    }

    private void Update()
    {
        float t = Time.time;
        if (invert) t = -t;
        Vector3 newY = new Vector3(transform.position.x, (Mathf.Sin(t * _randomFrequency) * amplitude) + _initialYOffset, transform.position.z);
        transform.position = newY;
    }
}
