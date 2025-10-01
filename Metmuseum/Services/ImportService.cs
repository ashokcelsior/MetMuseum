using Metmuseum.Data;
using Metmuseum.Models;
using Metmuseum.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Metmuseum.Services
{
    public class ImportService
    {
        private readonly IMetMuseumService _metMuseumService;
        private readonly ILogger<ImportService> _logger;
        private readonly int _batchSize;
        private readonly object _flushLock = new();
        private readonly ConcurrentBag<MetMuseumObject> _buffer = new();
        private int _processed = 0;
        private int _total = 0;

        public ImportService(IMetMuseumService metMuseumService, ILogger<ImportService> logger, int batchSize = 50)
        {
            _metMuseumService = metMuseumService;
            _logger = logger;
            _batchSize = batchSize;
        }

        public async Task RunAsync(int parallelism = 5, CancellationToken ct = default)
        {
            try
            {
                using (var context = CreateDbContext())
                {
                    await context.Database.EnsureCreatedAsync(ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing database.");
                return;
            }

            IReadOnlyList<int> ids;
            try
            {
                _logger.LogInformation("Fetching all object IDs...");
                ids = await _metMuseumService.GetAllIdsAsync(ct);
                _logger.LogInformation("Total IDs: {count}", ids.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch object IDs. Exiting...");
                return;
            }

            _total = ids.Count;

            using var semaphore = new SemaphoreSlim(parallelism);
            var tasks = new List<Task>();

            foreach (var id in ids)
            {
                await semaphore.WaitAsync(ct);

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await ProcessIdAsync(id, ct);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing object {id}", id);
                        IncrementProgress();
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, ct));
            }

            await Task.WhenAll(tasks);

            await FlushBufferAsync(ct);
            Console.WriteLine();
            _logger.LogInformation("Import finished.");
        }

        private async Task ProcessIdAsync(int id, CancellationToken ct)
        {
            try
            {
                var obj = await _metMuseumService.GetObjectAsync(id, ct);

                if (obj == null)
                {
                    _logger.LogWarning("Skipping object {id} after retries.", id);
                    IncrementProgress();
                    return;
                }

                var cleaned = JsonUtils.CleanForStorage(obj, "additionalImages", "constituents", "measurements");

                var metMuseumObject = new MetMuseumObject
                {
                    ObjectID = id,
                    Title = cleaned["title"]?.ToString(),
                    JsonData = cleaned.ToString(Newtonsoft.Json.Formatting.None),
                    RetrievedAt = DateTimeOffset.UtcNow
                };

                _buffer.Add(metMuseumObject);

                if (_buffer.Count >= _batchSize)
                {
                    try
                    {
                        await FlushBufferAsync(ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error flushing buffer.");
                    }
                }

                IncrementProgress();

                await Task.Delay(Random.Shared.Next(100, 500), ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing object {id}", id);
                IncrementProgress();
            }
        }

        private async Task FlushBufferAsync(CancellationToken ct)
        {
            if (_buffer.IsEmpty) return;

            MetMuseumObject[] items;
            lock (_flushLock)
            {
                if (_buffer.IsEmpty) return;
                items = _buffer.ToArray();
                _buffer.Clear();
            }

            try
            {
                using var context = CreateDbContext();

                var ids = items.Select(x => x.ObjectID).ToArray();
                var existingObjects = await context.MetObjects
                    .Where(x => ids.Contains(x.ObjectID))
                    .ToDictionaryAsync(x => x.ObjectID, ct);

                foreach (var obj in items)
                {
                    if (existingObjects.TryGetValue(obj.ObjectID, out var existing))
                    {
                        existing.Title = obj.Title;
                        existing.JsonData = obj.JsonData;
                        existing.RetrievedAt = obj.RetrievedAt;
                    }
                    else
                    {
                        await context.MetObjects.AddAsync(obj, ct);
                    }
                }

                await context.SaveChangesAsync(ct);
                _logger.LogInformation("Flushed {count} objects to DB", items.Length);
            }
            catch (Exception ex)
            {
                lock (_flushLock)
                {
                    foreach (var item in items)
                        _buffer.Add(item);
                }
                _logger.LogError(ex, "Error flushing buffer, items re-added to buffer.");
            }
        }


        private void IncrementProgress()
        {
            var done = Interlocked.Increment(ref _processed);
            try
            {
                DrawProgressBar(done, _total);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to draw progress bar.");
            }
        }

        private static void DrawProgressBar(int progress, int total, int barSize = 50)
        {
            if (Console.IsOutputRedirected) return;
            double pct = (double)progress / total;
            int chars = (int)(pct * barSize);
            Console.CursorLeft = 0;
            Console.Write($"[{new string('#', chars)}{new string('-', barSize - chars)}] {progress}/{total}");
        }

        protected virtual MetMuseumContext CreateDbContext()
        {
            return new MetMuseumContext();
        }
    }
}
