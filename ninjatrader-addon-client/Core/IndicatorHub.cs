using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Data;
using NinjaTrader.Cbi;
using NT8Bridge.Core.Models;
using NT8Bridge.Util;

namespace NT8Bridge.Core
{
    public class IndicatorHub : IDisposable
    {
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, IndicatorSubscription> _subscriptions = new ConcurrentDictionary<string, IndicatorSubscription>();
        private readonly ConcurrentDictionary<string, Indicator> _defaultIndicators = new ConcurrentDictionary<string, Indicator>();
        private readonly Dictionary<string, Type> _indicatorTypes = new Dictionary<string, Type>();

        public IndicatorHub(ILogger logger)
        {
            _logger = logger;
            InitializeDefaultIndicators();
        }

        private void InitializeDefaultIndicators()
        {
            // Register default indicator types
            _indicatorTypes["SMA"] = typeof(SMA);
            _indicatorTypes["EMA"] = typeof(EMA);
            _indicatorTypes["RSI"] = typeof(RSI);
            _indicatorTypes["MACD"] = typeof(MACD);
            _indicatorTypes["VWAP"] = typeof(VWAP);
            _indicatorTypes["Bollinger"] = typeof(Bollinger);
            _indicatorTypes["ATR"] = typeof(ATR);
            _indicatorTypes["Stochastics"] = typeof(Stochastics);
            _indicatorTypes["WilliamsR"] = typeof(WilliamsR);
            _indicatorTypes["CCI"] = typeof(CCI);

            _logger.LogInformation($"Initialized {_indicatorTypes.Count} default indicators");
        }

        public void Subscribe(string clientId, SubscribeIndicatorsCommand command)
        {
            try
            {
                var subscription = GetOrCreateSubscription(command.Symbol);
                subscription.AddClient(clientId, command.Indicators);
                _logger.LogInformation($"Client {clientId} subscribed to indicators for {command.Symbol}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error subscribing client {clientId} to indicators: {ex.Message}");
                throw;
            }
        }

        public void Unsubscribe(string clientId, string symbol)
        {
            try
            {
                if (_subscriptions.TryGetValue(symbol, out var subscription))
                {
                    subscription.RemoveClient(clientId);
                    
                    if (subscription.ClientCount == 0)
                    {
                        _subscriptions.TryRemove(symbol, out _);
                        _logger.LogInformation($"Removed indicator subscription for {symbol}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error unsubscribing client {clientId} from indicators: {ex.Message}");
            }
        }

        public void OnBarUpdate(Bars bars)
        {
            try
            {
                var symbol = bars.Instrument.FullName;
                if (!_subscriptions.TryGetValue(symbol, out var subscription))
                    return;

                var indicatorValues = CalculateIndicators(symbol, bars);
                if (indicatorValues.Count > 0)
                {
                    var indicatorEvent = new IndicatorEvent
                    {
                        Symbol = symbol,
                        Indicators = indicatorValues
                    };

                    // Broadcast to all subscribed clients
                    foreach (var clientId in subscription.GetClientIds())
                    {
                        BroadcastToClientAsync(clientId, indicatorEvent);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing bar update for indicators: {ex.Message}");
            }
        }

        private Dictionary<string, double> CalculateIndicators(string symbol, Bars bars)
        {
            var values = new Dictionary<string, double>();
            var subscription = _subscriptions[symbol];

            foreach (var indicatorName in subscription.GetRequestedIndicators())
            {
                try
                {
                    var value = CalculateIndicator(symbol, indicatorName, bars);
                    if (value.HasValue)
                    {
                        values[indicatorName] = value.Value;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Error calculating {indicatorName} for {symbol}: {ex.Message}");
                }
            }

            return values;
        }

        private double? CalculateIndicator(string symbol, string indicatorName, Bars bars)
        {
            try
            {
                // Get or create indicator instance
                var indicator = GetOrCreateIndicator(symbol, indicatorName);
                if (indicator == null)
                    return null;

                // Update indicator with latest bar
                indicator.Update();

                // Get the latest value
                return GetIndicatorValue(indicator, indicatorName);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error calculating {indicatorName}: {ex.Message}");
                return null;
            }
        }

        private Indicator GetOrCreateIndicator(string symbol, string indicatorName)
        {
            var key = $"{symbol}_{indicatorName}";
            
            return _defaultIndicators.GetOrAdd(key, k =>
            {
                try
                {
                    if (!_indicatorTypes.TryGetValue(indicatorName, out var indicatorType))
                    {
                        _logger.LogWarning($"Unknown indicator type: {indicatorName}");
                        return null;
                    }

                    var instrument = Instrument.GetInstrument(symbol);
                    if (instrument == null)
                    {
                        _logger.LogWarning($"Instrument not found: {symbol}");
                        return null;
                    }

                    // Create indicator instance
                    var indicator = (Indicator)Activator.CreateInstance(indicatorType);
                    indicator.Instrument = instrument;
                    indicator.Bars = instrument.Bars;

                    // Set default parameters
                    SetDefaultParameters(indicator, indicatorName);

                    _logger.LogInformation($"Created indicator {indicatorName} for {symbol}");
                    return indicator;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error creating indicator {indicatorName}: {ex.Message}");
                    return null;
                }
            });
        }

        private void SetDefaultParameters(Indicator indicator, string indicatorName)
        {
            try
            {
                switch (indicatorName)
                {
                    case "SMA":
                        SetProperty(indicator, "Period", 14);
                        break;
                    case "EMA":
                        SetProperty(indicator, "Period", 50);
                        break;
                    case "RSI":
                        SetProperty(indicator, "Period", 14);
                        break;
                    case "MACD":
                        SetProperty(indicator, "Fast", 12);
                        SetProperty(indicator, "Slow", 26);
                        SetProperty(indicator, "Smooth", 9);
                        break;
                    case "Bollinger":
                        SetProperty(indicator, "Period", 20);
                        SetProperty(indicator, "StdDev", 2);
                        break;
                    case "ATR":
                        SetProperty(indicator, "Period", 14);
                        break;
                    case "Stochastics":
                        SetProperty(indicator, "Period", 14);
                        SetProperty(indicator, "KPeriod", 3);
                        SetProperty(indicator, "DPeriod", 3);
                        break;
                    case "WilliamsR":
                        SetProperty(indicator, "Period", 14);
                        break;
                    case "CCI":
                        SetProperty(indicator, "Period", 14);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error setting default parameters for {indicatorName}: {ex.Message}");
            }
        }

        private void SetProperty(Indicator indicator, string propertyName, object value)
        {
            try
            {
                var property = indicator.GetType().GetProperty(propertyName);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(indicator, value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error setting property {propertyName}: {ex.Message}");
            }
        }

        private double? GetIndicatorValue(Indicator indicator, string indicatorName)
        {
            try
            {
                switch (indicatorName)
                {
                    case "SMA":
                    case "EMA":
                        return indicator.GetType().GetProperty("Value")?.GetValue(indicator) as double?;
                    case "RSI":
                        return indicator.GetType().GetProperty("Value")?.GetValue(indicator) as double?;
                    case "MACD":
                        var macdValue = indicator.GetType().GetProperty("Value")?.GetValue(indicator) as double?;
                        return macdValue;
                    case "VWAP":
                        return indicator.GetType().GetProperty("Value")?.GetValue(indicator) as double?;
                    case "Bollinger":
                        var upper = indicator.GetType().GetProperty("Upper")?.GetValue(indicator) as double?;
                        var middle = indicator.GetType().GetProperty("Middle")?.GetValue(indicator) as double?;
                        var lower = indicator.GetType().GetProperty("Lower")?.GetValue(indicator) as double?;
                        return middle; // Return middle line as primary value
                    case "ATR":
                        return indicator.GetType().GetProperty("Value")?.GetValue(indicator) as double?;
                    case "Stochastics":
                        var kValue = indicator.GetType().GetProperty("K")?.GetValue(indicator) as double?;
                        return kValue;
                    case "WilliamsR":
                        return indicator.GetType().GetProperty("Value")?.GetValue(indicator) as double?;
                    case "CCI":
                        return indicator.GetType().GetProperty("Value")?.GetValue(indicator) as double?;
                    default:
                        return indicator.GetType().GetProperty("Value")?.GetValue(indicator) as double?;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error getting value for {indicatorName}: {ex.Message}");
                return null;
            }
        }

        private IndicatorSubscription GetOrCreateSubscription(string symbol)
        {
            return _subscriptions.GetOrAdd(symbol, key =>
            {
                _logger.LogInformation($"Creating new indicator subscription for {key}");
                return new IndicatorSubscription(key);
            });
        }

        private async Task BroadcastToClientAsync(string clientId, IndicatorEvent indicatorEvent)
        {
            // This would be implemented by the ExternalCommandListener
            // For now, we'll just log the event
            _logger.LogDebug($"Broadcasting indicator update to client {clientId}");
            await Task.CompletedTask;
        }

        public void RegisterCustomIndicator(string name, Type indicatorType)
        {
            try
            {
                if (!typeof(Indicator).IsAssignableFrom(indicatorType))
                {
                    throw new ArgumentException($"Type {indicatorType.Name} does not inherit from Indicator");
                }

                _indicatorTypes[name] = indicatorType;
                _logger.LogInformation($"Registered custom indicator: {name}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error registering custom indicator {name}: {ex.Message}");
                throw;
            }
        }

        public List<string> GetAvailableIndicators()
        {
            return _indicatorTypes.Keys.ToList();
        }

        public void Dispose()
        {
            try
            {
                foreach (var indicator in _defaultIndicators.Values)
                {
                    indicator?.Dispose();
                }
                _defaultIndicators.Clear();
                _subscriptions.Clear();
                _logger.LogInformation("IndicatorHub disposed");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error disposing IndicatorHub: {ex.Message}");
            }
        }

        private class IndicatorSubscription
        {
            private readonly string _symbol;
            private readonly ConcurrentDictionary<string, HashSet<string>> _clientIndicators = new ConcurrentDictionary<string, HashSet<string>>();

            public IndicatorSubscription(string symbol)
            {
                _symbol = symbol;
            }

            public int ClientCount => _clientIndicators.Count;

            public void AddClient(string clientId, List<string> indicators)
            {
                _clientIndicators.TryAdd(clientId, new HashSet<string>(indicators));
            }

            public void RemoveClient(string clientId)
            {
                _clientIndicators.TryRemove(clientId, out _);
            }

            public HashSet<string> GetClientIds()
            {
                return new HashSet<string>(_clientIndicators.Keys);
            }

            public HashSet<string> GetRequestedIndicators()
            {
                var allIndicators = new HashSet<string>();
                foreach (var indicators in _clientIndicators.Values)
                {
                    foreach (var indicator in indicators)
                    {
                        allIndicators.Add(indicator);
                    }
                }
                return allIndicators;
            }
        }
    }
} 