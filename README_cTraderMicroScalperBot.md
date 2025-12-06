# cTrader Micro Scalper Bot (cAlgo Automate)

This file explains how to reuse `cTraderMicroScalperBot.cs` in another Git repository or within the cTrader Automate IDE.

## What the bot does
- Enforces three trades per day with configurable TP/SL in pips and a minimum gap between trades.
- Looks for mean-reversion entries near intraday/weekly extremes and recent support/resistance with wick/ATR filters for selectivity.
- Runs in UTC with configurable session hours.

## How to move the file into another repo
1. Copy `cTraderMicroScalperBot.cs` (and this README if you want the notes) into the target repository. Keeping the `AlgoTrading.cTrader` namespace avoids class-name collisions.
   - If you see build errors like **`Symbol`** or **`AverageTrueRange`** not found, ensure the project is being compiled inside cTrader Automate (which provides `cAlgo.API` and `cAlgo.API.Indicators`). Outside of Automate you’ll need stubs or a reference to the cTrader assemblies.
2. If the destination repo has a different root namespace, either keep this namespace or adjust it consistently across the file.
3. Open the repo in cTrader Automate (or drop the `.cs` file into **My Documents/cAlgo/Sources/Robots**). cTrader will compile it as a robot automatically.
4. Attach the bot to the symbol/timeframe you plan to trade. If the chart symbol differs from the `Symbol` parameter, the bot will log which symbol is used for orders.
5. Backtest/optimize before live use. Tight targets are sensitive to spread/commission.

## Parameter tips
- **Symbol**: The bot will default to the chart symbol if the requested symbol is unavailable. Keep the chart and parameter aligned when possible.
- **Volume**: Volume is in units; size it according to your broker’s contract size.
- **Session hours**: UTC hours; change them if your server is offset.
- **Recent Window**: Number of bars used for the short-term high/low buffer; reduce to make entries more reactive, increase to smooth.
- **Extreme Proximity / Close Off Extreme (pips)**: How tight the entry must be to a high/low and how far the close must pull back from the extreme to count as a rejection.
- **Min Confluence**: Minimum number of extremes (daily/weekly/recent) that must be hit to allow a trade.
- **ATR band (Min/Max)**: Only trades when volatility sits inside this band; widen to allow more trades, tighten to be pickier.
- **Wick:Body ratio**: Enforces rejection wicks (e.g., upper wick bigger than body for shorts) to filter noise.
- **Min Minutes Between Trades**: Forces spacing so the bot cannot cluster entries in one spike.

## Optimization / safety notes
- Start optimizations on the new filters (confluence, ATR band, wick ratio, proximity) to increase selectivity and reduce overtrading.
- Add your own risk controls (max daily loss, equity stop, news filter) before using live.
- Verify pip definitions for JPY/precious metals if you extend this sample.
