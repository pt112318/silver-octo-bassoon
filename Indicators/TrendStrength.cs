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
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// TrendStrength - Composite indicator measuring trend strength using multiple factors
    /// Combines ADX, EMA alignment, price momentum, and higher timeframe confirmation
    /// Returns a value from -100 (strong downtrend) to +100 (strong uptrend)
    /// </summary>
    public class TrendStrength : Indicator
    {
        #region Private Variables

        // Core indicators
        private ADX adx;
        private EMA ema8;
        private EMA ema21;
        private EMA ema50;
        private EMA ema200;
        private ATR atr;
        private SMA volumeSMA;

        // Momentum
        private Series<double> momentum;
        private SMA momentumSMA;

        // Internal calculations
        private Series<double> trendScore;
        private Series<double> smoothedScore;

        #endregion

        #region OnStateChange

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Trend Strength Indicator - Measures overall trend strength from -100 to +100";
                Name = "TrendStrength";
                Calculate = Calculate.OnBarClose;
                IsOverlay = false;
                DisplayInDataBox = true;
                DrawOnPricePanel = false;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines = true;
                PaintPriceMarkers = true;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                // Parameters
                ADXPeriod = 14;
                ADXThreshold = 25;
                FastEMAPeriod = 8;
                MediumEMAPeriod = 21;
                SlowEMAPeriod = 50;
                TrendEMAPeriod = 200;
                MomentumPeriod = 10;
                SmoothingPeriod = 3;
                VolumePeriod = 20;

                // Plots
                AddPlot(new Stroke(Brushes.DodgerBlue, 2), PlotStyle.Line, "TrendStrength");
                AddPlot(new Stroke(Brushes.Orange, 1), PlotStyle.Line, "SignalLine");
                AddLine(Brushes.Green, 50, "Strong Uptrend");
                AddLine(Brushes.DimGray, 0, "Neutral");
                AddLine(Brushes.Red, -50, "Strong Downtrend");
            }
            else if (State == State.Configure)
            {
                // No additional configuration needed
            }
            else if (State == State.DataLoaded)
            {
                // Initialize indicators
                adx = ADX(ADXPeriod);
                ema8 = EMA(FastEMAPeriod);
                ema21 = EMA(MediumEMAPeriod);
                ema50 = EMA(SlowEMAPeriod);
                ema200 = EMA(TrendEMAPeriod);
                atr = ATR(14);
                volumeSMA = SMA(Volume, VolumePeriod);

                // Initialize series
                momentum = new Series<double>(this);
                trendScore = new Series<double>(this);
                smoothedScore = new Series<double>(this);
            }
        }

        #endregion

        #region OnBarUpdate

        protected override void OnBarUpdate()
        {
            if (CurrentBar < TrendEMAPeriod + 10)
            {
                Value[0] = 0;
                SignalLine[0] = 0;
                return;
            }

            // Calculate momentum
            momentum[0] = Close[0] - Close[MomentumPeriod];

            // Calculate individual components
            double emaAlignmentScore = CalculateEMAAlignment();
            double adxScore = CalculateADXScore();
            double momentumScore = CalculateMomentumScore();
            double pricePositionScore = CalculatePricePositionScore();
            double volumeScore = CalculateVolumeScore();

            // Combine scores with weights
            double rawScore = (emaAlignmentScore * 0.35) +   // EMA alignment is most important
                             (adxScore * 0.25) +              // ADX shows trend strength
                             (momentumScore * 0.20) +         // Momentum confirmation
                             (pricePositionScore * 0.15) +    // Price position relative to EMAs
                             (volumeScore * 0.05);            // Volume confirmation

            // Clamp to -100 to +100 range
            trendScore[0] = Math.Max(-100, Math.Min(100, rawScore));

            // Apply smoothing
            double sum = 0;
            for (int i = 0; i < Math.Min(SmoothingPeriod, CurrentBar); i++)
                sum += trendScore[i];
            smoothedScore[0] = sum / Math.Min(SmoothingPeriod, CurrentBar);

            // Set output values
            Value[0] = smoothedScore[0];

            // Signal line (slower smoothing)
            double signalSum = 0;
            int signalPeriod = SmoothingPeriod * 3;
            for (int i = 0; i < Math.Min(signalPeriod, CurrentBar); i++)
                signalSum += trendScore[i];
            SignalLine[0] = signalSum / Math.Min(signalPeriod, CurrentBar);

            // Color the plot based on value
            if (smoothedScore[0] > 50)
                PlotBrushes[0][0] = Brushes.Lime;
            else if (smoothedScore[0] > 25)
                PlotBrushes[0][0] = Brushes.Green;
            else if (smoothedScore[0] > 0)
                PlotBrushes[0][0] = Brushes.DarkGreen;
            else if (smoothedScore[0] > -25)
                PlotBrushes[0][0] = Brushes.DarkRed;
            else if (smoothedScore[0] > -50)
                PlotBrushes[0][0] = Brushes.Red;
            else
                PlotBrushes[0][0] = Brushes.Crimson;
        }

        #endregion

        #region Component Calculations

        private double CalculateEMAAlignment()
        {
            // Perfect bullish alignment: ema8 > ema21 > ema50 > ema200
            // Perfect bearish alignment: ema8 < ema21 < ema50 < ema200

            double score = 0;

            // Check each EMA pair alignment
            if (ema8[0] > ema21[0]) score += 25; else score -= 25;
            if (ema21[0] > ema50[0]) score += 25; else score -= 25;
            if (ema50[0] > ema200[0]) score += 25; else score -= 25;

            // Check EMA slope direction (momentum of EMAs)
            if (ema8[0] > ema8[1]) score += 12.5; else score -= 12.5;
            if (ema21[0] > ema21[1]) score += 12.5; else score -= 12.5;

            return score;
        }

        private double CalculateADXScore()
        {
            // ADX indicates trend strength regardless of direction
            // Scale ADX to 0-100 range, then apply direction

            double strength = Math.Min(adx[0], 50) * 2; // Cap at 100

            // Determine direction from EMA alignment
            bool bullish = ema8[0] > ema21[0];

            return bullish ? strength : -strength;
        }

        private double CalculateMomentumScore()
        {
            // Normalize momentum relative to ATR
            if (atr[0] == 0) return 0;

            double normalizedMomentum = momentum[0] / (atr[0] * MomentumPeriod);

            // Scale to -100 to +100
            return Math.Max(-100, Math.Min(100, normalizedMomentum * 50));
        }

        private double CalculatePricePositionScore()
        {
            double score = 0;

            // Price relative to each EMA
            if (Close[0] > ema8[0]) score += 25; else score -= 25;
            if (Close[0] > ema21[0]) score += 25; else score -= 25;
            if (Close[0] > ema50[0]) score += 25; else score -= 25;
            if (Close[0] > ema200[0]) score += 25; else score -= 25;

            return score;
        }

        private double CalculateVolumeScore()
        {
            // Volume confirmation - higher volume in trend direction
            if (volumeSMA[0] == 0) return 0;

            double volumeRatio = Volume[0] / volumeSMA[0];
            bool bullishBar = Close[0] > Open[0];
            bool bearishBar = Close[0] < Open[0];
            bool trendUp = ema8[0] > ema21[0];

            // High volume in trend direction is positive
            if ((trendUp && bullishBar) || (!trendUp && bearishBar))
            {
                return Math.Min(100, (volumeRatio - 1) * 50);
            }
            else
            {
                // High volume against trend is negative
                return Math.Max(-100, -(volumeRatio - 1) * 50);
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Returns true if trend is strong enough for trading
        /// </summary>
        public bool IsTrendStrong()
        {
            return Math.Abs(Value[0]) >= 50;
        }

        /// <summary>
        /// Returns true if trend is bullish
        /// </summary>
        public bool IsBullish()
        {
            return Value[0] > 25;
        }

        /// <summary>
        /// Returns true if trend is bearish
        /// </summary>
        public bool IsBearish()
        {
            return Value[0] < -25;
        }

        /// <summary>
        /// Returns true if trend strength is increasing
        /// </summary>
        public bool IsTrendIncreasing()
        {
            if (CurrentBar < 2) return false;
            return Math.Abs(Value[0]) > Math.Abs(Value[1]);
        }

        /// <summary>
        /// Returns true if bullish crossover of signal line
        /// </summary>
        public bool BullishCrossover()
        {
            if (CurrentBar < 2) return false;
            return Value[0] > SignalLine[0] && Value[1] <= SignalLine[1];
        }

        /// <summary>
        /// Returns true if bearish crossover of signal line
        /// </summary>
        public bool BearishCrossover()
        {
            if (CurrentBar < 2) return false;
            return Value[0] < SignalLine[0] && Value[1] >= SignalLine[1];
        }

        #endregion

        #region Properties

        [NinjaScriptProperty]
        [Range(5, 50)]
        [Display(Name = "ADX Period", Order = 1, GroupName = "Parameters")]
        public int ADXPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(10, 50)]
        [Display(Name = "ADX Threshold", Order = 2, GroupName = "Parameters")]
        public int ADXThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(3, 20)]
        [Display(Name = "Fast EMA Period", Order = 3, GroupName = "Parameters")]
        public int FastEMAPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(10, 50)]
        [Display(Name = "Medium EMA Period", Order = 4, GroupName = "Parameters")]
        public int MediumEMAPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(20, 100)]
        [Display(Name = "Slow EMA Period", Order = 5, GroupName = "Parameters")]
        public int SlowEMAPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(100, 300)]
        [Display(Name = "Trend EMA Period", Order = 6, GroupName = "Parameters")]
        public int TrendEMAPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(5, 30)]
        [Display(Name = "Momentum Period", Order = 7, GroupName = "Parameters")]
        public int MomentumPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Smoothing Period", Order = 8, GroupName = "Parameters")]
        public int SmoothingPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(5, 50)]
        [Display(Name = "Volume Period", Order = 9, GroupName = "Parameters")]
        public int VolumePeriod { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> SignalLine
        {
            get { return Values[1]; }
        }

        #endregion
    }
}
