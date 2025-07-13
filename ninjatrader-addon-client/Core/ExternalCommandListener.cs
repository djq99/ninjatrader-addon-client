using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NT8Bridge.Core.Models;
using NT8Bridge.Util;

namespace NT8Bridge.Core
{
    public class ExternalCommandListener : IDisposable
    {
        private readonly ILogger _logger;
        private readonly TcpListener _tcpListener;
        private readonly HttpListener _httpListener;
        private readonly ConcurrentDictionary<string, ClientSession> _clients = new ConcurrentDictionary<string, ClientSession>();
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly Task _tcpTask;
        private readonly Task _websocketTask;
        private readonly Task _heartbeatTask;

        // Service references
        private HistoricalDataService _historicalDataService;
        private MarketDataStreamer _marketDataStreamer;
        private IndicatorHub _indicatorHub;
        private AccountBridge _accountBridge;
        private OrderRouter _orderRouter;

        private const int DEFAULT_PORT = 36973;
        private const int HEARTBEAT_INTERVAL_MS = 5000;

        public ExternalCommandListener(ILogger logger)
        {
            _logger = logger;
            _tcpListener = new TcpListener(IPAddress.Any, DEFAULT_PORT);
            _httpListener = new HttpListener();
            _cancellationTokenSource = new CancellationTokenSource();

            _tcpTask = Task.Run(AcceptTcpClientsAsync);
            _websocketTask = Task.Run(AcceptWebSocketClientsAsync);
            _heartbeatTask = Task.Run(SendHeartbeatsAsync);
        }

        public void RegisterServices(
            HistoricalDataService historicalDataService,
            MarketDataStreamer marketDataStreamer,
            IndicatorHub indicatorHub,
            AccountBridge accountBridge,
            OrderRouter orderRouter)
        {
            _historicalDataService = historicalDataService;
            _marketDataStreamer = marketDataStreamer;
            _indicatorHub = indicatorHub;
            _accountBridge = accountBridge;
            _orderRouter = orderRouter;
        }

        public void Start()
        {
            try
            {
                _tcpListener.Start();
                _httpListener.Prefixes.Add($"http://+:{DEFAULT_PORT}/");
                _httpListener.Start();

                _logger.LogInformation($"NT8Bridge server started on port {DEFAULT_PORT}");
                _logger.LogInformation("TCP and WebSocket protocols supported");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to start server: {ex.Message}");
                throw;
            }
        }

        public void Stop()
        {
            try
            {
                _cancellationTokenSource.Cancel();
                _tcpListener?.Stop();
                _httpListener?.Stop();

                // Disconnect all clients
                foreach (var client in _clients.Values)
                {
                    client.Disconnect();
                }
                _clients.Clear();

                _logger.LogInformation("NT8Bridge server stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error stopping server: {ex.Message}");
            }
        }

        private async Task AcceptTcpClientsAsync()
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    var tcpClient = await _tcpListener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleTcpClientAsync(tcpClient));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error accepting TCP client: {ex.Message}");
                }
            }
        }

        private async Task AcceptWebSocketClientsAsync()
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    var context = await _httpListener.GetContextAsync();
                    if (context.Request.IsWebSocketRequest)
                    {
                        var webSocketContext = await context.AcceptWebSocketAsync(null);
                        _ = Task.Run(() => HandleWebSocketClientAsync(webSocketContext.WebSocket));
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error accepting WebSocket client: {ex.Message}");
                }
            }
        }

        private async Task HandleTcpClientAsync(TcpClient tcpClient)
        {
            var clientId = Guid.NewGuid().ToString();
            var clientSession = new ClientSession(clientId, tcpClient, _logger);

            try
            {
                _clients.TryAdd(clientId, clientSession);
                _logger.LogInformation($"TCP client connected: {clientId}");

                await ProcessClientMessagesAsync(clientSession);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling TCP client {clientId}: {ex.Message}");
            }
            finally
            {
                _clients.TryRemove(clientId, out _);
                clientSession.Disconnect();
                _logger.LogInformation($"TCP client disconnected: {clientId}");
            }
        }

        private async Task HandleWebSocketClientAsync(WebSocket webSocket)
        {
            var clientId = Guid.NewGuid().ToString();
            var clientSession = new ClientSession(clientId, webSocket, _logger);

            try
            {
                _clients.TryAdd(clientId, clientSession);
                _logger.LogInformation($"WebSocket client connected: {clientId}");

                await ProcessClientMessagesAsync(clientSession);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling WebSocket client {clientId}: {ex.Message}");
            }
            finally
            {
                _clients.TryRemove(clientId, out _);
                clientSession.Disconnect();
                _logger.LogInformation($"WebSocket client disconnected: {clientId}");
            }
        }

        private async Task ProcessClientMessagesAsync(ClientSession clientSession)
        {
            var buffer = new byte[4096];

            while (clientSession.IsConnected)
            {
                try
                {
                    var message = await clientSession.ReceiveMessageAsync();
                    if (string.IsNullOrEmpty(message))
                        continue;

                    var response = await ProcessCommandAsync(clientSession.ClientId, message);
                    if (response != null)
                    {
                        await clientSession.SendMessageAsync(response);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error processing message for client {clientSession.ClientId}: {ex.Message}");
                    break;
                }
            }
        }

        private async Task<string> ProcessCommandAsync(string clientId, string message)
        {
            try
            {
                var command = JsonConvert.DeserializeObject<BaseCommand>(message);
                if (command == null)
                {
                    return CreateErrorResponse("Invalid JSON format");
                }

                switch (command.Command?.ToLower())
                {
                    case "fetchhistory":
                        return await HandleFetchHistoryAsync(clientId, JsonConvert.DeserializeObject<FetchHistoryCommand>(message));

                    case "subscribemarketdata":
                        return await HandleSubscribeMarketDataAsync(clientId, JsonConvert.DeserializeObject<SubscribeMarketDataCommand>(message));

                    case "unsubscribemarketdata":
                        return await HandleUnsubscribeMarketDataAsync(clientId, JsonConvert.DeserializeObject<UnsubscribeMarketDataCommand>(message));

                    case "subscribeindicators":
                        return await HandleSubscribeIndicatorsAsync(clientId, JsonConvert.DeserializeObject<SubscribeIndicatorsCommand>(message));

                    case "getaccountinfo":
                        return await HandleGetAccountInfoAsync(clientId, JsonConvert.DeserializeObject<GetAccountInfoCommand>(message));

                    case "placeorder":
                        return await HandlePlaceOrderAsync(clientId, JsonConvert.DeserializeObject<PlaceOrderCommand>(message));

                    case "amendorder":
                        return await HandleAmendOrderAsync(clientId, JsonConvert.DeserializeObject<AmendOrderCommand>(message));

                    case "cancelorder":
                        return await HandleCancelOrderAsync(clientId, JsonConvert.DeserializeObject<CancelOrderCommand>(message));

                    case "ping":
                        return await HandlePingAsync(clientId, JsonConvert.DeserializeObject<PingCommand>(message));

                    default:
                        return CreateErrorResponse($"Unknown command: {command.Command}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing command: {ex.Message}");
                return CreateErrorResponse($"Processing error: {ex.Message}");
            }
        }

        private async Task<string> HandleFetchHistoryAsync(string clientId, FetchHistoryCommand command)
        {
            try
            {
                var result = await _historicalDataService.GetHistoryAsync(command);
                return JsonConvert.SerializeObject(result);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling FetchHistory: {ex.Message}");
                return CreateErrorResponse(ex.Message);
            }
        }

        private async Task<string> HandleSubscribeMarketDataAsync(string clientId, SubscribeMarketDataCommand command)
        {
            try
            {
                _marketDataStreamer.Subscribe(clientId, command);
                return CreateSuccessResponse("Market data subscription successful");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling SubscribeMarketData: {ex.Message}");
                return CreateErrorResponse(ex.Message);
            }
        }

        private async Task<string> HandleUnsubscribeMarketDataAsync(string clientId, UnsubscribeMarketDataCommand command)
        {
            try
            {
                _marketDataStreamer.Unsubscribe(clientId, command);
                return CreateSuccessResponse("Market data unsubscription successful");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling UnsubscribeMarketData: {ex.Message}");
                return CreateErrorResponse(ex.Message);
            }
        }

        private async Task<string> HandleSubscribeIndicatorsAsync(string clientId, SubscribeIndicatorsCommand command)
        {
            try
            {
                _indicatorHub.Subscribe(clientId, command);
                return CreateSuccessResponse("Indicator subscription successful");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling SubscribeIndicators: {ex.Message}");
                return CreateErrorResponse(ex.Message);
            }
        }

        private async Task<string> HandleGetAccountInfoAsync(string clientId, GetAccountInfoCommand command)
        {
            try
            {
                _accountBridge.Subscribe(clientId, command);
                return CreateSuccessResponse("Account subscription successful");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling GetAccountInfo: {ex.Message}");
                return CreateErrorResponse(ex.Message);
            }
        }

        private async Task<string> HandlePlaceOrderAsync(string clientId, PlaceOrderCommand command)
        {
            try
            {
                var result = await _orderRouter.PlaceOrderAsync(clientId, command);
                return JsonConvert.SerializeObject(result);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling PlaceOrder: {ex.Message}");
                return CreateErrorResponse(ex.Message);
            }
        }

        private async Task<string> HandleAmendOrderAsync(string clientId, AmendOrderCommand command)
        {
            try
            {
                var result = await _orderRouter.AmendOrderAsync(clientId, command);
                return JsonConvert.SerializeObject(result);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling AmendOrder: {ex.Message}");
                return CreateErrorResponse(ex.Message);
            }
        }

        private async Task<string> HandleCancelOrderAsync(string clientId, CancelOrderCommand command)
        {
            try
            {
                var result = await _orderRouter.CancelOrderAsync(clientId, command);
                return JsonConvert.SerializeObject(result);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling CancelOrder: {ex.Message}");
                return CreateErrorResponse(ex.Message);
            }
        }

        private async Task<string> HandlePingAsync(string clientId, PingCommand command)
        {
            try
            {
                var pong = new PongEvent
                {
                    RequestId = command.RequestId,
                    ServerTime = DateTime.UtcNow,
                    LatencyMs = 0 // Would calculate actual latency
                };
                return JsonConvert.SerializeObject(pong);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling Ping: {ex.Message}");
                return CreateErrorResponse(ex.Message);
            }
        }

        private string CreateSuccessResponse(string message)
        {
            return JsonConvert.SerializeObject(new { success = true, message });
        }

        private string CreateErrorResponse(string error)
        {
            return JsonConvert.SerializeObject(new { success = false, error });
        }

        private async Task SendHeartbeatsAsync()
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    var heartbeat = new HeartbeatEvent
                    {
                        ServerTime = DateTime.UtcNow,
                        ConnectedClients = _clients.Count,
                        ActiveSubscriptions = _clients.Count // Simplified
                    };

                    var message = JsonConvert.SerializeObject(heartbeat);

                    foreach (var client in _clients.Values)
                    {
                        if (client.IsConnected)
                        {
                            await client.SendMessageAsync(message);
                        }
                    }

                    await Task.Delay(HEARTBEAT_INTERVAL_MS, _cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error sending heartbeats: {ex.Message}");
                }
            }
        }

        public async Task BroadcastToClientAsync(string clientId, BaseEvent eventData)
        {
            try
            {
                if (_clients.TryGetValue(clientId, out var client))
                {
                    var message = JsonConvert.SerializeObject(eventData);
                    await client.SendMessageAsync(message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error broadcasting to client {clientId}: {ex.Message}");
            }
        }

        public List<string> GetConnectedClients()
        {
            return _clients.Keys.ToList();
        }

        public void Dispose()
        {
            try
            {
                Stop();
                _cancellationTokenSource?.Dispose();
                _tcpListener?.Dispose();
                _httpListener?.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error disposing ExternalCommandListener: {ex.Message}");
            }
        }

        private class ClientSession : IDisposable
        {
            public string ClientId { get; }
            public bool IsConnected { get; private set; }

            private readonly TcpClient _tcpClient;
            private readonly WebSocket _webSocket;
            private readonly NetworkStream _networkStream;
            private readonly ILogger _logger;
            private readonly bool _isWebSocket;

            public ClientSession(string clientId, TcpClient tcpClient, ILogger logger)
            {
                ClientId = clientId;
                _tcpClient = tcpClient;
                _networkStream = tcpClient.GetStream();
                _logger = logger;
                _isWebSocket = false;
                IsConnected = true;
            }

            public ClientSession(string clientId, WebSocket webSocket, ILogger logger)
            {
                ClientId = clientId;
                _webSocket = webSocket;
                _logger = logger;
                _isWebSocket = true;
                IsConnected = true;
            }

            public async Task<string> ReceiveMessageAsync()
            {
                try
                {
                    if (_isWebSocket)
                    {
                        return await ReceiveWebSocketMessageAsync();
                    }
                    else
                    {
                        return await ReceiveTcpMessageAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error receiving message from {ClientId}: {ex.Message}");
                    IsConnected = false;
                    return null;
                }
            }

            private async Task<string> ReceiveTcpMessageAsync()
            {
                // Read length prefix (4 bytes)
                var lengthBytes = new byte[4];
                var bytesRead = await _networkStream.ReadAsync(lengthBytes, 0, 4);
                if (bytesRead != 4)
                    return null;

                var messageLength = BitConverter.ToInt32(lengthBytes, 0);
                if (messageLength <= 0 || messageLength > 1024 * 1024) // 1MB limit
                    return null;

                // Read message
                var messageBytes = new byte[messageLength];
                var totalRead = 0;
                while (totalRead < messageLength)
                {
                    var read = await _networkStream.ReadAsync(messageBytes, totalRead, messageLength - totalRead);
                    if (read == 0)
                        return null;
                    totalRead += read;
                }

                return Encoding.UTF8.GetString(messageBytes);
            }

            private async Task<string> ReceiveWebSocketMessageAsync()
            {
                var buffer = new byte[4096];
                var message = new StringBuilder();

                while (true)
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        IsConnected = false;
                        return null;
                    }

                    message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    if (result.EndOfMessage)
                        break;
                }

                return message.ToString();
            }

            public async Task SendMessageAsync(string message)
            {
                try
                {
                    if (_isWebSocket)
                    {
                        await SendWebSocketMessageAsync(message);
                    }
                    else
                    {
                        await SendTcpMessageAsync(message);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error sending message to {ClientId}: {ex.Message}");
                    IsConnected = false;
                }
            }

            private async Task SendTcpMessageAsync(string message)
            {
                var messageBytes = Encoding.UTF8.GetBytes(message);
                var lengthBytes = BitConverter.GetBytes(messageBytes.Length);

                await _networkStream.WriteAsync(lengthBytes, 0, 4);
                await _networkStream.WriteAsync(messageBytes, 0, messageBytes.Length);
                await _networkStream.FlushAsync();
            }

            private async Task SendWebSocketMessageAsync(string message)
            {
                var messageBytes = Encoding.UTF8.GetBytes(message);
                await _webSocket.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }

            public void Disconnect()
            {
                IsConnected = false;
                _tcpClient?.Close();
                _webSocket?.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnect", CancellationToken.None).Wait();
            }

            public void Dispose()
            {
                Disconnect();
                _tcpClient?.Dispose();
                _webSocket?.Dispose();
                _networkStream?.Dispose();
            }
        }
    }
} 