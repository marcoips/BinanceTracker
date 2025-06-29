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
        public ObservableCollection<string> AllSymbols { get; set; } = new();
        private string _selectedSymbol;
        public string SelectedSymbol
        {
            get => _selectedSymbol;
            set
            {
                if (_selectedSymbol != value)
                {
                    _selectedSymbol = value;
                    OnPropertyChanged(nameof(SelectedSymbol));
                    UpdateSelectedCoinIndicator();
                }
            }
        }

        private CoinIndicator _selectedCoinIndicator = new();
        public CoinIndicator SelectedCoinIndicator
        {
            get => _selectedCoinIndicator;
            set
            {
                _selectedCoinIndicator = value;
                OnPropertyChanged(nameof(SelectedCoinIndicator));
            }
        }

        private System.Timers.Timer _timer;
        private NotifyIcon _notifyIcon;

        // Track last alerted candle open time per symbol
        private ConcurrentDictionary<string, DateTime> _lastAlertedCandle = new();

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

            // Use Dispatcher to update AllSymbols and THEN start scanning
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                AllSymbols.Clear();
                foreach (var symbol in symbols)
                    AllSymbols.Add(symbol);
            });

            // Start scanning after AllSymbols is populated
            await ScanAllCoinsForAlerts();
            StartScanningAllCoins();
        }



        private void StartScanningAllCoins()
        {
            _timer?.Stop();
            _timer = new System.Timers.Timer(60_000); // 1 minute
            _timer.Elapsed += async (s, e) => await ScanAllCoinsForAlerts();
            _timer.AutoReset = true;
            _timer.Enabled = true;
            // Do NOT call Task.Run(ScanAllCoinsForAlerts) here, let the timer handle it
        }

        private async Task ScanAllCoinsForAlerts()
        {
            var client = new BinanceRestClient();
            foreach (var symbol in AllSymbols.ToList())
            {
                var klinesResult = await client.SpotApi.ExchangeData.GetKlinesAsync(
                    symbol,
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

                // Alert logic for any coin, only once per candle
                if (rsi < 30 && (double)currentPrice < vwap)
                {
                    if (!_lastAlertedCandle.TryGetValue(symbol, out var alertedTime) || alertedTime != lastCandleTime)
                    {
                        string alertMsg = $"{symbol}  BUY! ";
                        await SendDiscordAlertAsync(alertMsg);
                        _lastAlertedCandle[symbol] = lastCandleTime;
                    }
                }

                // If this is the selected coin, update the UI
                if (symbol == SelectedSymbol)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        SelectedCoinIndicator = new CoinIndicator
                        {
                            Symbol = symbol,
                            RSI = rsi,
                            VWAP = vwap,
                            CurrentPrice = currentPrice
                        };
                    });
                }

                // Optional: throttle requests to avoid rate limits
                await Task.Delay(200);
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

        // Update the UI for the selected coin immediately when changed
        private async void UpdateSelectedCoinIndicator()
        {
            if (string.IsNullOrEmpty(SelectedSymbol)) return;

            var client = new BinanceRestClient();
            var klinesResult = await client.SpotApi.ExchangeData.GetKlinesAsync(
                SelectedSymbol,
                Binance.Net.Enums.KlineInterval.FifteenMinutes,
                limit: 100);

            if (!klinesResult.Success) return;

            var quotes = klinesResult.Data.Select(k => new Quote
            {
                Date = k.OpenTime,
                Open = k.OpenPrice,
                High = k.HighPrice,
                Low = k.LowPrice,
                Close = k.ClosePrice,
                Volume = k.Volume
            }).ToList();

            if (quotes.Count < 14) return;

            var rsi = quotes.GetRsi(14).LastOrDefault()?.Rsi ?? 0;
            var vwap = quotes.GetVwap().LastOrDefault()?.Vwap ?? 0;
            var currentPrice = quotes.Last().Close;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                SelectedCoinIndicator = new CoinIndicator
                {
                    Symbol = SelectedSymbol,
                    RSI = rsi,
                    VWAP = vwap,
                    CurrentPrice = currentPrice
                };
            });
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class CoinIndicator
    {
        public string Symbol { get; set; }
        public double RSI { get; set; }
        public double VWAP { get; set; }
        public decimal CurrentPrice { get; set; }
    }
}
