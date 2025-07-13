using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NinjaTrader.Cbi;
using NT8Bridge.Core.Models;
using NT8Bridge.Util;

namespace NT8Bridge.Core
{
    public class OrderRouter : IDisposable
    {
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, OrderTracking> _orderTracking = new ConcurrentDictionary<string, OrderTracking>();
        private readonly ConcurrentDictionary<string, string> _clientToAccount = new ConcurrentDictionary<string, string>();

        public OrderRouter(ILogger logger)
        {
            _logger = logger;
        }

        public async Task<OrderStatusEvent> PlaceOrderAsync(string clientId, PlaceOrderCommand command)
        {
            try
            {
                // Validate command
                if (string.IsNullOrEmpty(command.Symbol))
                    throw new ArgumentException("Symbol is required");

                if (command.Quantity <= 0)
                    throw new ArgumentException("Quantity must be positive");

                // Get account
                var account = GetAccount(command.Account);
                if (account == null)
                    throw new ArgumentException($"Account not found: {command.Account}");

                // Get instrument
                var instrument = Instrument.GetInstrument(command.Symbol);
                if (instrument == null)
                    throw new ArgumentException($"Instrument not found: {command.Symbol}");

                // Create order
                var order = CreateOrder(instrument, command);
                if (order == null)
                    throw new InvalidOperationException("Failed to create order");

                // Track the order
                var tracking = new OrderTracking
                {
                    OrderId = order.OrderId,
                    ClientId = clientId,
                    RequestId = command.RequestId,
                    Symbol = command.Symbol,
                    Side = command.Side,
                    Quantity = command.Quantity,
                    OrderType = command.OrderType,
                    Price = command.Price,
                    State = "Working",
                    CreateTime = DateTime.UtcNow
                };

                _orderTracking[order.OrderId] = tracking;
                _clientToAccount[clientId] = account.Name;

                // Submit order
                var result = await SubmitOrderAsync(account, order);
                
                return new OrderStatusEvent
                {
                    RequestId = command.RequestId,
                    OrderId = order.OrderId,
                    Symbol = command.Symbol,
                    Side = command.Side,
                    Quantity = command.Quantity,
                    FilledQuantity = 0,
                    OrderType = command.OrderType,
                    Price = command.Price,
                    State = result ? "Working" : "Rejected",
                    Message = result ? "Order submitted successfully" : "Failed to submit order"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error placing order for client {clientId}: {ex.Message}");
                return new OrderStatusEvent
                {
                    RequestId = command.RequestId,
                    Success = false,
                    Error = ex.Message,
                    State = "Rejected"
                };
            }
        }

        public async Task<OrderStatusEvent> AmendOrderAsync(string clientId, AmendOrderCommand command)
        {
            try
            {
                if (string.IsNullOrEmpty(command.OrderId))
                    throw new ArgumentException("OrderId is required");

                // Find the order
                if (!_orderTracking.TryGetValue(command.OrderId, out var tracking))
                    throw new ArgumentException($"Order not found: {command.OrderId}");

                // Get account
                var account = Account.GetAccount(_clientToAccount[clientId]);
                if (account == null)
                    throw new ArgumentException("Account not found");

                // Get the order from NinjaTrader
                var order = account.Orders.FirstOrDefault(o => o.OrderId == command.OrderId);
                if (order == null)
                    throw new ArgumentException($"Order not found in NinjaTrader: {command.OrderId}");

                // Amend the order
                var result = await AmendOrderAsync(order, command);
                
                return new OrderStatusEvent
                {
                    RequestId = command.RequestId,
                    OrderId = command.OrderId,
                    Symbol = tracking.Symbol,
                    Side = tracking.Side,
                    Quantity = command.Quantity ?? tracking.Quantity,
                    OrderType = tracking.OrderType,
                    Price = command.Price ?? tracking.Price,
                    State = result ? "Working" : "Rejected",
                    Message = result ? "Order amended successfully" : "Failed to amend order"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error amending order for client {clientId}: {ex.Message}");
                return new OrderStatusEvent
                {
                    RequestId = command.RequestId,
                    Success = false,
                    Error = ex.Message,
                    State = "Rejected"
                };
            }
        }

        public async Task<OrderStatusEvent> CancelOrderAsync(string clientId, CancelOrderCommand command)
        {
            try
            {
                if (string.IsNullOrEmpty(command.OrderId))
                    throw new ArgumentException("OrderId is required");

                // Find the order
                if (!_orderTracking.TryGetValue(command.OrderId, out var tracking))
                    throw new ArgumentException($"Order not found: {command.OrderId}");

                // Get account
                var account = Account.GetAccount(_clientToAccount[clientId]);
                if (account == null)
                    throw new ArgumentException("Account not found");

                // Get the order from NinjaTrader
                var order = account.Orders.FirstOrDefault(o => o.OrderId == command.OrderId);
                if (order == null)
                    throw new ArgumentException($"Order not found in NinjaTrader: {command.OrderId}");

                // Cancel the order
                var result = await CancelOrderAsync(order);
                
                return new OrderStatusEvent
                {
                    RequestId = command.RequestId,
                    OrderId = command.OrderId,
                    Symbol = tracking.Symbol,
                    Side = tracking.Side,
                    Quantity = tracking.Quantity,
                    OrderType = tracking.OrderType,
                    Price = tracking.Price,
                    State = result ? "Cancelled" : "Rejected",
                    Message = result ? "Order cancelled successfully" : "Failed to cancel order"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error cancelling order for client {clientId}: {ex.Message}");
                return new OrderStatusEvent
                {
                    RequestId = command.RequestId,
                    Success = false,
                    Error = ex.Message,
                    State = "Rejected"
                };
            }
        }

        public void OnOrderUpdate(OrderEventArgs e)
        {
            try
            {
                if (!_orderTracking.TryGetValue(e.Order.OrderId, out var tracking))
                    return;

                // Update tracking info
                tracking.State = e.Order.OrderState.ToString();
                tracking.LastUpdate = DateTime.UtcNow;

                if (e.Order.OrderState == OrderState.Filled)
                {
                    tracking.FilledQuantity = e.Order.Filled;
                    tracking.AvgPrice = e.Order.AveragePrice;
                }

                // Create status event
                var statusEvent = new OrderStatusEvent
                {
                    OrderId = e.Order.OrderId,
                    Symbol = tracking.Symbol,
                    Side = tracking.Side,
                    Quantity = tracking.Quantity,
                    FilledQuantity = e.Order.Filled,
                    OrderType = tracking.OrderType,
                    Price = tracking.Price,
                    AvgPrice = e.Order.AveragePrice,
                    State = e.Order.OrderState.ToString(),
                    Message = e.Order.OrderState.ToString()
                };

                // Broadcast to client
                BroadcastToClientAsync(tracking.ClientId, statusEvent);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing order update: {ex.Message}");
            }
        }

        private Account GetAccount(string accountName)
        {
            try
            {
                if (string.IsNullOrEmpty(accountName))
                {
                    // Get default account
                    var accounts = Account.GetAccounts();
                    return accounts?.FirstOrDefault();
                }

                return Account.GetAccount(accountName);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting account {accountName}: {ex.Message}");
                return null;
            }
        }

        private Order CreateOrder(Instrument instrument, PlaceOrderCommand command)
        {
            try
            {
                var order = new Order();

                // Set basic properties
                order.Instrument = instrument;
                order.OrderType = GetOrderType(command.OrderType);
                order.OrderAction = command.Side.ToLower() == "buy" ? OrderAction.Buy : OrderAction.Sell;
                order.Quantity = command.Quantity;
                order.TimeInForce = GetTimeInForce(command.TimeInForce);

                // Set prices based on order type
                switch (order.OrderType)
                {
                    case OrderType.Limit:
                        if (!command.Price.HasValue)
                            throw new ArgumentException("Price is required for limit orders");
                        order.LimitPrice = command.Price.Value;
                        break;

                    case OrderType.Stop:
                        if (!command.StopPrice.HasValue)
                            throw new ArgumentException("StopPrice is required for stop orders");
                        order.StopPrice = command.StopPrice.Value;
                        break;

                    case OrderType.StopLimit:
                        if (!command.Price.HasValue || !command.StopPrice.HasValue)
                            throw new ArgumentException("Price and StopPrice are required for stop-limit orders");
                        order.LimitPrice = command.Price.Value;
                        order.StopPrice = command.StopPrice.Value;
                        break;
                }

                return order;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error creating order: {ex.Message}");
                return null;
            }
        }

        private OrderType GetOrderType(string orderType)
        {
            return orderType?.ToLower() switch
            {
                "market" => OrderType.Market,
                "limit" => OrderType.Limit,
                "stop" => OrderType.Stop,
                "stoplimit" => OrderType.StopLimit,
                _ => OrderType.Market
            };
        }

        private TimeInForce GetTimeInForce(string timeInForce)
        {
            return timeInForce?.ToLower() switch
            {
                "day" => TimeInForce.Day,
                "gtc" => TimeInForce.Gtc,
                "ioc" => TimeInForce.Ioc,
                "fok" => TimeInForce.Fok,
                _ => TimeInForce.Day
            };
        }

        private async Task<bool> SubmitOrderAsync(Account account, Order order)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var result = account.SubmitOrderUnmanaged(order);
                    _logger.LogInformation($"Order submitted: {order.OrderId} - {result}");
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error submitting order: {ex.Message}");
                    return false;
                }
            });
        }

        private async Task<bool> AmendOrderAsync(Order order, AmendOrderCommand command)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (command.Quantity.HasValue)
                        order.Quantity = command.Quantity.Value;

                    if (command.Price.HasValue)
                        order.LimitPrice = command.Price.Value;

                    if (command.StopPrice.HasValue)
                        order.StopPrice = command.StopPrice.Value;

                    var result = order.ChangeOrder();
                    _logger.LogInformation($"Order amended: {order.OrderId} - {result}");
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error amending order: {ex.Message}");
                    return false;
                }
            });
        }

        private async Task<bool> CancelOrderAsync(Order order)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var result = order.CancelOrder();
                    _logger.LogInformation($"Order cancelled: {order.OrderId} - {result}");
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error cancelling order: {ex.Message}");
                    return false;
                }
            });
        }

        private async Task BroadcastToClientAsync(string clientId, OrderStatusEvent statusEvent)
        {
            // This would be implemented by the ExternalCommandListener
            // For now, we'll just log the event
            _logger.LogDebug($"Broadcasting order status to client {clientId}: {statusEvent.State}");
            await Task.CompletedTask;
        }

        public List<OrderTracking> GetActiveOrders(string clientId)
        {
            return _orderTracking.Values
                .Where(t => t.ClientId == clientId && t.State == "Working")
                .ToList();
        }

        public void Dispose()
        {
            try
            {
                _orderTracking.Clear();
                _clientToAccount.Clear();
                _logger.LogInformation("OrderRouter disposed");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error disposing OrderRouter: {ex.Message}");
            }
        }

        public class OrderTracking
        {
            public string OrderId { get; set; }
            public string ClientId { get; set; }
            public int RequestId { get; set; }
            public string Symbol { get; set; }
            public string Side { get; set; }
            public int Quantity { get; set; }
            public string OrderType { get; set; }
            public double? Price { get; set; }
            public double? StopPrice { get; set; }
            public string State { get; set; }
            public int FilledQuantity { get; set; }
            public double? AvgPrice { get; set; }
            public DateTime CreateTime { get; set; }
            public DateTime LastUpdate { get; set; }
        }
    }
} 