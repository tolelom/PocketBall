using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class TcpServer : MonoBehaviour
{
    public static TcpServer Instance { get; private set; }

    private void Awake()
    {
        // 중복 인스턴스 방지
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject); // 씬 전환에도 유지
    }

    private TcpListener _listener;
    private TcpClient _client;
    private NetworkStream _stream;
    private byte[] _buffer = new byte[1024];

    private CancellationTokenSource _cts;

    // 통신 상수들
    private const int Port = 1447;
    private const int CodeSend = 9901;
    private const int CodeRequest = 9902;
    private const int SignalOrder = 9908;
    private const int SignalClose = 9909;

    // 게임 상수들
    private const int NumberOfBalls = 6;
    private const int TableWidth = 254;
    private const int TableHeight = 127;

    public TMP_Text informationText;

    private readonly ConcurrentQueue<string> _uiQueue = new ConcurrentQueue<string>();
    private string _lastStatus;

    // handshake 관리
    private bool _nicknameReceived = false;
    private string _clientNickname = "";
    private int _order = 1; // 선공(1) 혹은 후공(2)
    
    private bool _reading = false;
    
    private void Start()
    {
        
        BallManager2D.OnBallsSpawned += HandleBallsSpawned;
        StartServer();
    }


    private void Update()
    {
        while (_uiQueue.TryDequeue(out var msg))
        {
            _lastStatus = msg;
            if (informationText != null)
                informationText.text = msg;
        }
        
        BeginRead();
    }

    private void EnqueueStatus(string msg)
    {
        _uiQueue.Enqueue(msg);
    }


    private void StartServer()
    {
        _cts = new CancellationTokenSource();

        _listener = new TcpListener(IPAddress.Any, Port);
        _listener.Start();

        Debug.Log("Server started. Waiting for clients...");
        EnqueueStatus("Server started. Waiting for clients...");

        // 연결 대기 시작
        _listener.BeginAcceptTcpClient(OnAccept, null);
    }

    private void OnAccept(IAsyncResult ar)
    {
        if (_listener == null) return;

        TcpClient incoming = null;

        try
        {
            incoming = _listener.EndAcceptTcpClient(ar);
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Accept error: {e.Message}");
        }
        finally
        {
            try
            {
                _listener?.BeginAcceptTcpClient(OnAccept, null);
            }
            catch
            {
                // ignored
            }
        }

        if (incoming == null) return;

        // 이미 접속 중인 클라이언트가 있다면 
        if (_client != null && _client.Connected)
        {
            Debug.Log("An existing client is connected. Rejecting new connection.");
            try
            {
                incoming.Close();
            }
            catch
            {
            }

            return;
        }

        // 단일 클라이언트 세팅
        _client = incoming;
        _stream = _client.GetStream();

        _nicknameReceived = false;
        _clientNickname = "";

        Debug.Log("Client connected. Waiting for handshake...");
        EnqueueStatus("Client connected. Waiting for handshake...");

        BeginRead();
    }

    private void BeginRead()
    {
        // Debug.Log("BeginRead");
        if (_stream == null || _reading) return;
        try
        {
            // 비동기 수신 루프1
            _stream.BeginRead(_buffer, 0, _buffer.Length, OnReceive, null);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"BeginRead error: {e.Message}");
            EnqueueStatus($"BeginRead error: {e.Message}");
            CloseClient();
        }
    }

    private void OnReceive(IAsyncResult ar)
    {
        _reading = false;
        Debug.Log("OnReceive called");
        if (_stream == null) return;
        int read = 0;
        try
        {
            read = _stream.EndRead(ar);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"EndRead error: {e.Message}");
            EnqueueStatus($"EndRead error: {e.Message}");
            CloseClient();
            return;
        }

        
        Debug.Log($"[OnReceive] read={read}");
        if (read <= 0)
        {
            // 연결 종료 EOF
            Debug.Log("Client disconnected. Waiting for clients...");
            EnqueueStatus("Client disconnected. Waiting for clients...");
            CloseClient();
            return;
        }

        // 메시지 처리(예: UTF-8 문자열)
        var msg = Encoding.UTF8.GetString(_buffer, 0, read);
        Debug.Log($"Recv: {msg}");
        EnqueueStatus($"Recv: {msg}");

        // 오류로 인해 추가
        if (string.IsNullOrWhiteSpace(msg.Replace("/", "")))
        {
            BeginRead();
            return;
        }

        // handshake 처리
        if (!_nicknameReceived)
        {
            if (TryParseHandshake(msg, out string nickname))
            {
                _clientNickname = nickname;
                _nicknameReceived = true;

                Debug.Log($"Handshake successful! Nickname: {nickname}");
                EnqueueStatus($"Hello, {nickname}!");

                // Start 버튼 활성화
                GameManager.Instance.EnableStart();

                // 2단계: 순서 신호 및 상태 전송
                SendSignalFrame(SignalOrder, _order);
                SendStateFrame();
            }
            else
            {
                Debug.LogWarning("Invalid handshake packet.");
                EnqueueStatus("Invalid handshake. Ignored.");
            }
        }
        else
        {
            if (TryParseAnglePower(msg, out float angle, out float power))
            {
                Debug.Log($"Received angle={angle}, power={power}");
                EnqueueStatus($"Angle={angle}, Power={power}");

                // 직접 Shoot 호출 대신, 메인 스레드에 작업 위임
                UnityMainThreadDispatcher.Instance().Enqueue(() => {
                    BallManager2D.Instance.Shoot(angle, power);
                    StartCoroutine(WaitAndSendState());
                });
            }
        }

        // 항상 다음 수신 대기
        BeginRead();
    }


    private IEnumerator WaitAndSendState()
    {
        Debug.Log("WaitAndSendState called");
        // 모든 공이 멈출 때까지 대기
        while (true)
        {
            // 공 위치와 Rigidbody2D 배열을 BalllManager2D에서 가져옵니다.
            var balls = BallManager2D.Instance.Balls;  
            bool anyMoving = false;

            // 움직임 판정 임계값
            const float velocityThreshold = 0.05f;

            // 하나라도 속도가 임계값 이상이면 아직 움직이고 있음
            foreach (var rb in balls)
            {
                if (rb.linearVelocity.sqrMagnitude > velocityThreshold * velocityThreshold)
                {
                    anyMoving = true;
                    break;
                }
            }

            if (!anyMoving)
                break;  // 모두 멈춤

            yield return null;  // 다음 프레임까지 대기
        }

        // 모든 공이 멈추면 상태 프레임 전송
        Debug.Log("WaitAndSendState finished");
        SendStateFrame();
    }
    
    // angle/power 파싱 헬퍼
    private bool TryParseAnglePower(string msg, out float angle, out float power)
    {
        angle = power = 0f;
        var parts = msg.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        return float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out angle)
               && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out power);
    }

    // 신호 프레임 전송 (parts[0]=signal, parts[1]=value)
    private void SendSignalFrame(int signal, int value)
    {
        var parts = new string[NumberOfBalls * 2];

        for (int i = 0; i < parts.Length; i++)
            parts[i] = "0";

        parts[0] = signal.ToString();
        parts[1] = value.ToString();

        var frame = string.Join("/", parts) + "/";
        Send(frame);
    }


    private bool TryParseHandshake(string msg, out string nickname)
    {
        nickname = null;

        if (string.IsNullOrWhiteSpace(msg))
            return false;

        var parts = msg.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return false;

        if (parts[0] != CodeSend.ToString())
            return false;

        nickname = parts[1];
        return true;
    }

    private void SendStateFrame()
    {
        Debug.Log("Sending state frame...");
        var positions = BallManager2D.Instance.GetBallPositions();
        var parts = new string[positions.Length * 2];
        for (int i = 0; i < positions.Length; i++)
        {
            parts[i * 2]     = positions[i].x.ToString(CultureInfo.InvariantCulture);
            parts[i * 2 + 1] = positions[i].y.ToString(CultureInfo.InvariantCulture);
        }
        Send(string.Join("/", parts) + "/");
    }

    public void Send(string message)
    {
        if (_stream == null || _client == null || !_client.Connected) return;

        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            _stream.Write(data, 0, data.Length);
            _stream.Flush();
            EnqueueStatus($"Sent: {message}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Send error: {e.Message}");
            EnqueueStatus($"Send error: {e.Message}");
            CloseClient();
        }
    }

    private void CloseClient()
    {
        Debug.Log("Client closed.");
        try
        {
            _stream?.Close();
        }
        catch
        {
        }

        try
        {
            _client?.Close();
        }
        catch
        {
        }

        _stream = null;
        _client = null;
    }
    
    
    
    private void HandleBallsSpawned()
    {
        // 핸드셰이크가 끝난 상태라면 초기 상태 전송
        if (_nicknameReceived)
            SendStateFrame();
    }
    
    private void OnApplicationQuit()
    {
        Shutdown();
    }

    private void OnDestroy()
    {
        BallManager2D.OnBallsSpawned -= HandleBallsSpawned;
        Shutdown();
    }

    private void Shutdown()
    {
        _cts?.Cancel();
        CloseClient();
        try
        {
            _listener?.Stop();
        }
        catch
        {
        }

        _listener = null;
    }
    
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"OnSceneLoaded: stream={( _stream==null ? "null" : "OK")}, client={( _client==null ? "null" : "OK")}");
    }
}