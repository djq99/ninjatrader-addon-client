using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using NT8Bridge.Core;
using NT8Bridge.Util;

namespace NT8Bridge.Addon
{
    public partial class ConfigWindow : Window
    {
        private readonly ExternalCommandListener _commandListener;
        private readonly ILogger _logger;
        private readonly System.Windows.Threading.DispatcherTimer _updateTimer;
        private readonly List<double> _latencyHistory = new List<double>();
        private DateTime _startTime;

        public ConfigWindow(ExternalCommandListener commandListener, ILogger logger)
        {
            InitializeComponent();
            _commandListener = commandListener;
            _logger = logger;
            _startTime = DateTime.Now;

            // Initialize update timer
            _updateTimer = new System.Windows.Threading.DispatcherTimer();
            _updateTimer.Interval = TimeSpan.FromSeconds(1);
            _updateTimer.Tick += UpdateTimer_Tick;
            _updateTimer.Start();

            // Initialize UI
            InitializeUI();
        }

        private void InitializeUI()
        {
            // Set initial status
            UpdateStatus(false);
            
            // Load default symbols
            LoadDefaultSymbols();
            
            // Load default indicators
            LoadDefaultIndicators();
            
            // Initialize diagnostics
            UpdateDiagnostics();
        }

        private void LoadDefaultSymbols()
        {
            var defaultSymbols = new[] { "NQ 09-25", "ES 09-25", "YM 09-25", "CL 09-25" };
            foreach (var symbol in defaultSymbols)
            {
                SymbolsListBox.Items.Add(symbol);
            }
        }

        private void LoadDefaultIndicators()
        {
            var defaultIndicators = new[] { "SMA", "EMA", "RSI", "VWAP" };
            foreach (var indicator in defaultIndicators)
            {
                IndicatorsListBox.Items.Add(indicator);
            }
        }

        private void UpdateStatus(bool isRunning)
        {
            StatusText.Text = isRunning ? "Running" : "Stopped";
            StatusText.Foreground = isRunning ? Brushes.Green : Brushes.Red;
            StartStopButton.Content = isRunning ? "Stop Server" : "Start Server";
        }

        private void UpdateDiagnostics()
        {
            try
            {
                // Update uptime
                var uptime = DateTime.Now - _startTime;
                UptimeText.Text = $"{uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";

                // Update client count
                var clients = _commandListener?.GetConnectedClients() ?? new List<string>();
                ClientCountText.Text = clients.Count.ToString();
                TotalConnectionsText.Text = clients.Count.ToString();

                // Update memory usage (simplified)
                var memoryMB = GC.GetTotalMemory(false) / (1024 * 1024);
                MemoryText.Text = $"{memoryMB} MB";

                // Update latency (simulated)
                var latency = new Random().Next(1, 50);
                LatencyText.Text = $"{latency} ms";
                _latencyHistory.Add(latency);
                if (_latencyHistory.Count > 100)
                    _latencyHistory.RemoveAt(0);

                // Update messages per second (simulated)
                var messagesPerSec = new Random().Next(10, 1000);
                MessagesPerSecText.Text = messagesPerSec.ToString();

                // Update cache hit rate (simulated)
                var cacheHitRate = new Random().Next(70, 95);
                CacheHitRateText.Text = $"{cacheHitRate}%";

                // Update active subscriptions
                ActiveSubscriptionsText.Text = (clients.Count * 2).ToString();

                // Update latency chart
                UpdateLatencyChart();
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error updating diagnostics: {ex.Message}");
            }
        }

        private void UpdateLatencyChart()
        {
            try
            {
                LatencyChart.Children.Clear();

                if (_latencyHistory.Count < 2)
                    return;

                var canvas = LatencyChart;
                var width = canvas.ActualWidth;
                var height = canvas.ActualHeight;

                if (width <= 0 || height <= 0)
                    return;

                var maxLatency = _latencyHistory.Max();
                var minLatency = _latencyHistory.Min();
                var range = maxLatency - minLatency;
                if (range == 0) range = 1;

                var points = new PointCollection();
                for (int i = 0; i < _latencyHistory.Count; i++)
                {
                    var x = (i / (double)(_latencyHistory.Count - 1)) * width;
                    var y = height - ((_latencyHistory[i] - minLatency) / range) * height;
                    points.Add(new System.Windows.Point(x, y));
                }

                var polyline = new Polyline
                {
                    Points = points,
                    Stroke = Brushes.Blue,
                    StrokeThickness = 2
                };

                canvas.Children.Add(polyline);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error updating latency chart: {ex.Message}");
            }
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            UpdateDiagnostics();
        }

        private void StartStopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (StatusText.Text == "Stopped")
                {
                    // Start server
                    _commandListener?.Start();
                    UpdateStatus(true);
                    LogMessage("Server started");
                }
                else
                {
                    // Stop server
                    _commandListener?.Stop();
                    UpdateStatus(false);
                    LogMessage("Server stopped");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _logger?.LogError($"Error in StartStopButton_Click: {ex.Message}");
            }
        }

        private void RefreshClientsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var clients = _commandListener?.GetConnectedClients() ?? new List<string>();
                ClientsListBox.Items.Clear();
                foreach (var client in clients)
                {
                    ClientsListBox.Items.Add(client);
                }
                ClientCountText.Text = clients.Count.ToString();
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error refreshing clients: {ex.Message}");
            }
        }

        private void AddSymbolButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var symbol = SymbolTextBox.Text.Trim();
                if (string.IsNullOrEmpty(symbol))
                {
                    MessageBox.Show("Please enter a symbol", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!SymbolsListBox.Items.Contains(symbol))
                {
                    SymbolsListBox.Items.Add(symbol);
                    LogMessage($"Added symbol: {symbol}");
                }
                else
                {
                    MessageBox.Show("Symbol already exists", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error adding symbol: {ex.Message}");
            }
        }

        private void RemoveSymbolButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedItems = SymbolsListBox.SelectedItems.Cast<string>().ToList();
                foreach (var item in selectedItems)
                {
                    SymbolsListBox.Items.Remove(item);
                    LogMessage($"Removed symbol: {item}");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error removing symbol: {ex.Message}");
            }
        }

        private void AddIndicatorButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedItem = IndicatorComboBox.SelectedItem as ComboBoxItem;
                if (selectedItem == null)
                {
                    MessageBox.Show("Please select an indicator", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var indicator = selectedItem.Content.ToString();
                if (!IndicatorsListBox.Items.Contains(indicator))
                {
                    IndicatorsListBox.Items.Add(indicator);
                    LogMessage($"Added indicator: {indicator}");
                }
                else
                {
                    MessageBox.Show("Indicator already exists", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error adding indicator: {ex.Message}");
            }
        }

        private void RemoveIndicatorButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedItems = IndicatorsListBox.SelectedItems.Cast<string>().ToList();
                foreach (var item in selectedItems)
                {
                    IndicatorsListBox.Items.Remove(item);
                    LogMessage($"Removed indicator: {item}");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error removing indicator: {ex.Message}");
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Save configuration to file
                var config = new
                {
                    Port = int.Parse(PortTextBox.Text),
                    Protocol = (ProtocolComboBox.SelectedItem as ComboBoxItem)?.Content.ToString(),
                    AuthType = (AuthComboBox.SelectedItem as ComboBoxItem)?.Content.ToString(),
                    Symbols = SymbolsListBox.Items.Cast<string>().ToList(),
                    Indicators = IndicatorsListBox.Items.Cast<string>().ToList()
                };

                var configJson = Newtonsoft.Json.JsonConvert.SerializeObject(config, Newtonsoft.Json.Formatting.Indented);
                var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NT8Bridge", "config.json");
                
                Directory.CreateDirectory(Path.GetDirectoryName(configPath));
                File.WriteAllText(configPath, configJson);

                LogMessage("Configuration saved successfully");
                MessageBox.Show("Configuration saved successfully", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _logger?.LogError($"Error saving configuration: {ex.Message}");
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void LogMessage(string message)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                LogTextBox.AppendText($"[{timestamp}] {message}\n");
                LogTextBox.ScrollToEnd();
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error logging message: {ex.Message}");
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            try
            {
                _updateTimer?.Stop();
                base.OnClosing(e);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error closing window: {ex.Message}");
            }
        }
    }
} 