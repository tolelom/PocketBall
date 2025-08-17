using System;
using UnityEngine;

public class BallManager2D : MonoBehaviour
{
    public static BallManager2D Instance { get; private set; }

    public GameObject whiteBallPrefab;
    public GameObject blackBallPrefab;
    public GameObject colorBallPrefab;


    public Rigidbody2D[] _balls; // 총 6개 공
    public Rigidbody2D[] Balls => _balls;

    private const float TableWidth = 254f;
    private const float TableHeight = 127f;
    private const float BallDiameter = 5.73f;

    public static event Action OnBallsSpawned;
    
    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        SpawnBalls();
    }

    public void SpawnBalls()
    {
        // 기존 공 제거
        if (_balls != null)
            foreach (var b in _balls)
                Destroy(b.gameObject);

        _balls = new Rigidbody2D[6];

        // 1) 흰공 배치: 왼쪽 중앙
        Vector2 whitePos = new Vector2(TableWidth * (0.25f), TableHeight / 2f);
        _balls[0] = Instantiate(whiteBallPrefab, whitePos, Quaternion.identity)
            .GetComponent<Rigidbody2D>();

        // 2) 검정공 배치: 오른쪽 중앙
        Vector2 blackPos = new Vector2(TableWidth * (0.75f), TableHeight / 2f);
        _balls[1] = Instantiate(blackBallPrefab, blackPos, Quaternion.identity)
            .GetComponent<Rigidbody2D>();

        // 3) 색공 4개: 검정공을 중심으로 상·하·좌·우
        float delta = 10f;
        Vector2[] offsets = new Vector2[]
        {
            new Vector2(TableWidth * (0.75f), TableHeight / 2f + delta), // 위
            new Vector2(TableWidth * (0.75f), TableHeight / 2f - delta), // 아래
            new Vector2(TableWidth * (0.75f) - delta, TableHeight / 2f), // 왼쪽
            new Vector2(TableWidth * (0.75f) + delta, TableHeight / 2f), // 오른쪽
        };

        for (int i = 0; i < offsets.Length; i++)
        {
            Vector2 pos = blackPos + offsets[i];
            _balls[2 + i] = Instantiate(colorBallPrefab, offsets[i], Quaternion.identity)
                .GetComponent<Rigidbody2D>();
        }

        // Rigidbody2D 설정
        foreach (var rb in _balls)
        {
            rb.gravityScale = 0f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.linearDamping = 1f;            // 선형 마찰
            rb.angularDamping = 0f;       // 회전 감속 제거
            rb.freezeRotation = true;  // 회전 완전 고정
        }
        
        OnBallsSpawned?.Invoke();
    }


    public void Shoot(float angle, float power)
    {
        Debug.Log($"Shoot start: {angle}, {power}");

        // 파워를 6배로 임의 세팅
        power = power * 6;
        
        float rad = angle * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)).normalized;
    
        // 기존 속도 초기화
        _balls[0].linearVelocity = Vector2.zero;
    
        // 임펄스 힘 적용
        _balls[0].AddForce(dir * power, ForceMode2D.Impulse);
    
        // 적용된 속도 로그
        Debug.Log($"Applied velocity: {_balls[0].linearVelocity}");
        Debug.Log("EndShoot");
    }

    public bool CheckBalls()
    {
        bool check = true;
        for (int i = 1; i < 6; ++i)
        {
            if (Balls[i].transform.position.x == 0f && Balls[i].transform.position.y == 0f)
                continue;
            check = false;
        }

        return check;
    }

    private static bool op_Equality(Vector3 transformPosition, Vector2 zero)
    {
        throw new NotImplementedException();
    }

    public Vector2[] GetBallPositions()
    {
        Vector2[] pts = new Vector2[_balls.Length];
        for (int i = 0; i < _balls.Length; i++)
            pts[i] = _balls[i].position;
        return pts;
    }
}