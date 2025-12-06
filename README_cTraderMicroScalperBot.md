# cTrader Micro Scalper Bot (cAlgo Automate)

This file explains how to reuse `cTraderMicroScalperBot.cs` in another Git repository or within the cTrader Automate IDE.

## What the bot does
- Enforces three trades per day with configurable TP/SL in pips.
- Looks for mean-reversion entries near intraday/weekly extremes and recent support/resistance.
- Runs in UTC with configurable session hours.

## How to move the file into another repo
1. Copy `cTraderMicroScalperBot.cs` (and this README if you want the notes) into the target repository. Keeping the `AlgoTrading.cTrader` namespace avoids class-name collisions.
2. If the destination repo has a different root namespace, either keep this namespace or adjust it consistently across the file.
3. Open the repo in cTrader Automate (or drop the `.cs` file into **My Documents/cAlgo/Sources/Robots**). cTrader will compile it as a robot automatically.
4. Attach the bot to the symbol/timeframe you plan to trade. If the chart symbol differs from the `Symbol` parameter, the bot will log which symbol is used for orders.
5. Backtest/optimize before live use. Tight targets are sensitive to spread/commission.

## Parameter tips
- **Symbol**: The bot will default to the chart symbol if the requested symbol is unavailable. Keep the chart and parameter aligned when possible.
- **Volume**: Volume is in units; size it according to your broker’s contract size.
- **Session hours**: UTC hours; change them if your server is offset.
- **Recent Window**: Number of bars used for the short-term high/low buffer; reduce to make entries more reactive, increase to smooth.

## Safety notes
- Add your own risk controls (max daily loss, equity stop, news filter) before using live.
- Verify pip definitions for JPY/precious metals if you extend this sample.
