#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// High Probability AutoTrader - A multi-confirmation trading system
    /// Uses trend, momentum, volatility, and volume filters for high-probability setups
    /// </summary>
    public class HighProbabilityAutoTrader : Strategy
    {
        #region Private Variables

        // Indicators
        private EMA fastEMA;
        private EMA slowEMA;
        private EMA trendEMA;
        private RSI rsi;
        private ATR atr;
        private SMA volumeSMA;

        // State tracking
        private int consecutiveLosses;
        private double dailyPnL;
        private DateTime lastTradeDate;
        private int tradesToday;
        private double entryPrice;
        private double stopLossPrice;
        private double profitTargetPrice;
        private bool trailingStopActive;
        private double trailingStopPrice;

        // Signal states
        private bool longSignal;
        private bool shortSignal;
        private int barssinceEntry;

        #endregion

        #region OnStateChange

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"High Probability AutoTrader - Multi-confirmation trading system with advanced risk management";
                Name = "HighProbabilityAutoTrader";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 2;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 60;
                IsInstantiatedOnEachOptimizationIteration = true;

                // Default parameters - Trend
                FastEMAPeriod = 8;
                SlowEMAPeriod = 21;
                TrendEMAPeriod = 50;

                // Default parameters - Momentum
                RSIPeriod = 14;
                RSIUpperThreshold = 70;
                RSILowerThreshold = 30;
                RSILongMin = 40;
                RSIShortMax = 60;

                // Default parameters - Volatility
                ATRPeriod = 14;
                ATRMultiplierStop = 2.0;
                ATRMultiplierTarget = 4.0;
                MinATRThreshold = 0.5;

                // Default parameters - Volume
                VolumeSMAPeriod = 20;
                VolumeMultiplier = 1.5;

                // Default parameters - Risk Management
                RiskPercent = 1.0;
                MaxDailyLossPercent = 3.0;
                MaxConsecutiveLosses = 3;
                MaxTradesPerDay = 5;

                // Default parameters - Time Filter
                EnableTimeFilter = true;
                TradingStartTime = DateTime.Parse("09:30", System.Globalization.CultureInfo.InvariantCulture);
                TradingEndTime = DateTime.Parse("15:30", System.Globalization.CultureInfo.InvariantCulture);

                // Default parameters - Trailing Stop
                EnableTrailingStop = true;
                TrailingStopActivationATR = 1.0;
                TrailingStopATR = 1.5;

                // Default parameters - Trade Management
                EnableBreakeven = true;
                BreakevenActivationATR = 1.0;

                // Display
                EnableAlerts = true;
                ShowSignalsOnChart = true;
            }
            else if (State == State.Configure)
            {
                // Add secondary data series if needed
            }
            else if (State == State.DataLoaded)
            {
                // Initialize indicators
                fastEMA = EMA(FastEMAPeriod);
                slowEMA = EMA(SlowEMAPeriod);
                trendEMA = EMA(TrendEMAPeriod);
                rsi = RSI(RSIPeriod, 3);
                atr = ATR(ATRPeriod);
                volumeSMA = SMA(Volume, VolumeSMAPeriod);

                // Add indicators to chart
                AddChartIndicator(fastEMA);
                AddChartIndicator(slowEMA);
                AddChartIndicator(trendEMA);

                // Set colors
                fastEMA.Plots[0].Brush = Brushes.Lime;
                slowEMA.Plots[0].Brush = Brushes.Red;
                trendEMA.Plots[0].Brush = Brushes.Gold;

                // Reset state
                ResetDailyStats();
            }
        }

        #endregion

        #region OnBarUpdate

        protected override void OnBarUpdate()
        {
            // Ensure we have enough bars
            if (CurrentBar < BarsRequiredToTrade)
                return;

            // Reset daily stats on new day
            if (Time[0].Date != lastTradeDate.Date)
            {
                ResetDailyStats();
                lastTradeDate = Time[0].Date;
            }

            // Check circuit breakers
            if (!PassesCircuitBreakers())
            {
                if (Position.MarketPosition != MarketPosition.Flat)
                    ExitAllPositions("Circuit Breaker");
                return;
            }

            // Update trailing stop if active
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                ManageOpenPosition();
                barssinceEntry++;
            }
            else
            {
                barssinceEntry = 0;
                trailingStopActive = false;
            }

            // Generate signals
            GenerateSignals();

            // Execute trades
            ExecuteTrades();
        }

        #endregion

        #region Signal Generation

        private void GenerateSignals()
        {
            longSignal = false;
            shortSignal = false;

            // Skip if we have a position or outside trading hours
            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            if (!IsWithinTradingHours())
                return;

            // Calculate confirmations
            bool trendBullish = IsTrendBullish();
            bool trendBearish = IsTrendBearish();
            bool momentumLong = IsMomentumLong();
            bool momentumShort = IsMomentumShort();
            bool volatilityOK = IsVolatilityAcceptable();
            bool volumeConfirmed = IsVolumeConfirmed();
            bool emaCrossUp = IsEMACrossUp();
            bool emaCrossDown = IsEMACrossDown();

            // Long signal - ALL conditions must be true
            longSignal = trendBullish &&
                        momentumLong &&
                        volatilityOK &&
                        volumeConfirmed &&
                        emaCrossUp;

            // Short signal - ALL conditions must be true
            shortSignal = trendBearish &&
                         momentumShort &&
                         volatilityOK &&
                         volumeConfirmed &&
                         emaCrossDown;

            // Draw signals on chart
            if (ShowSignalsOnChart)
            {
                if (longSignal)
                    Draw.ArrowUp(this, "LongSignal" + CurrentBar, false, 0, Low[0] - atr[0], Brushes.Lime);
                if (shortSignal)
                    Draw.ArrowDown(this, "ShortSignal" + CurrentBar, false, 0, High[0] + atr[0], Brushes.Red);
            }
        }

        #endregion

        #region Condition Checks

        private bool IsTrendBullish()
        {
            // Price above trend EMA and fast EMA above slow EMA
            return Close[0] > trendEMA[0] && fastEMA[0] > slowEMA[0];
        }

        private bool IsTrendBearish()
        {
            // Price below trend EMA and fast EMA below slow EMA
            return Close[0] < trendEMA[0] && fastEMA[0] < slowEMA[0];
        }

        private bool IsMomentumLong()
        {
            // RSI in bullish zone but not overbought
            return rsi[0] > RSILongMin && rsi[0] < RSIUpperThreshold;
        }

        private bool IsMomentumShort()
        {
            // RSI in bearish zone but not oversold
            return rsi[0] < RSIShortMax && rsi[0] > RSILowerThreshold;
        }

        private bool IsVolatilityAcceptable()
        {
            // ATR must be above minimum threshold for sufficient movement
            return atr[0] > MinATRThreshold * TickSize;
        }

        private bool IsVolumeConfirmed()
        {
            // Current volume above average * multiplier
            return Volume[0] > volumeSMA[0] * VolumeMultiplier;
        }

        private bool IsEMACrossUp()
        {
            // Fast EMA crossed above slow EMA within last 3 bars
            return CrossAbove(fastEMA, slowEMA, 3);
        }

        private bool IsEMACrossDown()
        {
            // Fast EMA crossed below slow EMA within last 3 bars
            return CrossBelow(fastEMA, slowEMA, 3);
        }

        private bool IsWithinTradingHours()
        {
            if (!EnableTimeFilter)
                return true;

            TimeSpan currentTime = Time[0].TimeOfDay;
            TimeSpan startTime = TradingStartTime.TimeOfDay;
            TimeSpan endTime = TradingEndTime.TimeOfDay;

            return currentTime >= startTime && currentTime <= endTime;
        }

        private bool PassesCircuitBreakers()
        {
            // Check max daily loss
            double maxDailyLoss = Account.Get(AccountItem.CashValue, Currency.UsDollar) * (MaxDailyLossPercent / 100);
            if (dailyPnL <= -maxDailyLoss)
            {
                if (EnableAlerts)
                    Alert("DailyLoss", Priority.High, "Max daily loss reached. Trading halted.",
                          NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert1.wav", 10, Brushes.Red, Brushes.White);
                return false;
            }

            // Check consecutive losses
            if (consecutiveLosses >= MaxConsecutiveLosses)
            {
                if (EnableAlerts)
                    Alert("ConsecLoss", Priority.High, "Max consecutive losses reached. Trading halted.",
                          NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert1.wav", 10, Brushes.Red, Brushes.White);
                return false;
            }

            // Check max trades per day
            if (tradesToday >= MaxTradesPerDay)
            {
                return false;
            }

            return true;
        }

        #endregion

        #region Trade Execution

        private void ExecuteTrades()
        {
            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            double currentATR = atr[0];
            int positionSize = CalculatePositionSize(currentATR);

            if (positionSize <= 0)
                return;

            if (longSignal)
            {
                // Calculate stop and target
                stopLossPrice = Close[0] - (currentATR * ATRMultiplierStop);
                profitTargetPrice = Close[0] + (currentATR * ATRMultiplierTarget);

                EnterLong(positionSize, "Long Entry");
                SetStopLoss("Long Entry", CalculationMode.Price, stopLossPrice, false);
                SetProfitTarget("Long Entry", CalculationMode.Price, profitTargetPrice);

                entryPrice = Close[0];
                tradesToday++;

                if (EnableAlerts)
                    Alert("LongEntry", Priority.Medium, "LONG entry signal triggered",
                          NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert2.wav", 10, Brushes.Green, Brushes.White);

                PrintTradeInfo("LONG", positionSize, Close[0], stopLossPrice, profitTargetPrice);
            }
            else if (shortSignal)
            {
                // Calculate stop and target
                stopLossPrice = Close[0] + (currentATR * ATRMultiplierStop);
                profitTargetPrice = Close[0] - (currentATR * ATRMultiplierTarget);

                EnterShort(positionSize, "Short Entry");
                SetStopLoss("Short Entry", CalculationMode.Price, stopLossPrice, false);
                SetProfitTarget("Short Entry", CalculationMode.Price, profitTargetPrice);

                entryPrice = Close[0];
                tradesToday++;

                if (EnableAlerts)
                    Alert("ShortEntry", Priority.Medium, "SHORT entry signal triggered",
                          NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert2.wav", 10, Brushes.Red, Brushes.White);

                PrintTradeInfo("SHORT", positionSize, Close[0], stopLossPrice, profitTargetPrice);
            }
        }

        #endregion

        #region Position Management

        private void ManageOpenPosition()
        {
            double currentATR = atr[0];

            if (Position.MarketPosition == MarketPosition.Long)
            {
                ManageLongPosition(currentATR);
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                ManageShortPosition(currentATR);
            }
        }

        private void ManageLongPosition(double currentATR)
        {
            double currentProfit = Close[0] - entryPrice;
            double activationDistance = currentATR * TrailingStopActivationATR;

            // Check for breakeven activation
            if (EnableBreakeven && !trailingStopActive && currentProfit >= currentATR * BreakevenActivationATR)
            {
                double breakevenStop = entryPrice + (TickSize * 2); // Small buffer above entry
                if (breakevenStop > stopLossPrice)
                {
                    SetStopLoss("Long Entry", CalculationMode.Price, breakevenStop, false);
                    stopLossPrice = breakevenStop;
                    Print(Time[0] + " - Breakeven stop activated at " + breakevenStop);
                }
            }

            // Check for trailing stop activation
            if (EnableTrailingStop && currentProfit >= activationDistance)
            {
                trailingStopActive = true;
                double newTrailingStop = Close[0] - (currentATR * TrailingStopATR);

                if (newTrailingStop > stopLossPrice)
                {
                    SetStopLoss("Long Entry", CalculationMode.Price, newTrailingStop, false);
                    stopLossPrice = newTrailingStop;
                    Print(Time[0] + " - Trailing stop updated to " + newTrailingStop);
                }
            }
        }

        private void ManageShortPosition(double currentATR)
        {
            double currentProfit = entryPrice - Close[0];
            double activationDistance = currentATR * TrailingStopActivationATR;

            // Check for breakeven activation
            if (EnableBreakeven && !trailingStopActive && currentProfit >= currentATR * BreakevenActivationATR)
            {
                double breakevenStop = entryPrice - (TickSize * 2); // Small buffer below entry
                if (breakevenStop < stopLossPrice)
                {
                    SetStopLoss("Short Entry", CalculationMode.Price, breakevenStop, false);
                    stopLossPrice = breakevenStop;
                    Print(Time[0] + " - Breakeven stop activated at " + breakevenStop);
                }
            }

            // Check for trailing stop activation
            if (EnableTrailingStop && currentProfit >= activationDistance)
            {
                trailingStopActive = true;
                double newTrailingStop = Close[0] + (currentATR * TrailingStopATR);

                if (newTrailingStop < stopLossPrice)
                {
                    SetStopLoss("Short Entry", CalculationMode.Price, newTrailingStop, false);
                    stopLossPrice = newTrailingStop;
                    Print(Time[0] + " - Trailing stop updated to " + newTrailingStop);
                }
            }
        }

        private void ExitAllPositions(string reason)
        {
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(reason);
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort(reason);
        }

        #endregion

        #region Position Sizing

        private int CalculatePositionSize(double currentATR)
        {
            // Risk-based position sizing
            double accountValue = Account.Get(AccountItem.CashValue, Currency.UsDollar);
            double riskAmount = accountValue * (RiskPercent / 100);
            double stopDistance = currentATR * ATRMultiplierStop;

            // Calculate position size based on risk
            double tickValue = Instrument.MasterInstrument.PointValue * TickSize;
            double stopTicks = stopDistance / TickSize;
            double riskPerContract = stopTicks * tickValue;

            if (riskPerContract <= 0)
                return 0;

            int positionSize = (int)Math.Floor(riskAmount / riskPerContract);

            // Ensure minimum of 1 contract
            return Math.Max(1, positionSize);
        }

        #endregion

        #region Trade Tracking

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
                                                   MarketPosition marketPosition, string orderId, DateTime time)
        {
            // Track P&L for risk management
            if (execution.Order.OrderState == OrderState.Filled)
            {
                if (Position.MarketPosition == MarketPosition.Flat && execution.Order.IsExitStrategy)
                {
                    double tradePnL = 0;

                    if (execution.Order.Name.Contains("Long"))
                    {
                        tradePnL = (price - entryPrice) * execution.Quantity * Instrument.MasterInstrument.PointValue;
                    }
                    else if (execution.Order.Name.Contains("Short"))
                    {
                        tradePnL = (entryPrice - price) * execution.Quantity * Instrument.MasterInstrument.PointValue;
                    }

                    dailyPnL += tradePnL;

                    if (tradePnL < 0)
                        consecutiveLosses++;
                    else
                        consecutiveLosses = 0;

                    Print(Time[0] + " - Trade closed. P&L: $" + tradePnL.ToString("F2") + " | Daily P&L: $" + dailyPnL.ToString("F2"));
                }
            }
        }

        #endregion

        #region Helper Methods

        private void ResetDailyStats()
        {
            dailyPnL = 0;
            tradesToday = 0;
            consecutiveLosses = 0;
        }

        private void PrintTradeInfo(string direction, int size, double entry, double stop, double target)
        {
            double riskTicks = Math.Abs(entry - stop) / TickSize;
            double rewardTicks = Math.Abs(target - entry) / TickSize;
            double rr = rewardTicks / riskTicks;

            Print("=== " + direction + " TRADE ===");
            Print("Time: " + Time[0]);
            Print("Size: " + size + " contracts");
            Print("Entry: " + entry);
            Print("Stop: " + stop + " (" + riskTicks.ToString("F0") + " ticks)");
            Print("Target: " + target + " (" + rewardTicks.ToString("F0") + " ticks)");
            Print("R:R = 1:" + rr.ToString("F2"));
            Print("RSI: " + rsi[0].ToString("F2"));
            Print("ATR: " + atr[0].ToString("F4"));
            Print("Volume Ratio: " + (Volume[0] / volumeSMA[0]).ToString("F2") + "x");
            Print("==================");
        }

        #endregion

        #region Properties

        // Trend Parameters
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Fast EMA Period", Order = 1, GroupName = "1. Trend Parameters")]
        public int FastEMAPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Slow EMA Period", Order = 2, GroupName = "1. Trend Parameters")]
        public int SlowEMAPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Trend EMA Period", Order = 3, GroupName = "1. Trend Parameters")]
        public int TrendEMAPeriod { get; set; }

        // Momentum Parameters
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "RSI Period", Order = 1, GroupName = "2. Momentum Parameters")]
        public int RSIPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(50, 100)]
        [Display(Name = "RSI Upper Threshold", Order = 2, GroupName = "2. Momentum Parameters")]
        public int RSIUpperThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "RSI Lower Threshold", Order = 3, GroupName = "2. Momentum Parameters")]
        public int RSILowerThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "RSI Long Minimum", Order = 4, GroupName = "2. Momentum Parameters")]
        public int RSILongMin { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "RSI Short Maximum", Order = 5, GroupName = "2. Momentum Parameters")]
        public int RSIShortMax { get; set; }

        // Volatility Parameters
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ATR Period", Order = 1, GroupName = "3. Volatility Parameters")]
        public int ATRPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10)]
        [Display(Name = "ATR Stop Multiplier", Order = 2, GroupName = "3. Volatility Parameters")]
        public double ATRMultiplierStop { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 20)]
        [Display(Name = "ATR Target Multiplier", Order = 3, GroupName = "3. Volatility Parameters")]
        public double ATRMultiplierTarget { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Minimum ATR Threshold", Order = 4, GroupName = "3. Volatility Parameters")]
        public double MinATRThreshold { get; set; }

        // Volume Parameters
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Volume SMA Period", Order = 1, GroupName = "4. Volume Parameters")]
        public int VolumeSMAPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10)]
        [Display(Name = "Volume Multiplier", Order = 2, GroupName = "4. Volume Parameters")]
        public double VolumeMultiplier { get; set; }

        // Risk Management
        [NinjaScriptProperty]
        [Range(0.1, 10)]
        [Display(Name = "Risk Percent Per Trade", Order = 1, GroupName = "5. Risk Management")]
        public double RiskPercent { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 20)]
        [Display(Name = "Max Daily Loss Percent", Order = 2, GroupName = "5. Risk Management")]
        public double MaxDailyLossPercent { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Consecutive Losses", Order = 3, GroupName = "5. Risk Management")]
        public int MaxConsecutiveLosses { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Max Trades Per Day", Order = 4, GroupName = "5. Risk Management")]
        public int MaxTradesPerDay { get; set; }

        // Time Filter
        [NinjaScriptProperty]
        [Display(Name = "Enable Time Filter", Order = 1, GroupName = "6. Time Filter")]
        public bool EnableTimeFilter { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Trading Start Time", Order = 2, GroupName = "6. Time Filter")]
        public DateTime TradingStartTime { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Trading End Time", Order = 3, GroupName = "6. Time Filter")]
        public DateTime TradingEndTime { get; set; }

        // Trailing Stop
        [NinjaScriptProperty]
        [Display(Name = "Enable Trailing Stop", Order = 1, GroupName = "7. Trailing Stop")]
        public bool EnableTrailingStop { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10)]
        [Display(Name = "Trailing Stop Activation (ATR)", Order = 2, GroupName = "7. Trailing Stop")]
        public double TrailingStopActivationATR { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10)]
        [Display(Name = "Trailing Stop Distance (ATR)", Order = 3, GroupName = "7. Trailing Stop")]
        public double TrailingStopATR { get; set; }

        // Breakeven
        [NinjaScriptProperty]
        [Display(Name = "Enable Breakeven", Order = 1, GroupName = "8. Breakeven")]
        public bool EnableBreakeven { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10)]
        [Display(Name = "Breakeven Activation (ATR)", Order = 2, GroupName = "8. Breakeven")]
        public double BreakevenActivationATR { get; set; }

        // Display
        [NinjaScriptProperty]
        [Display(Name = "Enable Alerts", Order = 1, GroupName = "9. Display")]
        public bool EnableAlerts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Signals On Chart", Order = 2, GroupName = "9. Display")]
        public bool ShowSignalsOnChart { get; set; }

        #endregion
    }
}
