using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NinjaTrader.Cbi;
using NT8Bridge.Core.Models;
using NT8Bridge.Util;

namespace NT8Bridge.Core
{
    public class AccountBridge : IDisposable
    {
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, AccountSubscription> _subscriptions = new ConcurrentDictionary<string, AccountSubscription>();
        private readonly ConcurrentDictionary<string, AccountInfo> _accountCache = new ConcurrentDictionary<string, AccountInfo>();
        private readonly Timer _heartbeatTimer;
        private readonly object _lockObject = new object();

        private const int HEARTBEAT_INTERVAL_MS = 5000; // 5 seconds
        private const int MAX_UPDATE_FREQUENCY_MS = 200; // 5 Hz max

        public AccountBridge(ILogger logger)
        {
            _logger = logger;
            _heartbeatTimer = new Timer(OnHeartbeat, null, HEARTBEAT_INTERVAL_MS, HEARTBEAT_INTERVAL_MS);
        }

        public void Subscribe(string clientId, GetAccountInfoCommand command)
        {
            try
            {
                var accountName = command.AccountName ?? GetDefaultAccountName();
                var subscription = GetOrCreateSubscription(accountName);
                subscription.AddClient(clientId);
                
                // Send initial account info
                var accountInfo = GetAccountInfo(accountName);
                if (accountInfo != null)
                {
                    var accountEvent = new AccountEvent
                    {
                        AccountName = accountName,
                        Cash = accountInfo.Cash,
                        NetLiq = accountInfo.NetLiq,
                        UnrealPnL = accountInfo.UnrealPnL,
                        RealPnL = accountInfo.RealPnL,
                        BuyingPower = accountInfo.BuyingPower,
                        Margin = accountInfo.Margin
                    };

                    BroadcastToClientAsync(clientId, accountEvent);
                }

                _logger.LogInformation($"Client {clientId} subscribed to account {accountName}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error subscribing client {clientId} to account: {ex.Message}");
                throw;
            }
        }

        public void Unsubscribe(string clientId, string accountName)
        {
            try
            {
                if (_subscriptions.TryGetValue(accountName, out var subscription))
                {
                    subscription.RemoveClient(clientId);
                    
                    if (subscription.ClientCount == 0)
                    {
                        _subscriptions.TryRemove(accountName, out _);
                        _logger.LogInformation($"Removed account subscription for {accountName}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error unsubscribing client {clientId} from account: {ex.Message}");
            }
        }

        public void OnAccountItemUpdate(AccountItemEventArgs e)
        {
            try
            {
                var accountName = e.Account.Name;
                UpdateAccountCache(accountName, e);
                
                // Notify subscribed clients
                NotifyAccountUpdate(accountName);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing account item update: {ex.Message}");
            }
        }

        public void OnExecutionUpdate(ExecutionEventArgs e)
        {
            try
            {
                var accountName = e.Execution.Account.Name;
                UpdateAccountCache(accountName, e);
                
                // Notify subscribed clients
                NotifyAccountUpdate(accountName);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing execution update: {ex.Message}");
            }
        }

        public void OnPositionUpdate(PositionEventArgs e)
        {
            try
            {
                var accountName = e.Position.Account.Name;
                UpdateAccountCache(accountName, e);
                
                // Notify subscribed clients
                NotifyAccountUpdate(accountName);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing position update: {ex.Message}");
            }
        }

        private void UpdateAccountCache(string accountName, AccountItemEventArgs e)
        {
            try
            {
                var accountInfo = GetOrCreateAccountInfo(accountName);
                
                // Update account info based on the event
                switch (e.AccountItem)
                {
                    case AccountItem.CashValue:
                        accountInfo.Cash = e.Value;
                        break;
                    case AccountItem.NetLiquidation:
                        accountInfo.NetLiq = e.Value;
                        break;
                    case AccountItem.UnrealizedProfitLoss:
                        accountInfo.UnrealPnL = e.Value;
                        break;
                    case AccountItem.RealizedProfitLoss:
                        accountInfo.RealPnL = e.Value;
                        break;
                    case AccountItem.BuyingPower:
                        accountInfo.BuyingPower = e.Value;
                        break;
                    case AccountItem.Margin:
                        accountInfo.Margin = e.Value;
                        break;
                }

                accountInfo.LastUpdate = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating account cache for {accountName}: {ex.Message}");
            }
        }

        private void UpdateAccountCache(string accountName, ExecutionEventArgs e)
        {
            try
            {
                var accountInfo = GetOrCreateAccountInfo(accountName);
                
                // Update based on execution
                if (e.Execution.Order.OrderState == OrderState.Filled)
                {
                    // Recalculate account values after fill
                    RefreshAccountInfo(accountName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating account cache from execution: {ex.Message}");
            }
        }

        private void UpdateAccountCache(string accountName, PositionEventArgs e)
        {
            try
            {
                var accountInfo = GetOrCreateAccountInfo(accountName);
                
                // Update unrealized P&L based on position changes
                RefreshAccountInfo(accountName);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating account cache from position: {ex.Message}");
            }
        }

        private void NotifyAccountUpdate(string accountName)
        {
            try
            {
                if (!_subscriptions.TryGetValue(accountName, out var subscription))
                    return;

                var accountInfo = GetAccountInfo(accountName);
                if (accountInfo == null)
                    return;

                // Throttle updates to avoid spamming
                if ((DateTime.UtcNow - accountInfo.LastUpdate).TotalMilliseconds < MAX_UPDATE_FREQUENCY_MS)
                    return;

                var accountEvent = new AccountEvent
                {
                    AccountName = accountName,
                    Cash = accountInfo.Cash,
                    NetLiq = accountInfo.NetLiq,
                    UnrealPnL = accountInfo.UnrealPnL,
                    RealPnL = accountInfo.RealPnL,
                    BuyingPower = accountInfo.BuyingPower,
                    Margin = accountInfo.Margin
                };

                // Broadcast to all subscribed clients
                foreach (var clientId in subscription.GetClientIds())
                {
                    BroadcastToClientAsync(clientId, accountEvent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error notifying account update for {accountName}: {ex.Message}");
            }
        }

        private AccountInfo GetOrCreateAccountInfo(string accountName)
        {
            return _accountCache.GetOrAdd(accountName, key =>
            {
                var account = Account.GetAccount(key);
                if (account == null)
                {
                    _logger.LogWarning($"Account not found: {key}");
                    return new AccountInfo { AccountName = key };
                }

                return new AccountInfo
                {
                    AccountName = key,
                    Cash = account.CashValue,
                    NetLiq = account.NetLiquidation,
                    UnrealPnL = account.UnrealizedProfitLoss,
                    RealPnL = account.RealizedProfitLoss,
                    BuyingPower = account.BuyingPower,
                    Margin = account.Margin,
                    LastUpdate = DateTime.UtcNow
                };
            });
        }

        private AccountInfo GetAccountInfo(string accountName)
        {
            if (_accountCache.TryGetValue(accountName, out var accountInfo))
            {
                return accountInfo;
            }

            // Try to create if not exists
            return GetOrCreateAccountInfo(accountName);
        }

        private void RefreshAccountInfo(string accountName)
        {
            try
            {
                var account = Account.GetAccount(accountName);
                if (account == null)
                    return;

                var accountInfo = GetOrCreateAccountInfo(accountName);
                accountInfo.Cash = account.CashValue;
                accountInfo.NetLiq = account.NetLiquidation;
                accountInfo.UnrealPnL = account.UnrealizedProfitLoss;
                accountInfo.RealPnL = account.RealizedProfitLoss;
                accountInfo.BuyingPower = account.BuyingPower;
                accountInfo.Margin = account.Margin;
                accountInfo.LastUpdate = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error refreshing account info for {accountName}: {ex.Message}");
            }
        }

        private string GetDefaultAccountName()
        {
            try
            {
                var accounts = Account.GetAccounts();
                return accounts?.FirstOrDefault()?.Name ?? "Default";
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting default account: {ex.Message}");
                return "Default";
            }
        }

        private AccountSubscription GetOrCreateSubscription(string accountName)
        {
            return _subscriptions.GetOrAdd(accountName, key =>
            {
                _logger.LogInformation($"Creating new account subscription for {key}");
                return new AccountSubscription(key);
            });
        }

        private async Task BroadcastToClientAsync(string clientId, AccountEvent accountEvent)
        {
            // This would be implemented by the ExternalCommandListener
            // For now, we'll just log the event
            _logger.LogDebug($"Broadcasting account update to client {clientId}");
            await Task.CompletedTask;
        }

        private void OnHeartbeat(object state)
        {
            try
            {
                // Refresh all account info periodically
                foreach (var accountName in _subscriptions.Keys)
                {
                    RefreshAccountInfo(accountName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in account heartbeat: {ex.Message}");
            }
        }

        public List<string> GetSubscribedAccounts()
        {
            return _subscriptions.Keys.ToList();
        }

        public int GetClientCount(string accountName)
        {
            return _subscriptions.TryGetValue(accountName, out var subscription) ? subscription.ClientCount : 0;
        }

        public void Dispose()
        {
            try
            {
                _heartbeatTimer?.Dispose();
                _accountCache.Clear();
                _subscriptions.Clear();
                _logger.LogInformation("AccountBridge disposed");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error disposing AccountBridge: {ex.Message}");
            }
        }

        private class AccountInfo
        {
            public string AccountName { get; set; }
            public double Cash { get; set; }
            public double NetLiq { get; set; }
            public double UnrealPnL { get; set; }
            public double RealPnL { get; set; }
            public double BuyingPower { get; set; }
            public double Margin { get; set; }
            public DateTime LastUpdate { get; set; }
        }

        private class AccountSubscription
        {
            private readonly string _accountName;
            private readonly ConcurrentDictionary<string, bool> _clients = new ConcurrentDictionary<string, bool>();

            public AccountSubscription(string accountName)
            {
                _accountName = accountName;
            }

            public int ClientCount => _clients.Count;

            public void AddClient(string clientId)
            {
                _clients.TryAdd(clientId, true);
            }

            public void RemoveClient(string clientId)
            {
                _clients.TryRemove(clientId, out _);
            }

            public HashSet<string> GetClientIds()
            {
                return new HashSet<string>(_clients.Keys);
            }
        }
    }
} 