using System;
using System.Collections.Generic;
using UnityEngine;

public class WalkerRobotBuilder : MonoBehaviour
{
    /// <summary>
    ///
    ///     Builds the physics version of the LargeWalker (an ArticulationBody chain) from the robot
    ///     description exported by scripts/export_unity_robot.py. Masses, inertias, joint axes, limits
    ///     and collision shapes all come from the MuJoCo model the policy was trained on, already
    ///     converted to Unity axes.
    ///
    ///     Scale grows the robot from its trained size (0.48 m tall, 34 kg) the way a real animal would:
    ///     lengths x scale, masses x scale^3, inertias x scale^5. WalkerController scales the motors and
    ///     slows time by sqrt(scale) to match, so the trained policy walks the same way, just bigger and slower.
    ///
    /// </summary>



    #region JSON layout (matches export_unity_robot.py)

    [Serializable] private class RobotDescription
    {
        public string rootBody;
        public float standingHeight;
        public string imuBody;
        public float[] imuPosition;
        public BodyDescription[] bodies;
        public MeshDescription[] meshes;
        public FootDescription[] standingFeet;
    }

    [Serializable] private class BodyDescription
    {
        public string name;
        public int parent;
        public float[] position;
        public float[] rotation;
        public float mass;
        public float[] centerOfMass;
        public float[] inertia;
        public float[] inertiaRotation;
        public bool hasJoint;
        public string jointName;
        public float[] jointAnchor;
        public float[] jointAxis;
        public float jointLower;
        public float jointUpper;
        public float jointDamping;
        public float jointStanding;
        public ColliderDescription[] colliders;
        public VisualDescription[] visuals;
    }

    [Serializable] private class ColliderDescription
    {
        public string name;
        public string type;
        public float[] position;
        public float[] rotation;
        public float radius;
        public float height;
        public float[] size;
        public float friction;
    }

    [Serializable] private class VisualDescription
    {
        public string type;
        public float[] position;
        public float[] rotation;
        public float[] color;
        public int mesh;
        public float radius;
    }

    [Serializable] private class MeshDescription
    {
        public string name;
        public float[] vertices;
        public int[] triangles;
    }

    [Serializable] private class FootDescription
    {
        public string name;
        public float[] position;
    }

    #endregion



    [Header("--- References ---")]
    [Tooltip("Assets/Models/LargeWalkerRobot.json")]
    [SerializeField] private TextAsset robotDescription;
    [Tooltip("Optional. Copied and tinted per part. Leave empty to use URP/Lit")]
    [SerializeField] private Material baseMaterial;

    [Space]
    [Header("--- Build Settings ---")]
    [Tooltip("Size relative to the trained robot. 1 = trained size (0.48 m tall), 10 = native FBX size like the Medium Walker. Set back to 1 after retraining at full size")]
    [Min(0.01f)]
    [SerializeField] private float scale = 10f;
    [Tooltip("Layer for every robot collider. The default, Ignore Raycast, keeps the height scan from hitting the robot itself")]
    [SerializeField] private int robotLayer = 2;
    [Tooltip("Extra height above the standing pose when spawning, so the feet do not start inside the ground")]
    [SerializeField] private float spawnClearance = 0.01f;

    private RobotDescription _description;
    private readonly List<Transform> _feet = new();
    private Mesh _sphereMesh;

    /// <summary> The floating base. Its transform is the robot's body frame. </summary>
    public ArticulationBody Root { get; private set; }
    /// <summary> The 12 leg joints in the policy's joint order (WalkerPolicy.JointNames). </summary>
    public ArticulationBody[] Joints { get; private set; }
    /// <summary> Passive joint damping from the MuJoCo model (N m s / rad), joint order. </summary>
    public float[] JointDamping { get; private set; }
    /// <summary> Where the MuJoCo IMU site sits, in the root's local frame. </summary>
    public Vector3 ImuLocalPosition { get; private set; }
    public float StandingHeight => _description.standingHeight * scale;
    /// <summary> Size relative to the trained robot, fixed once the robot is built. </summary>
    public float Scale => scale;


    private void Awake()
    {
        if (robotDescription == null)
        {
            Debug.LogError($"{name}: WalkerRobotBuilder has no robot description assigned", this);
            enabled = false;
            return;
        }

        // The articulation lives under this object, so it has to be unscaled or PhysX gets confused.
        if (transform.lossyScale != Vector3.one)
            Debug.LogWarning($"{name}: the walker spawner should have scale (1, 1, 1), use the Scale setting to size the robot", this);

        Build();
    }

    private void Start()
    {
        if (Root == null) return;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        ResetToStanding(transform.position, Quaternion.LookRotation(forward, Vector3.up));
    }


    /// <summary> Puts the walker upright in its standing pose with its feet on the ground at groundPosition. </summary>
    public void ResetToStanding(Vector3 groundPosition, Quaternion heading)
    {
        Root.TeleportRoot(groundPosition + Vector3.up * (StandingHeight + spawnClearance * scale), heading);
        Root.linearVelocity = Vector3.zero;
        Root.angularVelocity = Vector3.zero;

        for (int j = 0; j < Joints.Length; j++)
        {
            Joints[j].jointPosition = new ArticulationReducedSpace(WalkerPolicy.StandingJointAngle(j));
            Joints[j].jointVelocity = new ArticulationReducedSpace(0f);
            Joints[j].jointForce = new ArticulationReducedSpace(0f);
        }
    }

    /// <summary>
    ///     Largest distance (m) between where the feet are and where MuJoCo puts them in the standing pose,
    ///     both in the root frame. Anything over a centimetre (times Scale) means a joint axis or sign is wrong.
    /// </summary>
    public float StandingFootError()
    {
        float worst = 0f;
        foreach (FootDescription expected in _description.standingFeet)
        {
            Transform foot = _feet.Find(f => f.name == expected.name);
            Vector3 actual = Root.transform.InverseTransformPoint(foot.position);
            worst = Mathf.Max(worst, Vector3.Distance(actual, ToVector3(expected.position) * scale));
        }
        return worst;
    }


    private void Build()
    {
        _description = JsonUtility.FromJson<RobotDescription>(robotDescription.text);
        _sphereMesh = BorrowSphereMesh();

        var meshes = new Mesh[_description.meshes.Length];
        for (int m = 0; m < meshes.Length; m++) meshes[m] = BuildMesh(_description.meshes[m], scale);

        var links = new ArticulationBody[_description.bodies.Length];
        var joints = new Dictionary<string, (ArticulationBody body, float damping)>();

        // A WalkerSkin with a source replaces the plain built-in meshes.
        var skin = GetComponent<WalkerSkin>();
        bool useSkin = skin != null && skin.HasSource;

        // Parents always come before their children in the export, so one pass builds the chain.
        for (int i = 0; i < _description.bodies.Length; i++)
        {
            BodyDescription body = _description.bodies[i];
            var link = new GameObject(body.name) { layer = robotLayer };

            bool isRoot = body.parent < 0;
            link.transform.SetParent(isRoot ? transform : links[body.parent].transform, false);
            // The root's MuJoCo position is its spawn height, ResetToStanding places it properly.
            link.transform.localPosition = isRoot ? Vector3.zero : ToVector3(body.position) * scale;
            link.transform.localRotation = isRoot ? Quaternion.identity : ToQuaternion(body.rotation);

            if (!useSkin)
                foreach (VisualDescription visual in body.visuals) AddVisual(link.transform, visual, meshes);
            foreach (ColliderDescription collider in body.colliders) AddCollider(link.transform, collider);

            var articulation = link.AddComponent<ArticulationBody>();
            // Mass grows with volume (scale^3), inertia with mass times length squared (scale^5).
            articulation.mass = body.mass * Mathf.Pow(scale, 3f);
            articulation.automaticCenterOfMass = false;
            articulation.centerOfMass = ToVector3(body.centerOfMass) * scale;
            articulation.automaticInertiaTensor = false;
            articulation.inertiaTensor = ToVector3(body.inertia) * Mathf.Pow(scale, 5f);
            articulation.inertiaTensorRotation = ToQuaternion(body.inertiaRotation);
            // MuJoCo has no velocity damping on bodies, joint damping is applied with the PD torque instead.
            articulation.linearDamping = 0f;
            articulation.angularDamping = 0f;
            articulation.jointFriction = 0f;

            if (body.hasJoint)
            {
                articulation.jointType = ArticulationJointType.RevoluteJoint;
                // A revolute joint turns about the anchor's X axis. The rest pose is the joint's zero.
                articulation.matchAnchors = true;
                articulation.anchorPosition = ToVector3(body.jointAnchor) * scale;
                articulation.anchorRotation = Quaternion.FromToRotation(Vector3.right, ToVector3(body.jointAxis));
                articulation.twistLock = ArticulationDofLock.LimitedMotion;

                // No spring or damper in the drive, the controller writes the PD torque itself.
                ArticulationDrive drive = articulation.xDrive;
                drive.lowerLimit = body.jointLower * Mathf.Rad2Deg;
                drive.upperLimit = body.jointUpper * Mathf.Rad2Deg;
                drive.stiffness = 0f;
                drive.damping = 0f;
                drive.forceLimit = float.MaxValue;
                articulation.xDrive = drive;

                // Damping is a torque per angular speed, which scales like the PD damping gain (scale^3.5).
                joints[body.jointName] = (articulation, body.jointDamping * Mathf.Pow(scale, 3.5f));
            }

            if (isRoot)
            {
                Root = articulation;
                ImuLocalPosition = ToVector3(_description.imuPosition) * scale;
            }
            links[i] = articulation;
        }

        Joints = new ArticulationBody[WalkerPolicy.JointNames.Length];
        JointDamping = new float[Joints.Length];
        for (int j = 0; j < Joints.Length; j++)
        {
            (Joints[j], JointDamping[j]) = joints[WalkerPolicy.JointNames[j]];
        }

        // Every joint is still at zero here, which is the pose the skin lines up against.
        if (useSkin) skin.Attach(this);
    }

    private void AddCollider(Transform link, ColliderDescription description)
    {
        var holder = new GameObject(description.name) { layer = robotLayer };
        holder.transform.SetParent(link, false);
        holder.transform.localPosition = ToVector3(description.position) * scale;
        holder.transform.localRotation = ToQuaternion(description.rotation);

        Collider collider;
        switch (description.type)
        {
            case "sphere":
                var sphere = holder.AddComponent<SphereCollider>();
                sphere.radius = description.radius * scale;
                collider = sphere;
                break;
            case "capsule":
                var capsule = holder.AddComponent<CapsuleCollider>();
                capsule.direction = 1;
                capsule.radius = description.radius * scale;
                capsule.height = description.height * scale;
                collider = capsule;
                break;
            case "ellipsoid":
                // Unity has no ellipsoid collider, a stretched convex sphere mesh is the closest match.
                holder.transform.localScale = ToVector3(description.size) * (2f * scale);
                var hull = holder.AddComponent<MeshCollider>();
                hull.sharedMesh = _sphereMesh;
                hull.convex = true;
                collider = hull;
                break;
            default:
                Debug.LogWarning($"{name}: unknown collider type {description.type}", this);
                Destroy(holder);
                return;
        }

        // Only the feet grip, like MuJoCo where every other collider is frictionless.
        // Maximum/Minimum win over whatever the ground's material combine mode is.
        bool grips = description.friction > 0f;
        collider.sharedMaterial = new PhysicsMaterial(description.name)
        {
            staticFriction = description.friction,
            dynamicFriction = description.friction,
            frictionCombine = grips ? PhysicsMaterialCombine.Maximum : PhysicsMaterialCombine.Minimum,
            bounciness = 0f,
            bounceCombine = PhysicsMaterialCombine.Minimum,
        };

        if (description.name.Contains("foot")) _feet.Add(holder.transform);
    }

    private void AddVisual(Transform link, VisualDescription description, Mesh[] meshes)
    {
        var holder = new GameObject("visual");
        holder.transform.SetParent(link, false);
        holder.transform.localPosition = ToVector3(description.position) * scale;
        holder.transform.localRotation = ToQuaternion(description.rotation);

        Mesh mesh;
        if (description.type == "mesh")
        {
            mesh = meshes[description.mesh];
        }
        else
        {
            mesh = _sphereMesh;
            holder.transform.localScale = Vector3.one * (description.radius * 2f * scale);
        }

        holder.AddComponent<MeshFilter>().sharedMesh = mesh;
        var material = baseMaterial != null ? new Material(baseMaterial) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.color = new Color(description.color[0], description.color[1], description.color[2], description.color[3]);
        holder.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    private static Mesh BuildMesh(MeshDescription description, float scale)
    {
        var vertices = new Vector3[description.vertices.Length / 3];
        for (int v = 0; v < vertices.Length; v++)
            vertices[v] = new Vector3(description.vertices[3 * v], description.vertices[3 * v + 1], description.vertices[3 * v + 2]) * scale;

        var mesh = new Mesh { name = description.name, vertices = vertices, triangles = description.triangles };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh BorrowSphereMesh()
    {
        // Unity's built-in sphere (radius 0.5), taken from a throwaway primitive.
        var primitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Mesh mesh = primitive.GetComponent<MeshFilter>().sharedMesh;
        DestroyImmediate(primitive);
        return mesh;
    }

    private static Vector3 ToVector3(float[] v) => new(v[0], v[1], v[2]);
    private static Quaternion ToQuaternion(float[] q) => new(q[0], q[1], q[2], q[3]);
}
