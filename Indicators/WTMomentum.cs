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
    /// WaveTrend Momentum Indicator (WTMomentum)
    /// A momentum oscillator based on the WaveTrend concept that identifies overbought/oversold conditions
    /// and potential trend reversals.
    /// </summary>
    public class WTMomentum : Indicator
    {
        #region Private Variables

        private Series<double> ap;      // Average price
        private Series<double> esa;     // EMA of average price
        private Series<double> d;       // EMA of absolute deviation
        private Series<double> ci;      // Channel index

        private int channelLength;
        private int averageLength;

        private double lastWTValue;
        private bool lastWasAboveThreshold;
        private bool lastWasBelowThreshold;

        #endregion

        #region OnStateChange

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"WaveTrend Momentum Indicator - Identifies overbought/oversold conditions and momentum shifts";
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

                // Add plots
                AddPlot(new Stroke(Brushes.Cyan, 2), PlotStyle.Line, "WTValue");
                AddPlot(new Stroke(Brushes.Yellow, 1), PlotStyle.Line, "WTSignal");

                // Add threshold lines
                AddLine(Brushes.DimGray, 0, "ZeroLine");
            }
            else if (State == State.Configure)
            {
                // Calculate channel and average lengths based on sensitivity
                // Lower sensitivity = longer periods = smoother but slower
                // Higher sensitivity = shorter periods = more responsive but noisier
                channelLength = Math.Max(5, 21 - Sensitivity);
                averageLength = Math.Max(3, 8 - (Sensitivity / 3));
            }
            else if (State == State.DataLoaded)
            {
                // Initialize series
                ap = new Series<double>(this);
                esa = new Series<double>(this);
                d = new Series<double>(this);
                ci = new Series<double>(this);
            }
        }

        #endregion

        #region OnBarUpdate

        protected override void OnBarUpdate()
        {
            // Calculate average price (typical price) for all bars
            ap[0] = (High[0] + Low[0] + Close[0]) / 3.0;

            // Calculate EMA of average price
            if (CurrentBar == 0)
            {
                esa[0] = ap[0];
                d[0] = 0;
                ci[0] = 0;
                Value[0] = 0;
                WTSignal[0] = 0;
                return;
            }

            double emaMultiplier = 2.0 / (channelLength + 1);
            esa[0] = (ap[0] - esa[1]) * emaMultiplier + esa[1];

            // Calculate EMA of absolute deviation
            double deviation = Math.Abs(ap[0] - esa[0]);
            d[0] = (deviation - d[1]) * emaMultiplier + d[1];

            // Calculate channel index (ci)
            // Avoid division by zero
            if (d[0] != 0)
            {
                ci[0] = (ap[0] - esa[0]) / (0.015 * d[0]);
            }
            else
            {
                ci[0] = 0;
            }

            // Need minimum bars for valid output
            if (CurrentBar < channelLength + averageLength)
            {
                Value[0] = ci[0];
                WTSignal[0] = ci[0];
                return;
            }

            // Calculate WaveTrend (EMA of ci)
            double wtMultiplier = 2.0 / (averageLength + 1);
            double wt1 = (ci[0] - Value[1]) * wtMultiplier + Value[1];

            // Calculate signal line (SMA of wt1)
            double wt2 = 0;
            int signalPeriod = 4;
            double sum = wt1;
            for (int i = 1; i < signalPeriod && i <= CurrentBar; i++)
            {
                sum += Value[i];
            }
            wt2 = sum / signalPeriod;

            // Set plot values
            Value[0] = wt1;
            WTSignal[0] = wt2;

            // Color bars based on momentum
            if (ColorBars)
            {
                if (wt1 > 0)
                {
                    BarBrush = PositiveBarColor;
                    CandleOutlineBrush = PositiveBarColor;
                }
                else
                {
                    BarBrush = NegativeBarColor;
                    CandleOutlineBrush = NegativeBarColor;
                }
            }

            // Draw threshold markers when crossing thresholds
            if (CurrentBar > 0)
            {
                // Check for crossing above upper threshold (overbought)
                if (wt1 >= Threshold && lastWTValue < Threshold)
                {
                    if (ShowThresholdMarkers)
                        Draw.Diamond(this, "UpperThresh" + CurrentBar, false, 0, Threshold + 5, UpperThresholdMarkerColor);

                    if (VerticalLines)
                        Draw.VerticalLine(this, "VLineUp" + CurrentBar, 0, VerticalLineUp.Brush);

                    if (AlertSounds && !string.IsNullOrEmpty(AlertSoundToPlay))
                        PlaySound(AlertSoundToPlay);
                }

                // Check for crossing below lower threshold (oversold)
                if (wt1 <= -Threshold && lastWTValue > -Threshold)
                {
                    if (ShowThresholdMarkers)
                        Draw.Diamond(this, "LowerThresh" + CurrentBar, false, 0, -Threshold - 5, LowerThresholdMarkerColor);

                    if (VerticalLines)
                        Draw.VerticalLine(this, "VLineDown" + CurrentBar, 0, VerticalLineDown.Brush);

                    if (AlertSounds && !string.IsNullOrEmpty(AlertSoundToPlay))
                        PlaySound(AlertSoundToPlay);
                }
            }

            lastWTValue = wt1;
            lastWasAboveThreshold = wt1 >= Threshold;
            lastWasBelowThreshold = wt1 <= -Threshold;
        }

        #endregion

        #region Properties

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> WTValue
        {
            get { return Values[0]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> WTSignal
        {
            get { return Values[1]; }
        }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Sensitivity", Description = "Sensitivity of the indicator (1-50, lower = smoother)", Order = 1, GroupName = "Parameters")]
        public int Sensitivity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Color Bars", Description = "Color price bars based on momentum direction", Order = 2, GroupName = "Parameters")]
        public bool ColorBars { get; set; }

        [NinjaScriptProperty]
        [Range(10, 100)]
        [Display(Name = "Threshold", Description = "Overbought/Oversold threshold level", Order = 3, GroupName = "Parameters")]
        public int Threshold { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Threshold Line Color", Order = 4, GroupName = "Visual")]
        public Brush ThresholdLineColor { get; set; }

        [Browsable(false)]
        public string ThresholdLineColorSerializable
        {
            get { return Serialize.BrushToString(ThresholdLineColor); }
            set { ThresholdLineColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Upper Threshold Marker Color", Order = 5, GroupName = "Visual")]
        public Brush UpperThresholdMarkerColor { get; set; }

        [Browsable(false)]
        public string UpperThresholdMarkerColorSerializable
        {
            get { return Serialize.BrushToString(UpperThresholdMarkerColor); }
            set { UpperThresholdMarkerColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Lower Threshold Marker Color", Order = 6, GroupName = "Visual")]
        public Brush LowerThresholdMarkerColor { get; set; }

        [Browsable(false)]
        public string LowerThresholdMarkerColorSerializable
        {
            get { return Serialize.BrushToString(LowerThresholdMarkerColor); }
            set { LowerThresholdMarkerColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Negative Bar Color", Order = 7, GroupName = "Visual")]
        public Brush NegativeBarColor { get; set; }

        [Browsable(false)]
        public string NegativeBarColorSerializable
        {
            get { return Serialize.BrushToString(NegativeBarColor); }
            set { NegativeBarColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Positive Bar Color", Order = 8, GroupName = "Visual")]
        public Brush PositiveBarColor { get; set; }

        [Browsable(false)]
        public string PositiveBarColorSerializable
        {
            get { return Serialize.BrushToString(PositiveBarColor); }
            set { PositiveBarColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Wick Color", Order = 9, GroupName = "Visual")]
        public Brush WickColor { get; set; }

        [Browsable(false)]
        public string WickColorSerializable
        {
            get { return Serialize.BrushToString(WickColor); }
            set { WickColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Vertical Lines", Description = "Draw vertical lines on threshold crossings", Order = 10, GroupName = "Visual")]
        public bool VerticalLines { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Vertical Line Up", Order = 11, GroupName = "Visual")]
        public Stroke VerticalLineUp { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Vertical Line Down", Order = 12, GroupName = "Visual")]
        public Stroke VerticalLineDown { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Alert Sound To Play", Description = "Sound file to play on threshold crossings", Order = 13, GroupName = "Alerts")]
        public string AlertSoundToPlay { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Alert Sounds", Description = "Enable sound alerts", Order = 14, GroupName = "Alerts")]
        public bool AlertSounds { get; set; }

        [Browsable(false)]
        [Display(Name = "Show Threshold Markers", Order = 15, GroupName = "Visual")]
        public bool ShowThresholdMarkers { get; set; } = true;

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
