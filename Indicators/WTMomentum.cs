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
    /// WTMomentum - Wave Trend Momentum Indicator
    /// A momentum oscillator that identifies overbought/oversold conditions and trend direction
    /// Based on the Wave Trend concept using EMA smoothing of price momentum
    /// </summary>
    public class WTMomentum : Indicator
    {
        #region Private Variables

        // Core calculation series
        private Series<double> ap;           // Average Price (HLC/3)
        private Series<double> esa;          // EMA of AP
        private Series<double> d;            // EMA of abs(AP - ESA)
        private Series<double> ci;           // (AP - ESA) / (0.015 * D)
        private Series<double> wt1;          // Wave Trend Line 1 (main)
        private Series<double> wt2;          // Wave Trend Line 2 (signal)

        // EMA components
        private EMA esaEMA;
        private EMA dEMA;
        private EMA wtEMA;

        // Alert tracking
        private bool lastAlertWasUp;
        private int lastAlertBar;

        #endregion

        #region OnStateChange

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Wave Trend Momentum - Identifies momentum direction and overbought/oversold conditions";
                Name = "WTMomentum";
                Calculate = Calculate.OnBarClose;
                IsOverlay = false;
                DisplayInDataBox = true;
                DrawOnPricePanel = false;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines = true;
                PaintPriceMarkers = true;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                // Default parameters
                Sensitivity = 10;
                ColorBars = true;
                Threshold = 53;
                ThresholdLineColor = Brushes.DimGray;
                UpperThresholdMarkerColor = Brushes.Lime;
                LowerThresholdMarkerColor = Brushes.Red;
                NegativeBarColor = Brushes.Red;
                PositiveBarColor = Brushes.Lime;
                WickColor = Brushes.White;
                VerticalLines = false;
                VerticalLineUp = new Stroke(Brushes.Green, 1);
                VerticalLineDown = new Stroke(Brushes.Red, 1);
                AlertSoundToPlay = @"";
                AlertSounds = false;

                // Plots
                AddPlot(new Stroke(Brushes.Lime, 2), PlotStyle.Line, "WTLine");
                AddPlot(new Stroke(Brushes.Red, 1), PlotStyle.Line, "SignalLine");

                // Reference lines
                AddLine(Brushes.DimGray, 0, "ZeroLine");
            }
            else if (State == State.Configure)
            {
                // Add threshold lines based on parameter
                AddLine(new Stroke(ThresholdLineColor, DashStyleHelper.Dash, 1), Threshold, "UpperThreshold");
                AddLine(new Stroke(ThresholdLineColor, DashStyleHelper.Dash, 1), -Threshold, "LowerThreshold");
            }
            else if (State == State.DataLoaded)
            {
                // Initialize series
                ap = new Series<double>(this);
                esa = new Series<double>(this);
                d = new Series<double>(this);
                ci = new Series<double>(this);
                wt1 = new Series<double>(this);
                wt2 = new Series<double>(this);

                // Reset alert tracking
                lastAlertWasUp = false;
                lastAlertBar = -1;
            }
        }

        #endregion

        #region OnBarUpdate

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Sensitivity * 2)
            {
                Value[0] = 0;
                Signal[0] = 0;
                return;
            }

            // Calculate Average Price (typical price)
            ap[0] = (High[0] + Low[0] + Close[0]) / 3.0;

            // Calculate ESA (EMA of AP)
            if (CurrentBar == 0)
                esa[0] = ap[0];
            else
            {
                double multiplier = 2.0 / (Sensitivity + 1);
                esa[0] = (ap[0] - esa[1]) * multiplier + esa[1];
            }

            // Calculate D (EMA of absolute difference)
            double absDiff = Math.Abs(ap[0] - esa[0]);
            if (CurrentBar == 0)
                d[0] = absDiff;
            else
            {
                double multiplier = 2.0 / (Sensitivity + 1);
                d[0] = (absDiff - d[1]) * multiplier + d[1];
            }

            // Calculate CI (normalized momentum)
            if (d[0] != 0)
                ci[0] = (ap[0] - esa[0]) / (0.015 * d[0]);
            else
                ci[0] = 0;

            // Calculate WT1 (EMA of CI) - Wave Trend main line
            if (CurrentBar == 0)
                wt1[0] = ci[0];
            else
            {
                int wtPeriod = (int)Math.Round(Sensitivity * 2.1);
                double multiplier = 2.0 / (wtPeriod + 1);
                wt1[0] = (ci[0] - wt1[1]) * multiplier + wt1[1];
            }

            // Calculate WT2 (SMA of WT1) - Signal line
            int signalPeriod = 4;
            double sum = 0;
            int count = Math.Min(signalPeriod, CurrentBar + 1);
            for (int i = 0; i < count; i++)
                sum += wt1[i];
            wt2[0] = sum / count;

            // Set output values
            Value[0] = wt1[0];
            Signal[0] = wt2[0];

            // Color the main plot based on value
            if (wt1[0] > Threshold)
                PlotBrushes[0][0] = UpperThresholdMarkerColor;
            else if (wt1[0] < -Threshold)
                PlotBrushes[0][0] = LowerThresholdMarkerColor;
            else if (wt1[0] > 0)
                PlotBrushes[0][0] = PositiveBarColor;
            else
                PlotBrushes[0][0] = NegativeBarColor;

            // Color price bars if enabled
            if (ColorBars)
            {
                if (wt1[0] > 0)
                    BarBrush = PositiveBarColor;
                else
                    BarBrush = NegativeBarColor;

                CandleOutlineBrush = WickColor;
            }

            // Draw vertical lines on crossovers if enabled
            if (VerticalLines && CurrentBar > 0)
            {
                // Bullish crossover (WT crosses above signal from below)
                if (wt1[0] > wt2[0] && wt1[1] <= wt2[1])
                {
                    Draw.VerticalLine(this, "VLineUp" + CurrentBar, 0, VerticalLineUp.Brush);
                }
                // Bearish crossover (WT crosses below signal from above)
                else if (wt1[0] < wt2[0] && wt1[1] >= wt2[1])
                {
                    Draw.VerticalLine(this, "VLineDown" + CurrentBar, 0, VerticalLineDown.Brush);
                }
            }

            // Alert on threshold crossings
            if (AlertSounds && !string.IsNullOrEmpty(AlertSoundToPlay) && CurrentBar != lastAlertBar)
            {
                // Alert on crossing above upper threshold
                if (wt1[0] > Threshold && wt1[1] <= Threshold && !lastAlertWasUp)
                {
                    Alert("WTUpper", Priority.Medium, "WTMomentum crossed above " + Threshold,
                          AlertSoundToPlay, 10, Brushes.Green, Brushes.White);
                    lastAlertWasUp = true;
                    lastAlertBar = CurrentBar;
                }
                // Alert on crossing below lower threshold
                else if (wt1[0] < -Threshold && wt1[1] >= -Threshold && lastAlertWasUp)
                {
                    Alert("WTLower", Priority.Medium, "WTMomentum crossed below " + (-Threshold),
                          AlertSoundToPlay, 10, Brushes.Red, Brushes.White);
                    lastAlertWasUp = false;
                    lastAlertBar = CurrentBar;
                }
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Returns true if momentum is bullish (above zero and rising)
        /// </summary>
        public bool IsBullish()
        {
            if (CurrentBar < 1) return false;
            return Value[0] > 0 && Value[0] > Value[1];
        }

        /// <summary>
        /// Returns true if momentum is bearish (below zero and falling)
        /// </summary>
        public bool IsBearish()
        {
            if (CurrentBar < 1) return false;
            return Value[0] < 0 && Value[0] < Value[1];
        }

        /// <summary>
        /// Returns true if in overbought zone
        /// </summary>
        public bool IsOverbought()
        {
            return Value[0] > Threshold;
        }

        /// <summary>
        /// Returns true if in oversold zone
        /// </summary>
        public bool IsOversold()
        {
            return Value[0] < -Threshold;
        }

        /// <summary>
        /// Returns true if bullish crossover occurred (WT crossed above signal)
        /// </summary>
        public bool BullishCrossover()
        {
            if (CurrentBar < 1) return false;
            return Value[0] > Signal[0] && Value[1] <= Signal[1];
        }

        /// <summary>
        /// Returns true if bearish crossover occurred (WT crossed below signal)
        /// </summary>
        public bool BearishCrossover()
        {
            if (CurrentBar < 1) return false;
            return Value[0] < Signal[0] && Value[1] >= Signal[1];
        }

        #endregion

        #region Properties

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Sensitivity", Description = "Lower values = more sensitive", Order = 1, GroupName = "Parameters")]
        public int Sensitivity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Color Bars", Description = "Color price bars based on momentum", Order = 2, GroupName = "Parameters")]
        public bool ColorBars { get; set; }

        [NinjaScriptProperty]
        [Range(10, 100)]
        [Display(Name = "Threshold", Description = "Overbought/Oversold threshold level", Order = 3, GroupName = "Parameters")]
        public int Threshold { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Threshold Line Color", Order = 4, GroupName = "Colors")]
        public Brush ThresholdLineColor { get; set; }

        [Browsable(false)]
        public string ThresholdLineColorSerializable
        {
            get { return Serialize.BrushToString(ThresholdLineColor); }
            set { ThresholdLineColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Upper Threshold Marker Color", Order = 5, GroupName = "Colors")]
        public Brush UpperThresholdMarkerColor { get; set; }

        [Browsable(false)]
        public string UpperThresholdMarkerColorSerializable
        {
            get { return Serialize.BrushToString(UpperThresholdMarkerColor); }
            set { UpperThresholdMarkerColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Lower Threshold Marker Color", Order = 6, GroupName = "Colors")]
        public Brush LowerThresholdMarkerColor { get; set; }

        [Browsable(false)]
        public string LowerThresholdMarkerColorSerializable
        {
            get { return Serialize.BrushToString(LowerThresholdMarkerColor); }
            set { LowerThresholdMarkerColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Negative Bar Color", Order = 7, GroupName = "Colors")]
        public Brush NegativeBarColor { get; set; }

        [Browsable(false)]
        public string NegativeBarColorSerializable
        {
            get { return Serialize.BrushToString(NegativeBarColor); }
            set { NegativeBarColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Positive Bar Color", Order = 8, GroupName = "Colors")]
        public Brush PositiveBarColor { get; set; }

        [Browsable(false)]
        public string PositiveBarColorSerializable
        {
            get { return Serialize.BrushToString(PositiveBarColor); }
            set { PositiveBarColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Wick Color", Order = 9, GroupName = "Colors")]
        public Brush WickColor { get; set; }

        [Browsable(false)]
        public string WickColorSerializable
        {
            get { return Serialize.BrushToString(WickColor); }
            set { WickColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Vertical Lines", Description = "Draw vertical lines on crossovers", Order = 10, GroupName = "Display")]
        public bool VerticalLines { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Vertical Line Up", Order = 11, GroupName = "Display")]
        public Stroke VerticalLineUp { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Vertical Line Down", Order = 12, GroupName = "Display")]
        public Stroke VerticalLineDown { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Alert Sound File", Order = 13, GroupName = "Alerts")]
        public string AlertSoundToPlay { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Alert Sounds", Order = 14, GroupName = "Alerts")]
        public bool AlertSounds { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Signal
        {
            get { return Values[1]; }
        }

        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
    {
        private WTMomentum[] cacheWTMomentum;
        public WTMomentum WTMomentum(int sensitivity, bool colorBars, int threshold, Brush thresholdLineColor, Brush upperThresholdMarkerColor, Brush lowerThresholdMarkerColor, Brush negativeBarColor, Brush positiveBarColor, Brush wickColor, bool verticalLines, Stroke verticalLineUp, Stroke verticalLineDown, string alertSoundToPlay, bool alertSounds)
        {
            return WTMomentum(Input, sensitivity, colorBars, threshold, thresholdLineColor, upperThresholdMarkerColor, lowerThresholdMarkerColor, negativeBarColor, positiveBarColor, wickColor, verticalLines, verticalLineUp, verticalLineDown, alertSoundToPlay, alertSounds);
        }

        public WTMomentum WTMomentum(ISeries<double> input, int sensitivity, bool colorBars, int threshold, Brush thresholdLineColor, Brush upperThresholdMarkerColor, Brush lowerThresholdMarkerColor, Brush negativeBarColor, Brush positiveBarColor, Brush wickColor, bool verticalLines, Stroke verticalLineUp, Stroke verticalLineDown, string alertSoundToPlay, bool alertSounds)
        {
            if (cacheWTMomentum != null)
                for (int idx = 0; idx < cacheWTMomentum.Length; idx++)
                    if (cacheWTMomentum[idx] != null && cacheWTMomentum[idx].Sensitivity == sensitivity && cacheWTMomentum[idx].ColorBars == colorBars && cacheWTMomentum[idx].Threshold == threshold && cacheWTMomentum[idx].ThresholdLineColor == thresholdLineColor && cacheWTMomentum[idx].UpperThresholdMarkerColor == upperThresholdMarkerColor && cacheWTMomentum[idx].LowerThresholdMarkerColor == lowerThresholdMarkerColor && cacheWTMomentum[idx].NegativeBarColor == negativeBarColor && cacheWTMomentum[idx].PositiveBarColor == positiveBarColor && cacheWTMomentum[idx].WickColor == wickColor && cacheWTMomentum[idx].VerticalLines == verticalLines && cacheWTMomentum[idx].VerticalLineUp == verticalLineUp && cacheWTMomentum[idx].VerticalLineDown == verticalLineDown && cacheWTMomentum[idx].AlertSoundToPlay == alertSoundToPlay && cacheWTMomentum[idx].AlertSounds == alertSounds && cacheWTMomentum[idx].EqualsInput(input))
                        return cacheWTMomentum[idx];
            return CacheIndicator<WTMomentum>(new WTMomentum(){ Sensitivity = sensitivity, ColorBars = colorBars, Threshold = threshold, ThresholdLineColor = thresholdLineColor, UpperThresholdMarkerColor = upperThresholdMarkerColor, LowerThresholdMarkerColor = lowerThresholdMarkerColor, NegativeBarColor = negativeBarColor, PositiveBarColor = positiveBarColor, WickColor = wickColor, VerticalLines = verticalLines, VerticalLineUp = verticalLineUp, VerticalLineDown = verticalLineDown, AlertSoundToPlay = alertSoundToPlay, AlertSounds = alertSounds }, input, ref cacheWTMomentum);
        }
    }
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
    public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
    {
        public Indicators.WTMomentum WTMomentum(int sensitivity, bool colorBars, int threshold, Brush thresholdLineColor, Brush upperThresholdMarkerColor, Brush lowerThresholdMarkerColor, Brush negativeBarColor, Brush positiveBarColor, Brush wickColor, bool verticalLines, Stroke verticalLineUp, Stroke verticalLineDown, string alertSoundToPlay, bool alertSounds)
        {
            return indicator.WTMomentum(Input, sensitivity, colorBars, threshold, thresholdLineColor, upperThresholdMarkerColor, lowerThresholdMarkerColor, negativeBarColor, positiveBarColor, wickColor, verticalLines, verticalLineUp, verticalLineDown, alertSoundToPlay, alertSounds);
        }

        public Indicators.WTMomentum WTMomentum(ISeries<double> input, int sensitivity, bool colorBars, int threshold, Brush thresholdLineColor, Brush upperThresholdMarkerColor, Brush lowerThresholdMarkerColor, Brush negativeBarColor, Brush positiveBarColor, Brush wickColor, bool verticalLines, Stroke verticalLineUp, Stroke verticalLineDown, string alertSoundToPlay, bool alertSounds)
        {
            return indicator.WTMomentum(input, sensitivity, colorBars, threshold, thresholdLineColor, upperThresholdMarkerColor, lowerThresholdMarkerColor, negativeBarColor, positiveBarColor, wickColor, verticalLines, verticalLineUp, verticalLineDown, alertSoundToPlay, alertSounds);
        }
    }
}

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
    {
        public Indicators.WTMomentum WTMomentum(int sensitivity, bool colorBars, int threshold, Brush thresholdLineColor, Brush upperThresholdMarkerColor, Brush lowerThresholdMarkerColor, Brush negativeBarColor, Brush positiveBarColor, Brush wickColor, bool verticalLines, Stroke verticalLineUp, Stroke verticalLineDown, string alertSoundToPlay, bool alertSounds)
        {
            return indicator.WTMomentum(Input, sensitivity, colorBars, threshold, thresholdLineColor, upperThresholdMarkerColor, lowerThresholdMarkerColor, negativeBarColor, positiveBarColor, wickColor, verticalLines, verticalLineUp, verticalLineDown, alertSoundToPlay, alertSounds);
        }

        public Indicators.WTMomentum WTMomentum(ISeries<double> input, int sensitivity, bool colorBars, int threshold, Brush thresholdLineColor, Brush upperThresholdMarkerColor, Brush lowerThresholdMarkerColor, Brush negativeBarColor, Brush positiveBarColor, Brush wickColor, bool verticalLines, Stroke verticalLineUp, Stroke verticalLineDown, string alertSoundToPlay, bool alertSounds)
        {
            return indicator.WTMomentum(input, sensitivity, colorBars, threshold, thresholdLineColor, upperThresholdMarkerColor, lowerThresholdMarkerColor, negativeBarColor, positiveBarColor, wickColor, verticalLines, verticalLineUp, verticalLineDown, alertSoundToPlay, alertSounds);
        }
    }
}

#endregion
