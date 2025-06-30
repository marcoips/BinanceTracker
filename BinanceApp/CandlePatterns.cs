using System;
using System.Collections.Generic;
using System.Linq;

namespace BinanceApp
{
    public static class CandlePatterns
    {
        // Represents a single OHLCV candle
        public class Candle
        {
            public DateTime Date { get; set; }
            public decimal Open { get; set; }
            public decimal High { get; set; }
            public decimal Low { get; set; }
            public decimal Close { get; set; }
            public decimal Volume { get; set; }
        }

        // Bullish Engulfing: Potential Buy Signal
        public static bool IsBullishEngulfing(IList<Candle> candles)
        {
            if (candles == null || candles.Count < 2) return false;
            var prev = candles[^2];
            var curr = candles[^1];
            return prev.Close < prev.Open && // Previous is bearish
                   curr.Close > curr.Open && // Current is bullish
                   curr.Open < prev.Close &&
                   curr.Close > prev.Open;
        }

        // Bearish Engulfing: Potential Sell Signal
        public static bool IsBearishEngulfing(IList<Candle> candles)
        {
            if (candles == null || candles.Count < 2) return false;
            var prev = candles[^2];
            var curr = candles[^1];
            return prev.Close > prev.Open && // Previous is bullish
                   curr.Close < curr.Open && // Current is bearish
                   curr.Open > prev.Close &&
                   curr.Close < prev.Open;
        }

        // Hammer: Potential Buy Signal
        public static bool IsHammer(IList<Candle> candles)
        {
            if (candles == null || candles.Count < 1) return false;
            var c = candles[^1];
            var body = Math.Abs(c.Close - c.Open);
            var lowerShadow = Math.Min(c.Open, c.Close) - c.Low;
            var upperShadow = c.High - Math.Max(c.Open, c.Close);
            return body < (c.High - c.Low) * 0.3m &&
                   lowerShadow > body * 2 &&
                   upperShadow < body;
        }

        // Shooting Star: Potential Sell Signal
        public static bool IsShootingStar(IList<Candle> candles)
        {
            if (candles == null || candles.Count < 1) return false;
            var c = candles[^1];
            var body = Math.Abs(c.Close - c.Open);
            var upperShadow = c.High - Math.Max(c.Open, c.Close);
            var lowerShadow = Math.Min(c.Open, c.Close) - c.Low;
            return body < (c.High - c.Low) * 0.3m &&
                   upperShadow > body * 2 &&
                   lowerShadow < body;
        }

        // Doji: Indecision, can be reversal signal
        public static bool IsDoji(IList<Candle> candles, decimal threshold = 0.1m)
        {
            if (candles == null || candles.Count < 1) return false;
            var c = candles[^1];
            var body = Math.Abs(c.Close - c.Open);
            return body <= (c.High - c.Low) * threshold;
        }

        // Morning Star: Potential Buy Signal
        public static bool IsMorningStar(IList<Candle> candles)
        {
            if (candles == null || candles.Count < 3) return false;
            var c1 = candles[^3];
            var c2 = candles[^2];
            var c3 = candles[^1];
            return c1.Close < c1.Open && // First is bearish
                   Math.Abs(c2.Close - c2.Open) < (c2.High - c2.Low) * 0.3m && // Second is small body
                   c3.Close > c3.Open && // Third is bullish
                   c3.Close > ((c1.Open + c1.Close) / 2);
        }

        // Evening Star: Potential Sell Signal
        public static bool IsEveningStar(IList<Candle> candles)
        {
            if (candles == null || candles.Count < 3) return false;
            var c1 = candles[^3];
            var c2 = candles[^2];
            var c3 = candles[^1];
            return c1.Close > c1.Open && // First is bullish
                   Math.Abs(c2.Close - c2.Open) < (c2.High - c2.Low) * 0.3m && // Second is small body
                   c3.Close < c3.Open && // Third is bearish
                   c3.Close < ((c1.Open + c1.Close) / 2);
        }

        // Pattern analysis and signal
        public static (List<string> Patterns, string Signal) AnalyzeCandlePatterns(IList<Candle> candles)
        {
            var patterns = new List<string>();
            bool isBuy = false, isSell = false;

            if (IsBullishEngulfing(candles))
            {
                patterns.Add("Bullish Engulfing - Bullish");
                isBuy = true;
            }
            if (IsHammer(candles))
            {
                patterns.Add("Hammer - Bullish");
                isBuy = true;
            }
            if (IsMorningStar(candles))
            {
                patterns.Add("Morning Star - Bullish");
                isBuy = true;
            }
            if (IsBearishEngulfing(candles))
            {
                patterns.Add("Bearish Engulfing - Bearish");
                isSell = true;
            }
            if (IsShootingStar(candles))
            {
                patterns.Add("Shooting Star - Bearish");
                isSell = true;
            }
            if (IsEveningStar(candles))
            {
                patterns.Add("Evening Star - Bearish");
                isSell = true;
            }
            if (IsDoji(candles))
            {
                patterns.Add("Doji - Neutral");
            }

            string signal = "----";
            if (isBuy && !isSell)
                signal = "BUY";
            else if (isSell && !isBuy)
                signal = "SELL";
            else if (isBuy && isSell)
                signal = "BUY/SELL";

            return (patterns, signal);
        }

    }
}
