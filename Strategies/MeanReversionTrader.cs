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
    /// Mean Reversion Trader - Trades price reversions to the mean
    /// Uses Bollinger Bands, RSI, and Keltner Channels for high-probability mean reversion setups
    /// Works best in ranging/consolidating markets
    /// </summary>
    public class MeanReversionTrader : Strategy
    {
        #region Private Variables

        // Indicators
        private Bollinger bb;
        private RSI rsi;
        private ATR atr;
        private SMA sma;
        private EMA ema;
        private SMA volumeSMA;
        private ADX adx;

        // Keltner Channel components
        private EMA keltnerMiddle;
        private ATR keltnerATR;

        // State tracking
        private double entryPrice;
        private double stopLossPrice;
        private double targetPrice;
        private int tradesToday;
        private double dailyPnL;
        private DateTime lastTradeDate;
        private int consecutiveLosses;
        private int barsInTrade;

        // Signal tracking
        private bool oversoldSignal;
        private bool overboughtSignal;
        private int barsAtExtreme;

        #endregion

        #region OnStateChange

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Mean Reversion Trader - Trades bounces from extreme price levels back to the mean";
                Name = "MeanReversionTrader";
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
                BarsRequiredToTrade = 50;
                IsInstantiatedOnEachOptimizationIteration = true;

                // Bollinger Band Parameters
                BBPeriod = 20;
                BBStdDev = 2.0;

                // RSI Parameters
                RSIPeriod = 14;
                RSIOverbought = 75;
                RSIOversold = 25;

                // Mean Parameters
                MeanPeriod = 20;
                MeanType = MeanCalculation.SMA;

                // Keltner Channel Parameters
                KeltnerPeriod = 20;
                KeltnerATRMult = 1.5;

                // ADX Filter
                ADXPeriod = 14;
                MaxADX = 25; // Only trade when ADX is below this (ranging market)

                // ATR Parameters
                ATRPeriod = 14;
                StopATRMultiplier = 2.5;

                // Volume Parameters
                VolumePeriod = 20;
                VolumeMultiplier = 1.0;

                // Entry Filters
                RequireBBSqueeze = false;
                SqueezeThreshold = 0.5;
                RequireDoubleConfirm = true;
                WaitBarsAtExtreme = 1;

                // Target Settings
                TargetToMean = true;
                FixedTargetATR = 3.0;

                // Risk Management
                RiskPercent = 1.0;
                MaxDailyLossPercent = 2.5;
                MaxTradesPerDay = 6;
                MaxConsecutiveLosses = 3;
                MaxBarsInTrade = 20;

                // Time Filter
                EnableTimeFilter = true;
                TradingStartTime = DateTime.Parse("09:45", System.Globalization.CultureInfo.InvariantCulture);
                TradingEndTime = DateTime.Parse("15:30", System.Globalization.CultureInfo.InvariantCulture);

                // Display
                ShowSignals = true;
                EnableAlerts = true;
            }
            else if (State == State.DataLoaded)
            {
                // Initialize indicators
                bb = Bollinger(BBStdDev, BBPeriod);
                rsi = RSI(RSIPeriod, 3);
                atr = ATR(ATRPeriod);
                sma = SMA(MeanPeriod);
                ema = EMA(MeanPeriod);
                volumeSMA = SMA(Volume, VolumePeriod);
                adx = ADX(ADXPeriod);

                // Keltner Channel
                keltnerMiddle = EMA(KeltnerPeriod);
                keltnerATR = ATR(KeltnerPeriod);

                // Add to chart
                AddChartIndicator(bb);
                AddChartIndicator(rsi);

                // Reset stats
                ResetDailyStats();
            }
        }

        #endregion

        #region OnBarUpdate

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade)
                return;

            // Reset daily stats
            if (Time[0].Date != lastTradeDate.Date)
            {
                ResetDailyStats();
                lastTradeDate = Time[0].Date;
            }

            // Check circuit breakers
            if (!PassesRiskChecks())
            {
                if (Position.MarketPosition != MarketPosition.Flat)
                    ExitAllPositions("Risk Limit");
                return;
            }

            // Manage existing position
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                ManagePosition();
                return;
            }

            // Reset extreme tracking when flat
            barsInTrade = 0;

            // Check for new signals during trading hours
            if (IsWithinTradingHours())
            {
                CheckForSignals();
            }
        }

        #endregion

        #region Signal Detection

        private void CheckForSignals()
        {
            // Only trade in ranging markets (low ADX)
            if (adx[0] > MaxADX)
            {
                barsAtExtreme = 0;
                return;
            }

            // Calculate Keltner Channel bounds
            double keltnerUpper = keltnerMiddle[0] + (keltnerATR[0] * KeltnerATRMult);
            double keltnerLower = keltnerMiddle[0] - (keltnerATR[0] * KeltnerATRMult);

            // Check for squeeze if required
            if (RequireBBSqueeze)
            {
                double bbWidth = (bb.Upper[0] - bb.Lower[0]) / bb.Middle[0];
                double avgWidth = 0;
                for (int i = 0; i < 20; i++)
                    avgWidth += (bb.Upper[i] - bb.Lower[i]) / bb.Middle[i];
                avgWidth /= 20;

                // Skip if BB is expanded (not in squeeze)
                if (bbWidth > avgWidth * (1 + SqueezeThreshold))
                    return;
            }

            // OVERSOLD SETUP (Long Entry)
            bool priceAtLowerBB = Low[0] <= bb.Lower[0];
            bool priceAtLowerKeltner = Low[0] <= keltnerLower;
            bool rsiOversold = rsi[0] <= RSIOversold;
            bool volumeOK = Volume[0] >= volumeSMA[0] * VolumeMultiplier;

            // Check for bullish reversal candle
            bool bullishCandle = Close[0] > Open[0] && Close[0] > (Low[0] + (High[0] - Low[0]) * 0.5);

            if (priceAtLowerBB && rsiOversold)
            {
                barsAtExtreme++;

                if (RequireDoubleConfirm)
                {
                    // Need price at both BB and Keltner lower bands
                    oversoldSignal = priceAtLowerBB && priceAtLowerKeltner && rsiOversold &&
                                    volumeOK && bullishCandle && barsAtExtreme >= WaitBarsAtExtreme;
                }
                else
                {
                    oversoldSignal = priceAtLowerBB && rsiOversold && volumeOK &&
                                    bullishCandle && barsAtExtreme >= WaitBarsAtExtreme;
                }
            }
            else if (!priceAtLowerBB)
            {
                if (barsAtExtreme > 0) barsAtExtreme = 0;
                oversoldSignal = false;
            }

            // OVERBOUGHT SETUP (Short Entry)
            bool priceAtUpperBB = High[0] >= bb.Upper[0];
            bool priceAtUpperKeltner = High[0] >= keltnerUpper;
            bool rsiOverbought = rsi[0] >= RSIOverbought;

            // Check for bearish reversal candle
            bool bearishCandle = Close[0] < Open[0] && Close[0] < (High[0] - (High[0] - Low[0]) * 0.5);

            if (priceAtUpperBB && rsiOverbought)
            {
                barsAtExtreme++;

                if (RequireDoubleConfirm)
                {
                    overboughtSignal = priceAtUpperBB && priceAtUpperKeltner && rsiOverbought &&
                                      volumeOK && bearishCandle && barsAtExtreme >= WaitBarsAtExtreme;
                }
                else
                {
                    overboughtSignal = priceAtUpperBB && rsiOverbought && volumeOK &&
                                      bearishCandle && barsAtExtreme >= WaitBarsAtExtreme;
                }
            }
            else if (!priceAtUpperBB)
            {
                if (barsAtExtreme > 0) barsAtExtreme = 0;
                overboughtSignal = false;
            }

            // Execute trades
            if (oversoldSignal)
            {
                EnterLongReversion();
                barsAtExtreme = 0;
            }
            else if (overboughtSignal)
            {
                EnterShortReversion();
                barsAtExtreme = 0;
            }

            // Draw signals
            if (ShowSignals)
            {
                if (priceAtLowerBB && rsiOversold)
                    Draw.Diamond(this, "OS" + CurrentBar, false, 0, Low[0] - atr[0] * 0.5, Brushes.Lime);
                if (priceAtUpperBB && rsiOverbought)
                    Draw.Diamond(this, "OB" + CurrentBar, false, 0, High[0] + atr[0] * 0.5, Brushes.Red);
            }
        }

        #endregion

        #region Trade Entry

        private void EnterLongReversion()
        {
            double currentATR = atr[0];
            int size = CalculatePositionSize(currentATR);

            if (size <= 0) return;

            // Stop below the recent low
            stopLossPrice = Low[0] - (currentATR * StopATRMultiplier);

            // Target to mean or fixed
            if (TargetToMean)
            {
                targetPrice = MeanType == MeanCalculation.SMA ? sma[0] : ema[0];
            }
            else
            {
                targetPrice = Close[0] + (currentATR * FixedTargetATR);
            }

            EnterLong(size, "MRLong");
            SetStopLoss("MRLong", CalculationMode.Price, stopLossPrice, false);
            SetProfitTarget("MRLong", CalculationMode.Price, targetPrice);

            entryPrice = Close[0];
            tradesToday++;
            oversoldSignal = false;

            if (EnableAlerts)
                Alert("MRLong", Priority.Medium, "Mean Reversion LONG - Price at lower extreme",
                      NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert2.wav", 10, Brushes.Green, Brushes.White);

            Print(Time[0] + " MR LONG @ " + Close[0] + " | Stop: " + stopLossPrice.ToString("F2") +
                  " | Target (Mean): " + targetPrice.ToString("F2") + " | RSI: " + rsi[0].ToString("F1"));
        }

        private void EnterShortReversion()
        {
            double currentATR = atr[0];
            int size = CalculatePositionSize(currentATR);

            if (size <= 0) return;

            // Stop above the recent high
            stopLossPrice = High[0] + (currentATR * StopATRMultiplier);

            // Target to mean or fixed
            if (TargetToMean)
            {
                targetPrice = MeanType == MeanCalculation.SMA ? sma[0] : ema[0];
            }
            else
            {
                targetPrice = Close[0] - (currentATR * FixedTargetATR);
            }

            EnterShort(size, "MRShort");
            SetStopLoss("MRShort", CalculationMode.Price, stopLossPrice, false);
            SetProfitTarget("MRShort", CalculationMode.Price, targetPrice);

            entryPrice = Close[0];
            tradesToday++;
            overboughtSignal = false;

            if (EnableAlerts)
                Alert("MRShort", Priority.Medium, "Mean Reversion SHORT - Price at upper extreme",
                      NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert2.wav", 10, Brushes.Red, Brushes.White);

            Print(Time[0] + " MR SHORT @ " + Close[0] + " | Stop: " + stopLossPrice.ToString("F2") +
                  " | Target (Mean): " + targetPrice.ToString("F2") + " | RSI: " + rsi[0].ToString("F1"));
        }

        #endregion

        #region Position Management

        private void ManagePosition()
        {
            barsInTrade++;

            // Exit if been in trade too long (time stop)
            if (barsInTrade >= MaxBarsInTrade)
            {
                ExitAllPositions("TimeStop");
                Print(Time[0] + " - Time stop triggered after " + barsInTrade + " bars");
                return;
            }

            // Trail stop towards mean as price approaches target
            double currentMean = MeanType == MeanCalculation.SMA ? sma[0] : ema[0];

            if (Position.MarketPosition == MarketPosition.Long)
            {
                // If price has moved halfway to mean, tighten stop
                double progressToMean = (Close[0] - entryPrice) / (targetPrice - entryPrice);

                if (progressToMean >= 0.5)
                {
                    double newStop = entryPrice + (atr[0] * 0.5); // Move stop to small profit
                    if (newStop > stopLossPrice)
                    {
                        SetStopLoss("MRLong", CalculationMode.Price, newStop, false);
                        stopLossPrice = newStop;
                    }
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                double progressToMean = (entryPrice - Close[0]) / (entryPrice - targetPrice);

                if (progressToMean >= 0.5)
                {
                    double newStop = entryPrice - (atr[0] * 0.5);
                    if (newStop < stopLossPrice)
                    {
                        SetStopLoss("MRShort", CalculationMode.Price, newStop, false);
                        stopLossPrice = newStop;
                    }
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

        #region Risk Management

        private bool PassesRiskChecks()
        {
            double maxLoss = Account.Get(AccountItem.CashValue, Currency.UsDollar) * (MaxDailyLossPercent / 100);
            if (dailyPnL <= -maxLoss) return false;
            if (tradesToday >= MaxTradesPerDay) return false;
            if (consecutiveLosses >= MaxConsecutiveLosses) return false;
            return true;
        }

        private bool IsWithinTradingHours()
        {
            if (!EnableTimeFilter) return true;
            TimeSpan current = Time[0].TimeOfDay;
            return current >= TradingStartTime.TimeOfDay && current <= TradingEndTime.TimeOfDay;
        }

        private int CalculatePositionSize(double currentATR)
        {
            double accountValue = Account.Get(AccountItem.CashValue, Currency.UsDollar);
            double riskAmount = accountValue * (RiskPercent / 100);
            double stopDistance = currentATR * StopATRMultiplier;
            double tickValue = Instrument.MasterInstrument.PointValue * TickSize;
            double riskPerContract = (stopDistance / TickSize) * tickValue;
            if (riskPerContract <= 0) return 0;
            return Math.Max(1, (int)Math.Floor(riskAmount / riskPerContract));
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
                                                   MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution.Order.OrderState == OrderState.Filled && Position.MarketPosition == MarketPosition.Flat)
            {
                double pnl = 0;
                if (execution.Order.Name.Contains("Long") || execution.Order.Name.Contains("MRLong"))
                    pnl = (price - entryPrice) * quantity * Instrument.MasterInstrument.PointValue;
                else
                    pnl = (entryPrice - price) * quantity * Instrument.MasterInstrument.PointValue;

                dailyPnL += pnl;
                if (pnl < 0) consecutiveLosses++;
                else consecutiveLosses = 0;

                Print(Time[0] + " MR Trade P&L: $" + pnl.ToString("F2") + " | Daily: $" + dailyPnL.ToString("F2"));
            }
        }

        #endregion

        #region Helper Methods

        private void ResetDailyStats()
        {
            tradesToday = 0;
            dailyPnL = 0;
            consecutiveLosses = 0;
            barsAtExtreme = 0;
        }

        #endregion

        #region Enums

        public enum MeanCalculation
        {
            SMA,
            EMA
        }

        #endregion

        #region Properties

        // Bollinger
        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "BB Period", Order = 1, GroupName = "1. Bollinger Bands")]
        public int BBPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 4)]
        [Display(Name = "BB Std Dev", Order = 2, GroupName = "1. Bollinger Bands")]
        public double BBStdDev { get; set; }

        // RSI
        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "RSI Period", Order = 1, GroupName = "2. RSI")]
        public int RSIPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(60, 95)]
        [Display(Name = "RSI Overbought", Order = 2, GroupName = "2. RSI")]
        public int RSIOverbought { get; set; }

        [NinjaScriptProperty]
        [Range(5, 40)]
        [Display(Name = "RSI Oversold", Order = 3, GroupName = "2. RSI")]
        public int RSIOversold { get; set; }

        // Mean
        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Mean Period", Order = 1, GroupName = "3. Mean")]
        public int MeanPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mean Type", Order = 2, GroupName = "3. Mean")]
        public MeanCalculation MeanType { get; set; }

        // Keltner
        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Keltner Period", Order = 1, GroupName = "4. Keltner Channel")]
        public int KeltnerPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 5)]
        [Display(Name = "Keltner ATR Mult", Order = 2, GroupName = "4. Keltner Channel")]
        public double KeltnerATRMult { get; set; }

        // ADX
        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "ADX Period", Order = 1, GroupName = "5. ADX Filter")]
        public int ADXPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(10, 50)]
        [Display(Name = "Max ADX (Range Filter)", Order = 2, GroupName = "5. ADX Filter")]
        public int MaxADX { get; set; }

        // ATR
        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "ATR Period", Order = 1, GroupName = "6. ATR")]
        public int ATRPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 5)]
        [Display(Name = "Stop ATR Mult", Order = 2, GroupName = "6. ATR")]
        public double StopATRMultiplier { get; set; }

        // Volume
        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Volume Period", Order = 1, GroupName = "7. Volume")]
        public int VolumePeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 5)]
        [Display(Name = "Volume Multiplier", Order = 2, GroupName = "7. Volume")]
        public double VolumeMultiplier { get; set; }

        // Entry Filters
        [NinjaScriptProperty]
        [Display(Name = "Require BB Squeeze", Order = 1, GroupName = "8. Entry Filters")]
        public bool RequireBBSqueeze { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 2)]
        [Display(Name = "Squeeze Threshold", Order = 2, GroupName = "8. Entry Filters")]
        public double SqueezeThreshold { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Double Confirm", Order = 3, GroupName = "8. Entry Filters")]
        public bool RequireDoubleConfirm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10)]
        [Display(Name = "Wait Bars At Extreme", Order = 4, GroupName = "8. Entry Filters")]
        public int WaitBarsAtExtreme { get; set; }

        // Target
        [NinjaScriptProperty]
        [Display(Name = "Target To Mean", Order = 1, GroupName = "9. Target")]
        public bool TargetToMean { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Fixed Target ATR", Order = 2, GroupName = "9. Target")]
        public double FixedTargetATR { get; set; }

        // Risk Management
        [NinjaScriptProperty]
        [Range(0.1, 5)]
        [Display(Name = "Risk %", Order = 1, GroupName = "10. Risk Management")]
        public double RiskPercent { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 10)]
        [Display(Name = "Max Daily Loss %", Order = 2, GroupName = "10. Risk Management")]
        public double MaxDailyLossPercent { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Trades/Day", Order = 3, GroupName = "10. Risk Management")]
        public int MaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Max Consecutive Losses", Order = 4, GroupName = "10. Risk Management")]
        public int MaxConsecutiveLosses { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Max Bars In Trade", Order = 5, GroupName = "10. Risk Management")]
        public int MaxBarsInTrade { get; set; }

        // Time Filter
        [NinjaScriptProperty]
        [Display(Name = "Enable Time Filter", Order = 1, GroupName = "11. Time Filter")]
        public bool EnableTimeFilter { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Start Time", Order = 2, GroupName = "11. Time Filter")]
        public DateTime TradingStartTime { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "End Time", Order = 3, GroupName = "11. Time Filter")]
        public DateTime TradingEndTime { get; set; }

        // Display
        [NinjaScriptProperty]
        [Display(Name = "Show Signals", Order = 1, GroupName = "12. Display")]
        public bool ShowSignals { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Alerts", Order = 2, GroupName = "12. Display")]
        public bool EnableAlerts { get; set; }

        #endregion
    }
}
