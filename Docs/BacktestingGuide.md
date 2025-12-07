# Backtesting Guide for High-Probability AutoTrader

This guide provides comprehensive instructions for backtesting the NinjaTrader AutoTrader strategies to ensure they perform well before live trading.

## Table of Contents

1. [Setting Up Backtests](#setting-up-backtests)
2. [Key Metrics to Evaluate](#key-metrics-to-evaluate)
3. [Optimization Guidelines](#optimization-guidelines)
4. [Common Pitfalls](#common-pitfalls)
5. [Walk-Forward Analysis](#walk-forward-analysis)
6. [Monte Carlo Simulation](#monte-carlo-simulation)

---

## Setting Up Backtests

### Step 1: Data Requirements

Before backtesting, ensure you have quality historical data:

- **Minimum Data**: 2 years of historical data
- **Recommended**: 5+ years to capture different market conditions
- **Data Quality**: Use tick or 1-minute data for accurate backtesting
- **Include**: Commission, slippage, and realistic fill assumptions

### Step 2: NinjaTrader Strategy Analyzer Setup

1. Open **Tools > Strategy Analyzer**
2. Select your strategy (HighProbabilityAutoTrader, MomentumScalper, or MeanReversionTrader)
3. Configure the following settings:

```
Instrument: ES 03-24 (or your preferred instrument)
Data Series: 5 Minute
From: [Start Date - at least 2 years back]
To: [End Date]

Account Settings:
- Starting Capital: $50,000 (recommended)
- Commission: $4.00 per round turn (adjust to your broker)
- Slippage: 2 ticks

Execution:
- Fill Type: Conservative (more realistic)
- Order Fill Resolution: High
```

### Step 3: Backtest Settings

#### For Trend Following (HighProbabilityAutoTrader):
```
Timeframe: 5-minute or 15-minute
Best Markets: ES, NQ, CL, GC
Best Conditions: Trending markets (ADX > 25)
```

#### For Scalping (MomentumScalper):
```
Timeframe: 1-minute or 5-minute
Best Markets: ES, NQ (high liquidity)
Best Sessions: Regular trading hours (9:30-16:00 EST)
```

#### For Mean Reversion (MeanReversionTrader):
```
Timeframe: 5-minute or 15-minute
Best Markets: Any liquid market
Best Conditions: Ranging markets (ADX < 25)
```

---

## Key Metrics to Evaluate

### Primary Metrics (Must Meet Criteria)

| Metric | Minimum Target | Excellent |
|--------|---------------|-----------|
| Net Profit | Positive | > 20% annually |
| Profit Factor | > 1.3 | > 2.0 |
| Win Rate | > 45% | > 55% |
| Max Drawdown | < 20% | < 10% |
| Sharpe Ratio | > 0.5 | > 1.5 |

### Secondary Metrics (Important for Validation)

| Metric | Description | Target |
|--------|-------------|--------|
| Average Trade | Net profit per trade | > $50 |
| Largest Win | Single best trade | < 30% of net profit |
| Largest Loss | Single worst trade | < 5% of capital |
| Consecutive Losses | Max losing streak | < 6 |
| Recovery Factor | Net Profit / Max DD | > 3.0 |

### Risk Metrics

```
Risk-Adjusted Return = (Annual Return - Risk Free Rate) / Max Drawdown
Target: > 1.5

Expectancy = (Win% × Avg Win) - (Loss% × Avg Loss)
Target: > $20 per trade
```

---

## Optimization Guidelines

### What to Optimize

**Recommended for optimization:**
- ATR Stop/Target Multipliers
- RSI Thresholds
- Time Filters
- Volume Multiplier

**Avoid over-optimizing:**
- EMA Periods (use standard values)
- Risk Percent (set by risk tolerance)
- Max Daily Loss (safety parameter)

### Optimization Process

1. **Initial Backtest**: Run with default parameters
2. **Identify Weak Points**: Look for large drawdowns or losing periods
3. **Single Parameter Optimization**: Change ONE parameter at a time
4. **Cross-Validation**: Test optimized parameters on out-of-sample data

### Parameter Ranges for Optimization

```
ATR Stop Multiplier: 1.5 to 3.0, step 0.25
ATR Target Multiplier: 2.0 to 5.0, step 0.5
RSI Overbought: 65 to 80, step 5
RSI Oversold: 20 to 35, step 5
Volume Multiplier: 1.0 to 2.0, step 0.1
```

### Avoiding Over-Optimization

Signs of curve-fitting:
- Perfect equity curve with no drawdowns
- Parameters that seem arbitrary (e.g., 17-period EMA)
- Profit factor > 5.0 (unrealistic)
- Win rate > 75% (suspicious)

---

## Common Pitfalls

### 1. Survivorship Bias
- **Problem**: Only testing on successful instruments
- **Solution**: Test on a basket of instruments including some that underperformed

### 2. Look-Ahead Bias
- **Problem**: Strategy uses future data in calculations
- **Solution**: Ensure all indicators use only past data; use `[1]` indexing

### 3. Unrealistic Assumptions
- **Problem**: Not accounting for slippage, commissions, or fills
- **Solution**: Always include:
  - Commission: $4-6 per round turn
  - Slippage: 1-2 ticks minimum
  - Conservative fill assumptions

### 4. Selection Bias
- **Problem**: Cherry-picking favorable time periods
- **Solution**: Test across multiple market conditions:
  - Bull markets
  - Bear markets
  - Sideways/ranging markets
  - High volatility (VIX > 25)
  - Low volatility (VIX < 15)

### 5. Optimization Bias
- **Problem**: Over-fitting to historical data
- **Solution**:
  - Use walk-forward analysis
  - Reserve 30% of data for out-of-sample testing
  - Use parameter clustering (robust parameters perform well across a range)

---

## Walk-Forward Analysis

Walk-forward analysis helps validate that optimized parameters work on unseen data.

### Process

1. **Divide Data**: Split into In-Sample (IS) and Out-of-Sample (OOS)
   ```
   Example for 5 years of data:
   - IS Period 1: Year 1-3 → Test on Year 4
   - IS Period 2: Year 2-4 → Test on Year 5
   ```

2. **Optimize on IS**: Find best parameters

3. **Test on OOS**: Apply IS parameters to OOS data

4. **Evaluate**: Compare IS and OOS performance
   ```
   OOS Profit Factor should be > 60% of IS Profit Factor
   OOS Win Rate should be within 10% of IS Win Rate
   ```

### Walk-Forward Schedule

| Window | In-Sample Period | Out-of-Sample |
|--------|------------------|---------------|
| 1 | Jan 2020 - Dec 2021 | Jan 2022 - Jun 2022 |
| 2 | Jul 2020 - Jun 2022 | Jul 2022 - Dec 2022 |
| 3 | Jan 2021 - Dec 2022 | Jan 2023 - Jun 2023 |
| 4 | Jul 2021 - Jun 2023 | Jul 2023 - Dec 2023 |

---

## Monte Carlo Simulation

Monte Carlo simulation tests strategy robustness by randomizing trade order.

### Purpose

- Tests if results depend on specific trade sequence
- Estimates confidence intervals for returns and drawdowns
- Identifies worst-case scenarios

### How to Interpret Results

After 1,000+ simulations:

```
95% Confidence Interval for Annual Return: 15% - 45%
  → Expect returns in this range 95% of the time

95% Confidence Interval for Max Drawdown: 8% - 22%
  → Expect drawdown in this range 95% of the time

Probability of Positive Returns: 87%
  → 87% of simulations were profitable
```

### Minimum Requirements

| Metric | Requirement |
|--------|-------------|
| Probability of Profit | > 80% |
| Median Annual Return | > 10% |
| 95th Percentile Drawdown | < 25% |

---

## Pre-Live Trading Checklist

Before going live, confirm:

- [ ] Backtested on 2+ years of data
- [ ] Profit factor > 1.3 on out-of-sample data
- [ ] Max drawdown < 20% of capital
- [ ] Win rate > 45%
- [ ] Walk-forward analysis shows consistent results
- [ ] Monte Carlo simulation shows 80%+ probability of profit
- [ ] Tested on multiple instruments (if applicable)
- [ ] Commission and slippage included in tests
- [ ] Paper traded for minimum 30 days
- [ ] Risk parameters set appropriately for account size

---

## Sample Backtest Report Template

```
Strategy: HighProbabilityAutoTrader
Instrument: ES
Timeframe: 5-Minute
Period: Jan 2022 - Dec 2023

=== PERFORMANCE SUMMARY ===
Net Profit: $24,350
Gross Profit: $48,200
Gross Loss: $23,850
Profit Factor: 2.02

=== TRADE STATISTICS ===
Total Trades: 187
Winners: 108 (57.8%)
Losers: 79 (42.2%)
Average Win: $446.30
Average Loss: $301.89

=== RISK METRICS ===
Max Drawdown: $5,420 (10.8%)
Largest Win: $1,850
Largest Loss: $892
Max Consecutive Wins: 8
Max Consecutive Losses: 4

=== RISK-ADJUSTED METRICS ===
Sharpe Ratio: 1.72
Sortino Ratio: 2.31
Recovery Factor: 4.49

=== VALIDATION STATUS ===
Walk-Forward Efficiency: 72%
Monte Carlo (95% CI): 12% - 38% annual return
Recommendation: APPROVED FOR PAPER TRADING
```

---

## Next Steps

1. Run initial backtest with default parameters
2. Document baseline performance metrics
3. Optimize one parameter at a time
4. Perform walk-forward analysis
5. Run Monte Carlo simulation
6. Paper trade for 30+ days
7. Go live with reduced position size
8. Scale up gradually as confidence grows

**Remember**: Past performance does not guarantee future results. Always trade with capital you can afford to lose.
