namespace MyBot.Core.Models;

/// <summary>Order side (buy or sell).</summary>
public enum OrderSide { Buy, Sell }

/// <summary>Order type.</summary>
public enum OrderType { Market, Limit, StopLoss, TakeProfit, StopLossLimit, TakeProfitLimit }

/// <summary>Order status.</summary>
public enum OrderStatus { New, PartiallyFilled, Filled, Canceled, Rejected, Expired, Unknown }

/// <summary>Time in force for limit orders.</summary>
public enum TimeInForce { GoodTillCanceled, ImmediateOrCancel, FillOrKill, GoodTillDate }
