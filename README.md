# NinjaTrader High-Probability AutoTrade System

A sophisticated automated trading system for NinjaTrader 8 that uses multiple confirmation signals to achieve high-probability trade setups.

## Strategy Overview

This system combines multiple technical analysis techniques to identify high-probability trading opportunities:

### Core Components

1. **Trend Identification** - EMA crossover system (8/21/50 EMAs)
2. **Momentum Confirmation** - RSI with dynamic thresholds
3. **Volatility Filter** - ATR-based trade filtering
4. **Volume Confirmation** - Above-average volume requirement
5. **Time Filter** - Avoids low-liquidity periods

### Risk Management

- **Dynamic Position Sizing** - Based on account risk percentage
- **ATR-Based Stops** - Volatility-adjusted stop losses
- **Trailing Stops** - Lock in profits as trade moves favorably
- **Maximum Daily Loss** - Circuit breaker for losing days
- **Maximum Consecutive Losses** - Prevents revenge trading

## Installation

1. Copy the `.cs` files from the `Strategies/` folder to:
   ```
   Documents\NinjaTrader 8\bin\Custom\Strategies\
   ```

2. Copy the indicator files from `Indicators/` to:
   ```
   Documents\NinjaTrader 8\bin\Custom\Indicators\
   ```

3. In NinjaTrader 8, go to **Tools > Compile** to compile the strategies

4. Apply to chart or use in Strategy Analyzer for backtesting

## Configuration

### Key Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `RiskPercent` | 1.0 | Risk per trade as % of account |
| `FastEMA` | 8 | Fast EMA period |
| `SlowEMA` | 21 | Slow EMA period |
| `TrendEMA` | 50 | Trend filter EMA period |
| `RSIPeriod` | 14 | RSI calculation period |
| `ATRPeriod` | 14 | ATR calculation period |
| `ATRMultiplier` | 2.0 | Stop loss ATR multiplier |
| `EnableTimeFilter` | true | Enable trading time restrictions |
| `StartTime` | 09:30 | Trading start time (EST) |
| `EndTime` | 15:30 | Trading end time (EST) |

## Strategy Logic

### Long Entry Conditions (ALL must be true)
1. Fast EMA > Slow EMA (bullish crossover confirmed)
2. Price > Trend EMA (overall uptrend)
3. RSI > 40 and < 70 (momentum without overbought)
4. Current volume > 1.5x average volume
5. ATR > minimum threshold (sufficient volatility)
6. Within allowed trading hours
7. No existing position

### Short Entry Conditions (ALL must be true)
1. Fast EMA < Slow EMA (bearish crossover confirmed)
2. Price < Trend EMA (overall downtrend)
3. RSI < 60 and > 30 (momentum without oversold)
4. Current volume > 1.5x average volume
5. ATR > minimum threshold (sufficient volatility)
6. Within allowed trading hours
7. No existing position

### Exit Conditions
- Stop Loss: ATR-based (default 2x ATR)
- Profit Target: Risk:Reward based (default 1:2)
- Trailing Stop: Activates after 1x ATR profit
- Time Exit: Close all positions before market close

## Backtesting Results

Recommended backtesting parameters:
- **Instruments**: ES, NQ, CL, GC (futures) or liquid stocks
- **Timeframe**: 5-minute or 15-minute charts
- **Period**: Minimum 2 years of data
- **Commission**: Include realistic commission costs

## File Structure

```
├── README.md
├── Strategies/
│   ├── HighProbabilityAutoTrader.cs    # Main strategy
│   └── RiskManager.cs                   # Risk management module
├── Indicators/
│   ├── TrendStrength.cs                 # Custom trend indicator
│   └── VolumeProfile.cs                 # Volume analysis
├── Config/
│   └── DefaultSettings.xml              # Default configuration
└── Docs/
    └── BacktestingGuide.md              # Backtesting documentation
```

## Disclaimer

This trading system is for educational purposes only. Trading involves substantial risk of loss. Past performance is not indicative of future results. Always test thoroughly in simulation before trading with real money.

## License

MIT License - See LICENSE file for details
