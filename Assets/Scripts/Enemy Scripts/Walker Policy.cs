using System;
using Unity.InferenceEngine;
using UnityEngine;

public class WalkerPolicy : MonoBehaviour
{
    /// <summary>
    ///
    ///     Loads a trained walker ONNX policy and runs it. Feed it the observation every policy step and
    ///     it hands back 12 actions. ActionsToJointTargets turns those actions into joint angle targets for
    ///     the PD drives. Sizes, standing angles and action scales come from the robot description's
    ///     policy block (WalkerRobotBuilder.Policy), so the same script runs the LargeWalker and the Regent.
    ///
    /// </summary>



    // Observation layout, in order. Everything is in the body frame (x forward, y left, z up, MuJoCo convention).
    public const int ActionSize = 12;
    public const int LinVelOffset = 0;         // 3  linear velocity (m/s)
    public const int AngVelOffset = 3;         // 3  angular velocity (rad/s)
    public const int GravityOffset = 6;        // 3  gravity unit vector, (0, 0, -1) when upright
    public const int JointPosOffset = 9;       // 12 joint angle minus standing angle, joint order
    public const int JointVelOffset = 21;      // 12 joint velocity, joint order
    public const int LastActionOffset = 33;    // 12 previous raw actions, joint order
    public const int CommandOffset = 45;       // 3  (vx, vy, yaw rate)
    public const int HeightScanOffset = 48;    // scan grid (x fastest), body height above hit / max distance

    // Joint positions, joint velocities, previous actions and the actions themselves all use this order.
    public static readonly string[] JointNames =
    {
        "FL_coxa_joint", "FL_femur_joint", "FL_tibia_joint", "FR_coxa_joint", "FR_femur_joint", "FR_tibia_joint",
        "RL_coxa_joint", "RL_femur_joint", "RL_tibia_joint", "RR_coxa_joint", "RR_femur_joint", "RR_tibia_joint"
    };

    // From the robot description (joint order): standing angles, action scales, the self-test output.
    private WalkerRobotBuilder.PolicyDescription _config;

    /// <summary> Length of the observation vector the policy expects. </summary>
    public int ObservationSize => Config.observationSize;
    /// <summary> The policy block of the robot description. </summary>
    public WalkerRobotBuilder.PolicyDescription Config => _config ??= GetComponent<WalkerRobotBuilder>().Policy;



    [Header("--- References ---")]
    [Tooltip("The trained policy, e.g. Assets/Models/RegentPolicy.onnx. It must match the robot description on the WalkerRobotBuilder")]
    [SerializeField] private ModelAsset policyModel;

    [Space]
    [Header("--- Inference Settings ---")]
    [Tooltip("CPU is fastest for a network this small, GPU would spend more time on the readback than the maths")]
    [SerializeField] private BackendType backend = BackendType.CPU;

    [Space(3)]
    [Header("=== DEBUG ===")]
    [Tooltip("Run the standing observation through the policy on Start and compare against the Python output")]
    [SerializeField] private bool runSelfTest = true;

    private Worker _worker;
    private Tensor<float> _input;
    private string _outputName;
    private readonly float[] _actions = new float[ActionSize];

    /// <summary> The actions from the last Act call, in joint order. Goes into the next observation. </summary>
    public float[] PreviousActions => _actions;


    private void Awake()
    {
        if (policyModel == null)
        {
            Debug.LogError($"{name}: WalkerPolicy has no policy model assigned", this);
            enabled = false;
            return;
        }

        Model model = ModelLoader.Load(policyModel);

        // The exporter names these "obs" and "actions". Anything else means the wrong file got dragged in.
        if (model.inputs.Count != 1 || model.inputs[0].name != "obs" || model.outputs.Count != 1)
        {
            Debug.LogError($"{name}: {policyModel.name} does not look like the walker policy (expected one input 'obs' and one output)", this);
            enabled = false;
            return;
        }
        _outputName = model.outputs[0].name;

        // The ONNX input width must match the robot description, or the wrong pair was dragged in.
        int width = model.inputs[0].shape.Get(1);
        if (width != ObservationSize)
        {
            Debug.LogError($"{name}: {policyModel.name} takes {width} observations but the robot description says {ObservationSize}", this);
            enabled = false;
            return;
        }

        _worker = new Worker(model, backend);
        _input = new Tensor<float>(new TensorShape(1, ObservationSize));
    }

    private void Start()
    {
        if (runSelfTest && _worker != null) SelfTest();
    }

    private void OnDestroy()
    {
        _worker?.Dispose();
        _input?.Dispose();
    }


    /// <summary>
    ///     Runs one policy step. Returns the 12 raw actions in joint order. The returned array is reused
    ///     every call, copy it if you need to keep it.
    /// </summary>
    public float[] Act(float[] observation)
    {
        if (observation.Length != ObservationSize)
            throw new ArgumentException($"Observation must have {ObservationSize} floats, got {observation.Length}");

        _input.Upload(observation);
        _worker.Schedule(_input);

        // DownloadToArray blocks until the result is ready, fine at 10-50 Hz on the CPU backend.
        var output = _worker.PeekOutput(_outputName) as Tensor<float>;
        float[] result = output.DownloadToArray();
        Array.Copy(result, _actions, ActionSize);
        return _actions;
    }

    /// <summary> Clears the previous actions, call this when the walker respawns. </summary>
    public void ResetPolicy()
    {
        Array.Clear(_actions, 0, ActionSize);
    }

    /// <summary>
    ///     Turns raw actions into joint angle targets (radians, MuJoCo joint convention), joint order.
    ///     target = standing angle + action * scale. The actions are not clipped, same as in training.
    /// </summary>
    public void ActionsToJointTargets(float[] actions, float[] targetsOut)
    {
        for (int j = 0; j < ActionSize; j++) targetsOut[j] = Config.standingAngles[j] + actions[j] * Config.actionScale[j];
    }

    /// <summary> Standing angle of joint j (joint order), radians. </summary>
    public float StandingJointAngle(int j) => Config.standingAngles[j];

    /// <summary> Observation of the walker standing still and upright on flat ground with a zero command. </summary>
    public float[] StandingObservation()
    {
        var obs = new float[ObservationSize];
        obs[GravityOffset + 2] = -1f;
        // Every scan ray sees flat ground one standing height below the body, over the scan range.
        for (int i = HeightScanOffset; i < ObservationSize; i++) obs[i] = Config.standHeight / Config.scanMaxDistance;
        return obs;
    }


    private void SelfTest()
    {
        float[] actions = Act(StandingObservation());

        float worstError = 0f;
        for (int a = 0; a < ActionSize; a++)
            worstError = Mathf.Max(worstError, Mathf.Abs(actions[a] - Config.selfTestExpected[a]));

        if (worstError < 1e-3f)
            Debug.Log($"{name}: walker policy loaded, self test passed (max error {worstError:E1})", this);
        else
            Debug.LogWarning($"{name}: walker policy self test FAILED, max error {worstError:F4}. Got [{string.Join(", ", actions)}]", this);

        // The self test should not leak into the first real observation
        ResetPolicy();
    }
}
