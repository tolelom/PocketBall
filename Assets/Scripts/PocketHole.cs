using UnityEngine;

public class PocketHole : MonoBehaviour
{
    private static readonly Vector2 WhiteBallStartPos = new Vector2(63.5f, 63.5f);

    // 공이 들어올 때 호출되는 이벤트
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("WhiteBall"))
        {
            // 흰 공은 초기 위치로 리셋
            other.attachedRigidbody.linearVelocity = Vector2.zero;
            other.transform.position = WhiteBallStartPos;
        }

        // Tag가 Ball인 공만 처리
        if (other.CompareTag("Ball"))
        {
            other.gameObject.transform.position = Vector2.zero;
            // 공 제거(또는 비활성화)
            other.gameObject.SetActive(false);
            // 점수 처리나 UI 업데이트 등 추가 로직 삽입 가능

            if (BallManager2D.Instance.CheckBalls())
            {
                BallManager2D.Instance.SpawnBalls();
            }
        }
    }
}