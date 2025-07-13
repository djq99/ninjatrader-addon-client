using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace NT8Bridge.Core.Models
{
    #region Base Classes
    public abstract class BaseCommand
    {
        [JsonProperty("cmd")]
        public string Command { get; set; }

        [JsonProperty("reqId")]
        public int RequestId { get; set; }

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public abstract class BaseEvent
    {
        [JsonProperty("evt")]
        public string Event { get; set; }

        [JsonProperty("reqId")]
        public int? RequestId { get; set; }

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [JsonProperty("success")]
        public bool Success { get; set; } = true;

        [JsonProperty("error")]
        public string Error { get; set; }
    }
    #endregion

    #region Historical Data Commands
    public class FetchHistoryCommand : BaseCommand
    {
        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        [JsonProperty("level")]
        public string Level { get; set; } // Tick, Second, Minute, Day, Year

        [JsonProperty("from")]
        public DateTime From { get; set; }

        [JsonProperty("to")]
        public DateTime To { get; set; }

        [JsonProperty("maxBars")]
        public int? MaxBars { get; set; }
    }

    public class HistoryChunkEvent : BaseEvent
    {
        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        [JsonProperty("level")]
        public string Level { get; set; }

        [JsonProperty("bars")]
        public List<BarData> Bars { get; set; } = new List<BarData>();

        [JsonProperty("isComplete")]
        public bool IsComplete { get; set; }
    }

    public class BarData
    {
        [JsonProperty("time")]
        public DateTime Time { get; set; }

        [JsonProperty("open")]
        public double Open { get; set; }

        [JsonProperty("high")]
        public double High { get; set; }

        [JsonProperty("low")]
        public double Low { get; set; }

        [JsonProperty("close")]
        public double Close { get; set; }

        [JsonProperty("volume")]
        public long Volume { get; set; }
    }
    #endregion

    #region Market Data Commands
    public class SubscribeMarketDataCommand : BaseCommand
    {
        [JsonProperty("symbols")]
        public List<string> Symbols { get; set; } = new List<string>();

        [JsonProperty("includeDepth")]
        public bool IncludeDepth { get; set; } = false;
    }

    public class UnsubscribeMarketDataCommand : BaseCommand
    {
        [JsonProperty("symbols")]
        public List<string> Symbols { get; set; } = new List<string>();
    }

    public class TickEvent : BaseEvent
    {
        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        [JsonProperty("bid")]
        public double Bid { get; set; }

        [JsonProperty("ask")]
        public double Ask { get; set; }

        [JsonProperty("last")]
        public double Last { get; set; }

        [JsonProperty("volume")]
        public long Volume { get; set; }

        [JsonProperty("bidSize")]
        public int BidSize { get; set; }

        [JsonProperty("askSize")]
        public int AskSize { get; set; }
    }

    public class DepthEvent : BaseEvent
    {
        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        [JsonProperty("side")]
        public string Side { get; set; } // Bid or Ask

        [JsonProperty("price")]
        public double Price { get; set; }

        [JsonProperty("size")]
        public int Size { get; set; }

        [JsonProperty("level")]
        public int Level { get; set; }

        [JsonProperty("action")]
        public string Action { get; set; } // Insert, Update, Delete
    }
    #endregion

    #region Indicator Commands
    public class SubscribeIndicatorsCommand : BaseCommand
    {
        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        [JsonProperty("indicators")]
        public List<string> Indicators { get; set; } = new List<string>();
    }

    public class IndicatorEvent : BaseEvent
    {
        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        [JsonProperty("indicators")]
        public Dictionary<string, double> Indicators { get; set; } = new Dictionary<string, double>();
    }
    #endregion

    #region Account Commands
    public class GetAccountInfoCommand : BaseCommand
    {
        [JsonProperty("accountName")]
        public string AccountName { get; set; }
    }

    public class AccountEvent : BaseEvent
    {
        [JsonProperty("accountName")]
        public string AccountName { get; set; }

        [JsonProperty("cash")]
        public double Cash { get; set; }

        [JsonProperty("netLiq")]
        public double NetLiq { get; set; }

        [JsonProperty("unrealPnL")]
        public double UnrealPnL { get; set; }

        [JsonProperty("realPnL")]
        public double RealPnL { get; set; }

        [JsonProperty("buyingPower")]
        public double BuyingPower { get; set; }

        [JsonProperty("margin")]
        public double Margin { get; set; }
    }
    #endregion

    #region Order Commands
    public class PlaceOrderCommand : BaseCommand
    {
        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        [JsonProperty("side")]
        public string Side { get; set; } // Buy or Sell

        [JsonProperty("quantity")]
        public int Quantity { get; set; }

        [JsonProperty("orderType")]
        public string OrderType { get; set; } // Market, Limit, Stop, StopLimit

        [JsonProperty("price")]
        public double? Price { get; set; }

        [JsonProperty("stopPrice")]
        public double? StopPrice { get; set; }

        [JsonProperty("timeInForce")]
        public string TimeInForce { get; set; } = "Day";

        [JsonProperty("account")]
        public string Account { get; set; }

        [JsonProperty("strategy")]
        public string Strategy { get; set; }
    }

    public class AmendOrderCommand : BaseCommand
    {
        [JsonProperty("orderId")]
        public string OrderId { get; set; }

        [JsonProperty("quantity")]
        public int? Quantity { get; set; }

        [JsonProperty("price")]
        public double? Price { get; set; }

        [JsonProperty("stopPrice")]
        public double? StopPrice { get; set; }
    }

    public class CancelOrderCommand : BaseCommand
    {
        [JsonProperty("orderId")]
        public string OrderId { get; set; }
    }

    public class OrderStatusEvent : BaseEvent
    {
        [JsonProperty("orderId")]
        public string OrderId { get; set; }

        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        [JsonProperty("side")]
        public string Side { get; set; }

        [JsonProperty("quantity")]
        public int Quantity { get; set; }

        [JsonProperty("filledQuantity")]
        public int FilledQuantity { get; set; }

        [JsonProperty("orderType")]
        public string OrderType { get; set; }

        [JsonProperty("price")]
        public double? Price { get; set; }

        [JsonProperty("avgPrice")]
        public double? AvgPrice { get; set; }

        [JsonProperty("state")]
        public string State { get; set; } // Working, Filled, Cancelled, Rejected

        [JsonProperty("message")]
        public string Message { get; set; }
    }
    #endregion

    #region Strategy Bridge Commands
    public class RunBacktestCommand : BaseCommand
    {
        [JsonProperty("strategyName")]
        public string StrategyName { get; set; }

        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        [JsonProperty("from")]
        public DateTime From { get; set; }

        [JsonProperty("to")]
        public DateTime To { get; set; }

        [JsonProperty("parameters")]
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
    }

    public class BacktestResultEvent : BaseEvent
    {
        [JsonProperty("strategyName")]
        public string StrategyName { get; set; }

        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        [JsonProperty("totalReturn")]
        public double TotalReturn { get; set; }

        [JsonProperty("sharpeRatio")]
        public double SharpeRatio { get; set; }

        [JsonProperty("maxDrawdown")]
        public double MaxDrawdown { get; set; }

        [JsonProperty("totalTrades")]
        public int TotalTrades { get; set; }

        [JsonProperty("winRate")]
        public double WinRate { get; set; }

        [JsonProperty("trades")]
        public List<TradeData> Trades { get; set; } = new List<TradeData>();
    }

    public class TradeData
    {
        [JsonProperty("entryTime")]
        public DateTime EntryTime { get; set; }

        [JsonProperty("exitTime")]
        public DateTime ExitTime { get; set; }

        [JsonProperty("side")]
        public string Side { get; set; }

        [JsonProperty("quantity")]
        public int Quantity { get; set; }

        [JsonProperty("entryPrice")]
        public double EntryPrice { get; set; }

        [JsonProperty("exitPrice")]
        public double ExitPrice { get; set; }

        [JsonProperty("pnl")]
        public double PnL { get; set; }
    }
    #endregion

    #region System Commands
    public class PingCommand : BaseCommand
    {
        [JsonProperty("clientId")]
        public string ClientId { get; set; }
    }

    public class PongEvent : BaseEvent
    {
        [JsonProperty("serverTime")]
        public DateTime ServerTime { get; set; }

        [JsonProperty("latency")]
        public long LatencyMs { get; set; }
    }

    public class HeartbeatEvent : BaseEvent
    {
        [JsonProperty("serverTime")]
        public DateTime ServerTime { get; set; }

        [JsonProperty("connectedClients")]
        public int ConnectedClients { get; set; }

        [JsonProperty("activeSubscriptions")]
        public int ActiveSubscriptions { get; set; }
    }
    #endregion
} 