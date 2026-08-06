using UnityEngine;

public class AIMove : MonoBehaviour
{
    private AISpawners m_AIManager;
    private bool m_hasTarget = false;
    private Vector3 m_wayPoint;
    private Vector3 m_lastWaypoint = Vector3.zero;
    private Animator m_animator;
    private float m_speed;
    private Collider m_collider;
    private RaycastHit m_hit;
    public bool useRandomTarget;

    [Header("Model Setup")]
    [Tooltip("Uniform scale applied to the prefab's own scale when the NPC spawns.")]
    [SerializeField] private float m_scaleMultiplier = 0.3f;

    [Tooltip("Yaw correction in degrees for models that don't face +Z. The PolyPack fish are " +
             "built along their X axis, so they need 90. Flip this to -90 if they swim tail-first.")]
    [SerializeField] private float m_modelYawOffset = 90f;

    [Tooltip("How far ahead, in world units, the NPC checks for obstacles.")]
    [SerializeField] private float m_lookAheadDistance = 1.5f;

    // Called by AISpawners right after AddComponent, before Start runs.
    public void SetScaleMultiplier(float multiplier)
    {
        m_scaleMultiplier = multiplier;
    }

    void Start()
    {
        m_AIManager = GetComponentInParent<AISpawners>();

        m_animator = GetComponent<Animator>();
        SetUpNPC();
    }

    void Update()
    {
        if (!m_hasTarget)
        {
            m_hasTarget = CanFindTarget();
        }
        else
        {
            RotateNPC(m_wayPoint, m_speed);
            transform.position = Vector3.MoveTowards(transform.position, m_wayPoint, m_speed * Time.deltaTime);
            CollidedNPC();
        }

        if (transform.position==m_wayPoint)
        {
            m_hasTarget = false;
        }
    }
    Vector3 GetWaypoint(bool isRandom){
        if(isRandom) return m_AIManager.RandomPosition();
        else return m_AIManager.RandomWayPoint();
    }
    void CollidedNPC(){
        RaycastHit hit;

        // Scan along the direction of travel, not transform.forward: the yaw offset
        // that fixes the sideways swimming leaves +Z pointing off the NPC's flank.
        // The distance is in world units now -- it used to be localScale.z, which at
        // 0.3x scale would shrink the check to almost nothing.
        Vector3 heading = m_wayPoint - transform.position;
        if(heading.sqrMagnitude < 0.0001f) return;

        if(Physics.Raycast(transform.position, heading.normalized, out hit, m_lookAheadDistance)){
            if(hit.collider == m_collider || hit.collider.CompareTag("waypoint")){
                return;
            }
            int randomNum = Random.Range(1,100);
            if(randomNum <40) m_hasTarget = false;
            Transform hitParent = hit.collider.transform.parent;
            if(hitParent != null) Debug.Log(hitParent.name + " " + hitParent.position);
        }
    }
    bool CanFindTarget(float start = 1f, float end = 7f)
    {
        if (m_AIManager == null) return false;

        m_wayPoint = m_AIManager.RandomWayPoint();
        if (m_lastWaypoint == m_wayPoint)
        {
            m_wayPoint = GetWaypoint(true);
            return false;
        }
        else
        {
            m_lastWaypoint = m_wayPoint;
            m_speed = Random.Range(start, end);
            if (m_animator != null) m_animator.speed = m_speed;
            return true;
        }
    }

    void RotateNPC(Vector3 waypoint, float currentSpeed)
    {
        float TurnSpeed = currentSpeed * Random.Range(1f, 3f);
        Vector3 LookAt = waypoint - transform.position;

        // LookRotation errors out on a zero vector, which happens the moment the
        // NPC lands exactly on its waypoint.
        if (LookAt.sqrMagnitude < 0.0001f) return;

        // LookRotation points the model's +Z down the travel direction. These fish
        // are modelled nose-along-X, so without the yaw offset they swim sideways.
        Quaternion facing = Quaternion.LookRotation(LookAt) * Quaternion.Euler(0f, m_modelYawOffset, 0f);
        transform.rotation = Quaternion.Slerp(transform.rotation, facing, TurnSpeed * Time.deltaTime);
    }

    void SetUpNPC()
    {
        transform.localScale *= m_scaleMultiplier;
        Collider ownCollider = GetComponent<Collider>();
        if(ownCollider != null && ownCollider.enabled){
            m_collider = ownCollider;
        }
        else{
            foreach(Collider child in GetComponentsInChildren<Collider>()){
                if(child.enabled){
                    m_collider = child;
                    break;
                }
            }
        }
    }
}