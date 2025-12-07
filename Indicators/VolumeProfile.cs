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
    /// VolumeProfile - Advanced volume analysis indicator
    /// Tracks volume trends, detects volume climax, and identifies accumulation/distribution patterns
    /// </summary>
    public class VolumeProfile : Indicator
    {
        #region Private Variables

        // Volume moving averages
        private SMA volumeSMA;
        private EMA volumeEMA;
        private SMA longVolumeSMA;

        // Volume analysis series
        private Series<double> relativeVolume;
        private Series<double> volumeTrend;
        private Series<double> buyVolume;
        private Series<double> sellVolume;
        private Series<double> volumeDelta;

        // Detection thresholds
        private double climaxThreshold;
        private double dryUpThreshold;

        #endregion

        #region OnStateChange

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Volume Profile - Advanced volume analysis for trade confirmation";
                Name = "VolumeProfile";
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
                ShortPeriod = 10;
                MediumPeriod = 20;
                LongPeriod = 50;
                ClimaxMultiplier = 2.5;
                DryUpMultiplier = 0.5;
                DeltaSmoothPeriod = 5;

                // Plots
                AddPlot(new Stroke(Brushes.DodgerBlue, 2), PlotStyle.Bar, "RelativeVolume");
                AddPlot(new Stroke(Brushes.Lime, 2), PlotStyle.Line, "VolumeDelta");
                AddPlot(new Stroke(Brushes.Orange, 1), PlotStyle.Line, "VolumeTrend");

                // Reference lines
                AddLine(Brushes.White, 1, "Average");
                AddLine(Brushes.Green, 2, "High Volume");
                AddLine(Brushes.Red, 0.5, "Low Volume");
            }
            else if (State == State.DataLoaded)
            {
                // Initialize indicators
                volumeSMA = SMA(Volume, ShortPeriod);
                volumeEMA = EMA(Volume, MediumPeriod);
                longVolumeSMA = SMA(Volume, LongPeriod);

                // Initialize series
                relativeVolume = new Series<double>(this);
                volumeTrend = new Series<double>(this);
                buyVolume = new Series<double>(this);
                sellVolume = new Series<double>(this);
                volumeDelta = new Series<double>(this);
            }
        }

        #endregion

        #region OnBarUpdate

        protected override void OnBarUpdate()
        {
            if (CurrentBar < LongPeriod)
            {
                RelativeVolumeValue[0] = 1;
                VolumeDeltaValue[0] = 0;
                VolumeTrendValue[0] = 0;
                return;
            }

            // Calculate relative volume
            double avgVolume = volumeSMA[0];
            if (avgVolume > 0)
                relativeVolume[0] = Volume[0] / avgVolume;
            else
                relativeVolume[0] = 1;

            // Estimate buy vs sell volume based on candle position
            CalculateBuySellVolume();

            // Calculate volume delta (smoothed)
            volumeDelta[0] = buyVolume[0] - sellVolume[0];

            // Calculate volume trend (is volume increasing or decreasing)
            volumeTrend[0] = volumeEMA[0] / longVolumeSMA[0];

            // Set plot values
            RelativeVolumeValue[0] = relativeVolume[0];

            // Smooth delta for plotting
            double deltaSum = 0;
            for (int i = 0; i < Math.Min(DeltaSmoothPeriod, CurrentBar); i++)
                deltaSum += volumeDelta[i];
            VolumeDeltaValue[0] = deltaSum / Math.Min(DeltaSmoothPeriod, CurrentBar);

            VolumeTrendValue[0] = volumeTrend[0];

            // Color relative volume bars
            ColorVolumeBars();
        }

        #endregion

        #region Calculations

        private void CalculateBuySellVolume()
        {
            double range = High[0] - Low[0];

            if (range == 0)
            {
                // No range, split evenly
                buyVolume[0] = Volume[0] * 0.5;
                sellVolume[0] = Volume[0] * 0.5;
                return;
            }

            // Calculate close position within the bar
            double closePosition = (Close[0] - Low[0]) / range;

            // Estimate buy/sell volume based on where price closed
            // Close near high = more buying, close near low = more selling
            buyVolume[0] = Volume[0] * closePosition;
            sellVolume[0] = Volume[0] * (1 - closePosition);

            // Adjust for bar direction
            if (Close[0] > Open[0])
            {
                // Bullish bar - weight more toward buying
                buyVolume[0] = buyVolume[0] * 1.2;
                sellVolume[0] = sellVolume[0] * 0.8;
            }
            else if (Close[0] < Open[0])
            {
                // Bearish bar - weight more toward selling
                buyVolume[0] = buyVolume[0] * 0.8;
                sellVolume[0] = sellVolume[0] * 1.2;
            }

            // Normalize to ensure total equals Volume
            double total = buyVolume[0] + sellVolume[0];
            if (total > 0)
            {
                buyVolume[0] = (buyVolume[0] / total) * Volume[0];
                sellVolume[0] = (sellVolume[0] / total) * Volume[0];
            }
        }

        private void ColorVolumeBars()
        {
            if (relativeVolume[0] >= ClimaxMultiplier)
            {
                // Volume climax
                if (Close[0] > Open[0])
                    PlotBrushes[0][0] = Brushes.Lime; // Bullish climax
                else
                    PlotBrushes[0][0] = Brushes.Red; // Bearish climax
            }
            else if (relativeVolume[0] >= 1.5)
            {
                // High volume
                if (Close[0] > Open[0])
                    PlotBrushes[0][0] = Brushes.Green;
                else
                    PlotBrushes[0][0] = Brushes.Crimson;
            }
            else if (relativeVolume[0] <= DryUpMultiplier)
            {
                // Dry up
                PlotBrushes[0][0] = Brushes.Gray;
            }
            else
            {
                // Normal volume
                if (Close[0] > Open[0])
                    PlotBrushes[0][0] = Brushes.DarkGreen;
                else
                    PlotBrushes[0][0] = Brushes.DarkRed;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Returns true if current volume is a climax (extremely high)
        /// </summary>
        public bool IsVolumeClimax()
        {
            return relativeVolume[0] >= ClimaxMultiplier;
        }

        /// <summary>
        /// Returns true if volume is drying up (very low)
        /// </summary>
        public bool IsVolumeDryUp()
        {
            return relativeVolume[0] <= DryUpMultiplier;
        }

        /// <summary>
        /// Returns true if volume is above average
        /// </summary>
        public bool IsAboveAverageVolume()
        {
            return relativeVolume[0] > 1;
        }

        /// <summary>
        /// Returns true if volume trend is increasing
        /// </summary>
        public bool IsVolumeTrendUp()
        {
            return volumeTrend[0] > 1;
        }

        /// <summary>
        /// Returns true if there's positive volume delta (more buying)
        /// </summary>
        public bool IsPositiveDelta()
        {
            return VolumeDeltaValue[0] > 0;
        }

        /// <summary>
        /// Returns true if volume confirms price direction
        /// </summary>
        public bool VolumeConfirmsPrice()
        {
            bool priceUp = Close[0] > Close[1];
            bool volumeUp = relativeVolume[0] > 1;
            bool deltaPositive = VolumeDeltaValue[0] > 0;

            if (priceUp)
                return volumeUp && deltaPositive;
            else
                return volumeUp && !deltaPositive;
        }

        /// <summary>
        /// Returns accumulation/distribution signal
        /// 1 = Accumulation (buying on down bars), -1 = Distribution, 0 = Neutral
        /// </summary>
        public int GetAccumulationDistribution()
        {
            // Look for high volume on down bars followed by reversal (accumulation)
            // or high volume on up bars followed by reversal (distribution)

            if (CurrentBar < 3) return 0;

            // Accumulation: High volume down bar, followed by up bar
            if (relativeVolume[1] > 1.5 && Close[1] < Open[1] && Close[0] > Open[0])
                return 1;

            // Distribution: High volume up bar, followed by down bar
            if (relativeVolume[1] > 1.5 && Close[1] > Open[1] && Close[0] < Open[0])
                return -1;

            return 0;
        }

        #endregion

        #region Properties

        [NinjaScriptProperty]
        [Range(5, 30)]
        [Display(Name = "Short Period", Order = 1, GroupName = "Parameters")]
        public int ShortPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(10, 50)]
        [Display(Name = "Medium Period", Order = 2, GroupName = "Parameters")]
        public int MediumPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(30, 100)]
        [Display(Name = "Long Period", Order = 3, GroupName = "Parameters")]
        public int LongPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1.5, 5)]
        [Display(Name = "Climax Multiplier", Order = 4, GroupName = "Parameters")]
        public double ClimaxMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 0.8)]
        [Display(Name = "Dry Up Multiplier", Order = 5, GroupName = "Parameters")]
        public double DryUpMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Delta Smooth Period", Order = 6, GroupName = "Parameters")]
        public int DeltaSmoothPeriod { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> RelativeVolumeValue
        {
            get { return Values[0]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> VolumeDeltaValue
        {
            get { return Values[1]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> VolumeTrendValue
        {
            get { return Values[2]; }
        }

        #endregion
    }
}
