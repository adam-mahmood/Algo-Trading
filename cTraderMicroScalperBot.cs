using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using cAlgo.API;

// cAlgo/cTrader Automate example micro-scalper targeting ~0.5 pip take-profit.
// Replace parameters (volume, symbol, sessions) and backtest thoroughly before live use.
// The strategy looks for mean-reversion entries near intraday/weekly extremes and
// short-term support/resistance, while enforcing a three-trades-per-day limit.

[Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
public class MicroScalperBot : Robot
{
    [Parameter("Symbol", DefaultValue = "EURUSD")]
    public string SymbolName { get; set; }

    [Parameter("Volume (units)", DefaultValue = 10000)]
    public int Volume { get; set; }

    [Parameter("Max Trades Per Day", DefaultValue = 3)]
    public int MaxTradesPerDay { get; set; }

    [Parameter("Take Profit (pips)", DefaultValue = 0.5)]
    public double TakeProfitPips { get; set; }

    [Parameter("Stop Loss (pips)", DefaultValue = 2.0)]
    public double StopLossPips { get; set; }

    [Parameter("Session Start (UTC hour)", DefaultValue = 6)]
    public int SessionStartHour { get; set; }

    [Parameter("Session End (UTC hour)", DefaultValue = 20)]
    public int SessionEndHour { get; set; }

    [Parameter("Recent Window (bars)", DefaultValue = 50)]
    public int RecentWindow { get; set; }

    private DateTime _currentDay;
    private int _tradesToday;
    private double _dailyHigh;
    private double _dailyLow;
    private double _weeklyHigh;
    private double _weeklyLow;
    private readonly Queue<double> _recentPrices = new();

    protected override void OnStart()
    {
        _currentDay = Server.Time.Date;
        _tradesToday = 0;
        InitializeLevelsFromHistory();
        PrimeRecentPrices();
    }

    protected override void OnBar()
    {
        var bar = Bars.Last(1);

        if (bar.OpenTime.Date != _currentDay)
        {
            ResetDailyCounters(bar);
        }

        UpdateDayWeekTracking(bar);
        UpdateRecentPrices(bar.Close);

        if (_tradesToday >= MaxTradesPerDay)
            return;

        if (!InSession(Server.Time))
            return;

        var direction = EvaluateSignal(bar);
        if (direction == TradeDirection.Flat)
            return;

        PlaceTrade(direction);
    }

    private void PlaceTrade(TradeDirection direction)
    {
        var result = ExecuteMarketOrder(
            direction == TradeDirection.Long ? TradeType.Buy : TradeType.Sell,
            SymbolName,
            Volume,
            nameof(MicroScalperBot),
            StopLossPips,
            TakeProfitPips);

        if (result?.IsSuccessful == true)
        {
            _tradesToday++;
        }
        else if (result != null)
        {
            Print($"Order failed: {result.Error}\n{result.Comments}");
        }
    }

    private bool InSession(DateTime utcNow)
    {
        var hour = utcNow.Hour;
        return hour >= SessionStartHour && hour <= SessionEndHour;
    }

    private TradeDirection EvaluateSignal(Bar bar)
    {
        var price = bar.Close;
        var recentHigh = _recentPrices.Count > 0 ? _recentPrices.Max() : price;
        var recentLow = _recentPrices.Count > 0 ? _recentPrices.Min() : price;

        bool nearDailyHigh = price >= _dailyHigh * 0.999;
        bool nearDailyLow = price <= _dailyLow * 1.001;
        bool nearWeeklyHigh = price >= _weeklyHigh * 0.999;
        bool nearWeeklyLow = price <= _weeklyLow * 1.001;
        bool nearRecentHigh = price >= recentHigh * 0.999;
        bool nearRecentLow = price <= recentLow * 1.001;

        if (nearDailyHigh || nearWeeklyHigh || nearRecentHigh)
            return TradeDirection.Short;

        if (nearDailyLow || nearWeeklyLow || nearRecentLow)
            return TradeDirection.Long;

        return TradeDirection.Flat;
    }

    private void ResetDailyCounters(Bar bar)
    {
        _currentDay = bar.OpenTime.Date;
        _tradesToday = 0;
        _dailyHigh = bar.High;
        _dailyLow = bar.Low;
    }

    private void UpdateDayWeekTracking(Bar bar)
    {
        _dailyHigh = Math.Max(_dailyHigh, bar.High);
        _dailyLow = Math.Min(_dailyLow, bar.Low);

        var isoWeek = ISOWeek.GetWeekOfYear(bar.OpenTime.Date);
        var currentWeek = ISOWeek.GetWeekOfYear(Server.Time.Date);

        if (isoWeek != currentWeek)
        {
            _weeklyHigh = bar.High;
            _weeklyLow = bar.Low;
        }
        else
        {
            _weeklyHigh = Math.Max(_weeklyHigh, bar.High);
            _weeklyLow = Math.Min(_weeklyLow, bar.Low);
        }
    }

    private void UpdateRecentPrices(double price)
    {
        _recentPrices.Enqueue(price);
        while (_recentPrices.Count > RecentWindow)
            _recentPrices.Dequeue();
    }

    private void InitializeLevelsFromHistory()
    {
        if (Bars.Count == 0)
        {
            _dailyHigh = _weeklyHigh = double.MinValue;
            _dailyLow = _weeklyLow = double.MaxValue;
            return;
        }

        var today = Server.Time.Date;
        var currentWeek = ISOWeek.GetWeekOfYear(today);

        var todayBars = Bars.Where(b => b.OpenTime.Date == today);
        if (todayBars.Any())
        {
            _dailyHigh = todayBars.Max(b => b.High);
            _dailyLow = todayBars.Min(b => b.Low);
        }
        else
        {
            var lastBar = Bars.Last(1);
            _dailyHigh = lastBar.High;
            _dailyLow = lastBar.Low;
        }

        var weekBars = Bars.Where(b => ISOWeek.GetWeekOfYear(b.OpenTime.Date) == currentWeek);
        if (weekBars.Any())
        {
            _weeklyHigh = weekBars.Max(b => b.High);
            _weeklyLow = weekBars.Min(b => b.Low);
        }
        else
        {
            var lastBar = Bars.Last(1);
            _weeklyHigh = lastBar.High;
            _weeklyLow = lastBar.Low;
        }
    }

    private void PrimeRecentPrices()
    {
        if (Bars.Count == 0)
            return;

        int start = Math.Max(0, Bars.Count - RecentWindow);
        for (int i = start; i < Bars.Count; i++)
            _recentPrices.Enqueue(Bars[i].Close);
    }
}

public enum TradeDirection
{
    Flat,
    Long,
    Short
}

public static class ISOWeek
{
    public static int GetWeekOfYear(DateTime date)
    {
        var day = (int)CultureInfo.InvariantCulture.Calendar.GetDayOfWeek(date);
        if (day == 0)
            day = 7;

        return CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(
            date,
            CalendarWeekRule.FirstFourDayWeek,
            DayOfWeek.Monday);
    }
}
