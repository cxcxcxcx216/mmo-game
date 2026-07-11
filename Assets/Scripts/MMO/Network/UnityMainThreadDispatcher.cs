using System;
using System.Collections.Generic;
using UnityEngine;

namespace MMO.Network
{
    /// <summary>
    /// Unity 主线程派发器。
    /// 子线程通过 Enqueue 将操作投递到主线程执行。
    /// </summary>
    public class UnityMainThreadDispatcher : MonoBehaviour
    {
        private static UnityMainThreadDispatcher _instance;
        private static readonly object _lock = new();

        private readonly Queue<Action> _actions = new();
        private readonly object _actionLock = new();

        public static UnityMainThreadDispatcher Instance
        {
            get
            {
                if (_instance != null) return _instance;
                lock (_lock)
                {
                    if (_instance != null) return _instance;
                    var go = new GameObject("[MainThreadDispatcher]");
                    _instance = go.AddComponent<UnityMainThreadDispatcher>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        public static void Enqueue(Action action)
        {
            var inst = Instance;
            lock (inst._actionLock)
            {
                inst._actions.Enqueue(action);
            }
        }

        private void Update()
        {
            lock (_actionLock)
            {
                while (_actions.Count > 0)
                {
                    var action = _actions.Dequeue();
                    try { action.Invoke(); }
                    catch (Exception e) { Debug.LogError($"[MainThread] 异常: {e}"); }
                }
            }
        }
    }
}
