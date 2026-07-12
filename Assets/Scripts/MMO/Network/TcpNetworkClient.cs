using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace MMO.Network
{
    /// <summary>
    /// MMO TCP 网络客户端。
    /// 帧格式: [length:int32_be][msgId:int32_be][seq:int32_be][protobuf_payload]
    /// length = 4(msgId) + 4(seq) + len(payload)
    /// 异步接收，主线程派发消息。
    /// </summary>
    public class TcpNetworkClient : MonoBehaviour
    {
        [Header("连接配置")]
        [SerializeField] private string host = "127.0.0.1";
        [SerializeField] private int port = 9100;
        [SerializeField] private float heartbeatInterval = 15f;

        public string Host => host;
        public int Port => port;
        public bool IsConnected => _tcp?.Connected == true;
        public string SessionKey { get; set; }

        private TcpClient _tcp;
        private NetworkStream _stream;
        private Thread _receiveThread;
        private Thread _heartbeatThread;
        private volatile bool _running;

        private readonly object _sendLock = new();

        // 主线程消息队列（线程安全）
        private readonly Queue<(int msgId, int seq, byte[] payload)> _recvQueue = new();
        private readonly object _queueLock = new();

        // 请求序列号（防重放，单调递增）
        private int _seqCounter;
        private long _lastServerTime;

        /// <summary>每帧最多派发的消息数量，防止突发大量消息卡死主线程。</summary>
        private const int MaxMessagesPerFrame = 100;

        // ==================== 事件 ====================
        public event Action<int, byte[]> OnMessageReceived;   // msgId, payload
        public event Action OnConnected;
        public event Action<string> OnDisconnected;
        public event Action<string> OnConnectFailed;

        private void Awake()
        {
        }

        private void Update()
        {
            // 在主线程派发积压消息（每帧上限 MaxMessagesPerFrame，超出延后下一帧处理）
            lock (_queueLock)
            {
                int processed = 0;
                while (_recvQueue.Count > 0 && processed < MaxMessagesPerFrame)
                {
                    var (msgId, seq, payload) = _recvQueue.Dequeue();
                    try
                    {
                        if (msgId == Protocol.MsgId.HEARTBEAT_ACK)
                        {
                            var ack = Protocol.HeartbeatAck.Deserialize(payload);
                            _lastServerTime = ack.serverTime;
                        }
                        OnMessageReceived?.Invoke(msgId, payload);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[Net] 派发消息 msgId={msgId} 异常: {e}");
                    }
                    processed++;
                }

                if (_recvQueue.Count > 0)
                {
                    Debug.LogWarning($"[Net] 本帧消息已达上限 {MaxMessagesPerFrame}，剩余 {_recvQueue.Count} 条延后下一帧处理");
                }
            }
        }

        private void OnApplicationQuit()
        {
            Disconnect();
        }

        /// <summary>异步连接服务器</summary>
        public void Connect(string hostOverride = null, int portOverride = 0)
        {
            if (hostOverride != null) host = hostOverride;
            if (portOverride > 0) port = portOverride;

            Disconnect(silent: true);

            _running = true;
            _seqCounter = 0;

            _receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "MMO-Recv" };
            _heartbeatThread = new Thread(HeartbeatLoop) { IsBackground = true, Name = "MMO-Heartbeat" };

            // 同步连接（在子线程中），避免阻塞主线程
            new Thread(() =>
            {
                try
                {
                    _tcp = new TcpClient();
                    var ar = _tcp.BeginConnect(host, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5)))
                    {
                        _tcp.Close();
                        DispatchConnectFailed($"连接超时 {host}:{port}");
                        return;
                    }
                    _tcp.EndConnect(ar);
                    _stream = _tcp.GetStream();
                    _tcp.NoDelay = true;

                    UnityEngine.Debug.Log($"[Net] 已连接 {host}:{port}");

                    // 主线程回调
                    UnityMainThreadDispatcher.Enqueue(() => OnConnected?.Invoke());

                    _receiveThread.Start();
                    _heartbeatThread.Start();
                }
                catch (Exception e)
                {
                    // 如果是主动断开导致的异常，不报错
                    if (_running)
                        DispatchConnectFailed($"连接失败: {e.Message}");
                }
            }) { IsBackground = true, Name = "MMO-Connect" }.Start();
        }

        /// <summary>断开连接</summary>
        /// <param name="silent">静默断开，不触发 OnDisconnected 事件</param>
        public void Disconnect(bool silent = false)
        {
            _running = false;
            try { _stream?.Close(); } catch (Exception e) { Debug.LogWarning($"[TcpClient] 关闭流异常: {e.Message}"); }
            try { _tcp?.Close(); } catch (Exception e) { Debug.LogWarning($"[TcpClient] 关闭Socket异常: {e.Message}"); }
            _stream = null;
            _tcp = null;

            try { _receiveThread?.Join(500); } catch (Exception e) { Debug.LogWarning($"[TcpClient] 等待接收线程结束异常: {e.Message}"); }
            try { _heartbeatThread?.Join(500); } catch (Exception e) { Debug.LogWarning($"[TcpClient] 等待心跳线程结束异常: {e.Message}"); }
            _receiveThread = null;
            _heartbeatThread = null;

            if (!silent)
                UnityMainThreadDispatcher.Enqueue(() => OnDisconnected?.Invoke("主动断开"));
        }

        /// <summary>发送消息（自动递增 seq 防重放）</summary>
        public void Send(int msgId, byte[] payload, int? seq = null)
        {
            if (!IsConnected || _stream == null)
            {
                Debug.LogWarning($"[Net] 未连接，无法发送 msgId={msgId}");
                return;
            }

            int actualSeq = seq ?? Interlocked.Increment(ref _seqCounter);
            int length = 4 + 4 + (payload?.Length ?? 0);

            // 大端序帧: [length:int32_be][msgId:int32_be][seq:int32_be][payload]
            byte[] frame = new byte[4 + length];
            WriteInt32BE(frame, 0, length);
            WriteInt32BE(frame, 4, msgId);
            WriteInt32BE(frame, 8, actualSeq);
            if (payload != null && payload.Length > 0)
            {
                Buffer.BlockCopy(payload, 0, frame, 12, payload.Length);
            }

            Debug.Log($"[Net] SEND msgId={msgId} seq={actualSeq} payloadLen={payload?.Length ?? 0}");

            lock (_sendLock)
            {
                try
                {
                    _stream.Write(frame, 0, frame.Length);
                    _stream.Flush();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Net] 发送失败: {e.Message}");
                    UnityMainThreadDispatcher.Enqueue(() => OnDisconnected?.Invoke($"发送异常: {e.Message}"));
                }
            }
        }

        // ==================== 内部线程 ====================

        private void ReceiveLoop()
        {
            byte[] lengthBuf = new byte[4];
            while (_running && _tcp != null && _tcp.Connected)
            {
                try
                {
                    // 读 length (4 bytes, big-endian)
                    if (!ReadExact(lengthBuf, 0, 4))
                    {
                        break;
                    }
                    int length = ReadInt32BE(lengthBuf, 0);
                    if (length < 8 || length > 1024 * 1024)
                    {
                        Debug.LogError($"[Net] 非法帧长度: {length}");
                        break;
                    }

                    // 读 body: [msgId:int32_be][seq:int32_be][payload]
                    byte[] body = new byte[length];
                    if (!ReadExact(body, 0, length))
                    {
                        break;
                    }

                    int msgId = ReadInt32BE(body, 0);
                    int seq = ReadInt32BE(body, 4);
                    byte[] payload = new byte[length - 8];
                    Buffer.BlockCopy(body, 8, payload, 0, payload.Length);

                    UnityEngine.Debug.Log($"[Net] RECV msgId={msgId} seq={seq} payloadLen={payload.Length}");

                    // 入队，主线程派发
                    lock (_queueLock)
                    {
                        _recvQueue.Enqueue((msgId, seq, payload));
                    }
                }
                catch (Exception e)
                {
                    if (_running)
                        Debug.LogError($"[Net] 接收异常: {e.Message}");
                    break;
                }
            }

            if (_running)
            {
                UnityMainThreadDispatcher.Enqueue(() => OnDisconnected?.Invoke("连接断开"));
            }
        }

        private void HeartbeatLoop()
        {
            float timer = 0;
            while (_running && _tcp != null && _tcp.Connected)
            {
                Thread.Sleep(1000);
                timer += 1f;
                if (timer >= heartbeatInterval)
                {
                    timer = 0;
                    Send(Protocol.MsgId.HEARTBEAT_REQ, Array.Empty<byte>());
                }
            }
        }

        // ==================== IO 辅助 ====================

        private bool ReadExact(byte[] buf, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = _stream.Read(buf, offset + total, count - total);
                if (read == 0) return false;
                total += read;
            }
            return true;
        }

        private static void WriteInt32BE(byte[] buf, int offset, int value)
        {
            buf[offset] = (byte)(value >> 24);
            buf[offset + 1] = (byte)(value >> 16);
            buf[offset + 2] = (byte)(value >> 8);
            buf[offset + 3] = (byte)value;
        }

        private static int ReadInt32BE(byte[] buf, int offset)
        {
            return (buf[offset] << 24) | (buf[offset + 1] << 16) | (buf[offset + 2] << 8) | buf[offset + 3];
        }

        private void DispatchConnectFailed(string reason)
        {
            _running = false;
            UnityEngine.Debug.LogError($"[Net] {reason}");
            UnityMainThreadDispatcher.Enqueue(() => OnConnectFailed?.Invoke(reason));
        }
    }
}
