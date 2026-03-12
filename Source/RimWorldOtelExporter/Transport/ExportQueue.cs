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
            public Payload(string endpoint, byte[] data) { Endpoint = endpoint; Data = data; }
        }

        private readonly ConcurrentQueue<Payload> _queue = new ConcurrentQueue<Payload>();
        private readonly OtlpHttpSender _sender;
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _wakeSignal = new ManualResetEventSlim(false);
        private volatile bool _stopping;

        // Circuit breaker state
        private int _consecutiveFailures;
        private DateTime _retryAfter = DateTime.MinValue;
        private bool _offline;

        private const int MaxConsecutiveFailures = 10;
        private const int MaxBackoffSeconds = 60;

        public event Action<string>? OnWarning;
        public event Action<string, int>? OnExportSuccess; // endpoint, payloadBytes

        public ExportQueue(OtlpHttpSender sender)
        {
            _sender = sender;
            _thread = new Thread(Run) { IsBackground = true, Name = "RimWorldOtelExporter.ExportQueue" };
            _thread.Start();
        }

        public void Enqueue(string endpoint, byte[] data)
        {
            if (_offline || _stopping) return;
            _queue.Enqueue(new Payload(endpoint, data));
            _wakeSignal.Set();
        }

        /// <summary>Call when mod settings change to exit offline mode and reset circuit breaker.</summary>
        public void ResetCircuitBreaker()
        {
            _consecutiveFailures = 0;
            _retryAfter = DateTime.MinValue;
            _offline = false;
        }

        private void Run()
        {
            while (!_stopping)
            {
                _wakeSignal.Wait(TimeSpan.FromSeconds(5));
                _wakeSignal.Reset();

                if (_stopping) break;
                if (_offline) continue;
                if (DateTime.UtcNow < _retryAfter) continue;

                while (_queue.TryDequeue(out var payload))
                {
                    if (_stopping) break;
                    Send(payload);
                }
            }
        }

        private void Send(Payload payload)
        {
            try
            {
                _sender.Send(payload.Endpoint, payload.Data);
                _consecutiveFailures = 0;
                _retryAfter = DateTime.MinValue;
                OnExportSuccess?.Invoke(payload.Endpoint, payload.Data.Length);
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                int backoff = Math.Min((int)Math.Pow(2, _consecutiveFailures), MaxBackoffSeconds);
                _retryAfter = DateTime.UtcNow.AddSeconds(backoff);

                OnWarning?.Invoke($"[OtelExporter] Export failed (attempt {_consecutiveFailures}): {ex.Message}. Retry in {backoff}s.");

                if (_consecutiveFailures >= MaxConsecutiveFailures)
                {
                    _offline = true;
                    OnWarning?.Invoke("[OtelExporter] Too many failures — switching to offline mode. Re-save mod settings to retry.");
                }
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
