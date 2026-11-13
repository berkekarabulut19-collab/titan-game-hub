using System;
using System.Collections.Concurrent;
using System.Threading;

namespace TitanGameHub
{
    /// <summary>
    /// Basit arka plan iş kuyruğu: indirme, tarama, güncelleme gibi işleri sıraya alır.
    /// </summary>
    public class BackgroundJob
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public Action Work { get; set; }
        public DateTime EnqueuedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public bool Succeeded { get; set; }
        public string Error { get; set; }
    }

    public static class BackgroundJobQueue
    {
        private static readonly ConcurrentQueue<BackgroundJob> _queue = new ConcurrentQueue<BackgroundJob>();
        private static int _running = 0;
        private static int _workerCount = 2;

        public static string Enqueue(string name, Action work)
        {
            var job = new BackgroundJob
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 10),
                Name = name ?? "Job",
                Work = work,
                EnqueuedAt = DateTime.UtcNow
            };
            _queue.Enqueue(job);
            EnsureWorkers();
            return job.Id;
        }

        private static void EnsureWorkers()
        {
            for (int i = 0; i < _workerCount; i++)
            {
                ThreadPool.QueueUserWorkItem(delegate { WorkerLoop(); });
            }
        }

        private static void WorkerLoop()
        {
            try
            {
                Interlocked.Increment(ref _running);
                while (true)
                {
                    BackgroundJob job;
                    if (!_queue.TryDequeue(out job))
                    {
                        Thread.Sleep(100);
                        if (_queue.IsEmpty) break;
                        continue;
                    }
                    try
                    {
                        job.StartedAt = DateTime.UtcNow;
                        if (job.Work != null) job.Work();
                        job.Succeeded = true;
                    }
                    catch (Exception ex)
                    {
                        job.Succeeded = false;
                        job.Error = ex.Message;
                    }
                    finally
                    {
                        job.FinishedAt = DateTime.UtcNow;
                    }
                }
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }

        public static int PendingCount { get { return _queue.Count; } }
        public static int RunningCount { get { return _running; } }
    }
}
