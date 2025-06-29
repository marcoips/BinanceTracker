using System;
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
                    if (!string.IsNullOrEmpty(_selectedSymbol))
                        StartTrackingSelectedSymbol();
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
                AllSymbols.Clear();
                foreach (var symbol in symbols)
                    AllSymbols.Add(symbol);
            });
        }

        private void StartTrackingSelectedSymbol()
        {
            _timer?.Stop();
            _timer = new System.Timers.Timer(60_000); // 1 minute
            _timer.Elapsed += async (s, e) => await FetchSelectedCoinIndicator();
            _timer.AutoReset = true;
            _timer.Enabled = true;
            Task.Run(FetchSelectedCoinIndicator);
        }

        private async Task FetchSelectedCoinIndicator()
        {
            if (string.IsNullOrEmpty(SelectedSymbol)) return;

            var client = new BinanceRestClient();
            var klinesResult = await client.SpotApi.ExchangeData.GetKlinesAsync(
                SelectedSymbol,
                Binance.Net.Enums.KlineInterval.FifteenMinutes, // 15-minute interval
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
