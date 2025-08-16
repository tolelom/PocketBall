using UnityEngine;

public class DebugBallMotion : MonoBehaviour
{
    public Vector2 initialVelocity = new Vector2(10f, 1f);

    void Start()
    {
        var rb = GetComponent<Rigidbody2D>();
        rb.linearVelocity = initialVelocity;
    }
}