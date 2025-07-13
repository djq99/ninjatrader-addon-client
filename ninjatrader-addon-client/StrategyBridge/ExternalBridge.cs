using System;
using System.Collections.Generic;
using NinjaTrader.NinjaScript;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NT8Bridge.StrategyBridge;

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// External Bridge Strategy - Allows external algorithms to be back-tested in NinjaTrader's Strategy Analyzer.
    /// This strategy acts as a wrapper around external algorithms loaded from ExternalAlgo.dll.
    /// </summary>
    public class ExternalBridge : Strategy
    {
        [NinjaScriptProperty]
        [Display(Name = "Algorithm Name", Description = "Name of the external algorithm to load", Order = 1, GroupName = "Parameters")]
        public string AlgoName { get; set; } = "DefaultAlgo";

        [NinjaScriptProperty]
        [Display(Name = "Parameters JSON", Description = "JSON string containing algorithm parameters", Order = 2, GroupName = "Parameters")]
        public string ParamJson { get; set; } = "{}";

        [NinjaScriptProperty]
        [Display(Name = "Enable Logging", Description = "Enable detailed logging of algorithm calls", Order = 3, GroupName = "Parameters")]
        public bool EnableLogging { get; set; } = false;

        private IExternalAlgo _algo;
        private bool _isInitialized = false;

        protected override void OnStateChange()
        {
            try
            {
                switch (State)
                {
                    case State.Configure:
                        // Load the external algorithm
                        LoadExternalAlgorithm();
                        break;

                    case State.DataLoaded:
                        // Initialize the algorithm
                        InitializeAlgorithm();
                        break;

                    case State.Terminated:
                        // Clean up
                        TerminateAlgorithm();
                        break;
                }
            }
            catch (Exception ex)
            {
                Print($"Error in OnStateChange: {ex.Message}");
                if (EnableLogging)
                {
                    Print($"Stack trace: {ex.StackTrace}");
                }
            }
        }

        protected override void OnBarUpdate()
        {
            try
            {
                if (_algo != null && _isInitialized)
                {
                    if (EnableLogging)
                    {
                        Print($"OnBarUpdate called for {AlgoName} at {Time}");
                    }

                    _algo.OnBar(BarsArray[0]);
                }
            }
            catch (Exception ex)
            {
                Print($"Error in OnBarUpdate: {ex.Message}");
                if (EnableLogging)
                {
                    Print($"Stack trace: {ex.StackTrace}");
                }
            }
        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            try
            {
                if (_algo != null && _isInitialized)
                {
                    if (EnableLogging)
                    {
                        Print($"OnMarketData called for {AlgoName} at {e.Time}");
                    }

                    _algo.OnTick(e);
                }
            }
            catch (Exception ex)
            {
                Print($"Error in OnMarketData: {ex.Message}");
                if (EnableLogging)
                {
                    Print($"Stack trace: {ex.StackTrace}");
                }
            }
        }

        public override void OnOrderUpdate(Order order, double avgPrice, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            try
            {
                if (_algo != null && _isInitialized)
                {
                    if (EnableLogging)
                    {
                        Print($"OnOrderUpdate called for {AlgoName} - Order: {orderId}, State: {order.OrderState}");
                    }

                    _algo.OnOrderUpdate(order, avgPrice, quantity, marketPosition, orderId, time);
                }
            }
            catch (Exception ex)
            {
                Print($"Error in OnOrderUpdate: {ex.Message}");
                if (EnableLogging)
                {
                    Print($"Stack trace: {ex.StackTrace}");
                }
            }
        }

        private void LoadExternalAlgorithm()
        {
            try
            {
                if (string.IsNullOrEmpty(AlgoName))
                {
                    throw new InvalidOperationException("Algorithm name is required");
                }

                Print($"Loading external algorithm: {AlgoName}");
                _algo = ExternalAlgoFactory.Create(AlgoName, ParamJson);
                
                Print($"Successfully loaded algorithm: {_algo.Name} v{_algo.Version}");
                Print($"Description: {_algo.Description}");
                
                if (EnableLogging)
                {
                    Print($"Parameters: {ParamJson}");
                }
            }
            catch (Exception ex)
            {
                Print($"Failed to load external algorithm '{AlgoName}': {ex.Message}");
                throw;
            }
        }

        private void InitializeAlgorithm()
        {
            try
            {
                if (_algo == null)
                {
                    throw new InvalidOperationException("External algorithm not loaded");
                }

                Print($"Initializing algorithm: {_algo.Name}");
                _algo.Initialize(this);
                _isInitialized = true;
                
                Print($"Algorithm '{_algo.Name}' initialized successfully");
            }
            catch (Exception ex)
            {
                Print($"Failed to initialize algorithm: {ex.Message}");
                throw;
            }
        }

        private void TerminateAlgorithm()
        {
            try
            {
                if (_algo != null && _isInitialized)
                {
                    Print($"Terminating algorithm: {_algo.Name}");
                    _algo.OnTermination();
                    _algo = null;
                    _isInitialized = false;
                    Print("Algorithm terminated successfully");
                }
            }
            catch (Exception ex)
            {
                Print($"Error terminating algorithm: {ex.Message}");
            }
        }

        protected override void OnTermination()
        {
            try
            {
                TerminateAlgorithm();
                base.OnTermination();
            }
            catch (Exception ex)
            {
                Print($"Error in OnTermination: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the current algorithm instance (for debugging/testing).
        /// </summary>
        public IExternalAlgo CurrentAlgorithm => _algo;

        /// <summary>
        /// Gets whether the algorithm is initialized.
        /// </summary>
        public bool IsAlgorithmInitialized => _isInitialized;
    }
} 