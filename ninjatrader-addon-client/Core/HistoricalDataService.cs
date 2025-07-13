using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NinjaTrader.Data;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using Microsoft.Extensions.Caching.Memory;
using NT8Bridge.Core.Models;
using NT8Bridge.Util;

namespace NT8Bridge.Core
{
    public class HistoricalDataService : IDisposable
    {
        private readonly ILogger _logger;
        private readonly MemoryCache _cache;
        private readonly MemoryCacheEntryOptions _cacheOptions;
        private readonly Dictionary<string, BarsRequest> _activeRequests = new Dictionary<string, BarsRequest>();

        public HistoricalDataService(ILogger logger)
        {
            _logger = logger;
            _cache = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = 500 * 1024 * 1024, // 500 MB
                ExpirationScanFrequency = TimeSpan.FromMinutes(5)
            });

            _cacheOptions = new MemoryCacheEntryOptions
            {
                Size = 1024 * 1024, // 1 MB per entry
                SlidingExpiration = TimeSpan.FromHours(1),
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
            };
        }

        public async Task<HistoryChunkEvent> GetHistoryAsync(FetchHistoryCommand command)
        {
            try
            {
                var cacheKey = GenerateCacheKey(command);
                
                // Check cache first
                if (_cache.TryGetValue(cacheKey, out List<BarData> cachedBars))
                {
                    _logger.LogInformation($"Cache hit for {command.Symbol} {command.Level}");
                    return new HistoryChunkEvent
                    {
                        RequestId = command.RequestId,
                        Symbol = command.Symbol,
                        Level = command.Level,
                        Bars = cachedBars,
                        IsComplete = true
                    };
                }

                // Create bars request
                var barsRequest = new BarsRequest
                {
                    Instrument = GetInstrument(command.Symbol),
                    BarsPeriod = GetBarsPeriod(command.Level),
                    StartTime = command.From,
                    EndTime = command.To,
                    MaxBars = command.MaxBars ?? 10000
                };

                // Store active request
                _activeRequests[command.RequestId.ToString()] = barsRequest;

                var bars = await GetBarsAsync(barsRequest);
                var barDataList = ConvertToBarData(bars);

                // Cache the result
                _cache.Set(cacheKey, barDataList, _cacheOptions);

                // Clean up active request
                _activeRequests.Remove(command.RequestId.ToString());

                return new HistoryChunkEvent
                {
                    RequestId = command.RequestId,
                    Symbol = command.Symbol,
                    Level = command.Level,
                    Bars = barDataList,
                    IsComplete = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error fetching historical data for {command.Symbol}: {ex.Message}");
                return new HistoryChunkEvent
                {
                    RequestId = command.RequestId,
                    Symbol = command.Symbol,
                    Level = command.Level,
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        private Instrument GetInstrument(string symbol)
        {
            try
            {
                var instrument = Instrument.GetInstrument(symbol);
                if (instrument == null)
                {
                    throw new ArgumentException($"Instrument not found: {symbol}");
                }
                return instrument;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting instrument for {symbol}: {ex.Message}");
                throw;
            }
        }

        private BarsPeriod GetBarsPeriod(string level)
        {
            return level.ToLower() switch
            {
                "tick" => BarsPeriod.Tick,
                "second" => BarsPeriod.Second,
                "minute" => BarsPeriod.Minute,
                "day" => BarsPeriod.Day,
                "week" => BarsPeriod.Week,
                "month" => BarsPeriod.Month,
                "year" => BarsPeriod.Year,
                _ => throw new ArgumentException($"Unsupported bar level: {level}")
            };
        }

        private async Task<Bars> GetBarsAsync(BarsRequest request)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var bars = request.GetBars();
                    if (bars == null || bars.Count == 0)
                    {
                        throw new InvalidOperationException($"No bars returned for {request.Instrument.FullName}");
                    }
                    return bars;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error getting bars: {ex.Message}");
                    throw;
                }
            });
        }

        private List<BarData> ConvertToBarData(Bars bars)
        {
            var barDataList = new List<BarData>();
            
            for (int i = 0; i < bars.Count; i++)
            {
                var bar = bars.GetBar(i);
                barDataList.Add(new BarData
                {
                    Time = bar.Time,
                    Open = bar.Open,
                    High = bar.High,
                    Low = bar.Low,
                    Close = bar.Close,
                    Volume = bar.Volume
                });
            }

            return barDataList;
        }

        private string GenerateCacheKey(FetchHistoryCommand command)
        {
            return $"{command.Symbol}_{command.Level}_{command.From:yyyyMMddHHmmss}_{command.To:yyyyMMddHHmmss}_{command.MaxBars}";
        }

        public void CancelRequest(int requestId)
        {
            if (_activeRequests.TryGetValue(requestId.ToString(), out var request))
            {
                try
                {
                    request.Cancel();
                    _activeRequests.Remove(requestId.ToString());
                    _logger.LogInformation($"Cancelled historical data request {requestId}");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error cancelling request {requestId}: {ex.Message}");
                }
            }
        }

        public void ClearCache()
        {
            _cache.Clear();
            _logger.LogInformation("Historical data cache cleared");
        }

        public void Dispose()
        {
            try
            {
                // Cancel all active requests
                foreach (var request in _activeRequests.Values)
                {
                    request?.Cancel();
                }
                _activeRequests.Clear();

                // Dispose cache
                _cache?.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error disposing HistoricalDataService: {ex.Message}");
            }
        }
    }
} 