using System;
using Unity.InferenceEngine;
using UnityEngine;

public class WalkerPolicy : MonoBehaviour
{
    /// <summary>
    ///
    ///     Loads the trained LargeWalker ONNX policy and runs it. Feed it the 235 float observation
    ///     every policy step (50 Hz) and it hands back 12 actions. ActionsToJointTargets turns those
    ///     actions into joint angle targets for the PD drives.
    ///
    /// </summary>



    // Observation layout, in order. Everything is in the body frame (x forward, y left, z up, MuJoCo convention).
    public const int ObservationSize = 235;
    public const int ActionSize = 12;
    public const int LinVelOffset = 0;         // 3  linear velocity (m/s)
    public const int AngVelOffset = 3;         // 3  angular velocity (rad/s)
    public const int GravityOffset = 6;        // 3  gravity unit vector, (0, 0, -1) when upright
    public const int JointPosOffset = 9;       // 12 joint angle minus standing angle, joint order
    public const int JointVelOffset = 21;      // 12 joint velocity, joint order
    public const int LastActionOffset = 33;    // 12 previous raw actions, joint order
    public const int CommandOffset = 45;       // 3  (vx, vy, yaw rate)
    public const int HeightScanOffset = 48;    // 187 (17 x 11 grid, x fastest), body height above hit * 0.2

    // Joint positions, joint velocities, previous actions and the actions themselves all use this order.
    public static readonly string[] JointNames =
    {
        "FL_coxa_joint", "FL_femur_joint", "FL_tibia_joint", "FR_coxa_joint", "FR_femur_joint", "FR_tibia_joint",
        "RL_coxa_joint", "RL_femur_joint", "RL_tibia_joint", "RR_coxa_joint", "RR_femur_joint", "RR_tibia_joint"
    };

    // Per joint type (coxa, femur, tibia). Joint j has type j % 3.
    private static readonly float[] StandingAngle = { 0f, 0.1745f, -1.4835f };
    private static readonly float[] ActionScale = { 0.1140f, 0.1163f, 0.1256f };

    // ONNX output for the standing observation, computed in Python from the same file.
    private static readonly float[] SelfTestExpected =
    {
        -0.09510f, -0.38788f, -0.12779f, -0.06498f, -0.62404f, -0.03433f,
        0.12240f, -0.48866f, 0.08653f, 0.03362f, -0.40376f, -0.13268f
    };



    [Header("--- References ---")]
    [Tooltip("The trained policy. Drag Assets/Models/LargeWalkerPolicy.onnx here")]
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

        // DownloadToArray blocks until the result is ready, fine at 50 Hz on the CPU backend.
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
    public static void ActionsToJointTargets(float[] actions, float[] targetsOut)
    {
        for (int j = 0; j < ActionSize; j++)
        {
            int type = j % 3;
            targetsOut[j] = StandingAngle[type] + actions[j] * ActionScale[type];
        }
    }

    /// <summary> Standing angle of joint j (joint order), radians. </summary>
    public static float StandingJointAngle(int j) => StandingAngle[j % 3];

    /// <summary> Observation of the walker standing still and upright on flat ground with a zero command. </summary>
    public static float[] StandingObservation()
    {
        var obs = new float[ObservationSize];
        obs[GravityOffset + 2] = -1f;
        // Root sits 0.48 m above the ground when standing, scaled by 0.2
        for (int i = HeightScanOffset; i < ObservationSize; i++) obs[i] = 0.48f * 0.2f;
        return obs;
    }


    private void SelfTest()
    {
        float[] actions = Act(StandingObservation());

        float worstError = 0f;
        for (int a = 0; a < ActionSize; a++)
            worstError = Mathf.Max(worstError, Mathf.Abs(actions[a] - SelfTestExpected[a]));

        if (worstError < 1e-3f)
            Debug.Log($"{name}: walker policy loaded, self test passed (max error {worstError:E1})", this);
        else
            Debug.LogWarning($"{name}: walker policy self test FAILED, max error {worstError:F4}. Got [{string.Join(", ", actions)}]", this);

        // The self test should not leak into the first real observation
        ResetPolicy();
    }
}
