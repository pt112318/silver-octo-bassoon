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
    /// Uses trend, momentum (RSI + WTMomentum), volatility, and volume filters for high-probability setups
    /// Includes optional WTMomentum indicator for enhanced momentum confirmation
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

        // WTMomentum indicator
        private WTMomentum wtMomentum;

        // WTBarsV3 indicator
        private WTBarsV3 wtBars;

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

                // Default parameters - WTMomentum
                EnableWTMomentum = true;
                WTSensitivity = 10;
                WTThreshold = 53;
                WTColorBars = true;

                // Default parameters - WTBarsV3
                EnableWTBars = false;
                WTBarsPeriod = 10;
                WTBarsVersion = 1;
                WTBarsShadowWidth = 2;
                WTBarsColorsByMomo = true;
                WTBarsColorsByMomoSensitivity = 10;
                WTBarsThreshold = 53;
                WTBarsPlotTI = false;
                WTBarsPeriod1 = 9;
                WTBarsPeriod2 = 21;
                WTBarsLevel1 = 60;
                WTBarsLevel2 = 53;
                WTBarsIntentOffsetTics = 10;
                WTBarsTIThresholdPct = 50;
                WTBarsRangeBracket = false;
                WTBarsLineThickness = 2;
                WTBarsShowExitProjection = false;
                WTBarsStopBarCount = 3;
                WTBarsStopBarOffsetSteps = 2;
                WTBarsShowStackedBars = false;
                WTBarsStackedNBars = 3;
                WTBarsStackedTrendBars = 2;
                WTBarsStackedResetBars = 1;
                WTBarsStackedResetAtAWstart = true;
                WTBarsShowPotentialSetups = false;
                WTBarsEntryOffsetSteps = 2;
                WTBarsInitialStopSteps = 8;
                WTBarsFixedTargetSteps = 16;
                WTBarsMoneyMgtPct = 50;
                WTBarsMatchesForSetup = 3;
                WTBarsActiveModeStartTime = 930;
                WTBarsActiveModeMinutes = 390;
                WTBarsTrendRiderMinutes = 60;

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

                // Initialize WTMomentum indicator
                if (EnableWTMomentum)
                {
                    wtMomentum = WTMomentum(
                        WTSensitivity,           // sensitivity
                        WTColorBars,             // colorBars
                        WTThreshold,             // threshold
                        Brushes.DimGray,         // thresholdLineColor
                        Brushes.Lime,            // upperThresholdMarkerColor
                        Brushes.Red,             // lowerThresholdMarkerColor
                        Brushes.Red,             // negativeBarColor
                        Brushes.Lime,            // positiveBarColor
                        Brushes.White,           // wickColor
                        false,                   // verticalLines
                        new Stroke(Brushes.Green, 1),  // verticalLineUp
                        new Stroke(Brushes.Red, 1),    // verticalLineDown
                        @"",                     // alertSoundToPlay
                        false                    // alertSounds
                    );
                    AddChartIndicator(wtMomentum);
                }

                // Initialize WTBarsV3 indicator
                if (EnableWTBars)
                {
                    wtBars = WTBarsV3(
                        WTBarsPeriod,                    // wTPeriod
                        WTBarsVersion,                   // wTVersion
                        Brushes.Lime,                    // barColorUp
                        Brushes.Red,                     // barColorDown
                        Brushes.DimGray,                 // shadowColor
                        Brushes.Yellow,                  // dojiColor
                        Brushes.Gray,                    // neutralIntentColor
                        WTBarsShadowWidth,               // shadowWidth
                        @"",                             // sound_MomentumBar
                        @"",                             // sound_DojiBar
                        @"",                             // sound_PauseBar
                        @"",                             // sound_NewSetupBar
                        false,                           // alert_MomentumBar
                        false,                           // alert_DojiBar
                        false,                           // alert_PauseBar
                        false,                           // alert_NewSetupBar
                        WTBarsColorsByMomo,              // colorsByMomo
                        WTBarsColorsByMomoSensitivity,   // colorsByMomo_Sensitivity
                        WTBarsThreshold,                 // threshold
                        WTBarsPlotTI,                    // plot_TI
                        WTBarsPeriod1,                   // period1
                        WTBarsPeriod2,                   // period2
                        WTBarsLevel1,                    // level1
                        WTBarsLevel2,                    // level2
                        WTBarsIntentOffsetTics,          // intentOffsetTics
                        WTBarsTIThresholdPct,            // tI_ThresholdPct
                        WTBarsRangeBracket,              // rangeBracket
                        WTBarsLineThickness,             // lineThickness
                        Brushes.Lime,                    // closeUpColor
                        Brushes.Red,                     // closeDownColor
                        WTBarsShowExitProjection,        // showExitProjection
                        WTBarsStopBarCount,              // stopBarCount
                        WTBarsStopBarOffsetSteps,        // stopBarOffsetSteps
                        WTBarsShowStackedBars,           // showStackedBars
                        WTBarsStackedNBars,              // stackedNBars
                        WTBarsStackedTrendBars,          // stackedTrendBars
                        WTBarsStackedResetBars,          // stackedResetBars
                        WTBarsStackedResetAtAWstart,     // stackedResetAtAWstart
                        Brushes.Lime,                    // stackedColorUp
                        Brushes.Red,                     // stackedColorDn
                        WTBarsShowPotentialSetups,       // showPotentialSetups
                        WTBarsEntryOffsetSteps,          // entryOffsetSteps
                        WTBarsInitialStopSteps,          // initialStopSteps
                        WTBarsFixedTargetSteps,          // fixedTargetSteps
                        WTBarsMoneyMgtPct,               // moneyMgtPct
                        WTBarsMatchesForSetup,           // matchesForSetup
                        WTBarsActiveModeStartTime,       // activeModeStartTime
                        WTBarsActiveModeMinutes,         // activeModeMinutes
                        WTBarsTrendRiderMinutes          // trendRiderMinutes
                    );
                    AddChartIndicator(wtBars);
                }

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

            // WTMomentum confirmation
            bool wtMomentumLong = IsWTMomentumBullish();
            bool wtMomentumShort = IsWTMomentumBearish();

            // Long signal - ALL conditions must be true (including WTMomentum if enabled)
            longSignal = trendBullish &&
                        momentumLong &&
                        volatilityOK &&
                        volumeConfirmed &&
                        emaCrossUp &&
                        wtMomentumLong;

            // Short signal - ALL conditions must be true (including WTMomentum if enabled)
            shortSignal = trendBearish &&
                         momentumShort &&
                         volatilityOK &&
                         volumeConfirmed &&
                         emaCrossDown &&
                         wtMomentumShort;

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

        private bool IsWTMomentumBullish()
        {
            // If WTMomentum is disabled, always return true (no filter)
            if (!EnableWTMomentum || wtMomentum == null)
                return true;

            // WTMomentum bullish conditions:
            // 1. WTMomentum value is positive (above zero line) - momentum is bullish
            // 2. OR WTMomentum is crossing up from below threshold (reversal from oversold)
            // 3. WTMomentum is rising (current > previous)

            double wtValue = wtMomentum[0];
            double wtValuePrev = CurrentBar > 0 ? wtMomentum[1] : wtValue;

            // Bullish: WTMomentum is positive and rising, or crossing up from negative
            bool isPositive = wtValue > 0;
            bool isRising = wtValue > wtValuePrev;
            bool crossingUp = wtValue > -WTThreshold && wtValuePrev <= -WTThreshold;

            // For long entries: WTMomentum should be positive and rising,
            // or recovering from oversold (below -threshold)
            return (isPositive && isRising) || crossingUp || (wtValue > -WTThreshold && isRising);
        }

        private bool IsWTMomentumBearish()
        {
            // If WTMomentum is disabled, always return true (no filter)
            if (!EnableWTMomentum || wtMomentum == null)
                return true;

            // WTMomentum bearish conditions:
            // 1. WTMomentum value is negative (below zero line) - momentum is bearish
            // 2. OR WTMomentum is crossing down from above threshold (reversal from overbought)
            // 3. WTMomentum is falling (current < previous)

            double wtValue = wtMomentum[0];
            double wtValuePrev = CurrentBar > 0 ? wtMomentum[1] : wtValue;

            // Bearish: WTMomentum is negative and falling, or crossing down from positive
            bool isNegative = wtValue < 0;
            bool isFalling = wtValue < wtValuePrev;
            bool crossingDown = wtValue < WTThreshold && wtValuePrev >= WTThreshold;

            // For short entries: WTMomentum should be negative and falling,
            // or reversing from overbought (above +threshold)
            return (isNegative && isFalling) || crossingDown || (wtValue < WTThreshold && isFalling);
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
            if (EnableWTMomentum && wtMomentum != null)
                Print("WTMomentum: " + wtMomentum[0].ToString("F2"));
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

        // WTMomentum Parameters
        [NinjaScriptProperty]
        [Display(Name = "Enable WTMomentum Filter", Description = "Use WTMomentum indicator for additional momentum confirmation", Order = 1, GroupName = "10. WTMomentum")]
        public bool EnableWTMomentum { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "WT Sensitivity", Description = "WTMomentum sensitivity (lower = more sensitive)", Order = 2, GroupName = "10. WTMomentum")]
        public int WTSensitivity { get; set; }

        [NinjaScriptProperty]
        [Range(10, 100)]
        [Display(Name = "WT Threshold", Description = "Overbought/Oversold threshold level", Order = 3, GroupName = "10. WTMomentum")]
        public int WTThreshold { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "WT Color Bars", Description = "Color price bars based on WTMomentum", Order = 4, GroupName = "10. WTMomentum")]
        public bool WTColorBars { get; set; }

        // WTBarsV3 Parameters
        [NinjaScriptProperty]
        [Display(Name = "Enable WTBars", Description = "Enable WTBarsV3 indicator overlay on chart", Order = 1, GroupName = "11. WTBarsV3")]
        public bool EnableWTBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "WTBars Period", Description = "WTBarsV3 period", Order = 2, GroupName = "11. WTBarsV3")]
        public int WTBarsPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, 3)]
        [Display(Name = "WTBars Version", Description = "WTBarsV3 calculation version", Order = 3, GroupName = "11. WTBarsV3")]
        public int WTBarsVersion { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Shadow Width", Description = "Width of bar shadows", Order = 4, GroupName = "11. WTBarsV3")]
        public int WTBarsShadowWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Colors By Momentum", Description = "Color bars based on momentum", Order = 5, GroupName = "11. WTBarsV3")]
        public bool WTBarsColorsByMomo { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Colors By Momo Sensitivity", Description = "Sensitivity for momentum coloring", Order = 6, GroupName = "11. WTBarsV3")]
        public int WTBarsColorsByMomoSensitivity { get; set; }

        [NinjaScriptProperty]
        [Range(10, 100)]
        [Display(Name = "WTBars Threshold", Description = "Threshold level for WTBars", Order = 7, GroupName = "11. WTBarsV3")]
        public int WTBarsThreshold { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Plot TI", Description = "Plot trend intent", Order = 8, GroupName = "11. WTBarsV3")]
        public bool WTBarsPlotTI { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Period 1", Description = "First period for calculations", Order = 9, GroupName = "11. WTBarsV3")]
        public int WTBarsPeriod1 { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Period 2", Description = "Second period for calculations", Order = 10, GroupName = "11. WTBarsV3")]
        public int WTBarsPeriod2 { get; set; }

        [NinjaScriptProperty]
        [Range(10, 100)]
        [Display(Name = "Level 1", Description = "First threshold level", Order = 11, GroupName = "11. WTBarsV3")]
        public int WTBarsLevel1 { get; set; }

        [NinjaScriptProperty]
        [Range(10, 100)]
        [Display(Name = "Level 2", Description = "Second threshold level", Order = 12, GroupName = "11. WTBarsV3")]
        public int WTBarsLevel2 { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Intent Offset Tics", Description = "Offset for intent display", Order = 13, GroupName = "11. WTBarsV3")]
        public int WTBarsIntentOffsetTics { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "TI Threshold Pct", Description = "Trend intent threshold percentage", Order = 14, GroupName = "11. WTBarsV3")]
        public int WTBarsTIThresholdPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Range Bracket", Description = "Enable range bracket display", Order = 15, GroupName = "11. WTBarsV3")]
        public bool WTBarsRangeBracket { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Line Thickness", Description = "Thickness of lines", Order = 16, GroupName = "11. WTBarsV3")]
        public int WTBarsLineThickness { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Exit Projection", Description = "Display exit projection", Order = 17, GroupName = "11. WTBarsV3")]
        public bool WTBarsShowExitProjection { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Stop Bar Count", Description = "Number of bars for stop calculation", Order = 18, GroupName = "11. WTBarsV3")]
        public int WTBarsStopBarCount { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Stop Bar Offset Steps", Description = "Offset steps for stop", Order = 19, GroupName = "11. WTBarsV3")]
        public int WTBarsStopBarOffsetSteps { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Stacked Bars", Description = "Display stacked bar indicator", Order = 20, GroupName = "11. WTBarsV3")]
        public bool WTBarsShowStackedBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Stacked N Bars", Description = "Number of bars for stacked calculation", Order = 21, GroupName = "11. WTBarsV3")]
        public int WTBarsStackedNBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Stacked Trend Bars", Description = "Trend bars for stacked calculation", Order = 22, GroupName = "11. WTBarsV3")]
        public int WTBarsStackedTrendBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Stacked Reset Bars", Description = "Reset bars for stacked calculation", Order = 23, GroupName = "11. WTBarsV3")]
        public int WTBarsStackedResetBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Stacked Reset At AW Start", Description = "Reset stacked at active window start", Order = 24, GroupName = "11. WTBarsV3")]
        public bool WTBarsStackedResetAtAWstart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Potential Setups", Description = "Display potential trade setups", Order = 25, GroupName = "11. WTBarsV3")]
        public bool WTBarsShowPotentialSetups { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Entry Offset Steps", Description = "Offset steps for entry", Order = 26, GroupName = "11. WTBarsV3")]
        public double WTBarsEntryOffsetSteps { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Initial Stop Steps", Description = "Initial stop loss in steps", Order = 27, GroupName = "11. WTBarsV3")]
        public double WTBarsInitialStopSteps { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Fixed Target Steps", Description = "Fixed profit target in steps", Order = 28, GroupName = "11. WTBarsV3")]
        public double WTBarsFixedTargetSteps { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Money Mgt Pct", Description = "Money management percentage", Order = 29, GroupName = "11. WTBarsV3")]
        public int WTBarsMoneyMgtPct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Matches For Setup", Description = "Required matches for valid setup", Order = 30, GroupName = "11. WTBarsV3")]
        public int WTBarsMatchesForSetup { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Active Mode Start Time", Description = "Start time for active mode (HHMM)", Order = 31, GroupName = "11. WTBarsV3")]
        public double WTBarsActiveModeStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1440)]
        [Display(Name = "Active Mode Minutes", Description = "Duration of active mode in minutes", Order = 32, GroupName = "11. WTBarsV3")]
        public double WTBarsActiveModeMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1440)]
        [Display(Name = "Trend Rider Minutes", Description = "Duration of trend rider mode in minutes", Order = 33, GroupName = "11. WTBarsV3")]
        public double WTBarsTrendRiderMinutes { get; set; }

        #endregion
    }
}
