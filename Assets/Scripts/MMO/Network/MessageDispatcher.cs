using System;
using System.Collections.Generic;

namespace MMO.Network
{
    /// <summary>
    /// 消息分发器。按 msgId 注册处理器，主线程调用。
    /// </summary>
    public class MessageDispatcher
    {
        private readonly Dictionary<int, Action<byte[]>> _handlers = new();

        public void Register<T>(int msgId, Action<byte[]> handler)
        {
            _handlers[msgId] = handler;
        }

        public void Register<T>(int msgId, Action<T> handler, Func<byte[], T> deserializer)
        {
            _handlers[msgId] = payload =>
            {
                T msg = deserializer(payload);
                handler(msg);
            };
        }

        public void Unregister(int msgId)
        {
            _handlers.Remove(msgId);
        }

        public void Dispatch(int msgId, byte[] payload)
        {
            if (_handlers.TryGetValue(msgId, out var handler))
            {
                handler(payload);
            }
            else
            {
                UnityEngine.Debug.LogWarning($"[Dispatcher] 未注册的消息 msgId={msgId} payloadLen={payload?.Length ?? 0}，已丢弃");
            }
        }

        public void Clear()
        {
            _handlers.Clear();
        }
    }
}
