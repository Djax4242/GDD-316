using System.Collections.Generic;
using UnityEngine;

public class WalkerSkin : MonoBehaviour
{
    /// <summary>
    ///
    ///     Dresses the physics walker in an existing walker's meshes and materials (e.g. the Medium Walker
    ///     prefab). A copy of the source is stripped down to its renderers and bones, lined up with the
    ///     physics robot, and every frame each leg bone is snapped to the physics link it belongs to.
    ///
    ///     Each physics link's +z (MuJoCo +x) runs along its leg segment, so every bone is turned from its
    ///     bind direction onto its link's direction and pinned to the link's joint. When the joint-zero pose is
    ///     the FBX rest pose that turn is zero; a model built with a fixed hip droop (the Regent) gets the
    ///     droop applied to the skin the same way. The fit is logged so a mismatch shows up.
    ///
    /// </summary>



    [Header("--- References ---")]
    [Tooltip("The walker whose look to use, e.g. Assets/Prefabs/Walkers/Medium Walker.prefab. Leave empty to keep the plain built-in meshes")]
    [SerializeField] private GameObject skinSource;

    [Space(3)]
    [Header("=== DEBUG ===")]
    [Tooltip("Log how closely the skin's bones line up with the physics joints")]
    [SerializeField] private bool logFit = true;

    private Transform _skin;
    private Transform _robotRoot;
    private Vector3 _rootOffsetPosition;
    private Quaternion _rootOffsetRotation;
    private readonly List<(Transform bone, Transform link, Vector3 position, Quaternion rotation)> _bones = new();

    public bool HasSource => skinSource != null;


    /// <summary> Called by WalkerRobotBuilder right after it builds the robot, while every joint is still at zero. </summary>
    public void Attach(WalkerRobotBuilder robot)
    {
        _robotRoot = robot.Root.transform;
        _skin = CreateStrippedCopy();

        // Leg chains in the skin: each "Bone" is a hip, with Bone.001 (knee) and Bone.002 (ankle) below it.
        var chains = new List<Transform[]>();
        foreach (Transform t in _skin.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != "Bone") continue;
            Transform knee = t.Find("Bone.001");
            Transform ankle = knee != null ? knee.Find("Bone.002") : null;
            if (ankle != null) chains.Add(new[] { t, knee, ankle });
        }
        if (chains.Count != 4)
        {
            Debug.LogError($"{name}: WalkerSkin expected 4 leg chains (Bone/Bone.001/Bone.002) in {skinSource.name}, found {chains.Count}", this);
            Destroy(_skin.gameObject);
            _skin = null;
            return;
        }

        // Robot links per leg (coxa, femur, tibia), legs in joint order FL, FR, RL, RR.
        var links = new Transform[4][];
        for (int leg = 0; leg < 4; leg++)
            links[leg] = new[] { robot.Joints[3 * leg].transform, robot.Joints[3 * leg + 1].transform, robot.Joints[3 * leg + 2].transform };

        int[] legOfChain = FitSkinToRobot(chains, links);
        Dictionary<Transform, Matrix4x4> bind = BindPoses();

        // Remember how every bone sits on its physics link, and the skin root relative to the body.
        // Bone heads are the joints, so a bone is pinned to its link's origin and turned from its bind direction
        // (head to the next head) onto the link's segment direction (link +z). The check: with that turn, each
        // bone's far end must land on the next link's joint, i.e. the segment lengths agree.
        float worst = 0f, largestTurn = 0f;
        for (int c = 0; c < 4; c++)
        {
            for (int d = 0; d < 3; d++)
            {
                Transform bone = chains[c][d];
                Transform link = links[legOfChain[c]][d];
                Matrix4x4 m = bind[bone];
                Vector3 head = m.GetPosition();
                Transform next = d < 2 ? chains[c][d + 1] : bone.Find(bone.name + "_end");
                Vector3 bindDirection = next != null ? (d < 2 ? bind[next].GetPosition() : BindEnd(bone, m)) - head : link.forward;
                Quaternion turn = Quaternion.FromToRotation(bindDirection, link.forward);
                largestTurn = Mathf.Max(largestTurn, Quaternion.Angle(Quaternion.identity, turn));
                _bones.Add((bone, link, Vector3.zero, Quaternion.Inverse(link.rotation) * (turn * m.rotation)));
                if (d < 2)
                {
                    Vector3 end = link.position + turn * bindDirection;
                    worst = Mathf.Max(worst, (end - links[legOfChain[c]][d + 1].position).magnitude);
                }
                worst = Mathf.Max(worst, (head - link.position).magnitude * (d == 0 ? 1f : 0f));
            }
        }
        _rootOffsetPosition = _robotRoot.InverseTransformPoint(_skin.position);
        _rootOffsetRotation = Quaternion.Inverse(_robotRoot.rotation) * _skin.rotation;

        if (logFit)
        {
            float relative = worst / Mathf.Max(robot.Scale, 1e-3f);
            string verdict = relative < 0.01f ? "lined up" : "NOT lined up, the skin will not match the physics";
            Debug.Log($"{name}: skin from {skinSource.name} {verdict} (worst bone-to-joint gap {worst:F3} m, bones turned up to {largestTurn:F1} deg onto the links)", this);
        }
    }


    private static Vector3 BindEnd(Transform bone, Matrix4x4 bindPose)
    {
        // The _end bone carries no skin weights, so it has no bind pose; use its rest offset from the bone.
        Transform end = bone.Find(bone.name + "_end");
        return bindPose.MultiplyPoint3x4(end.localPosition);
    }


    private void LateUpdate()
    {
        if (_skin == null) return;

        // Parents before children: the skin root, then each chain hip -> knee -> ankle (the order they were added).
        _skin.SetPositionAndRotation(_robotRoot.TransformPoint(_rootOffsetPosition), _robotRoot.rotation * _rootOffsetRotation);
        foreach ((Transform bone, Transform link, Vector3 position, Quaternion rotation) in _bones)
            bone.SetPositionAndRotation(link.TransformPoint(position), link.rotation * rotation);
    }


    private Transform CreateStrippedCopy()
    {
        // Instantiate under an inactive holder so none of the source's scripts (AI, IK, NavMesh) ever wake up.
        var holder = new GameObject("Skin (stripping)");
        holder.SetActive(false);
        holder.transform.SetParent(transform, false);
        GameObject copy = Instantiate(skinSource, holder.transform);
        copy.name = $"Skin ({skinSource.name})";

        // Scripts first (they may require the built-in components), then everything that is not a renderer.
        MonoBehaviour[] scripts = copy.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = scripts.Length - 1; i >= 0; i--) DestroyImmediate(scripts[i]);
        foreach (Component component in copy.GetComponentsInChildren<Component>(true))
        {
            if (component is Transform || component is MeshFilter || component is Renderer) continue;
            DestroyImmediate(component);
        }
        foreach (SkinnedMeshRenderer skinned in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            skinned.updateWhenOffscreen = true;

        copy.transform.SetParent(transform, false);
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        copy.transform.localScale = Vector3.one;
        copy.SetActive(true);
        Destroy(holder);
        return copy.transform;
    }

    private int[] FitSkinToRobot(List<Transform[]> chains, Transform[][] links)
    {
        // Line the four skin hips up with the four robot hips: uniform scale, a turn about the vertical, and a shift.
        Dictionary<Transform, Matrix4x4> bind = BindPoses();
        var skinHips = new Vector3[4];
        var robotHips = new Vector3[4];
        for (int i = 0; i < 4; i++)
        {
            skinHips[i] = bind[chains[i][0]].GetPosition();
            robotHips[i] = links[i][0].position;
        }
        Vector3 skinCentre = Average(skinHips);
        Vector3 robotCentre = Average(robotHips);

        float skinRadius = 0f, robotRadius = 0f;
        for (int i = 0; i < 4; i++)
        {
            skinRadius += Flat(skinHips[i] - skinCentre).magnitude;
            robotRadius += Flat(robotHips[i] - robotCentre).magnitude;
        }
        float scale = robotRadius / skinRadius;

        // Try every way of assigning skin legs to robot legs that keeps them in order around the body.
        int[] skinOrder = SortedByAngle(skinHips, skinCentre);
        int[] robotOrder = SortedByAngle(robotHips, robotCentre);
        int[] best = null;
        Quaternion bestTurn = Quaternion.identity;
        float bestError = float.MaxValue;
        for (int shift = 0; shift < 4; shift++)
        {
            float cross = 0f, dot = 0f;
            for (int k = 0; k < 4; k++)
            {
                Vector3 s = Flat(skinHips[skinOrder[k]] - skinCentre);
                Vector3 r = Flat(robotHips[robotOrder[(k + shift) % 4]] - robotCentre);
                cross += s.z * r.x - s.x * r.z;
                dot += s.x * r.x + s.z * r.z;
            }
            // Turn about +y (Unity measures it clockwise from above).
            Quaternion turn = Quaternion.AngleAxis(Mathf.Atan2(cross, dot) * Mathf.Rad2Deg, Vector3.up);

            float error = 0f;
            var assignment = new int[4];
            for (int k = 0; k < 4; k++)
            {
                int chain = skinOrder[k];
                int leg = robotOrder[(k + shift) % 4];
                assignment[chain] = leg;
                Vector3 moved = robotCentre + turn * ((skinHips[chain] - skinCentre) * scale);
                error += (moved - robotHips[leg]).sqrMagnitude;
            }
            if (error < bestError)
            {
                bestError = error;
                bestTurn = turn;
                best = assignment;
            }
        }

        // Apply it to the skin root (currently at the builder's origin, unrotated, unscaled).
        _skin.position = robotCentre + bestTurn * ((_skin.position - skinCentre) * scale);
        _skin.rotation = bestTurn * _skin.rotation;
        _skin.localScale = Vector3.one * scale;
        return best;
    }

    private Dictionary<Transform, Matrix4x4> BindPoses()
    {
        // Where each bone was when the meshes were bound, worked out from the renderers' bind poses.
        var bind = new Dictionary<Transform, Matrix4x4>();
        foreach (SkinnedMeshRenderer skinned in _skin.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Matrix4x4[] poses = skinned.sharedMesh.bindposes;
            for (int i = 0; i < skinned.bones.Length && i < poses.Length; i++)
                if (skinned.bones[i] != null) bind[skinned.bones[i]] = skinned.transform.localToWorldMatrix * poses[i].inverse;
        }
        return bind;
    }

    private static int[] SortedByAngle(Vector3[] points, Vector3 centre)
    {
        var order = new[] { 0, 1, 2, 3 };
        System.Array.Sort(order, (a, b) => Angle(points[a] - centre).CompareTo(Angle(points[b] - centre)));
        return order;
    }

    private static float Angle(Vector3 v) => Mathf.Atan2(v.z, v.x);
    private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);
    private static Vector3 Average(Vector3[] points) => (points[0] + points[1] + points[2] + points[3]) / 4f;
}
