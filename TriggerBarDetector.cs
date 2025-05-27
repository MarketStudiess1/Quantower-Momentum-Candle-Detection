using System;
using System.Drawing;
using System.Linq;
using TradingPlatform.BusinessLayer;
using TradingPlatform.BusinessLayer.Utils;

namespace TriggerBar;

public class TriggerBarDetector : Indicator
{
    // Configuración de detección
    [InputParameter("Detect Elephant Bars", 10)]
    public bool detectElephantBars = true;
    [InputParameter("Detect Tail Bars", 20)]
    public bool detectTailBars = true;
    [InputParameter("Detect Engulfing Bars", 30)]
    public bool detectEngulfingBars = true;
    [InputParameter("Detect Swing High/Low", 40)]
    public bool detectSwingHighLow = true;

    // Configuración de Elephant Bars
    [InputParameter("Elephant Min Size (ATR Multiple)", 11, 0.1, 5.0, 0.1, 2)]
    public double elephantMinSize = 1.3;
    [InputParameter("Elephant Body Size (%)", 12, 0.1, 100.0, 0.1, 1)]
    public double elephantBodySizePercent = 70.0;
    [InputParameter("Elephant Bullish Color", 13)]
    public Color elephantBullishColor = Color.Green;
    [InputParameter("Elephant Bearish Color", 14)]
    public Color elephantBearishColor = Color.Red;

    // Configuración de Tail Bars
    [InputParameter("Tail Bar Min Size (ATR Multiple)", 21, 0.1, 10.0, 0.1, 2)]
    public double tailBarMinSize = 1.0;
    [InputParameter("Tail Min Percent (%)", 22, 0.1, 100.0, 0.1, 1)]
    public double tailMinPercent = 75.0;
    [InputParameter("Tail Bullish Color", 24)]
    public Color tailBullishColor = Color.PaleGreen;
    [InputParameter("Tail Bearish Color", 25)]
    public Color tailBearishColor = Color.Orange;

    // Configuración de Engulfing Bars
    [InputParameter("Float Allowance (Ticks)", 31, 0.0, 10.0, 0.1, 2)]
    public double floatAllowance = 0.0;
    [InputParameter("Engulfing Min Size (ATR Multiple)", 32, 0.1, 100.0, 0.1, 2)]
    public double engulfingMinSize = 1.0;
    [InputParameter("Engulf Wick", 33)]
    public bool engulfWick = false;
    [InputParameter("Engulfing Bullish Color", 34)]
    public Color engulfingBullishColor = Color.PowderBlue;
    [InputParameter("Engulfing Bearish Color", 35)]
    public Color engulfingBearishColor = Color.Pink;

    // Configuración de Swing High/Low
    [InputParameter("Swing Lookback", 41, 1, 50, 1, 0)]
    public int swingLookback = 10;
    [InputParameter("Swing Confirmation Bars", 42, 1, 10, 1, 0)]
    public int swingConfirmationBars = 1;
    [InputParameter("Swing High Color", 43)]
    public Color swingHighColor = Color.DarkSeaGreen;
    [InputParameter("Swing Low Color", 44)]
    public Color swingLowColor = Color.DarkSalmon;

    // Configuración de ATR
    [InputParameter("ATR Period", 1, 1, 999, 1, 0)]
    public int ATRPeriod = 14;

    // Configuración de visualización
    [InputParameter("Arrow Vertical Offset (Ticks)", 50, 0, 100, 1, 0)]
    public int arrowVerticalOffset = 5;
    [InputParameter("Arrow Size (Pixels)", 51, 5, 20, 1, 0)]
    public int arrowSize = 8;

    private Indicator atr;
    private double[] atrCache;
    private readonly SolidBrush[] brushes;

    public override string ShortName => "TriggerBar";
    public override string SourceCodeLink => "https://github.com/Quantower/Scripts/blob/main/Indicators/TriggerBarDetector.cs";

    public TriggerBarDetector() : base()
    {
        Name = "Trigger Bar Detector";
        Description = "Detects momentum candles (Elephant, Tail, Engulfing, Swing High/Low) and marks them with arrows.";
        SeparateWindow = false;
        UpdateType = IndicatorUpdateType.OnBarClose;

        brushes = new SolidBrush[]
        {
            new SolidBrush(elephantBearishColor), new SolidBrush(elephantBullishColor),
            new SolidBrush(tailBearishColor), new SolidBrush(tailBullishColor),
            new SolidBrush(engulfingBearishColor), new SolidBrush(engulfingBullishColor),
            new SolidBrush(swingHighColor), new SolidBrush(swingLowColor)
        };
    }

    protected override void OnInit()
    {
        atr = Core.Indicators.BuiltIn.ATR(ATRPeriod, MaMode.SMA, IndicatorCalculationType.AllAvailableData);
        AddIndicator(atr);
        atrCache = new double[HistoricalData.Count];
    }

    protected override void OnUpdate(UpdateArgs args)
    {
        int index = Count - 1;
        if (index < ATRPeriod) return;

        atr.Calculate(HistoricalData);
        atrCache[index] = atr.GetValue(0);
    }

    public override void OnPaintChart(PaintChartEventArgs args)
    {
        base.OnPaintChart(args);
        if (CurrentChart == null || !HistoricalData.Any()) return;

        Graphics graphics = args.Graphics;
        RectangleF prevClip = graphics.ClipBounds;
        graphics.SetClip(args.Rectangle);

        try
        {
            var mainWindow = CurrentChart.MainWindow;
            DateTime leftTime = mainWindow.CoordinatesConverter.GetTime(0);
            DateTime rightTime = mainWindow.CoordinatesConverter.GetTime(mainWindow.ClientRectangle.Width);
            int leftIndex = (int)mainWindow.CoordinatesConverter.GetBarIndex(leftTime);
            int rightIndex = (int)Math.Ceiling(mainWindow.CoordinatesConverter.GetBarIndex(rightTime));

            leftIndex = Math.Max(0, leftIndex - swingLookback);
            rightIndex = Math.Min(Count - 1, rightIndex);

            for (int i = leftIndex; i <= rightIndex; i++)
            {
                if (i < ATRPeriod || i < swingLookback) continue;

                IHistoryItem historyItem = HistoricalData[i, SeekOriginHistory.Begin];
                if (historyItem == null) continue;

                BarType barType = DetectBarType(i);
                if (barType == BarType.CommonBar) continue;

                bool isBullish = (barType == BarType.BullishElephant || barType == BarType.BullishTail ||
                                 barType == BarType.BullishEngulfing || barType == BarType.SwingLow);
                double labelPrice = isBullish ? historyItem[PriceType.Low] : historyItem[PriceType.High];
                double priceWithOffset = isBullish
                    ? labelPrice - arrowVerticalOffset * Symbol.TickSize
                    : labelPrice + arrowVerticalOffset * Symbol.TickSize;


                int x = (int)mainWindow.CoordinatesConverter.GetChartX(historyItem.TimeLeft);

                int barCenterX = x + this.CurrentChart.BarsWidth / 2;

                int y = (int)mainWindow.CoordinatesConverter.GetChartY(priceWithOffset);

                SolidBrush brush = brushes[(int)barType];
                DrawArrow(graphics, brush, barCenterX, y, isBullish);
            }
        }
        finally
        {
            graphics.SetClip(prevClip);
        }
    }

    private void DrawArrow(Graphics graphics, SolidBrush brush, int x, int y, bool isUpArrow)
    {
        PointF[] points;
        int halfSize = arrowSize / 2;

        if (isUpArrow)
        {
            points = new PointF[]
            {
                new PointF(x, y - halfSize), // Vértice superior
                new PointF(x - halfSize, y + halfSize), // Esquina inferior izquierda
                new PointF(x + halfSize, y + halfSize) // Esquina inferior derecha
            };
        }
        else
        {
            points = new PointF[]
            {
                new PointF(x, y + halfSize), // Vértice inferior
                new PointF(x - halfSize, y - halfSize), // Esquina superior izquierda
                new PointF(x + halfSize, y - halfSize) // Esquina superior derecha
            };
        }

        graphics.FillPolygon(brush, points);
    }

    private BarType DetectBarType(int index)
    {
        if (index >= Count || index < 1) return BarType.CommonBar;

        IHistoryItem currentItem = HistoricalData[index, SeekOriginHistory.Begin];
        IHistoryItem prevItem = HistoricalData[index - 1, SeekOriginHistory.Begin];
        if (currentItem == null || prevItem == null) return BarType.CommonBar;

        double closePrice = currentItem[PriceType.Close];
        double openPrice = currentItem[PriceType.Open];
        double lowPrice = currentItem[PriceType.Low];
        double highPrice = currentItem[PriceType.High];
        double prevClose = prevItem[PriceType.Close];
        double prevOpen = prevItem[PriceType.Open];
        double prevHigh = prevItem[PriceType.High];
        double prevLow = prevItem[PriceType.Low];

        double lowerTail = closePrice > openPrice ? openPrice - lowPrice : closePrice - lowPrice;
        double upperTail = closePrice > openPrice ? highPrice - closePrice : highPrice - openPrice;
        double bodySize = Math.Abs(closePrice - openPrice);
        double candleRange = highPrice - lowPrice;
        double bodyPercent = candleRange != 0 ? (bodySize / candleRange) * 100 : 0;
        double atrValue = atrCache[index];
        bool isBullish = closePrice > openPrice;
        double tailRatio = lowerTail > upperTail ? (lowerTail / candleRange) * 100 : (upperTail / candleRange) * 100;

        if (detectElephantBars && candleRange >= (elephantMinSize * atrValue) && bodyPercent >= elephantBodySizePercent)
            return isBullish ? BarType.BullishElephant : BarType.BearishElephant;

        if (detectTailBars && candleRange >= (tailBarMinSize * atrValue) && tailRatio >= tailMinPercent)
            return lowerTail > upperTail ? BarType.BullishTail : BarType.BearishTail;

        if (detectEngulfingBars && candleRange >= (engulfingMinSize * atrValue))
        {
            if (engulfWick)
            {
                if (closePrice > openPrice && prevClose < prevOpen && openPrice < prevLow && closePrice > prevHigh)
                    return BarType.BullishEngulfing;
                if (closePrice < openPrice && prevClose > prevOpen && openPrice > prevHigh && closePrice < prevLow)
                    return BarType.BearishEngulfing;
            }
            else
            {
                if (closePrice > openPrice && prevClose < prevOpen && openPrice < prevClose && closePrice > prevOpen)
                    return BarType.BullishEngulfing;
                if (closePrice < openPrice && prevClose > prevOpen && openPrice > prevClose && closePrice < prevOpen)
                    return BarType.BearishEngulfing;
            }
        }

        if (detectSwingHighLow && index >= swingLookback && index < Count - swingConfirmationBars)
        {
            if (IsSwingHigh(index))
                return BarType.SwingHigh;
            if (IsSwingLow(index))
                return BarType.SwingLow;
        }

        return BarType.CommonBar;
    }

    private bool IsSwingLow(int index)
    {
        IHistoryItem currentItem = HistoricalData[index, SeekOriginHistory.Begin];
        if (currentItem == null) return false;

        double currentHigh = currentItem[PriceType.High];
        for (int i = 1; i <= swingLookback; i++)
        {
            if (index - i >= 0)
            {
                IHistoryItem leftItem = HistoricalData[index - i, SeekOriginHistory.Begin];
                if (leftItem != null && leftItem[PriceType.High] > currentHigh)
                    return false;
            }
            if (index + i < Count)
            {
                IHistoryItem rightItem = HistoricalData[index + i, SeekOriginHistory.Begin];
                if (rightItem != null && rightItem[PriceType.High] > currentHigh)
                    return false;
            }
        }
        return true;
    }

    private bool IsSwingHigh(int index)
    {
        IHistoryItem currentItem = HistoricalData[index, SeekOriginHistory.Begin];
        if (currentItem == null) return false;

        double currentLow = currentItem[PriceType.Low];
        for (int i = 1; i <= swingLookback; i++)
        {
            if (index - i >= 0)
            {
                IHistoryItem leftItem = HistoricalData[index - i, SeekOriginHistory.Begin];
                if (leftItem != null && leftItem[PriceType.Low] < currentLow)
                    return false;
            }
            if (index + i < Count)
            {
                IHistoryItem rightItem = HistoricalData[index + i, SeekOriginHistory.Begin];
                if (rightItem != null && rightItem[PriceType.Low] < currentLow)
                    return false;
            }
        }
        return true;
    }

    public enum BarType
    {
        BearishElephant,
        BullishElephant,
        BearishTail,
        BullishTail,
        BearishEngulfing,
        BullishEngulfing,
        SwingHigh,
        SwingLow,
        CommonBar
    }

    protected override void OnClear()
    {
        atr?.Dispose();
        foreach (var brush in brushes)
            brush?.Dispose();
        atrCache = null;
    }

}
