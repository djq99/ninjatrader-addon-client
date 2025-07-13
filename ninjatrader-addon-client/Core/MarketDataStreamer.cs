using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NinjaTrader.Data;
using NinjaTrader.Cbi;
using NT8Bridge.Core.Models;
using NT8Bridge.Util;

namespace NT8Bridge.Core
{
    public class MarketDataStreamer : IDisposable
    {
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, MarketDataSubscription> _subscriptions = new ConcurrentDictionary<string, MarketDataSubscription>();
        private readonly ConcurrentDictionary<string, HashSet<string>> _symbolToClients = new ConcurrentDictionary<string, HashSet<string>>();
        private readonly RingBuffer<MarketDataFrame> _ringBuffer;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly Task _processingTask;
        private readonly object _lockObject = new object();

        private const int RING_BUFFER_SIZE = 10000;
        private const int MAX_LATENCY_MS = 100;

        public MarketDataStreamer(ILogger logger)
        {
            _logger = logger;
            _ringBuffer = new RingBuffer<MarketDataFrame>(RING_BUFFER_SIZE);
            _cancellationTokenSource = new CancellationTokenSource();
            _processingTask = Task.Run(ProcessMarketDataAsync);
        }

        public void Subscribe(string clientId, SubscribeMarketDataCommand command)
        {
            try
            {
                foreach (var symbol in command.Symbols)
                {
                    var subscription = GetOrCreateSubscription(symbol);
                    subscription.AddClient(clientId, command.IncludeDepth);
                    
                    // Track client subscriptions
                    _symbolToClients.AddOrUpdate(symbol, 
                        new HashSet<string> { clientId },
                        (key, existing) => { existing.Add(clientId); return existing; });

                    _logger.LogInformation($"Client {clientId} subscribed to {symbol}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error subscribing client {clientId}: {ex.Message}");
                throw;
            }
        }

        public void Unsubscribe(string clientId, UnsubscribeMarketDataCommand command)
        {
            try
            {
                foreach (var symbol in command.Symbols)
                {
                    if (_subscriptions.TryGetValue(symbol, out var subscription))
                    {
                        subscription.RemoveClient(clientId);
                        
                        if (subscription.ClientCount == 0)
                        {
                            _subscriptions.TryRemove(symbol, out _);
                            _symbolToClients.TryRemove(symbol, out _);
                            _logger.LogInformation($"Removed subscription for {symbol}");
                        }
                    }

                    // Remove from tracking
                    if (_symbolToClients.TryGetValue(symbol, out var clients))
                    {
                        clients.Remove(clientId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error unsubscribing client {clientId}: {ex.Message}");
            }
        }

        public void OnMarketData(MarketDataEventArgs e)
        {
            try
            {
                if (!_subscriptions.TryGetValue(e.Instrument.FullName, out var subscription))
                    return;

                var frame = new MarketDataFrame
                {
                    Type = MarketDataType.Tick,
                    Symbol = e.Instrument.FullName,
                    Timestamp = DateTime.UtcNow,
                    Bid = e.Bid,
                    Ask = e.Ask,
                    Last = e.Last,
                    Volume = e.Volume,
                    BidSize = e.BidSize,
                    AskSize = e.AskSize,
                    ClientIds = subscription.GetClientIds()
                };

                _ringBuffer.Write(frame);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing market data for {e.Instrument?.FullName}: {ex.Message}");
            }
        }

        public void OnMarketDepth(MarketDepthEventArgs e)
        {
            try
            {
                if (!_subscriptions.TryGetValue(e.Instrument.FullName, out var subscription))
                    return;

                // Only send depth data to clients that requested it
                var depthClients = subscription.GetDepthClientIds();
                if (depthClients.Count == 0)
                    return;

                var frame = new MarketDataFrame
                {
                    Type = MarketDataType.Depth,
                    Symbol = e.Instrument.FullName,
                    Timestamp = DateTime.UtcNow,
                    Side = e.MarketDataType == MarketDataType.Ask ? "Ask" : "Bid",
                    Price = e.Price,
                    Size = e.Size,
                    Level = e.Level,
                    Action = e.Action.ToString(),
                    ClientIds = depthClients
                };

                _ringBuffer.Write(frame);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing market depth for {e.Instrument?.FullName}: {ex.Message}");
            }
        }

        private MarketDataSubscription GetOrCreateSubscription(string symbol)
        {
            return _subscriptions.GetOrAdd(symbol, key =>
            {
                _logger.LogInformation($"Creating new market data subscription for {key}");
                return new MarketDataSubscription(key);
            });
        }

        private async Task ProcessMarketDataAsync()
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    if (_ringBuffer.TryRead(out var frame))
                    {
                        await ProcessFrameAsync(frame);
                    }
                    else
                    {
                        await Task.Delay(1, _cancellationTokenSource.Token); // 1ms delay
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error in market data processing loop: {ex.Message}");
                    await Task.Delay(100, _cancellationTokenSource.Token);
                }
            }
        }

        private async Task ProcessFrameAsync(MarketDataFrame frame)
        {
            try
            {
                var latency = (DateTime.UtcNow - frame.Timestamp).TotalMilliseconds;
                
                if (latency > MAX_LATENCY_MS)
                {
                    _logger.LogWarning($"High latency detected: {latency:F2}ms for {frame.Symbol}");
                }

                // Convert frame to appropriate event
                BaseEvent eventData = frame.Type switch
                {
                    MarketDataType.Tick => new TickEvent
                    {
                        Symbol = frame.Symbol,
                        Bid = frame.Bid,
                        Ask = frame.Ask,
                        Last = frame.Last,
                        Volume = frame.Volume,
                        BidSize = frame.BidSize,
                        AskSize = frame.AskSize
                    },
                    MarketDataType.Depth => new DepthEvent
                    {
                        Symbol = frame.Symbol,
                        Side = frame.Side,
                        Price = frame.Price,
                        Size = frame.Size,
                        Level = frame.Level,
                        Action = frame.Action
                    },
                    _ => null
                };

                if (eventData != null)
                {
                    // Broadcast to all subscribed clients
                    foreach (var clientId in frame.ClientIds)
                    {
                        await BroadcastToClientAsync(clientId, eventData);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing market data frame: {ex.Message}");
            }
        }

        private async Task BroadcastToClientAsync(string clientId, BaseEvent eventData)
        {
            // This would be implemented by the ExternalCommandListener
            // For now, we'll just log the event
            _logger.LogDebug($"Broadcasting {eventData.Event} to client {clientId}");
            await Task.CompletedTask;
        }

        public List<string> GetSubscribedSymbols()
        {
            return _subscriptions.Keys.ToList();
        }

        public int GetClientCount(string symbol)
        {
            return _subscriptions.TryGetValue(symbol, out var subscription) ? subscription.ClientCount : 0;
        }

        public void Stop()
        {
            try
            {
                _cancellationTokenSource.Cancel();
                _processingTask?.Wait(TimeSpan.FromSeconds(5));
                _logger.LogInformation("MarketDataStreamer stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error stopping MarketDataStreamer: {ex.Message}");
            }
        }

        public void Dispose()
        {
            try
            {
                Stop();
                _cancellationTokenSource?.Dispose();
                _ringBuffer?.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error disposing MarketDataStreamer: {ex.Message}");
            }
        }

        private enum MarketDataType
        {
            Tick,
            Depth
        }

        private class MarketDataFrame
        {
            public MarketDataType Type { get; set; }
            public string Symbol { get; set; }
            public DateTime Timestamp { get; set; }
            public double Bid { get; set; }
            public double Ask { get; set; }
            public double Last { get; set; }
            public long Volume { get; set; }
            public int BidSize { get; set; }
            public int AskSize { get; set; }
            public string Side { get; set; }
            public double Price { get; set; }
            public int Size { get; set; }
            public int Level { get; set; }
            public string Action { get; set; }
            public HashSet<string> ClientIds { get; set; }
        }

        private class MarketDataSubscription
        {
            private readonly string _symbol;
            private readonly ConcurrentDictionary<string, bool> _clients = new ConcurrentDictionary<string, bool>();
            private readonly ConcurrentDictionary<string, bool> _depthClients = new ConcurrentDictionary<string, bool>();

            public MarketDataSubscription(string symbol)
            {
                _symbol = symbol;
            }

            public int ClientCount => _clients.Count;

            public void AddClient(string clientId, bool includeDepth)
            {
                _clients.TryAdd(clientId, true);
                if (includeDepth)
                {
                    _depthClients.TryAdd(clientId, true);
                }
            }

            public void RemoveClient(string clientId)
            {
                _clients.TryRemove(clientId, out _);
                _depthClients.TryRemove(clientId, out _);
            }

            public HashSet<string> GetClientIds()
            {
                return new HashSet<string>(_clients.Keys);
            }

            public HashSet<string> GetDepthClientIds()
            {
                return new HashSet<string>(_depthClients.Keys);
            }
        }
    }
} 