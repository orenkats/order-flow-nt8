using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using System.Xml.Serialization;
using NinjaTrader.NinjaScript.DrawingTools;

namespace NinjaTrader.NinjaScript.Indicators
{
    public class BarVolumeProfile : Indicator
    {
		private double imbalanceThreshold = 2.5; 
        private Dictionary<int, Dictionary<double, VolumeData>> barVolumeLevels;
		private Dictionary<int, BarData> barData;       
        private SharpDX.Direct2D1.Brush volumeBrushDX;
        private SharpDX.Direct2D1.Brush pocBrushDX;
        private SharpDX.Direct2D1.Brush deltaBuyBrushDX;
        private SharpDX.Direct2D1.Brush deltaSellBrushDX;
		private SharpDX.Direct2D1.Brush regBrushDX;
        private SharpDX.DirectWrite.TextFormat textFormat;
		private SharpDX.DirectWrite.TextFormat boldTextFormat;
		private SharpDX.DirectWrite.TextFormat cvdTextFormat;
		private SharpDX.DirectWrite.TextFormat cvd15TextFormat;
		Brush textcolortext = Brushes.Black;

        private class VolumeData
        {
            public long TotalVolume { get; set; }
            public long BuyVolume { get; set; }
            public long SellVolume { get; set; }
            public long Delta => BuyVolume - SellVolume; // Difference between buy and sell volume
            
        }
		
		public class BarData
		{
		    public long BarDelta { get; set; }  
		    public double CVD5 { get; set; }    
			public double CVD15 { get; set; }   
		    public double? POC { get; set; }    
			public List<Tuple<double, double>> StackedImbalances { get; set; } = new List<Tuple<double, double>>(); 
		    public double? lowestBuyImbalanceLevel { get; set; }
		    public double? highestSellImbalanceLevel { get; set; }
			public bool BullishDiv { get; set; } 
    		public bool BearishDiv { get; set; } 
		}
		
		public BarData GetBarData(int barIndex)
		{
		    if (!barData.ContainsKey(barIndex))
		    {
		        Print($"BarData not available for BarIndex: {barIndex}");
		        return null;
		    }
		    return barData[barIndex];
		}

		private double lastPivotHigh = 0;
		private int lastPivotHighIndex = 0;
        private double lastPivotDeltaHigh = 0; 
		private double lastPivotLow = 0;
		private int lastPivotLowIndex = 0;
        private double lastPivotDeltaLow = 0; 
		
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"BarVolumeProfile";
                Name                                = "1 Bar Volume Profile";
                IsOverlay                           = true;
                IsChartOnly                         = true;
                DrawOnPricePanel                    = true;
                DrawHorizontalGridLines             = true;
                DrawVerticalGridLines               = true;
                PaintPriceMarkers                   = true;
                Bar_Width                           = 2.1;
                Calculate = Calculate.OnEachTick;
                ScaleJustification                  = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive            = false;
                MaximumBarsLookBack                 = MaximumBarsLookBack.Infinite;
                VolumeBrush                         = Brushes.Blue;
				RegBrush                            = Brushes.Black;
                POCBrush                            = Brushes.Black;
                DeltaBuyBrush                       = Brushes.Green;
                DeltaSellBrush                      = Brushes.Red;
                Text_size                           = 9;
                TicksPerLevel                       = 5;
				ShowVolumeText                      = true;
				ShowImbLine                         = false;
				
				 // Define a bold and larger text format
		        boldTextFormat = new SharpDX.DirectWrite.TextFormat(
		            NinjaTrader.Core.Globals.DirectWriteFactory,
		            "Arial",                                 
		            SharpDX.DirectWrite.FontWeight.Bold,     
		            SharpDX.DirectWrite.FontStyle.Normal,    
		            SharpDX.DirectWrite.FontStretch.Normal,  
		            10f                                      
        		);
                
            }
            else if (State == State.Configure)
            {
                barVolumeLevels = new Dictionary<int, Dictionary<double, VolumeData>>();
				barData = new Dictionary<int, BarData>();
                AddDataSeries(BarsPeriodType.Tick, 1);
            }

            else if (State == State.Terminated)
            {
                if (volumeBrushDX != null)
                {
                    volumeBrushDX.Dispose();
                    volumeBrushDX = null;
                }
                if (pocBrushDX != null)
                {
                    pocBrushDX.Dispose();
                    pocBrushDX = null;
                }
                if (deltaBuyBrushDX != null)
                {
                    deltaBuyBrushDX.Dispose();
                    deltaBuyBrushDX = null;
                }
                if (deltaSellBrushDX != null)
                {
                    deltaSellBrushDX.Dispose();
                    deltaSellBrushDX = null;
                }
				if (regBrushDX != null)
                {
                    regBrushDX.Dispose();
                    regBrushDX = null;
                }
                if (textFormat != null)
                {
                    textFormat.Dispose();
                    textFormat = null;
                }
				if (cvdTextFormat != null)
                {
                    cvdTextFormat.Dispose();
                    cvdTextFormat = null;
                }
				if (cvd15TextFormat != null)
                {
                    cvd15TextFormat.Dispose();
                    cvd15TextFormat = null;
                }
            }
        }
                          
		protected override void OnMarketData(MarketDataEventArgs e)
		{
			if (BarsInProgress == 0)
				return;
		    if (e.MarketDataType == MarketDataType.Last && CurrentBar > 0)
		    {
		        // Synchronize the current tick with the primary bar index
		        int primaryBarIndex = BarsArray[0].GetBar(Time[0]);
		        if (primaryBarIndex < 0)
		            return; // Skip if the mapping fails
		
		        // Initialize dictionaries for primaryBarIndex
		        if (!barVolumeLevels.ContainsKey(primaryBarIndex))
		        {
		            barVolumeLevels[primaryBarIndex] = new Dictionary<double, VolumeData>();
		        }
		
		        if (!barData.ContainsKey(primaryBarIndex))
		        {
		            barData[primaryBarIndex] = new BarData();
		        }
		
		        // Calculate the level key
		        double levelKey = Math.Floor(e.Price / (TickSize * TicksPerLevel)) * (TickSize * TicksPerLevel);
		
		        // Initialize VolumeData for this level if it doesn't exist
		        if (!barVolumeLevels[primaryBarIndex].ContainsKey(levelKey))
		        {
		            barVolumeLevels[primaryBarIndex][levelKey] = new VolumeData();
		        }
		
		        // Update total, buy, and sell volumes
		        barVolumeLevels[primaryBarIndex][levelKey].TotalVolume += e.Volume;
		
		        if (e.Price >= e.Ask && e.Ask > 0) // Ensure bid/ask are valid
		        {
		            barVolumeLevels[primaryBarIndex][levelKey].BuyVolume += e.Volume;
		        }
		        else if (e.Price <= e.Bid && e.Bid > 0)
		        {
		            barVolumeLevels[primaryBarIndex][levelKey].SellVolume += e.Volume;
		        }
		
		        // Update BarDelta
		        barData[primaryBarIndex].BarDelta = barVolumeLevels[primaryBarIndex]
		            .Values.Sum(v => v.Delta);
		
		        // Calculate CVD5 and CVD15
		        double sumLast4BarDeltas = 0;
		        double sumLast14BarDeltas = 0;
		
		        for (int i = primaryBarIndex - 1; i >= Math.Max(primaryBarIndex - 4, 0); i--)
		        {
		            if (barData.ContainsKey(i))
		            {
		                sumLast4BarDeltas += barData[i].BarDelta;
		            }
		        }
		
		        for (int i = primaryBarIndex - 1; i >= Math.Max(primaryBarIndex - 14, 0); i--)
		        {
		            if (barData.ContainsKey(i))
		            {
		                sumLast14BarDeltas += barData[i].BarDelta;
		            }
		        }
		
		        barData[primaryBarIndex].CVD5 = sumLast4BarDeltas + barData[primaryBarIndex].BarDelta;
		        barData[primaryBarIndex].CVD15 = sumLast14BarDeltas + barData[primaryBarIndex].BarDelta;
		
		        // Calculate POC
		        double pocKey = barVolumeLevels[primaryBarIndex]
		            .OrderByDescending(kv => kv.Value.TotalVolume)
		            .FirstOrDefault().Key;
		
		        barData[primaryBarIndex].POC = pocKey;
				
		        // Debug output
		        //Print($"Processed MarketData: PrimaryBarIndex={primaryBarIndex}, LevelKey={levelKey}, BuyVolume={barVolumeLevels[primaryBarIndex][levelKey].BuyVolume}, SellVolume={barVolumeLevels[primaryBarIndex][levelKey].SellVolume}");
		    }
		}
		
		protected override void OnBarUpdate()
		{
		    // Ensure this only processes the primary bar series
		    if (BarsInProgress != 0 )
		        return;

			if (!barData.ContainsKey(CurrentBar))
		    {
		        barData[CurrentBar] = new BarData();
				Print($"Initialized BarData for CurrentBar: {CurrentBar}");
		    }
			// Populate BarData for the CurrentBar
		    if (CurrentBar > 0)
		        CalculateImbalancesForBar(CurrentBar - 1); 
		   	
		    // Reset divergence flags for the current bar
	        barData[CurrentBar].BullishDiv = false;
	        barData[CurrentBar].BearishDiv = false;
			DetectDivergences();
		}
			
		private void CalculateImbalancesForBar(int barIndex)
		{
		    if (!barVolumeLevels.ContainsKey(barIndex)) return;
			
			double? lowestBuyImbalanceLevel = null;
    		double? highestSellImbalanceLevel = null;
		
		    foreach (var level in barVolumeLevels[barIndex].Keys)
		    {
		        double prevLevel = level - TickSize * TicksPerLevel;
		
		        if (barVolumeLevels[barIndex].ContainsKey(prevLevel))
		        {
		            double buySellRatio = barVolumeLevels[barIndex][level].BuyVolume /
		                                  (double)barVolumeLevels[barIndex][prevLevel].SellVolume;
		
		            double sellBuyRatio = barVolumeLevels[barIndex][prevLevel].SellVolume /
		                                  (double)barVolumeLevels[barIndex][level].BuyVolume;
		
		            if (buySellRatio >= imbalanceThreshold)
		            {
		                barData[barIndex].StackedImbalances.Add(new Tuple<double, double>(level, buySellRatio));
						lowestBuyImbalanceLevel = lowestBuyImbalanceLevel.HasValue
                    	? Math.Max(lowestBuyImbalanceLevel.Value, level)
                    	: level;
		                Print($"[PrevBar] Buy Imbalance at Level={level}, Ratio={buySellRatio:F1}");
		            }
		
		            if (sellBuyRatio >= imbalanceThreshold)
		            {
		                barData[barIndex].StackedImbalances.Add(new Tuple<double, double>(prevLevel, -sellBuyRatio));
						highestSellImbalanceLevel = highestSellImbalanceLevel.HasValue
                    	? Math.Max(highestSellImbalanceLevel.Value, prevLevel)
                    	: prevLevel;
		                Print($"[PrevBar] Sell Imbalance at Level={level}, Ratio={sellBuyRatio:F1}");
		            }
		        }
		    }
			
			// Store max imbalance levels in BarData
		    if (barData.ContainsKey(barIndex))
		    {
		        barData[barIndex].lowestBuyImbalanceLevel = lowestBuyImbalanceLevel;
		        barData[barIndex].highestSellImbalanceLevel = highestSellImbalanceLevel;
		    }
		}
		
		private void DetectDivergences()
		{
		    // Ensure enough bars are loaded
		    if (CurrentBar < 5 || BarsInProgress != 0) 
		        return;
		
		    // Detect Pivot High and Low
		    bool isPivotHigh = High[2] > High[3] && High[2] > High[1];
    		bool isPivotLow = Low[2] < Low[3] && Low[2] < Low[1];
			
		    if (isPivotHigh)
		    {
		        lastPivotHigh = High[2];
		        lastPivotHighIndex = CurrentBar - 2;
		
		        if (barData.ContainsKey(CurrentBar - 2))
		        {
		            lastPivotDeltaHigh = barData[CurrentBar - 2].CVD5; // Store CVD5 at pivot high
		        }
		    }
		
		    if (isPivotLow)
		    {
		        lastPivotLow = Low[2];
		        lastPivotLowIndex = CurrentBar - 2;
		
		        if (barData.ContainsKey(CurrentBar - 2))
		        {
		            lastPivotDeltaLow = barData[CurrentBar - 2].CVD5; // Store CVD5 at pivot low
		        }
		    }
		
		    // Detect Bearish Delta Divergence
		    if (lastPivotHigh != 0 && High[1] >= lastPivotHigh && barData.ContainsKey(CurrentBar - 1) && barData[CurrentBar - 1].CVD5 <= lastPivotDeltaHigh && barData[CurrentBar - 1].BarDelta <= barData[CurrentBar - 2].BarDelta)
		    {
				barData[CurrentBar - 1].BearishDiv = true;
		        // Calculate percentage change in CVD5
		        double percentageChange = ((barData[CurrentBar - 1].CVD5 - lastPivotDeltaHigh) / Math.Abs(lastPivotDeltaHigh)) * 100;
				
		        // Draw arrow and percentage text
		        DrawingTools.Draw.ArrowDown(this, $"BearishDivergence_{CurrentBar}", false, 1, High[1] + 10 * TickSize, Brushes.Red);
		
		        // Draw a red line from the last pivot high to the current high
		        DrawingTools.Draw.Line(this,$"BearishLine_{CurrentBar}",false,CurrentBar - lastPivotHighIndex, lastPivotHigh, 1, High[1], Brushes.Red,DashStyleHelper.Solid,1);
		
		       // Render percentage change text
		       //DrawingTools.Draw.Text(this,$"BearishChange_{CurrentBar}",$"{percentageChange:F1}%",1,High[1] + 20 * TickSize,Brushes.Red); // Use your desired color
		    }
			
		
		    // Detect Bullish Delta Divergence
		    if (lastPivotLow != 0 && Low[1] <= lastPivotLow && barData.ContainsKey(CurrentBar - 1) && barData[CurrentBar - 1].CVD5 > lastPivotDeltaLow  && barData[CurrentBar - 1].BarDelta >= barData[CurrentBar - 2].BarDelta)
		    {
				barData[CurrentBar - 1].BullishDiv = true;
		        // Calculate percentage change in CVD5
		        double percentageChange = ((barData[CurrentBar - 1].CVD5 - lastPivotDeltaLow) / Math.Abs(lastPivotDeltaLow)) * 100;
				
		        // Draw arrow and percentage text
		        DrawingTools.Draw.ArrowUp(this, $"BullishDivergence_{CurrentBar}", false, 1, Low[1] - 10 * TickSize, Brushes.Green);
		
		        // Draw a green line from the last pivot low to the current low
		        DrawingTools.Draw.Line(this,$"BullishLine_{CurrentBar}",false,CurrentBar - lastPivotLowIndex, lastPivotLow, 1, Low[1], Brushes.Green,DashStyleHelper.Solid,1);
		
		        // Render percentage change text
		        //DrawingTools.Draw.Text(this,$"BullishChange_{CurrentBar}",$"{percentageChange:F1}%",1,Low[1] - 20 * TickSize,Brushes.Green);
		    }
		}

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
		    if (Bars == null || ChartControl == null) return;
		
		    for (int bar = ChartBars.FromIndex; bar <= ChartBars.ToIndex; bar++)
		    {
		        // Ensure bar data exists
		        if (!barData.ContainsKey(bar) || !barVolumeLevels.ContainsKey(bar)) continue;
		
		        var volumeLevels = barVolumeLevels[bar];
		        var currentBarData = barData[bar];
		        float x = (float)chartControl.GetXByBarIndex(ChartBars, bar);
			
				if (ShowImbLine)
				{
					// Draw small line for lowestBuyImbalanceLevel
			        if (currentBarData.lowestBuyImbalanceLevel.HasValue)
			        {
			            float y = (float)chartScale.GetYByValue(currentBarData.lowestBuyImbalanceLevel.Value);
			            RenderTarget.DrawLine(
			                new SharpDX.Vector2(x , y + 6), 
			                new SharpDX.Vector2(x + 10, y + 6), 
			                deltaBuyBrushDX, 
			                1f               
			            );
			        }
			
			        // Draw small line for highestSellImbalanceLevel
			        if (currentBarData.highestSellImbalanceLevel.HasValue)
			        {
			            float y = (float)chartScale.GetYByValue(currentBarData.highestSellImbalanceLevel.Value);
			            RenderTarget.DrawLine(
			                new SharpDX.Vector2(x , y - 6), 
			                new SharpDX.Vector2(x + 10, y - 6), 
			                deltaSellBrushDX, 
			                1f                
			            );
			        }
				}
		        // Render Buy and Sell volumes for each price level
		        foreach (var level in volumeLevels)
		        {
		            double price = level.Key;
		            VolumeData volumeData = level.Value;
		            float y = (float)chartScale.GetYByValue(price);
		
		            // Format BuyVolume and SellVolume text
		            string buyVolumeText = $"{volumeData.BuyVolume}";
		            string sellVolumeText = $"{volumeData.SellVolume}";
		
		            // Use TextLayout to measure text width
		            var buyTextLayout = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, buyVolumeText, textFormat, float.MaxValue, float.MaxValue);
		            var sellTextLayout = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, sellVolumeText, textFormat, float.MaxValue, float.MaxValue);
		
		            float buyTextWidth = buyTextLayout.Metrics.Width;
		            float sellTextWidth = sellTextLayout.Metrics.Width;
		
		            // Calculate positions for centered alignment
		            float buyTextX = x + (float)ChartControl.BarWidth * 2 - buyTextWidth / 2 + 6;
		            float sellTextX = x - (float)ChartControl.BarWidth * 2 - sellTextWidth / 2 - 6;
					
					 // Determine color based on stacked imbalance
		            bool isBuyImbalance = currentBarData.StackedImbalances.Any(imbalance => imbalance.Item1 == price && imbalance.Item2 >= imbalanceThreshold);
					bool isSellImbalance = currentBarData.StackedImbalances.Any(imbalance => imbalance.Item1 == price && imbalance.Item2 <= -imbalanceThreshold);

		
		            var buyTextBrush = isBuyImbalance ? deltaBuyBrushDX : regBrushDX; 
		            var sellTextBrush = isSellImbalance ? deltaSellBrushDX : regBrushDX; 
		
		            if (ShowVolumeText)
		            {
		                // Render BuyVolume on the left
		                RenderTarget.DrawTextLayout(
		                    new SharpDX.Vector2(buyTextX, y - 6),
		                    buyTextLayout,
		                    buyTextBrush);
		
		                // Render SellVolume on the right
		                RenderTarget.DrawTextLayout(
		                    new SharpDX.Vector2(sellTextX, y - 6),
		                    sellTextLayout,
		                    sellTextBrush);
		            }
	
		            // Dispose of TextLayouts to prevent memory leaks
		            buyTextLayout.Dispose();
		            sellTextLayout.Dispose();
		        }
		
		        // Render POC as a single rectangle surrounding the text
		        if (currentBarData.POC.HasValue)
		        {
		            float pocY = (float)chartScale.GetYByValue(currentBarData.POC.Value);
		
		            // Get the volume data for the POC level
		            double pocLevel = currentBarData.POC.Value;
		            if (!barVolumeLevels[bar].ContainsKey(pocLevel))
		                continue; // Skip if POC level is missing
		
		            VolumeData pocVolumeData = barVolumeLevels[bar][pocLevel];
		
		            // Format the text for buy and sell volumes
		            string buyVolumeText = $"{pocVolumeData.BuyVolume}";
		            string sellVolumeText = $"{pocVolumeData.SellVolume}";
		
		            // Measure text layouts for buy and sell volumes
		            var buyTextLayout = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, buyVolumeText, textFormat, float.MaxValue, float.MaxValue);
		            var sellTextLayout = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, sellVolumeText, textFormat, float.MaxValue, float.MaxValue);
		
		            float buyTextWidth = buyTextLayout.Metrics.Width;
		            float sellTextWidth = sellTextLayout.Metrics.Width;
		            float textHeight = Math.Max(buyTextLayout.Metrics.Height, sellTextLayout.Metrics.Height);
		
		            // Calculate positions
		            float buyTextX = x - (float)ChartControl.BarWidth * 2 - buyTextWidth / 2 - 6;
		            float sellTextX = x + (float)ChartControl.BarWidth * 2 - sellTextWidth / 2 + 6;
		
		            // Calculate the rectangle dimensions to encompass both buy and sell text
		            float rectX = buyTextX - 4; // Slight padding to the left
		            float rectWidth = (sellTextX + sellTextWidth) - buyTextX + 8; // Cover from left edge of buy text to right edge of sell text, with padding
		            float rectY = pocY - textHeight / 2 - 4; // Center vertically around the text
		            float rectHeight = textHeight + 2; // Add padding
		
		            // Define brushes
		            var borderBrush = pocBrushDX; // Border color
		            var fillBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(0, 0, 0, 0.1f)); // Semi-transparent background
		
		            // Draw the rectangle
		            RenderTarget.FillRectangle(new SharpDX.RectangleF(rectX, rectY, rectWidth, rectHeight), fillBrush);
		            RenderTarget.DrawRectangle(new SharpDX.RectangleF(rectX, rectY, rectWidth, rectHeight), borderBrush, 1f);
		
		            // Dispose text layouts
		            buyTextLayout.Dispose();
		            sellTextLayout.Dispose();
		        }
				
				// Render Bar Delta above the candle's high
		        string barDeltaText = $"{currentBarData.BarDelta:F0}";
		
		        // Calculate the position above the bar's high
		        float barHighY = (float)chartScale.GetYByValue(ChartBars.Bars.GetHigh(bar)); // Get the Y-coordinate for the high
		        float barDeltaY = barHighY - 30; // Offset for positioning the text above the high
		
		        // Use TextLayout to measure text width for Bar Delta
		        var barDeltaLayout = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, barDeltaText, boldTextFormat, float.MaxValue, float.MaxValue);
		
		        float barDeltaWidth = barDeltaLayout.Metrics.Width;
		        float barDeltaX = x - barDeltaWidth / 2; // Center text above the bar
		      
		        // Define base position for the "table" rendering
		        float baseY = (float)chartScale.GetYByValue(chartScale.MinValue) - 50; // Adjust as needed
		        float rowHeight = 15; // Height between rows in the "table"
		
		        
		        string cvd5Text = $"{currentBarData.CVD5:F0}";
		        string cvd15Text = $"{currentBarData.CVD15:F0}";
		
		        var cvd5Layout = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, cvd5Text, textFormat, float.MaxValue, float.MaxValue);
		        var cvd15Layout = new SharpDX.DirectWrite.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, cvd15Text, textFormat, float.MaxValue, float.MaxValue);
		
		        float cvd5Width = cvd5Layout.Metrics.Width;
		        float cvd15Width = cvd15Layout.Metrics.Width;
		        
		        float cvd5X = x - cvd5Width / 2;
		        float cvd15X = x - cvd15Width / 2;
		
		        // Determine colors for each metric
		        SharpDX.Direct2D1.Brush barDeltaBrush = currentBarData.BarDelta > 0 ? deltaBuyBrushDX : deltaSellBrushDX;
		        SharpDX.Direct2D1.Brush cvd5Brush = currentBarData.CVD5 > 0 ? deltaBuyBrushDX : deltaSellBrushDX;
		        SharpDX.Direct2D1.Brush cvd15Brush = currentBarData.CVD15 > 0 ? deltaBuyBrushDX : deltaSellBrushDX;
		
		        // Render each row in the "table"
		        RenderTarget.DrawTextLayout(
		            new SharpDX.Vector2(barDeltaX, barDeltaY),
		            barDeltaLayout,
		            barDeltaBrush);
				
				// Render Bar Delta above the bar's high
		        
		        RenderTarget.DrawTextLayout(
		            new SharpDX.Vector2(cvd5X, baseY + rowHeight),
		            cvd5Layout,
		            cvd5Brush);
		
		        RenderTarget.DrawTextLayout(
		            new SharpDX.Vector2(cvd15X, baseY + rowHeight * 2),
		            cvd15Layout,
		            cvd15Brush);
		
		        // Dispose layouts to avoid memory leaks
		        barDeltaLayout.Dispose();
		        cvd5Layout.Dispose();
		        cvd15Layout.Dispose();
		    }
		}

        
        public override void OnRenderTargetChanged()
        {
			
            if (regBrushDX != null)
                regBrushDX.Dispose();
			 if (volumeBrushDX != null)
                volumeBrushDX.Dispose();
            if (pocBrushDX != null)
                pocBrushDX.Dispose();
            if (deltaBuyBrushDX != null)
                deltaBuyBrushDX.Dispose();
            if (deltaSellBrushDX != null)
                deltaSellBrushDX.Dispose();
			if (cvdTextFormat != null) 
				cvdTextFormat.Dispose();
			if (cvd15TextFormat != null) 
				cvd15TextFormat.Dispose();

            if (RenderTarget != null)
            {
				regBrushDX = RegBrush.ToDxBrush(RenderTarget);
                volumeBrushDX = VolumeBrush.ToDxBrush(RenderTarget);
                pocBrushDX = POCBrush.ToDxBrush(RenderTarget);
                deltaBuyBrushDX = DeltaBuyBrush.ToDxBrush(RenderTarget);
                deltaSellBrushDX = DeltaSellBrush.ToDxBrush(RenderTarget);
                textFormat = new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", Text_size);
				cvdTextFormat = new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", Text_size);
				cvd15TextFormat = new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", Text_size);
            }
        }
		
		[XmlIgnore]
        [Display(Name = "Text Color", Description = "Color of volume bars", Order = 1, GroupName = "Parameters")]
        public Brush RegBrush { get; set; }

        [XmlIgnore]
        [Display(Name = "Volume Color", Description = "Color of volume bars", Order = 1, GroupName = "Parameters")]
        public Brush VolumeBrush { get; set; }

        [Browsable(false)]
        public string VolumeBrushSerializable
        {
            get { return Serialize.BrushToString(VolumeBrush); }
            set { VolumeBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "POC Color", Description = "Color of the Point of Control", Order = 2, GroupName = "Parameters")]
        public Brush POCBrush { get; set; }

        [Browsable(false)]
        public string POCBrushSerializable
        {
            get { return Serialize.BrushToString(POCBrush); }
            set { POCBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Delta Buy Color", Description = "Color of positive delta", Order = 3, GroupName = "Parameters")]
        public Brush DeltaBuyBrush { get; set; }

        [Browsable(false)]
        public string DeltaBuyBrushSerializable
        {
            get { return Serialize.BrushToString(DeltaBuyBrush); }
            set { DeltaBuyBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Delta Sell Color", Description = "Color of negative delta", Order = 4, GroupName = "Parameters")]
        public Brush DeltaSellBrush { get; set; }

        [Browsable(false)]
        public string DeltaSellBrushSerializable
        {
            get { return Serialize.BrushToString(DeltaSellBrush); }
            set { DeltaSellBrush = Serialize.StringToBrush(value); }
        }

        [Range(1, 100)]
        [Display(Name = "Ticks Per Level", Description = "Number of ticks per volume level", Order = 5, GroupName = "Parameters")]
        public int TicksPerLevel { get; set; }
		
        [Range(0, int.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Bar Width", Description = "Volume Bar percentage width distance", GroupName = "Parameters", Order = 6)]
		public double Bar_Width
		{ get; set; }
	
		[Range(1, int.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Volume Text Size", Description = "Volume Text Size", GroupName = "Parameters", Order = 8)]
		public int Text_size
		{ get; set; }        
		
		[Display(Name = "Show Volume Text", Description = "Display volume text on the chart", Order = 7, GroupName = "Parameters")]
        public bool ShowVolumeText { get; set; }
		
		[Display(Name = "Show imblance line", Description = "Display volume text on the chart", Order = 7, GroupName = "Parameters")]
        public bool ShowImbLine { get; set; }

    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private BarVolumeProfile[] cacheBarVolumeProfile;
		public BarVolumeProfile BarVolumeProfile(double bar_Width, int text_size)
		{
			return BarVolumeProfile(Input, bar_Width, text_size);
		}

		public BarVolumeProfile BarVolumeProfile(ISeries<double> input, double bar_Width, int text_size)
		{
			if (cacheBarVolumeProfile != null)
				for (int idx = 0; idx < cacheBarVolumeProfile.Length; idx++)
					if (cacheBarVolumeProfile[idx] != null && cacheBarVolumeProfile[idx].Bar_Width == bar_Width && cacheBarVolumeProfile[idx].Text_size == text_size && cacheBarVolumeProfile[idx].EqualsInput(input))
						return cacheBarVolumeProfile[idx];
			return CacheIndicator<BarVolumeProfile>(new BarVolumeProfile(){ Bar_Width = bar_Width, Text_size = text_size }, input, ref cacheBarVolumeProfile);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.BarVolumeProfile BarVolumeProfile(double bar_Width, int text_size)
		{
			return indicator.BarVolumeProfile(Input, bar_Width, text_size);
		}

		public Indicators.BarVolumeProfile BarVolumeProfile(ISeries<double> input , double bar_Width, int text_size)
		{
			return indicator.BarVolumeProfile(input, bar_Width, text_size);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.BarVolumeProfile BarVolumeProfile(double bar_Width, int text_size)
		{
			return indicator.BarVolumeProfile(Input, bar_Width, text_size);
		}

		public Indicators.BarVolumeProfile BarVolumeProfile(ISeries<double> input , double bar_Width, int text_size)
		{
			return indicator.BarVolumeProfile(input, bar_Width, text_size);
		}
	}
}

#endregion
