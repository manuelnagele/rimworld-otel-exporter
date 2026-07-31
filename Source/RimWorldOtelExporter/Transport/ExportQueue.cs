using System;
using System.Collections.Concurrent;
using System.Threading;

namespace RimWorldOtelExporter.Transport
{
    /// <summary>
    /// Thread-safe export queue backed by a dedicated background Thread.
    /// Game tick thread enqueues byte[] payloads; background thread drains and sends them.
    /// Uses Thread (not Task) — Mono threadpool under Unity is unreliable.
    /// </summary>
    public sealed class ExportQueue : IDisposable
    {
        public readonly struct Payload
        {
            public readonly string Endpoint;
            public readonly byte[] Data;
            public readonly bool IsLogs;
            public Payload(string endpoint, byte[] data, bool isLogs)
            {
                Endpoint = endpoint; Data = data; IsLogs = isLogs;
            }
        }

        private readonly ConcurrentQueue<Payload> _queue = new ConcurrentQueue<Payload>();
        private readonly OtlpHttpSender _sender;
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _wakeSignal = new ManualResetEventSlim(false);
        private volatile bool _stopping;

        // Circuit breaker state
        private volatile int _consecutiveFailures;
        private DateTime _retryAfter = DateTime.MinValue;
        private volatile bool _offline;
        private DateTime _nextProbe = DateTime.MinValue;

        private const int MaxConsecutiveFailures = 10;
        private const int MaxBackoffSeconds = 60;
        private const int OfflineProbeSeconds = 180; // auto re-probe every 3 min while offline
        private const int MaxQueueDepth = 128;       // bound memory during long backoffs

        public event Action<string>? OnWarning;
        public event Action<string, int>? OnExportSuccess;  // endpoint, payloadBytes
        public event Action<string, string>? OnExportFailure; // endpoint, error

        // Read-only status for the settings UI.
        public bool IsOffline => _offline;
        public int ConsecutiveFailures => _consecutiveFailures;
        public int QueueDepth => _queue.Count;
        public DateTime NextRetryUtc => _offline ? _nextProbe : _retryAfter;

        public ExportQueue(OtlpHttpSender sender)
        {
            _sender = sender;
            _thread = new Thread(Run) { IsBackground = true, Name = "RimWorldOtelExporter.ExportQueue" };
            _thread.Start();
        }

        public void Enqueue(string endpoint, byte[] data)
        {
            if (_stopping) return;
            bool isLogs = endpoint.EndsWith("/v1/logs", StringComparison.Ordinal);
            // While offline, KEEP event logs (deaths/raids are irreplaceable) but drop metrics —
            // gauges are re-derived every cycle, so a stale metric point is worthless on recovery.
            if (_offline && !isLogs) return;
            while (_queue.Count >= MaxQueueDepth && _queue.TryDequeue(out _)) { }
            _queue.Enqueue(new Payload(endpoint, data, isLogs));
            _wakeSignal.Set();
        }

        /// <summary>Call when mod settings change to exit offline mode and reset circuit breaker.</summary>
        public void ResetCircuitBreaker()
        {
            _consecutiveFailures = 0;
            _retryAfter = DateTime.MinValue;
            _nextProbe = DateTime.MinValue;
            _offline = false;
        }

        private void Run()
        {
            while (!_stopping)
            {
                _wakeSignal.Wait(TimeSpan.FromSeconds(5));
                _wakeSignal.Reset();

                if (_stopping) break;

                // Offline: wait until the periodic re-probe deadline, then reopen for one attempt.
                // Unattended recovery — no need for the player to reopen Mod Settings.
                if (_offline)
                {
                    if (DateTime.UtcNow < _nextProbe) continue;
                    _offline = false; // probe window: allow the next enqueued payload(s) to try
                }

                if (DateTime.UtcNow < _retryAfter) continue;

                while (_queue.TryDequeue(out var payload))
                {
                    if (_stopping) break;
                    if (!Send(payload)) break; // stop draining this cycle on failure
                }
            }
        }

        private bool Send(Payload payload)
        {
            try
            {
                _sender.Send(payload.Endpoint, payload.Data);
                _consecutiveFailures = 0;
                _retryAfter = DateTime.MinValue;
                _offline = false;
                OnExportSuccess?.Invoke(payload.Endpoint, payload.Data.Length);
                return true;
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                int backoff = Math.Min((int)Math.Pow(2, Math.Min(_consecutiveFailures, 6)), MaxBackoffSeconds);
                _retryAfter = DateTime.UtcNow.AddSeconds(backoff);

                OnExportFailure?.Invoke(payload.Endpoint, ex.Message);
                OnWarning?.Invoke($"[OtelExporter] Export failed (attempt {_consecutiveFailures}): {ex.Message}. Retry in {backoff}s.");

                if (_consecutiveFailures >= MaxConsecutiveFailures)
                {
                    _offline = true;
                    _nextProbe = DateTime.UtcNow.AddSeconds(OfflineProbeSeconds);
                    OnWarning?.Invoke($"[OtelExporter] Too many failures — pausing exports; auto-retry in {OfflineProbeSeconds}s (or re-save mod settings).");
                }

                // Re-queue irreplaceable event logs so a transient blip / outage doesn't lose them.
                if (payload.IsLogs && _queue.Count < MaxQueueDepth)
                    _queue.Enqueue(payload);

                return false;
            }
        }

        public void Dispose()
        {
            _stopping = true;
            _wakeSignal.Set();
            _thread.Join(TimeSpan.FromSeconds(3));
            _wakeSignal.Dispose();
        }
    }
}
