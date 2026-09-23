using System;
using UnityEngine;

[RequireComponent(typeof(WalkerRobotBuilder), typeof(WalkerPolicy))]
public class WalkerController : MonoBehaviour
{
    /// <summary>
    ///
    ///     Makes the physics LargeWalker walk with the trained policy. Every 0.02 s it measures the robot,
    ///     builds the 235 float observation, asks the policy for 12 actions and turns them into joint
    ///     angle targets. Every physics step it pushes the joints toward those targets with the same PD
    ///     law the robot was trained with, applied as a joint torque.
    ///
    ///     The policy thinks in MuJoCo axes (x forward, y left, z up). Unity is (x right, y up, z forward),
    ///     so every vector is converted on its way into the observation.
    ///
    ///     When the robot is built bigger than it was trained (WalkerRobotBuilder Scale = k), time runs
    ///     sqrt(k) slower: the policy step becomes 0.02 * sqrt(k) s and the motors get k^4 stiffness and
    ///     torque and k^3.5 damping. Everything going into the policy is converted back to the trained size
    ///     (speeds / sqrt(k), turn rates and joint speeds * sqrt(k), heights / k), so the policy sees exactly
    ///     what it saw in training. The command settings below are all in trained-size units.
    ///
    ///     Tested in plain MuJoCo with the same maths by scripts/sim2sim_check.py
    ///
    /// </summary>



    private enum CommandMode { Manual, FollowTarget }

    // At the trained size
    private const float PolicyStep = 0.02f;
    private const float ScanMaxDistance = 5f;

    // PD gains per joint type (coxa, femur, tibia), from largewalker_constants.py
    private static readonly float[] Stiffness = { 197.392f, 386.888f, 238.845f };
    private static readonly float[] Damping = { 12.566f, 24.630f, 15.205f };
    private static readonly float[] EffortLimit = { 90f, 180f, 120f };



    [Header("--- Command ---")]
    [Tooltip("FollowTarget: walk to the target and stop next to it. Manual: walk with the command below")]
    [SerializeField] private CommandMode commandMode = CommandMode.FollowTarget;
    [Tooltip("In trained-size units. x = forward speed (m/s), y = sideways speed to the LEFT (m/s), z = turn rate to the LEFT (rad/s). Trained range: x -1.2..2, y -0.6..0.6, z -1.2..1.2. At Scale k the robot really moves x * sqrt(k) m/s and turns z / sqrt(k) rad/s")]
    [SerializeField] private Vector3 manualCommand = new(0.8f, 0f, 0f);
    [Tooltip("What to walk to. Left empty, it chases the player (like WalkerAI)")]
    [SerializeField] private Transform target;
    [Tooltip("Stops once this close to the target, in trained-size metres (real distance = this * Scale)")]
    [SerializeField] private float goalRadius = 0.77f;
    [SerializeField] private float maxSpeed = 0.8f;
    [Tooltip("Slower commands are rounded to zero (the policy stands still) or up to this when not there yet")]
    [SerializeField] private float minSpeed = 0.35f;
    [Tooltip("Turn rate per radian of heading error")]
    [SerializeField] private float yawGain = 1.5f;
    [Tooltip("Forward speed per trained-size metre of distance")]
    [SerializeField] private float linearGain = 1f;
    [SerializeField] private float maxYawRate = 1.2f;

    [Space]
    [Header("--- Simulation ---")]
    [Tooltip("Physics steps per policy step. 20 = 1000 Hz at the trained size (316 Hz at Scale 10). The PD torque is applied explicitly, which goes unstable below ~10 (checked in MuJoCo)")]
    [Min(1)]
    [SerializeField] private int physicsStepsPerPolicyStep = 20;
    [Tooltip("What the height scan can see. Keep the robot's own layer out of it")]
    [SerializeField] private LayerMask terrainLayers = Physics.DefaultRaycastLayers;
    [Tooltip("Stand the walker back up where it is if it tips past ~72 degrees")]
    [SerializeField] private bool respawnIfFallen = true;

    [Space(3)]
    [Header("=== DEBUG ===")]
    [Tooltip("Check the standing pose against MuJoCo on the first physics steps and log the result")]
    [SerializeField] private bool checkStandingPose = true;
    [SerializeField] private bool showHeightScan;
    [SerializeField] private bool showCommand;
    [SerializeField] private Vector3 currentCommand;

    private WalkerRobotBuilder _robot;
    private WalkerPolicy _policy;
    private readonly float[] _observation = new float[WalkerPolicy.ObservationSize];
    private readonly float[] _targets = new float[WalkerPolicy.ActionSize];
    private readonly Vector3[] _scanHits = new Vector3[187];
    private int _decimation;
    private int _physicsStep;
    private float _scale;
    private float _timeScale;
    private readonly float[] _stiffness = new float[3];
    private readonly float[] _damping = new float[3];
    private readonly float[] _effortLimit = new float[3];


    private void Awake()
    {
        _robot = GetComponent<WalkerRobotBuilder>();
        _policy = GetComponent<WalkerPolicy>();

        // Froude scaling: a robot k times bigger with k^3 the mass moves like the small one when its time runs
        // sqrt(k) slower and its motors are k^4 (torque, stiffness) and k^3.5 (damping) stronger.
        _scale = _robot.Scale;
        _timeScale = Mathf.Sqrt(_scale);
        for (int type = 0; type < 3; type++)
        {
            _stiffness[type] = Stiffness[type] * Mathf.Pow(_scale, 4f);
            _damping[type] = Damping[type] * Mathf.Pow(_scale, 3.5f);
            _effortLimit[type] = EffortLimit[type] * Mathf.Pow(_scale, 4f);
        }

        // This changes the fixed timestep for the whole project.
        _decimation = physicsStepsPerPolicyStep;
        Time.fixedDeltaTime = PolicyStep * _timeScale / _decimation;
    }

    private void Start()
    {
        for (int j = 0; j < _targets.Length; j++) _targets[j] = WalkerPolicy.StandingJointAngle(j);

        if (target == null)
        {
            var player = FindAnyObjectByType<PlayerMovement>();
            if (player != null) target = player.transform;
        }
    }

    private void FixedUpdate()
    {
        if (_robot.Root == null) return;

        // The transforms only catch up with the teleported standing pose after one physics step.
        if (checkStandingPose && _physicsStep == 1) CheckStandingPose();

        if (_physicsStep % _decimation == 0)
        {
            if (respawnIfFallen && IsFallen()) Respawn();
            RunPolicy();
        }

        ApplyTorques();
        _physicsStep++;
    }


    private void RunPolicy()
    {
        currentCommand = commandMode == CommandMode.FollowTarget && target != null ? FollowCommand() : manualCommand;

        BuildObservation();
        float[] actions = _policy.Act(_observation);
        WalkerPolicy.ActionsToJointTargets(actions, _targets);
    }

    private void ApplyTorques()
    {
        ArticulationBody[] joints = _robot.Joints;
        for (int j = 0; j < joints.Length; j++)
        {
            int type = j % 3;
            float q = joints[j].jointPosition[0];
            float qd = joints[j].jointVelocity[0];

            float torque = Mathf.Clamp(_stiffness[type] * (_targets[j] - q) - _damping[type] * qd, -_effortLimit[type], _effortLimit[type]);
            // MuJoCo's passive joint damping acts on top of the motor, outside its torque limit.
            torque -= _robot.JointDamping[j] * qd;

            joints[j].jointForce = new ArticulationReducedSpace(torque);
        }
    }

    private void BuildObservation()
    {
        ArticulationBody root = _robot.Root;
        Transform body = root.transform;

        // Velocities of the IMU point, in the body frame.
        Vector3 imuWorld = body.TransformPoint(_robot.ImuLocalPosition);
        Vector3 linearVelocity = body.InverseTransformDirection(root.GetPointVelocity(imuWorld));
        Vector3 angularVelocity = body.InverseTransformDirection(root.angularVelocity);
        Vector3 gravity = body.InverseTransformDirection(Vector3.down);

        // Back to trained-size units: speeds / sqrt(k), rates * sqrt(k).
        Write(WalkerPolicy.LinVelOffset, ToMujoco(linearVelocity) / _timeScale);
        Write(WalkerPolicy.AngVelOffset, AngularToMujoco(angularVelocity) * _timeScale);
        Write(WalkerPolicy.GravityOffset, ToMujoco(gravity));

        ArticulationBody[] joints = _robot.Joints;
        for (int j = 0; j < joints.Length; j++)
        {
            _observation[WalkerPolicy.JointPosOffset + j] = joints[j].jointPosition[0] - WalkerPolicy.StandingJointAngle(j);
            _observation[WalkerPolicy.JointVelOffset + j] = joints[j].jointVelocity[0] * _timeScale;
        }

        Array.Copy(_policy.PreviousActions, 0, _observation, WalkerPolicy.LastActionOffset, WalkerPolicy.ActionSize);
        Write(WalkerPolicy.CommandOffset, currentCommand);
        HeightScan(body);
    }

    private void HeightScan(Transform body)
    {
        // A 17 x 11 grid of downward rays, 0.15 m apart, centred on the body and turned with its heading only.
        Vector3 forward = Vector3.ProjectOnPlane(body.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.01f)
        {
            // Body pointing straight up or down, fall back to its left axis like mjlab does
            Vector3 left = Vector3.ProjectOnPlane(-body.right, Vector3.up);
            forward = Vector3.Cross(Vector3.up, left);
        }
        forward.Normalize();
        Vector3 leftward = Vector3.Cross(forward, Vector3.up);
        Vector3 origin = body.position;

        int i = 0;
        for (int iy = 0; iy < 11; iy++)
        {
            float offsetLeft = -0.75f + 0.15f * iy;
            for (int ix = 0; ix < 17; ix++)
            {
                float offsetForward = -1.2f + 0.15f * ix;
                Vector3 start = origin + (forward * offsetForward + leftward * offsetLeft) * _scale;

                // Height of the body above the ground in trained-size metres, over 5 m. A miss reads as the full 5 m.
                float maxDistance = ScanMaxDistance * _scale;
                bool hit = Physics.Raycast(start, Vector3.down, out RaycastHit info, maxDistance, terrainLayers, QueryTriggerInteraction.Ignore);
                _observation[WalkerPolicy.HeightScanOffset + i] = hit ? info.distance / maxDistance : 1f;
                _scanHits[i] = hit ? info.point : start + Vector3.down * maxDistance;
                i++;
            }
        }
    }

    private Vector3 FollowCommand()
    {
        // Port of GoalVelocityCommand (goal_command.py): turn toward the target, walk, stop inside the radius.
        Transform body = _robot.Root.transform;
        Vector3 toTarget = Vector3.ProjectOnPlane(target.position - body.position, Vector3.up);
        float distance = toTarget.magnitude / _scale;
        if (distance < goalRadius) return Vector3.zero;

        // Headings measured the MuJoCo way, counter-clockwise from above.
        float heading = Mathf.Atan2(-body.forward.x, body.forward.z);
        float targetHeading = Mathf.Atan2(-toTarget.x, toTarget.z);
        float headingError = Mathf.DeltaAngle(heading * Mathf.Rad2Deg, targetHeading * Mathf.Rad2Deg) * Mathf.Deg2Rad;

        float yawRate = Mathf.Clamp(yawGain * headingError, -maxYawRate, maxYawRate);
        float speed = Mathf.Clamp(linearGain * distance, 0f, maxSpeed) * Mathf.Clamp01(Mathf.Cos(headingError));
        if (speed < minSpeed) speed = Mathf.Abs(headingError) < 0.5f ? minSpeed : 0f;

        return new Vector3(speed, 0f, yawRate);
    }

    private bool IsFallen()
    {
        // Up axis of the body against world up, 0.3 is about 72 degrees of tilt.
        return Vector3.Dot(_robot.Root.transform.up, Vector3.up) < 0.3f;
    }

    private void Respawn()
    {
        Transform body = _robot.Root.transform;
        Vector3 forward = Vector3.ProjectOnPlane(body.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;

        // Drop the walker back in standing pose over wherever it fell
        Vector3 ground = body.position;
        if (Physics.Raycast(body.position + Vector3.up * (2f * _scale), Vector3.down, out RaycastHit hit, 10f * _scale, terrainLayers, QueryTriggerInteraction.Ignore))
            ground = hit.point;

        _robot.ResetToStanding(ground, Quaternion.LookRotation(forward, Vector3.up));
        _policy.ResetPolicy();
        for (int j = 0; j < _targets.Length; j++) _targets[j] = WalkerPolicy.StandingJointAngle(j);
    }

    private void CheckStandingPose()
    {
        float error = _robot.StandingFootError();
        // Compared at the trained size
        error /= _scale;
        if (error < 0.01f)
            Debug.Log($"{name}: standing pose matches MuJoCo (feet within {error * 100f:F2} cm at trained size, scale {_scale})", this);
        else
            Debug.LogError($"{name}: standing pose does NOT match MuJoCo, a foot is {error * 100f:F1} cm off at trained size. A joint axis or angle sign is wrong", this);
    }


    // Unity (x right, y up, z forward) -> MuJoCo (x forward, y left, z up).
    private static Vector3 ToMujoco(Vector3 v) => new(v.z, -v.x, v.y);
    // Angular velocity flips sign as well, because the handedness changes.
    private static Vector3 AngularToMujoco(Vector3 w) => new(-w.z, w.x, -w.y);

    private void Write(int offset, Vector3 v)
    {
        _observation[offset] = v.x;
        _observation[offset + 1] = v.y;
        _observation[offset + 2] = v.z;
    }



#if UNITY_EDITOR

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying || _robot == null || _robot.Root == null) return;

        if (showHeightScan)
        {
            Gizmos.color = Color.magenta;
            foreach (Vector3 hit in _scanHits) Gizmos.DrawSphere(hit, 0.015f * _scale);
        }

        if (showCommand)
        {
            // Command arrow in the body's heading frame (forward and left)
            Transform body = _robot.Root.transform;
            Vector3 forward = Vector3.ProjectOnPlane(body.forward, Vector3.up).normalized;
            Vector3 left = Vector3.Cross(forward, Vector3.up);
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(body.position, (forward * currentCommand.x + left * currentCommand.y) * _scale);
        }
    }

#endif
}
