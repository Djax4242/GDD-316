using System;
using UnityEngine;

public class WalkerDemoDriver : MonoBehaviour
{
    /// <summary>
    ///
    ///     Test helper: plays a list of commands (trained-size units) on a WalkerController, each for a set
    ///     time, so a policy can be checked or filmed without a player to follow.
    ///
    /// </summary>



    [Serializable] public struct Step
    {
        public string label;
        [Tooltip("x = forward m/s, y = left m/s, z = turn left rad/s")]
        public Vector3 command;
        public float seconds;
    }

    [SerializeField] private WalkerController walker;
    [SerializeField] private Step[] steps =
    {
        new() { label = "stand", command = Vector3.zero, seconds = 3f },
        new() { label = "walk 3 m/s", command = new Vector3(3f, 0f, 0f), seconds = 6f },
        new() { label = "run 8 m/s", command = new Vector3(8f, 0f, 0f), seconds = 8f },
        new() { label = "turn left", command = new Vector3(4f, 0f, 0.4f), seconds = 8f },
        new() { label = "sideways", command = new Vector3(0f, 2.5f, 0f), seconds = 5f },
        new() { label = "stop", command = Vector3.zero, seconds = 4f },
    };

    private float _start = -1f;

    /// <summary> Label of the step being played, for overlays. </summary>
    public string CurrentLabel { get; private set; } = "";
    public Vector3 CurrentCommand { get; private set; }
    public bool Finished { get; private set; }


    private void FixedUpdate()
    {
        if (walker == null) return;
        if (_start < 0f) _start = Time.time;

        float t = Time.time - _start;
        foreach (Step step in steps)
        {
            if (t < step.seconds)
            {
                CurrentLabel = step.label;
                CurrentCommand = step.command;
                walker.SetManualCommand(step.command);
                return;
            }
            t -= step.seconds;
        }
        Finished = true;
        walker.SetManualCommand(Vector3.zero);
    }
}
