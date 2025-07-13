using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NinjaTrader.Gui;
using NinjaTrader.Gui.AddOns;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Core;
using NT8Bridge.Core;
using NT8Bridge.Util;

namespace NT8Bridge.Addon
{
    [AddOn("NT8Bridge", "NinjaTrader 8 Bridge Add-On", "High-performance data & execution gateway for external strategies")]
    public class NT8BridgeAddon : IAddOn
    {
        private static NT8BridgeAddon _instance;
        private ExternalCommandListener _commandListener;
        private HistoricalDataService _historicalDataService;
        private MarketDataStreamer _marketDataStreamer;
        private IndicatorHub _indicatorHub;
        private AccountBridge _accountBridge;
        private OrderRouter _orderRouter;
        private ConfigWindow _configWindow;
        private ILogger _logger;

        public static NT8BridgeAddon Instance => _instance;

        public void OnStartup()
        {
            try
            {
                _instance = this;
                _logger = new ConsoleLogger();
                _logger.LogInformation("NT8Bridge Add-On starting...");

                // Initialize core services
                InitializeServices();

                // Register with NinjaTrader
                RegisterWithNinjaTrader();

                _logger.LogInformation("NT8Bridge Add-On started successfully");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Failed to start NT8Bridge Add-On: {ex.Message}");
                throw;
            }
        }

        public void OnShutdown()
        {
            try
            {
                _logger?.LogInformation("NT8Bridge Add-On shutting down...");

                // Stop all services
                _commandListener?.Stop();
                _marketDataStreamer?.Stop();
                _historicalDataService?.Dispose();
                _accountBridge?.Dispose();
                _orderRouter?.Dispose();
                _indicatorHub?.Dispose();

                _logger?.LogInformation("NT8Bridge Add-On shutdown complete");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error during shutdown: {ex.Message}");
            }
        }

        private void InitializeServices()
        {
            // Initialize core services in dependency order
            _historicalDataService = new HistoricalDataService(_logger);
            _marketDataStreamer = new MarketDataStreamer(_logger);
            _indicatorHub = new IndicatorHub(_logger);
            _accountBridge = new AccountBridge(_logger);
            _orderRouter = new OrderRouter(_logger);

            // Initialize command listener last (depends on other services)
            _commandListener = new ExternalCommandListener(_logger);
            _commandListener.RegisterServices(_historicalDataService, _marketDataStreamer, 
                _indicatorHub, _accountBridge, _orderRouter);
        }

        private void RegisterWithNinjaTrader()
        {
            // Register market data handlers
            NinjaTrader.Core.Globals.MarketDataUpdate += OnMarketDataUpdate;
            NinjaTrader.Core.Globals.MarketDepthUpdate += OnMarketDepthUpdate;

            // Register account handlers
            if (NinjaTrader.Core.Globals.Accounts != null)
            {
                foreach (var account in NinjaTrader.Core.Globals.Accounts)
                {
                    account.AccountItemUpdate += OnAccountItemUpdate;
                    account.ExecutionUpdate += OnExecutionUpdate;
                    account.PositionUpdate += OnPositionUpdate;
                }
            }

            // Register order handlers
            NinjaTrader.Core.Globals.OrderUpdate += OnOrderUpdate;
        }

        private void OnMarketDataUpdate(object sender, MarketDataEventArgs e)
        {
            try
            {
                _marketDataStreamer?.OnMarketData(e);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error in market data update: {ex.Message}");
            }
        }

        private void OnMarketDepthUpdate(object sender, MarketDepthEventArgs e)
        {
            try
            {
                _marketDataStreamer?.OnMarketDepth(e);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error in market depth update: {ex.Message}");
            }
        }

        private void OnAccountItemUpdate(object sender, AccountItemEventArgs e)
        {
            try
            {
                _accountBridge?.OnAccountItemUpdate(e);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error in account item update: {ex.Message}");
            }
        }

        private void OnExecutionUpdate(object sender, ExecutionEventArgs e)
        {
            try
            {
                _accountBridge?.OnExecutionUpdate(e);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error in execution update: {ex.Message}");
            }
        }

        private void OnPositionUpdate(object sender, PositionEventArgs e)
        {
            try
            {
                _accountBridge?.OnPositionUpdate(e);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error in position update: {ex.Message}");
            }
        }

        private void OnOrderUpdate(object sender, OrderEventArgs e)
        {
            try
            {
                _orderRouter?.OnOrderUpdate(e);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error in order update: {ex.Message}");
            }
        }

        public void ShowConfigWindow()
        {
            if (_configWindow == null || !_configWindow.IsLoaded)
            {
                _configWindow = new ConfigWindow(_commandListener, _logger);
                _configWindow.Show();
            }
            else
            {
                _configWindow.Activate();
            }
        }

        public void StartServer()
        {
            try
            {
                _commandListener?.Start();
                _logger?.LogInformation("NT8Bridge server started");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Failed to start server: {ex.Message}");
            }
        }

        public void StopServer()
        {
            try
            {
                _commandListener?.Stop();
                _logger?.LogInformation("NT8Bridge server stopped");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Failed to stop server: {ex.Message}");
            }
        }
    }
} 