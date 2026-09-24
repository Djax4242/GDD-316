using UnityEngine;

public class PlayerPathMover : MonoBehaviour
{
    /// <summary>
    ///
    ///     Test helper: moves the player along a loop of waypoints at a fixed speed (its rigidbody made
    ///     kinematic), so a chasing enemy can be checked or filmed without anyone at the keyboard.
    ///
    /// </summary>



    [SerializeField] private Rigidbody player;
    [Tooltip("World positions, visited in order and looped")]
    [SerializeField] private Vector3[] waypoints =
    {
        new(0f, 1f, 150f), new(150f, 1f, 150f), new(150f, 1f, -50f), new(-100f, 1f, -50f),
    };
    [SerializeField] private float speed = 5f;
    [Tooltip("Seconds to wait before the player starts moving")]
    [SerializeField] private float startDelay = 3f;
    [Tooltip("Put a tall red pole on the player so it can be seen next to a 20 m walker")]
    [SerializeField] private bool showBeacon = true;

    private int _next;
    private float _elapsed;


    private void Start()
    {
        var movement = FindAnyObjectByType<PlayerMovement>();
        if (player == null && movement != null) player = movement.GetComponent<Rigidbody>();
        if (player == null) return;
        // The route drives the player; its own movement script would fight a kinematic body every step.
        if (movement != null) movement.enabled = false;
        player.isKinematic = true;

        if (showBeacon)
        {
            var beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beacon.name = "Test beacon";
            Destroy(beacon.GetComponent<Collider>());
            beacon.transform.SetParent(player.transform, false);
            beacon.transform.localPosition = new Vector3(0f, 8f, 0f);
            beacon.transform.localScale = new Vector3(1.5f, 8f, 1.5f);
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(1f, 0.1f, 0.1f) };
            beacon.GetComponent<Renderer>().sharedMaterial = material;
        }
    }

    private void FixedUpdate()
    {
        if (player == null || waypoints.Length == 0) return;
        _elapsed += Time.fixedDeltaTime;
        if (_elapsed < startDelay) return;

        Vector3 goal = waypoints[_next];
        Vector3 step = Vector3.MoveTowards(player.position, goal, speed * Time.fixedDeltaTime);
        player.MovePosition(step);
        if ((step - goal).sqrMagnitude < 0.01f) _next = (_next + 1) % waypoints.Length;
    }
}
