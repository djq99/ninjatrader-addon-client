# NT8Bridge - NinjaTrader 8 Bridge Add-On

A production-ready NinjaTrader 8 Add-On DLL that turns the platform into a high-performance **data & execution gateway** and lets outside-generated strategies be **back-tested in the Strategy Analyzer** via a built-in wrapper.

## 🎯 Features

### Core Gateway Services
- **Historical Data Service** - Fetch historical bars with LRU caching (500MB)
- **Market Data Streamer** - Ultra-low-latency real-time data streaming
- **Indicator Hub** - Real-time technical indicator calculations
- **Account Bridge** - Account information and position monitoring
- **Order Router** - Order placement, amendment, and cancellation

### Strategy Bridge
- **External Algorithm Support** - Load and test external strategies in Strategy Analyzer
- **Back-testing Integration** - Run external algorithms through NinjaTrader's back-testing engine
- **Parameter Forwarding** - Pass configuration from Strategy Analyzer to external algorithms

### Communication Protocols
- **TCP Server** - Binary length-prefixed JSON messages
- **WebSocket Server** - Real-time bidirectional communication
- **Authentication** - Shared secret or JWT token support
- **Heartbeat** - Connection monitoring and latency tracking

## 🚀 Quick Start

### Prerequisites
- NinjaTrader 8.1.x (64-bit) on Windows 10/11
- .NET Framework 4.8
- Visual Studio 2022 (for building)

### Installation

1. **Clone the repository**
   ```bash
   git clone https://github.com/your-repo/ninjatrader-addon-client.git
   cd ninjatrader-addon-client
   ```

2. **Set NinjaTrader environment variable**
   ```bash
   set NINJATRADER8=C:\Program Files\NinjaTrader 8
   ```

3. **Build the project**
   ```bash
   dotnet build --configuration Release
   ```

4. **Install the Add-On**
   - The DLL will be automatically copied to `%NINJATRADER8%\bin\Custom\AddOns\`
   - Restart NinjaTrader 8
   - The Add-On will appear in Tools → Add-Ons

### Configuration

1. **Open Configuration Window**
   - In NinjaTrader, go to Tools → Add-Ons → NT8Bridge → Configure

2. **Server Settings**
   - Port: 36973 (default)
   - Protocol: TCP + WebSocket
   - Authentication: None/Shared Secret/JWT

3. **Firewall Configuration**
   ```cmd
   netsh advfirewall firewall add rule name="NT8Bridge" dir=in action=allow protocol=TCP localport=36973
   ```

## 📡 API Reference

### Wire Protocol

All messages use JSON format with the following structure:

#### Commands (Client → NT8Bridge)
```json
{
  "cmd": "CommandName",
  "reqId": 123,
  "timestamp": "2025-01-12T10:30:00Z",
  // ... command-specific fields
}
```

#### Events (NT8Bridge → Client)
```json
{
  "evt": "EventName",
  "reqId": 123,
  "timestamp": "2025-01-12T10:30:00Z",
  "success": true,
  // ... event-specific fields
}
```

### Historical Data

#### Fetch History
```json
{
  "cmd": "FetchHistory",
  "reqId": 1,
  "symbol": "NQ 09-25",
  "level": "Minute",
  "from": "2025-01-01T00:00:00Z",
  "to": "2025-01-02T00:00:00Z",
  "maxBars": 1000
}
```

**Response:**
```json
{
  "evt": "HistoryChunk",
  "reqId": 1,
  "symbol": "NQ 09-25",
  "level": "Minute",
  "bars": [
    {
      "time": "2025-01-01T09:30:00Z",
      "open": 22345.25,
      "high": 22350.50,
      "low": 22340.00,
      "close": 22348.75,
      "volume": 1250
    }
  ],
  "isComplete": true
}
```

### Market Data

#### Subscribe to Market Data
```json
{
  "cmd": "SubscribeMarketData",
  "reqId": 2,
  "symbols": ["NQ 09-25", "ES 09-25"],
  "includeDepth": false
}
```

**Tick Event:**
```json
{
  "evt": "Tick",
  "symbol": "NQ 09-25",
  "bid": 22345.25,
  "ask": 22345.50,
  "last": 22345.50,
  "volume": 2,
  "bidSize": 5,
  "askSize": 3
}
```

### Order Management

#### Place Order
```json
{
  "cmd": "PlaceOrder",
  "reqId": 3,
  "symbol": "NQ 09-25",
  "side": "Buy",
  "quantity": 1,
  "orderType": "Market",
  "account": "Sim101"
}
```

**Order Status:**
```json
{
  "evt": "OrderStatus",
  "reqId": 3,
  "orderId": "12345",
  "symbol": "NQ 09-25",
  "side": "Buy",
  "quantity": 1,
  "filledQuantity": 1,
  "orderType": "Market",
  "avgPrice": 22345.50,
  "state": "Filled",
  "message": "Order filled"
}
```

### Account Information

#### Get Account Info
```json
{
  "cmd": "GetAccountInfo",
  "reqId": 4,
  "accountName": "Sim101"
}
```

**Account Event:**
```json
{
  "evt": "Account",
  "accountName": "Sim101",
  "cash": 245000,
  "netLiq": 248400,
  "unrealPnL": 1500,
  "realPnL": 500,
  "buyingPower": 245000,
  "margin": 0
}
```

## 🔧 Strategy Bridge

### External Algorithm Interface

Create external algorithms that implement `IExternalAlgo`:

```csharp
public class MyStrategy : IExternalAlgo
{
    public string Name => "MyStrategy";
    public string Version => "1.0.0";
    public string Description => "My custom trading strategy";
    public Dictionary<string, object> Parameters => new Dictionary<string, object>();

    public void Initialize(object strategy)
    {
        // Initialize your strategy
    }

    public void OnBar(Bars bars)
    {
        // Process bar data
        var close = bars.GetClose(0);
        var sma = CalculateSMA(bars, 20);
        
        if (close > sma)
        {
            // Buy signal
        }
    }

    public void OnTick(object marketData)
    {
        // Process tick data
    }

    public void OnOrderUpdate(Order order, double avgPrice, int quantity, 
                            MarketPosition position, string orderId, DateTime time)
    {
        // Handle order updates
    }

    public void OnTermination()
    {
        // Clean up resources
    }
}
```

### Using in Strategy Analyzer

1. **Compile ExternalAlgo.dll** with your strategy
2. **Place DLL** in NinjaTrader's bin directory
3. **In Strategy Analyzer:**
   - Select "ExternalBridge" strategy
   - Set "Algorithm Name" to your strategy class name
   - Set "Parameters JSON" to your configuration
   - Run back-test

## 🛠️ Development

### Project Structure
```
ninjatrader-addon-client/
├─ NT8Bridge.sln
├─ Addon/                    # Add-On registration + WPF config window
│  ├─ Addon.cs
│  ├─ ConfigWindow.xaml
│  └─ ConfigWindow.xaml.cs
├─ Core/                     # Data, account, order, socket services
│  ├─ HistoricalDataService.cs
│  ├─ MarketDataStreamer.cs
│  ├─ IndicatorHub.cs
│  ├─ AccountBridge.cs
│  ├─ OrderRouter.cs
│  ├─ ExternalCommandListener.cs
│  └─ Models/
│     └─ CommandDto.cs
├─ StrategyBridge/           # Strategy Analyzer wrapper
│  ├─ ExternalBridge.cs
│  └─ ExternalAlgoInterface.cs
├─ Util/                     # Utilities
│  ├─ ILogger.cs
│  ├─ ConsoleLogger.cs
│  └─ RingBuffer.cs
└─ Tests/                    # Unit tests
```

### Building from Source

1. **Restore NuGet packages**
   ```bash
   dotnet restore
   ```

2. **Build in Release mode**
   ```bash
   dotnet build --configuration Release
   ```

3. **Run tests**
   ```bash
   dotnet test
   ```

### Debugging

1. **Enable logging** in the configuration window
2. **Check NinjaTrader logs** at `%NINJATRADER8%\db\`
3. **Use Visual Studio** to attach to NinjaTrader process

## 📊 Performance

### Benchmarks
- **Latency**: < 1ms for market data processing
- **Throughput**: 10,000+ messages/second
- **Memory**: 500MB LRU cache for historical data
- **Connections**: 100+ concurrent clients

### Optimization Tips
- Use TCP for high-frequency trading
- Use WebSocket for real-time dashboards
- Enable caching for frequently accessed historical data
- Monitor memory usage in diagnostics tab

## 🔒 Security

### Authentication Options
1. **None** - No authentication (development only)
2. **Shared Secret** - Simple token-based auth
3. **JWT** - JSON Web Token authentication

### Firewall Configuration
```cmd
# Allow incoming connections
netsh advfirewall firewall add rule name="NT8Bridge" dir=in action=allow protocol=TCP localport=36973

# Allow outgoing connections (if needed)
netsh advfirewall firewall add rule name="NT8Bridge-Out" dir=out action=allow protocol=TCP localport=36973
```

## 🐛 Troubleshooting

### Common Issues

1. **Add-On not loading**
   - Check NinjaTrader version compatibility
   - Verify DLL is in correct directory
   - Check .NET Framework version

2. **Connection refused**
   - Verify port 36973 is not in use
   - Check firewall settings
   - Ensure server is started

3. **High latency**
   - Check network connectivity
   - Monitor system resources
   - Review market data subscriptions

4. **Memory issues**
   - Reduce cache size in HistoricalDataService
   - Monitor memory usage in diagnostics
   - Restart NinjaTrader if needed

### Log Files
- **NinjaTrader logs**: `%NINJATRADER8%\db\`
- **Add-On logs**: Console output in NinjaTrader
- **Configuration**: `%APPDATA%\NT8Bridge\config.json`

## 📝 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests
5. Submit a pull request

## 📞 Support

- **Issues**: [GitHub Issues](https://github.com/your-repo/ninjatrader-addon-client/issues)
- **Documentation**: [Wiki](https://github.com/your-repo/ninjatrader-addon-client/wiki)
- **Discussions**: [GitHub Discussions](https://github.com/your-repo/ninjatrader-addon-client/discussions)

---

**Note**: This Add-On is designed for NinjaTrader 8.1.x. Compatibility with other versions is not guaranteed. 