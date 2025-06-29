using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using Binance.Net.Clients;
using Skender.Stock.Indicators;
using System.Windows.Forms;
using System.Drawing;
using System.Net.Http;
using System.Text.Json;
using System.Text;

namespace BinanceApp
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public ObservableCollection<CoinIndicator> AllCoinIndicators { get; set; } = new();

        private CoinIndicator _selectedBoughtCoin;
        public CoinIndicator SelectedBoughtCoin
        {
            get => _selectedBoughtCoin;
            set
            {
                _selectedBoughtCoin = value;
                OnPropertyChanged(nameof(SelectedBoughtCoin));
            }
        }

        private System.Timers.Timer _timer;
        private NotifyIcon _notifyIcon;

        // Track last alerted candle open time per symbol for buy and sell
        private ConcurrentDictionary<string, DateTime> _lastBuyAlertedCandle = new();
        private ConcurrentDictionary<string, DateTime> _lastSellAlertedCandle = new();

        // Set your Discord webhook URL here
        private const string DiscordWebhookUrl = "https://discord.com/api/webhooks/1388951845992665208/aKR6tKgPS3Wuz5uEgRLQnrMf82x6_UeZjdd4wtmi0RT4LFtPlPuaIHcp0SEOwjsRgaOl";

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            SetupTrayIcon();
            LoadAllSymbols();
        }

        private void SetupTrayIcon()
        {
            _notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Visible = true,
                Text = "Binance Coin Tracker"
            };
            _notifyIcon.DoubleClick += (s, e) =>
            {
                this.Show();
                this.WindowState = WindowState.Normal;
                this.Activate();
            };
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            if (WindowState == WindowState.Minimized)
            {
                this.Hide();
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _notifyIcon?.Dispose();
            base.OnClosing(e);
        }

        private async void LoadAllSymbols()
        {
            var client = new BinanceRestClient();
            var exchangeInfo = await client.SpotApi.ExchangeData.GetExchangeInfoAsync();
            if (!exchangeInfo.Success) return;

            var symbols = exchangeInfo.Data.Symbols
                .Where(s => s.QuoteAsset == "EUR" && s.Status == Binance.Net.Enums.SymbolStatus.Trading)
                .Select(s => s.Name)
                .OrderBy(s => s)
                .ToList();

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                AllCoinIndicators.Clear();
                foreach (var symbol in symbols)
                    AllCoinIndicators.Add(new CoinIndicator { Symbol = symbol });
            });

            await ScanAllCoinsForAlerts();
            StartScanningAllCoins();
        }

        private void StartScanningAllCoins()
        {
            _timer?.Stop();
            _timer = new System.Timers.Timer(900_000); // 15 minutes
            _timer.Elapsed += async (s, e) => await ScanAllCoinsForAlerts();
            _timer.AutoReset = true;
            _timer.Enabled = true;
        }

        private async Task ScanAllCoinsForAlerts()
        {
            var client = new BinanceRestClient();
            foreach (var coin in AllCoinIndicators.ToList())
            {
                var klinesResult = await client.SpotApi.ExchangeData.GetKlinesAsync(
                    coin.Symbol,
                    Binance.Net.Enums.KlineInterval.FifteenMinutes,
                    limit: 100);

                if (!klinesResult.Success) continue;

                var quotes = klinesResult.Data.Select(k => new Quote
                {
                    Date = k.OpenTime,
                    Open = k.OpenPrice,
                    High = k.HighPrice,
                    Low = k.LowPrice,
                    Close = k.ClosePrice,
                    Volume = k.Volume
                }).ToList();

                if (quotes.Count < 14) continue;

                var lastCandle = klinesResult.Data.Last();
                var lastCandleTime = lastCandle.OpenTime;

                var rsi = quotes.GetRsi(14).LastOrDefault()?.Rsi ?? 0;
                var vwap = quotes.GetVwap().LastOrDefault()?.Vwap ?? 0;
                var currentPrice = quotes.Last().Close;

                // Buy alert logic for any coin, only once per candle
                if (rsi < 30 && (double)currentPrice < vwap)
                {
                    if (!_lastBuyAlertedCandle.TryGetValue(coin.Symbol, out var alertedTime) || alertedTime != lastCandleTime)
                    {
                        string alertMsg = $"{coin.Symbol} BUY OPPORTUNITY!";
                        await SendDiscordAlertAsync(alertMsg);
                        _lastBuyAlertedCandle[coin.Symbol] = lastCandleTime;
                    }
                }

                // Sell alert logic for only the selected bought coin, only once per candle
                if (SelectedBoughtCoin != null &&
                    coin.Symbol == SelectedBoughtCoin.Symbol &&
                    rsi > 70 && (double)currentPrice > vwap)
                {
                    if (!_lastSellAlertedCandle.TryGetValue(coin.Symbol, out var alertedTime) || alertedTime != lastCandleTime)
                    {
                        string alertMsg = $"{coin.Symbol} SELL OPPORTUNITY!";
                        await SendDiscordAlertAsync(alertMsg);
                        _lastSellAlertedCandle[coin.Symbol] = lastCandleTime;
                    }
                }

                // Update the coin's indicator in the list
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    coin.RSI = rsi;
                    coin.VWAP = vwap;
                    coin.CurrentPrice = currentPrice;
                });

                await Task.Delay(200); // Throttle to avoid rate limits
            }
        }

        // Send alert to Discord webhook
        private async Task SendDiscordAlertAsync(string message)
        {
            try
            {
                using var client = new HttpClient();
                var payload = new { content = message };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                await client.PostAsync(DiscordWebhookUrl, content);
            }
            catch
            {
                // Optionally handle/log errors
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class CoinIndicator : INotifyPropertyChanged
    {
        public string Symbol { get; set; }

        private double _rsi;
        public double RSI { get => _rsi; set { _rsi = value; OnPropertyChanged(nameof(RSI)); } }

        private double _vwap;
        public double VWAP { get => _vwap; set { _vwap = value; OnPropertyChanged(nameof(VWAP)); } }

        private decimal _currentPrice;
        public decimal CurrentPrice { get => _currentPrice; set { _currentPrice = value; OnPropertyChanged(nameof(CurrentPrice)); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
