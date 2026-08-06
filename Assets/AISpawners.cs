using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class AIObjects
{
    public string AIGroupName { get { return m_aiGroupName; } }
    public GameObject objectPrefab { get { return m_prefab; } }
    public int maxAI { get { return m_maxAI; } }
    public int spawnRate { get { return m_spawnRate; } }
    public int spawnAmount { get { return m_spawnAmount; } }
    public bool randomizeState { get { return m_randomizeState; } }
    public bool enableSpawner { get { return m_enableSpawner; } }
    public float scaleMultiplier { get { return m_scaleMultiplier; } }

    [Header("AI Group Stats")]
    [SerializeField] private string m_aiGroupName;
    [SerializeField] private GameObject m_prefab;
    [SerializeField] [Range(0, 40)] private int m_maxAI;
    [SerializeField] [Range(0, 20)] private int m_spawnRate;
    [SerializeField] [Range(0, 20)] private int m_spawnAmount;
    [Tooltip("Scale applied to this prefab on spawn. Packs are authored at wildly different " +
             "sizes -- the PolyPack fish are roughly 5x the FishAlive ones -- so this has to be " +
             "set per group rather than globally.")]
    [SerializeField] [Range(0.01f, 10f)] private float m_scaleMultiplier = 0.3f;

    [Header("Main Settings")]
    [SerializeField] private bool m_enableSpawner;
    [SerializeField] private bool m_randomizeState;

    // Unity needs a parameterless constructor to run the field initializers above
    // when it deserializes this class out of the scene.
    public AIObjects() { }

    public AIObjects(string aiGroupName, GameObject prefab, int maxAI, int spawnRate, int spawnAmount, bool randomizeState)
    {
        m_aiGroupName = aiGroupName;
        m_prefab = prefab;
        m_maxAI = maxAI;
        m_spawnRate = spawnRate;
        m_spawnAmount = spawnAmount;
        m_randomizeState = randomizeState;
    }

    public void setValue(int MaxAI, int SpawnRate, int SpawnAmount)
    {
        m_maxAI = MaxAI;
        m_spawnRate = SpawnRate;
        m_spawnAmount = SpawnAmount;
    }
}

public class AISpawners : MonoBehaviour
{
    public List<Transform> Waypoints = new List<Transform>();
    public float spawnTimer { get { return m_spawnTimer; } }
    public Vector3 spawnAread { get { return m_spawnAread; } }

    [Header("Global Stats")]
    [Range(0f, 600f)]
    [SerializeField] private float m_spawnTimer;
    [SerializeField] private Color m_spawnAreaColor = new Color(0f, 1f, 0f, 0.3f);
    [SerializeField] private Vector3 m_spawnAread = new Vector3(20f, 10f, 20f);

    [Header("Waypoints")]
    [Tooltip("Hides the waypoint boxes at runtime: turns off their renderers and colliders so they can't be seen or bumped into, but still act as navigation targets. They stay visible in the Scene view as wire cubes.")]
    [SerializeField] private bool m_hideWaypoints = true;
    [SerializeField] private Color m_waypointGizmoColor = new Color(1f, 0.8f, 0f, 0.8f);

    [Header("AI Groups Setting")]
    public AIObjects[] aiObjects = new AIObjects[5];

    private GameObject[] m_AIGroupSpawn;

    void Start()
    {
        GetWayPoints();
        RandomizeGroups();
        CreateAIGroups();

        // A repeat rate of 0 -- which is what m_spawnTimer defaults to -- makes
        // InvokeRepeating fire every single frame and flood the scene with NPCs.
        float repeatRate = Mathf.Max(0.1f, spawnTimer);
        if (spawnTimer < 0.1f)
        {
            Debug.LogWarning(name + ": Spawn Timer is " + spawnTimer + "s, clamping to " + repeatRate +
                             "s so spawning doesn't run every frame.", this);
        }
        InvokeRepeating(nameof(SpawnNPC), 0.5f, repeatRate);
    }

    void SpawnNPC()
    {
        if (m_AIGroupSpawn == null) return;

        for (int i = 0; i < aiObjects.Length && i < m_AIGroupSpawn.Length; i++)
        {
            AIObjects group = aiObjects[i];
            if (group == null || !group.enableSpawner || group.objectPrefab == null) continue;

            // Use the group object we already created. GameObject.Find scans the
            // whole scene by name on every tick, skips inactive objects, and would
            // latch onto any unrelated object that happens to share the name.
            GameObject tempGroup = m_AIGroupSpawn[i];
            if (tempGroup == null) continue;

            int capacity = group.maxAI - tempGroup.transform.childCount;
            if (capacity <= 0 || group.spawnAmount <= 0) continue;

            // Random.Range is max-exclusive for ints, so spawnAmount was never
            // reachable. Clamping to capacity keeps the group under maxAI, which a
            // plain childCount check can't do once it spawns a whole batch.
            int spawnCount = Mathf.Min(Random.Range(1, group.spawnAmount + 1), capacity);
            for (int y = 0; y < spawnCount; y++)
            {
                Quaternion randomRotation = Quaternion.Euler(Random.Range(-20, 20), Random.Range(0, 360), 0);
                GameObject tempSpawn = Instantiate(group.objectPrefab, RandomPosition(), randomRotation);
                tempSpawn.transform.SetParent(tempGroup.transform, true);

                AIMove move = tempSpawn.GetComponent<AIMove>();
                if (move == null) move = tempSpawn.AddComponent<AIMove>();

                // Guard against a 0 arriving from scene data serialized before this
                // field existed, which would scale the NPC out of existence.
                move.SetScaleMultiplier(group.scaleMultiplier > 0f ? group.scaleMultiplier : 1f);
            }
        }
    }

    public Vector3 RandomPosition()
    {
        Vector3 randomPos = new Vector3(
            Random.Range(-spawnAread.x, spawnAread.x),
            Random.Range(-spawnAread.y, spawnAread.y),
            Random.Range(-spawnAread.z, spawnAread.z)
        );
        randomPos = transform.TransformPoint(randomPos * 0.5f);
        return randomPos;
    }

    public Vector3 RandomWayPoint()
    {
        // With no waypoints tagged under the spawner this used to index [0] on an
        // empty list and throw on every NPC, every frame.
        if (Waypoints.Count == 0) return transform.position;

        int randomWP = Random.Range(0, Waypoints.Count);
        return Waypoints[randomWP].position;
    }

    void RandomizeGroups()
    {
        for (int i = 0; i < aiObjects.Length; i++)
        {
            AIObjects aiObject = aiObjects[i];
            if (aiObject == null) continue;

            if (aiObject.randomizeState)
            {
                aiObject.setValue(Random.Range(1, 31), Random.Range(1, 21), Random.Range(1, 11));
            }
        }
    }

    void CreateAIGroups()
    {
        m_AIGroupSpawn = new GameObject[aiObjects.Length];

        for (int i = 0; i < aiObjects.Length; i++)
        {
            if (aiObjects[i] == null) continue;

            // An unnamed group used to produce a nameless GameObject that
            // GameObject.Find could never retrieve, so it silently never spawned.
            string groupName = string.IsNullOrEmpty(aiObjects[i].AIGroupName)
                ? "AI Group " + i
                : aiObjects[i].AIGroupName;

            GameObject aiGroupSpawn = new GameObject(groupName);
            aiGroupSpawn.transform.SetParent(transform, false);
            m_AIGroupSpawn[i] = aiGroupSpawn;
        }
    }

    void GetWayPoints()
    {
        // The list is public and serialized, so anything left in it from the
        // inspector would be duplicated by the scan below.
        Waypoints.Clear();

        Transform[] wp = GetComponentsInChildren<Transform>();
        foreach (Transform t in wp)
        {
            if (t != transform && t.CompareTag("waypoint"))
            {
                Waypoints.Add(t);
                if (m_hideWaypoints) HideWaypoint(t);
            }
        }
    }

    // Waypoints are only ever read for their position, so the box mesh and its
    // collider serve no purpose in play mode -- they just block the NPCs and
    // clutter the view.
    void HideWaypoint(Transform waypoint)
    {
        foreach (Renderer r in waypoint.GetComponentsInChildren<Renderer>())
        {
            r.enabled = false;
        }
        foreach (Collider c in waypoint.GetComponentsInChildren<Collider>())
        {
            c.enabled = false;
        }
    }

    void OnDrawGizmosSelected()
    {
        // Drawn through the spawner's own transform to match RandomPosition(),
        // which uses TransformPoint -- a world-axis cube drifts away from the real
        // spawn volume as soon as this object is rotated or scaled.
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = m_spawnAreaColor;
        Gizmos.DrawCube(Vector3.zero, spawnAread);
        Gizmos.matrix = Matrix4x4.identity;

        // Keep the waypoints locatable in the Scene view even once they're hidden.
        Gizmos.color = m_waypointGizmoColor;
        foreach (Transform t in GetComponentsInChildren<Transform>())
        {
            if (t == transform || !t.CompareTag("waypoint")) continue;
            Gizmos.DrawWireCube(t.position, t.lossyScale);
        }
    }
}