using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace YTShortsManager.Services
{
    /// <summary>
    /// Кэш длительности видео + ограничение "одна операция одновременно".
    /// Раньше каждый запрос запускал yt-dlp и блокировал HTTP-сервер.
    /// </summary>
    public sealed class VideoInfoCache
    {
        private readonly record struct Entry(int Duration, DateTime ExpiresUtc);

        private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

        /// <summary>Верхняя граница кэша: без неё любое расширение/скрипт может раздуть память.</summary>
        private const int MaxEntries = 200;

        private readonly Dictionary<string, Entry> _cache = new(StringComparer.Ordinal);
        private readonly SemaphoreSlim _oneAtATime = new(1, 1);

        public async Task<int> GetDurationAsync(
            string url,
            Func<CancellationToken, Task<int>> factory,
            CancellationToken cancellationToken)
        {
            lock (_cache)
            {
                if (_cache.TryGetValue(url, out Entry entry) && entry.ExpiresUtc > DateTime.UtcNow)
                    return entry.Duration;
            }

            await _oneAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (_cache)
                {
                    if (_cache.TryGetValue(url, out Entry entry) && entry.ExpiresUtc > DateTime.UtcNow)
                        return entry.Duration;
                }

                int duration = await factory(cancellationToken).ConfigureAwait(false);

                if (duration > 0)
                {
                    lock (_cache)
                    {
                        if (_cache.Count >= MaxEntries) EvictExpiredOrOldest();

                        // Если после очистки место всё ещё занято, не пускаем новую запись,
                        // чтобы размер не рос бесконечно.
                        if (_cache.Count < MaxEntries)
                            _cache[url] = new Entry(duration, DateTime.UtcNow + Ttl);
                    }
                }

                return duration;
            }
            finally
            {
                _oneAtATime.Release();
            }
        }

        /// <summary>Сначала выкидывает истёкшие записи, затем самую давнюю. Вызывать под lock(_cache).</summary>
        private void EvictExpiredOrOldest()
        {
            DateTime now = DateTime.UtcNow;

            foreach (string key in _cache
                         .Where(pair => pair.Value.ExpiresUtc <= now)
                         .Select(pair => pair.Key)
                         .ToList())
            {
                _cache.Remove(key);
            }

            if (_cache.Count < MaxEntries) return;

            string? oldestKey = null;
            DateTime oldestExpiry = DateTime.MaxValue;

            foreach (KeyValuePair<string, Entry> pair in _cache)
            {
                if (pair.Value.ExpiresUtc >= oldestExpiry) continue;
                oldestExpiry = pair.Value.ExpiresUtc;
                oldestKey = pair.Key;
            }

            if (oldestKey != null) _cache.Remove(oldestKey);
        }
    }
}
