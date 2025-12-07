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
    /// Momentum Scalper - Quick momentum-based trades with tight risk management
    /// Ideal for liquid instruments on short timeframes (1-5 minute charts)
    /// </summary>
    public class MomentumScalper : Strategy
    {
        #region Private Variables

        // Indicators
        private MACD macd;
        private Stochastics stoch;
        private EMA fastEMA;
        private EMA slowEMA;
        private ATR atr;
        private SMA volumeSMA;
        private Bollinger bb;

        // State tracking
        private double entryPrice;
        private double stopLossPrice;
        private int tradesToday;
        private double dailyPnL;
        private DateTime lastTradeDate;
        private int consecutiveWins;
        private int consecutiveLosses;
        private bool inCooldown;
        private int cooldownBars;

        // Signal tracking
        private double lastMACDHist;
        private bool macdCrossUp;
        private bool macdCrossDown;

        #endregion

        #region OnStateChange

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Momentum Scalper - Quick momentum trades with tight stops for scalping";
                Name = "MomentumScalper";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 1;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 50;
                IsInstantiatedOnEachOptimizationIteration = true;

                // MACD Parameters
                MACDFast = 12;
                MACDSlow = 26;
                MACDSignal = 9;

                // Stochastics Parameters
                StochPeriodD = 3;
                StochPeriodK = 14;
                StochSmooth = 3;
                StochOverbought = 80;
                StochOversold = 20;

                // EMA Parameters
                FastEMAPeriod = 9;
                SlowEMAPeriod = 21;

                // Bollinger Band Parameters
                BBPeriod = 20;
                BBStdDev = 2.0;

                // ATR Parameters
                ATRPeriod = 14;
                StopATRMultiplier = 1.5;
                TargetATRMultiplier = 2.0;

                // Volume Parameters
                VolumePeriod = 20;
                VolumeThreshold = 1.2;

                // Risk Management
                RiskPercent = 0.5;
                MaxDailyLossPercent = 2.0;
                MaxTradesPerDay = 10;
                MaxConsecutiveLosses = 4;
                CooldownBars = 5;

                // Time Filter
                EnableTimeFilter = true;
                TradingStartTime = DateTime.Parse("09:35", System.Globalization.CultureInfo.InvariantCulture);
                TradingEndTime = DateTime.Parse("15:45", System.Globalization.CultureInfo.InvariantCulture);

                // Trade Management
                EnableQuickExit = true;
                QuickExitBars = 10;

                // Display
                ShowSignals = true;
                EnableAlerts = true;
            }
            else if (State == State.DataLoaded)
            {
                // Initialize indicators
                macd = MACD(MACDFast, MACDSlow, MACDSignal);
                stoch = Stochastics(StochPeriodD, StochPeriodK, StochSmooth);
                fastEMA = EMA(FastEMAPeriod);
                slowEMA = EMA(SlowEMAPeriod);
                atr = ATR(ATRPeriod);
                volumeSMA = SMA(Volume, VolumePeriod);
                bb = Bollinger(BBStdDev, BBPeriod);

                // Add to chart
                AddChartIndicator(macd);
                AddChartIndicator(stoch);
                AddChartIndicator(fastEMA);
                AddChartIndicator(slowEMA);

                // Colors
                fastEMA.Plots[0].Brush = Brushes.Cyan;
                slowEMA.Plots[0].Brush = Brushes.Orange;

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

            // Track MACD crossovers
            TrackMACDCross();

            // Manage cooldown
            if (inCooldown)
            {
                cooldownBars--;
                if (cooldownBars <= 0)
                    inCooldown = false;
                return;
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

            // Check for new signals
            if (IsWithinTradingHours())
            {
                CheckForSignals();
            }
        }

        #endregion

        #region Signal Detection

        private void TrackMACDCross()
        {
            macdCrossUp = macd.Diff[0] > 0 && macd.Diff[1] <= 0;
            macdCrossDown = macd.Diff[0] < 0 && macd.Diff[1] >= 0;
            lastMACDHist = macd.Diff[0];
        }

        private void CheckForSignals()
        {
            bool longSetup = CheckLongSetup();
            bool shortSetup = CheckShortSetup();

            if (longSetup)
            {
                EnterLongTrade();
            }
            else if (shortSetup)
            {
                EnterShortTrade();
            }
        }

        private bool CheckLongSetup()
        {
            // Conditions for long entry:
            // 1. MACD histogram crossed above zero (within last 2 bars)
            bool macdBullish = macd.Diff[0] > 0 && CrossAbove(macd.Diff, 0, 2);

            // 2. Stochastics coming out of oversold OR in bullish momentum
            bool stochBullish = (stoch.K[0] > stoch.D[0]) &&
                               (stoch.K[1] <= StochOversold || stoch.K[0] > stoch.K[1]);

            // 3. Price above fast EMA
            bool priceAboveEMA = Close[0] > fastEMA[0];

            // 4. Fast EMA above slow EMA (trend confirmation)
            bool trendUp = fastEMA[0] > slowEMA[0];

            // 5. Volume confirmation
            bool volumeOK = Volume[0] > volumeSMA[0] * VolumeThreshold;

            // 6. Price bouncing off lower Bollinger or in lower half
            bool bbCondition = Close[0] < bb.Middle[0] || Low[0] <= bb.Lower[0];

            // Combined signal
            bool signal = macdBullish && stochBullish && priceAboveEMA && trendUp && volumeOK;

            if (signal && ShowSignals)
            {
                Draw.ArrowUp(this, "LongSig" + CurrentBar, false, 0, Low[0] - atr[0] * 0.5, Brushes.Lime);
            }

            return signal;
        }

        private bool CheckShortSetup()
        {
            // Conditions for short entry:
            // 1. MACD histogram crossed below zero (within last 2 bars)
            bool macdBearish = macd.Diff[0] < 0 && CrossBelow(macd.Diff, 0, 2);

            // 2. Stochastics coming out of overbought OR in bearish momentum
            bool stochBearish = (stoch.K[0] < stoch.D[0]) &&
                               (stoch.K[1] >= StochOverbought || stoch.K[0] < stoch.K[1]);

            // 3. Price below fast EMA
            bool priceBelowEMA = Close[0] < fastEMA[0];

            // 4. Fast EMA below slow EMA (trend confirmation)
            bool trendDown = fastEMA[0] < slowEMA[0];

            // 5. Volume confirmation
            bool volumeOK = Volume[0] > volumeSMA[0] * VolumeThreshold;

            // 6. Price bouncing off upper Bollinger or in upper half
            bool bbCondition = Close[0] > bb.Middle[0] || High[0] >= bb.Upper[0];

            // Combined signal
            bool signal = macdBearish && stochBearish && priceBelowEMA && trendDown && volumeOK;

            if (signal && ShowSignals)
            {
                Draw.ArrowDown(this, "ShortSig" + CurrentBar, false, 0, High[0] + atr[0] * 0.5, Brushes.Red);
            }

            return signal;
        }

        #endregion

        #region Trade Entry

        private void EnterLongTrade()
        {
            double currentATR = atr[0];
            int size = CalculatePositionSize(currentATR);

            if (size <= 0) return;

            stopLossPrice = Close[0] - (currentATR * StopATRMultiplier);
            double targetPrice = Close[0] + (currentATR * TargetATRMultiplier);

            EnterLong(size, "ScalpLong");
            SetStopLoss("ScalpLong", CalculationMode.Price, stopLossPrice, false);
            SetProfitTarget("ScalpLong", CalculationMode.Price, targetPrice);

            entryPrice = Close[0];
            tradesToday++;

            if (EnableAlerts)
                Alert("ScalpLong", Priority.Medium, "SCALP LONG Entry",
                      NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert2.wav", 10, Brushes.Green, Brushes.White);

            Print(Time[0] + " SCALP LONG @ " + Close[0] + " | Stop: " + stopLossPrice + " | Target: " + targetPrice);
        }

        private void EnterShortTrade()
        {
            double currentATR = atr[0];
            int size = CalculatePositionSize(currentATR);

            if (size <= 0) return;

            stopLossPrice = Close[0] + (currentATR * StopATRMultiplier);
            double targetPrice = Close[0] - (currentATR * TargetATRMultiplier);

            EnterShort(size, "ScalpShort");
            SetStopLoss("ScalpShort", CalculationMode.Price, stopLossPrice, false);
            SetProfitTarget("ScalpShort", CalculationMode.Price, targetPrice);

            entryPrice = Close[0];
            tradesToday++;

            if (EnableAlerts)
                Alert("ScalpShort", Priority.Medium, "SCALP SHORT Entry",
                      NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert2.wav", 10, Brushes.Red, Brushes.White);

            Print(Time[0] + " SCALP SHORT @ " + Close[0] + " | Stop: " + stopLossPrice + " | Target: " + targetPrice);
        }

        #endregion

        #region Position Management

        private void ManagePosition()
        {
            // Quick exit if trade goes against us and momentum reverses
            if (EnableQuickExit && BarsSinceEntryExecution() >= QuickExitBars)
            {
                if (Position.MarketPosition == MarketPosition.Long)
                {
                    // Exit if MACD turns negative while in long
                    if (macd.Diff[0] < 0 && Close[0] < entryPrice)
                    {
                        ExitLong("QuickExit");
                        StartCooldown();
                        return;
                    }
                }
                else if (Position.MarketPosition == MarketPosition.Short)
                {
                    // Exit if MACD turns positive while in short
                    if (macd.Diff[0] > 0 && Close[0] > entryPrice)
                    {
                        ExitShort("QuickExit");
                        StartCooldown();
                        return;
                    }
                }
            }

            // Move stop to breakeven after 1 ATR profit
            double currentATR = atr[0];

            if (Position.MarketPosition == MarketPosition.Long)
            {
                double profit = Close[0] - entryPrice;
                if (profit >= currentATR && stopLossPrice < entryPrice)
                {
                    double newStop = entryPrice + (2 * TickSize);
                    SetStopLoss("ScalpLong", CalculationMode.Price, newStop, false);
                    stopLossPrice = newStop;
                    Print(Time[0] + " - Moved stop to breakeven: " + newStop);
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                double profit = entryPrice - Close[0];
                if (profit >= currentATR && stopLossPrice > entryPrice)
                {
                    double newStop = entryPrice - (2 * TickSize);
                    SetStopLoss("ScalpShort", CalculationMode.Price, newStop, false);
                    stopLossPrice = newStop;
                    Print(Time[0] + " - Moved stop to breakeven: " + newStop);
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

        private void StartCooldown()
        {
            inCooldown = true;
            cooldownBars = CooldownBars;
        }

        #endregion

        #region Risk Management

        private bool PassesRiskChecks()
        {
            // Daily loss limit
            double maxLoss = Account.Get(AccountItem.CashValue, Currency.UsDollar) * (MaxDailyLossPercent / 100);
            if (dailyPnL <= -maxLoss)
                return false;

            // Max trades per day
            if (tradesToday >= MaxTradesPerDay)
                return false;

            // Consecutive losses
            if (consecutiveLosses >= MaxConsecutiveLosses)
                return false;

            return true;
        }

        private bool IsWithinTradingHours()
        {
            if (!EnableTimeFilter)
                return true;

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
                if (execution.Order.Name.Contains("Long") || execution.Order.Name.Contains("ScalpLong"))
                {
                    pnl = (price - entryPrice) * quantity * Instrument.MasterInstrument.PointValue;
                }
                else
                {
                    pnl = (entryPrice - price) * quantity * Instrument.MasterInstrument.PointValue;
                }

                dailyPnL += pnl;

                if (pnl > 0)
                {
                    consecutiveWins++;
                    consecutiveLosses = 0;
                }
                else
                {
                    consecutiveLosses++;
                    consecutiveWins = 0;
                    StartCooldown(); // Cooldown after loss
                }

                Print(Time[0] + " Trade P&L: $" + pnl.ToString("F2") + " | Daily: $" + dailyPnL.ToString("F2") +
                      " | Streak: " + (consecutiveWins > 0 ? "+" + consecutiveWins : "-" + consecutiveLosses));
            }
        }

        #endregion

        #region Helper Methods

        private void ResetDailyStats()
        {
            tradesToday = 0;
            dailyPnL = 0;
            consecutiveLosses = 0;
            consecutiveWins = 0;
            inCooldown = false;
        }

        #endregion

        #region Properties

        // MACD
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "MACD Fast", Order = 1, GroupName = "1. MACD")]
        public int MACDFast { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "MACD Slow", Order = 2, GroupName = "1. MACD")]
        public int MACDSlow { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "MACD Signal", Order = 3, GroupName = "1. MACD")]
        public int MACDSignal { get; set; }

        // Stochastics
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stoch Period D", Order = 1, GroupName = "2. Stochastics")]
        public int StochPeriodD { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stoch Period K", Order = 2, GroupName = "2. Stochastics")]
        public int StochPeriodK { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stoch Smooth", Order = 3, GroupName = "2. Stochastics")]
        public int StochSmooth { get; set; }

        [NinjaScriptProperty]
        [Range(50, 100)]
        [Display(Name = "Stoch Overbought", Order = 4, GroupName = "2. Stochastics")]
        public int StochOverbought { get; set; }

        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Stoch Oversold", Order = 5, GroupName = "2. Stochastics")]
        public int StochOversold { get; set; }

        // EMA
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Fast EMA", Order = 1, GroupName = "3. EMA")]
        public int FastEMAPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Slow EMA", Order = 2, GroupName = "3. EMA")]
        public int SlowEMAPeriod { get; set; }

        // Bollinger
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "BB Period", Order = 1, GroupName = "4. Bollinger Bands")]
        public int BBPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 5)]
        [Display(Name = "BB Std Dev", Order = 2, GroupName = "4. Bollinger Bands")]
        public double BBStdDev { get; set; }

        // ATR
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ATR Period", Order = 1, GroupName = "5. ATR")]
        public int ATRPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10)]
        [Display(Name = "Stop ATR Multiplier", Order = 2, GroupName = "5. ATR")]
        public double StopATRMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 20)]
        [Display(Name = "Target ATR Multiplier", Order = 3, GroupName = "5. ATR")]
        public double TargetATRMultiplier { get; set; }

        // Volume
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Volume Period", Order = 1, GroupName = "6. Volume")]
        public int VolumePeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10)]
        [Display(Name = "Volume Threshold", Order = 2, GroupName = "6. Volume")]
        public double VolumeThreshold { get; set; }

        // Risk Management
        [NinjaScriptProperty]
        [Range(0.1, 5)]
        [Display(Name = "Risk %", Order = 1, GroupName = "7. Risk Management")]
        public double RiskPercent { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 10)]
        [Display(Name = "Max Daily Loss %", Order = 2, GroupName = "7. Risk Management")]
        public double MaxDailyLossPercent { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Max Trades/Day", Order = 3, GroupName = "7. Risk Management")]
        public int MaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Consecutive Losses", Order = 4, GroupName = "7. Risk Management")]
        public int MaxConsecutiveLosses { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Cooldown Bars", Order = 5, GroupName = "7. Risk Management")]
        public int CooldownBars { get; set; }

        // Time Filter
        [NinjaScriptProperty]
        [Display(Name = "Enable Time Filter", Order = 1, GroupName = "8. Time Filter")]
        public bool EnableTimeFilter { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Start Time", Order = 2, GroupName = "8. Time Filter")]
        public DateTime TradingStartTime { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "End Time", Order = 3, GroupName = "8. Time Filter")]
        public DateTime TradingEndTime { get; set; }

        // Trade Management
        [NinjaScriptProperty]
        [Display(Name = "Enable Quick Exit", Order = 1, GroupName = "9. Trade Management")]
        public bool EnableQuickExit { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Quick Exit Bars", Order = 2, GroupName = "9. Trade Management")]
        public int QuickExitBars { get; set; }

        // Display
        [NinjaScriptProperty]
        [Display(Name = "Show Signals", Order = 1, GroupName = "10. Display")]
        public bool ShowSignals { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Alerts", Order = 2, GroupName = "10. Display")]
        public bool EnableAlerts { get; set; }

        #endregion
    }
}
