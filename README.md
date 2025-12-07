# NinjaTrader High-Probability AutoTrade System

A sophisticated automated trading system for NinjaTrader 8 that uses multiple confirmation signals to achieve high-probability trade setups.

## Strategy Overview

This system combines multiple technical analysis techniques to identify high-probability trading opportunities:

### Core Components

1. **Trend Identification** - EMA crossover system (8/21/50 EMAs)
2. **Momentum Confirmation** - RSI with dynamic thresholds
3. **WTMomentum Filter** - Wave Trend momentum for enhanced confirmation
4. **Volatility Filter** - ATR-based trade filtering
5. **Volume Confirmation** - Above-average volume requirement
6. **Time Filter** - Avoids low-liquidity periods

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
| `EnableWTMomentum` | true | Enable WTMomentum filter |
| `WTSensitivity` | 10 | WTMomentum sensitivity (lower = more responsive) |
| `WTThreshold` | 53 | Overbought/Oversold threshold level |

## Strategy Logic

### Long Entry Conditions (ALL must be true)
1. Fast EMA > Slow EMA (bullish crossover confirmed)
2. Price > Trend EMA (overall uptrend)
3. RSI > 40 and < 70 (momentum without overbought)
4. WTMomentum positive and rising, or crossing up from oversold
5. Current volume > 1.5x average volume
6. ATR > minimum threshold (sufficient volatility)
7. Within allowed trading hours
8. No existing position

### Short Entry Conditions (ALL must be true)
1. Fast EMA < Slow EMA (bearish crossover confirmed)
2. Price < Trend EMA (overall downtrend)
3. RSI < 60 and > 30 (momentum without oversold)
4. WTMomentum negative and falling, or crossing down from overbought
5. Current volume > 1.5x average volume
6. ATR > minimum threshold (sufficient volatility)
7. Within allowed trading hours
8. No existing position

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
│   ├── HighProbabilityAutoTrader.cs    # Main strategy with WTMomentum
│   ├── MomentumScalper.cs              # Quick momentum scalping strategy
│   └── MeanReversionTrader.cs          # Mean reversion strategy
├── Indicators/
│   ├── TrendStrength.cs                 # Custom trend indicator
│   └── VolumeProfile.cs                 # Volume analysis
├── Config/
│   └── DefaultSettings.xml              # Default configuration
└── Docs/
    └── BacktestingGuide.md              # Backtesting documentation
```

## WTMomentum Integration

The strategy integrates with the **WTMomentum** (Wave Trend Momentum) indicator for enhanced momentum confirmation. This indicator provides:

- **Overbought/Oversold Detection**: Identifies extreme momentum levels
- **Momentum Direction**: Confirms if momentum is aligned with the trade direction
- **Reversal Signals**: Detects potential trend reversals at threshold crossings

### WTMomentum Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `EnableWTMomentum` | true | Toggle WTMomentum filter on/off |
| `WTSensitivity` | 10 | Controls indicator responsiveness (1-50) |
| `WTThreshold` | 53 | Overbought/Oversold level (10-100) |
| `WTColorBars` | true | Color price bars based on momentum |

**Note**: You must have the WTMomentum indicator installed in your NinjaTrader 8 for this feature to work. If not installed, set `EnableWTMomentum = false`.

## Disclaimer

This trading system is for educational purposes only. Trading involves substantial risk of loss. Past performance is not indicative of future results. Always test thoroughly in simulation before trading with real money.

## License

MIT License - See LICENSE file for details
