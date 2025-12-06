using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using cAlgo.API;

// cAlgo/cTrader Automate example micro-scalper targeting ~0.5 pip take-profit.
// Replace parameters (volume, symbol, sessions) and backtest thoroughly before live use.
// The strategy looks for mean-reversion entries near intraday/weekly extremes and
// short-term support/resistance, while enforcing a three-trades-per-day limit.
namespace AlgoTrading.cTrader
{
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

        [Parameter("Extreme Proximity (pips)", DefaultValue = 1.5)]
        public double ExtremeProximityPips { get; set; }

        [Parameter("Min Confluence (extremes hit)", DefaultValue = 2)]
        public int MinConfluence { get; set; }

        [Parameter("ATR Period", DefaultValue = 14)]
        public int AtrPeriod { get; set; }

        [Parameter("Min ATR (pips)", DefaultValue = 0.4)]
        public double MinAtrPips { get; set; }

        [Parameter("Max ATR (pips)", DefaultValue = 2.5)]
        public double MaxAtrPips { get; set; }

        [Parameter("Min Wick:Body Ratio", DefaultValue = 1.5)]
        public double WickToBodyRatio { get; set; }

        [Parameter("Close Off Extreme (pips)", DefaultValue = 0.1)]
        public double CloseOffExtremePips { get; set; }

        [Parameter("Min Minutes Between Trades", DefaultValue = 60)]
        public int MinMinutesBetweenTrades { get; set; }

        private DateTime _currentDay;
        private int _tradesToday;
        private double _dailyHigh;
        private double _dailyLow;
        private double _weeklyHigh;
        private double _weeklyLow;
        private readonly Queue<double> _recentPrices = new();
        private Symbol _symbol;
        private DateTime _lastTradeTime;
        private AverageTrueRange _atr;

        protected override void OnStart()
        {
            _symbol = Symbols.GetSymbol(SymbolName);
            if (_symbol == null)
            {
                Print($"Symbol '{SymbolName}' not found. Using chart symbol {Symbol.Name} instead.");
                _symbol = Symbol;
                SymbolName = Symbol.Name;
            }

            if (!string.Equals(_symbol.Name, Symbol.Name, StringComparison.OrdinalIgnoreCase))
            {
                Print($"Symbol parameter ({_symbol.Name}) differs from chart symbol ({Symbol.Name}). Using {_symbol.Name} for orders.");
            }

            _currentDay = Server.Time.Date;
            _tradesToday = 0;
            InitializeLevelsFromHistory();
            PrimeRecentPrices();
            _lastTradeTime = DateTime.MinValue;
            _atr = Indicators.AverageTrueRange(AtrPeriod, MovingAverageType.Exponential);
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

            if (MinutesSinceLastTrade() < MinMinutesBetweenTrades)
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
                _symbol.Name,
                Volume,
                nameof(MicroScalperBot),
                StopLossPips,
                TakeProfitPips);

            if (result?.IsSuccessful == true)
            {
                _tradesToday++;
                _lastTradeTime = Server.Time;
            }
            else if (result != null)
            {
                var error = result.Error != null ? result.Error.ToString() : "Unknown error";
                Print($"Order failed: {error}");
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

            var atrPips = _atr.Result.LastValue / _symbol.PipSize;
            if (atrPips < MinAtrPips || atrPips > MaxAtrPips)
                return TradeDirection.Flat;

            var tolerance = ExtremeProximityPips * _symbol.PipSize;

            bool nearDailyHigh = _dailyHigh != double.MinValue && price <= _dailyHigh && (_dailyHigh - price) <= tolerance;
            bool nearDailyLow = _dailyLow != double.MaxValue && price >= _dailyLow && (price - _dailyLow) <= tolerance;
            bool nearWeeklyHigh = _weeklyHigh != double.MinValue && price <= _weeklyHigh && (_weeklyHigh - price) <= tolerance;
            bool nearWeeklyLow = _weeklyLow != double.MaxValue && price >= _weeklyLow && (price - _weeklyLow) <= tolerance;
            bool nearRecentHigh = price <= recentHigh && (recentHigh - price) <= tolerance;
            bool nearRecentLow = price >= recentLow && (price - recentLow) <= tolerance;

            int shortConfluence = CountConfluence(nearDailyHigh, nearWeeklyHigh, nearRecentHigh);
            int longConfluence = CountConfluence(nearDailyLow, nearWeeklyLow, nearRecentLow);

            var upperWick = bar.High - Math.Max(bar.Open, bar.Close);
            var lowerWick = Math.Min(bar.Open, bar.Close) - bar.Low;
            var body = Math.Abs(bar.Close - bar.Open);
            var wickRequirement = body * WickToBodyRatio;
            var closeOffHigh = bar.High - bar.Close;
            var closeOffLow = bar.Close - bar.Low;
            var closeOffset = CloseOffExtremePips * _symbol.PipSize;

            bool shortPattern =
                shortConfluence >= MinConfluence &&
                (bar.High - price) <= tolerance &&
                upperWick >= wickRequirement &&
                closeOffHigh >= closeOffset;

            bool longPattern =
                longConfluence >= MinConfluence &&
                (price - bar.Low) <= tolerance &&
                lowerWick >= wickRequirement &&
                closeOffLow >= closeOffset;

            if (shortPattern)
                return TradeDirection.Short;

            if (longPattern)
                return TradeDirection.Long;

            return TradeDirection.Flat;
        }

        private int MinutesSinceLastTrade()
        {
            if (_lastTradeTime == DateTime.MinValue)
                return int.MaxValue;

            return (int)(Server.Time - _lastTradeTime).TotalMinutes;
        }

        private int CountConfluence(params bool[] hits)
        {
            int count = 0;
            foreach (var hit in hits)
            {
                if (hit)
                    count++;
            }

            return count;
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
}
