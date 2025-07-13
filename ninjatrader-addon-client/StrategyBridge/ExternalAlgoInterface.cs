using System;
using System.Collections.Generic;
using NinjaTrader.Data;
using NinjaTrader.Cbi;

namespace NT8Bridge.StrategyBridge
{
    /// <summary>
    /// Interface that external algorithms must implement to be compatible with the Strategy Bridge.
    /// </summary>
    public interface IExternalAlgo
    {
        /// <summary>
        /// Called when the strategy is initialized.
        /// </summary>
        /// <param name="strategy">The NinjaTrader strategy instance</param>
        void Initialize(object strategy);

        /// <summary>
        /// Called on each bar update.
        /// </summary>
        /// <param name="bars">The bars data</param>
        void OnBar(Bars bars);

        /// <summary>
        /// Called on each tick/market data update.
        /// </summary>
        /// <param name="marketData">Market data event</param>
        void OnTick(object marketData);

        /// <summary>
        /// Called when an order is updated.
        /// </summary>
        /// <param name="order">The order</param>
        /// <param name="avgPrice">Average price</param>
        /// <param name="quantity">Quantity</param>
        /// <param name="position">Market position</param>
        /// <param name="orderId">Order ID</param>
        /// <param name="time">Timestamp</param>
        void OnOrderUpdate(Order order, double avgPrice, int quantity, MarketPosition position, string orderId, DateTime time);

        /// <summary>
        /// Called when the strategy is terminated.
        /// </summary>
        void OnTermination();

        /// <summary>
        /// Gets the algorithm name.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the algorithm version.
        /// </summary>
        string Version { get; }

        /// <summary>
        /// Gets the algorithm description.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Gets the algorithm parameters.
        /// </summary>
        Dictionary<string, object> Parameters { get; }
    }

    /// <summary>
    /// Factory for creating external algorithm instances.
    /// </summary>
    public static class ExternalAlgoFactory
    {
        /// <summary>
        /// Creates an external algorithm instance by name.
        /// </summary>
        /// <param name="algoName">The algorithm name</param>
        /// <param name="paramJson">JSON string containing parameters</param>
        /// <returns>The algorithm instance</returns>
        public static IExternalAlgo Create(string algoName, string paramJson)
        {
            try
            {
                // Load the external DLL
                var assemblyPath = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(typeof(ExternalAlgoFactory).Assembly.Location),
                    "ExternalAlgo.dll");

                if (!System.IO.File.Exists(assemblyPath))
                {
                    throw new InvalidOperationException($"ExternalAlgo.dll not found at: {assemblyPath}");
                }

                var assembly = System.Reflection.Assembly.LoadFrom(assemblyPath);
                
                // Find the algorithm type
                var algoType = assembly.GetType(algoName) ?? 
                              assembly.GetType($"ExternalAlgo.{algoName}") ??
                              assembly.GetType($"Algorithms.{algoName}");

                if (algoType == null)
                {
                    throw new InvalidOperationException($"Algorithm type '{algoName}' not found in ExternalAlgo.dll");
                }

                // Create instance
                var instance = Activator.CreateInstance(algoType) as IExternalAlgo;
                if (instance == null)
                {
                    throw new InvalidOperationException($"Type '{algoName}' does not implement IExternalAlgo");
                }

                // Set parameters if provided
                if (!string.IsNullOrEmpty(paramJson))
                {
                    SetParameters(instance, paramJson);
                }

                return instance;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to create external algorithm '{algoName}': {ex.Message}", ex);
            }
        }

        private static void SetParameters(IExternalAlgo algo, string paramJson)
        {
            try
            {
                var parameters = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(paramJson);
                if (parameters != null)
                {
                    // Use reflection to set properties
                    var type = algo.GetType();
                    foreach (var param in parameters)
                    {
                        var property = type.GetProperty(param.Key);
                        if (property != null && property.CanWrite)
                        {
                            var value = Convert.ChangeType(param.Value, property.PropertyType);
                            property.SetValue(algo, value);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to set parameters: {ex.Message}", ex);
            }
        }
    }
} 