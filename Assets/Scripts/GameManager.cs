using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement; 

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public Button startButton;
    
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        startButton.interactable = false;
        startButton.onClick.AddListener(OnStartButtonClicked);
    }
    
    // TcpServer에서 호출할 메서드
    public void EnableStart()
    {
        startButton.interactable = true;
        startButton.onClick.AddListener(OnStartButtonClicked);
    }
    
    public void OnStartButtonClicked()
    {
        // 버튼 비활성화(중복 클릭 방지)
        startButton.interactable = false;
        // PocketBallScene 로드
        SceneManager.LoadScene("PocketBallScene");
    }
}