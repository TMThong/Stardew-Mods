using System;
using System.Collections.Concurrent;
using System.Threading;
using StardewModdingAPI;

namespace StardewConnect.Utilities
{
    /// <summary>
    /// Marshals work from background threads (the WebSocket receive loop, WebRTC callbacks)
    /// onto the game's update thread.
    ///
    /// Nothing outside this class may touch Stardew Valley state from a network thread:
    /// the game is not thread safe and a stray mutation is a crash waiting to happen.
    /// </summary>
    internal sealed class MainThreadDispatcher
    {
        /// <summary>Guard against one bad tick starving the game loop.</summary>
        private const int MaxActionsPerTick = 64;

        private readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();
        private readonly IMonitor monitor;
        private int mainThreadId = -1;

        public MainThreadDispatcher(IMonitor monitor)
        {
            this.monitor = monitor;
        }

        /// <summary>Number of actions still waiting to run.</summary>
        public int PendingCount => this.queue.Count;

        /// <summary>Records the update thread. Call once from a SMAPI event handler.</summary>
        public void CaptureMainThread()
        {
            this.mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>True when the caller is already on the game's update thread.</summary>
        public bool IsOnMainThread => this.mainThreadId == -1 || Thread.CurrentThread.ManagedThreadId == this.mainThreadId;

        /// <summary>Queues <paramref name="action"/> to run on the next game tick.</summary>
        public void Invoke(Action action)
        {
            if (action == null)
                return;
            this.queue.Enqueue(action);
        }

        /// <summary>Runs <paramref name="action"/> immediately when already on the update thread, otherwise queues it.</summary>
        public void InvokeOrRunNow(Action action)
        {
            if (action == null)
                return;

            if (this.IsOnMainThread)
            {
                this.Run(action);
                return;
            }

            this.queue.Enqueue(action);
        }

        /// <summary>Drains the queue. Call once per game tick from <c>UpdateTicked</c>.</summary>
        public void Pump()
        {
            for (int index = 0; index < MaxActionsPerTick; index++)
            {
                if (!this.queue.TryDequeue(out Action action))
                    return;
                this.Run(action);
            }
        }

        /// <summary>Drops everything still queued, e.g. when the session is torn down.</summary>
        public void Clear()
        {
            while (this.queue.TryDequeue(out _))
            {
                // discard
            }
        }

        private void Run(Action action)
        {
            try
            {
                action();
            }
            catch (Exception error)
            {
                this.monitor.Log($"A queued main-thread action failed: {error}", LogLevel.Error);
            }
        }
    }
}
