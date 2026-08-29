using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Therabby.LoadBalanceViewer;

public sealed class UiSnapshot
{
    public string Type { get; set; } = "state";
    public string Mode { get; set; } = "real";
    public bool Connected { get; set; }
    public string ConnectionText { get; set; } = "Not connected";
    public string ConnectionState { get; set; } = "idle";

    public double Lf { get; set; }
    public double Rf { get; set; }
    public double Lb { get; set; }
    public double Rb { get; set; }
    public double Total { get; set; }

    public double? LeftPct { get; set; }
    public double? RightPct { get; set; }
    public double? FrontPct { get; set; }
    public double? BackPct { get; set; }
    public double? CopX { get; set; }
    public double? CopY { get; set; }
    public double? RelativeX { get; set; }
    public double? RelativeY { get; set; }

    public bool WeightPresent { get; set; }
    public bool ZeroApplied { get; set; }
    public bool CenterApplied { get; set; }
    public double? CenterX { get; set; }
    public double? CenterY { get; set; }

    public bool Logging { get; set; }
    public string LogState { get; set; } = "Stopped";
    public string ZeroState { get; set; } = "Not set";
    public string CenterState { get; set; } = "Not set";

    public int SampleCount { get; set; }
    public double DisplaySmoothing { get; set; } = .26;

    public long AlertSerial { get; set; }
    public string? AlertText { get; set; }
    public string AlertType { get; set; } = "warn";
}

public sealed class LoadBalanceController : IDisposable
{
    private readonly object _gate = new();
    private IWbbDevice? _device;
    private string _mode = "real";

    private WbbFrame? _lastFrame;
    private Metrics _lastRawMetrics = new();

    private readonly CornerValues _zero = new();
    private (double X, double Y)? _center;
    private bool _zeroApplied;

    private CornerValues? _displayKg;
    private readonly double _displayAlpha = .26;

    private readonly List<(long Tick, CornerValues Kg)> _recent = new();
    private readonly SessionLogger _logger = new();

    private string _connectionText = "Not connected";
    private string _connectionState = "idle";
    private string _zeroState = "Not set";
    private string _centerState = "Not set";
    private string _logState = "Stopped";

    private long _alertSerial;
    private string? _alertText;
    private string _alertType = "warn";

    public double WeightThresholdKg { get; set; } = 5.0;

    public string Mode
    {
        get { lock (_gate) return _mode; }
    }

    public async Task SetModeAsync(string mode)
    {
        mode = mode == "mock" ? "mock" : "real";

        await Task.Run(() =>
        {
            lock (_gate)
            {
                if (_mode == mode && _device is not null) return;
            }

            DetachDevice();

            lock (_gate)
            {
                _mode = mode;
                ResetProcessingLocked();
            }

            if (mode == "mock")
            {
                var d = new MockWbbDevice();
                AttachDevice(d);
                d.Connect();

                lock (_gate)
                {
                    _connectionText = "Mock Device";
                    _connectionState = "mock";
                }
            }
            else
            {
                lock (_gate)
                {
                    _connectionText = "Not connected";
                    _connectionState = "idle";
                }
            }
        });
    }

    public async Task ConnectRealAsync(bool silent = false)
    {
        if (Mode != "real") await SetModeAsync("real");

        lock (_gate)
        {
            _connectionText = "Connecting…";
            _connectionState = "idle";
        }

        try
        {
            await Task.Run(() =>
            {
                DetachDevice();
                var d = new RealWbbDevice();
                d.Connect();
                AttachDevice(d);

                lock (_gate)
                {
                    _connectionText = d.Name;
                    _connectionState = "connected";
                    ResetProcessingLocked();
                }
            });

            Alert("実機接続を開始しました。必要に応じて、ボードから降りた状態でZEROを実行してください。", "ok");
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _connectionText = "Connection failed";
                _connectionState = "error";
            }

            if (!silent) Alert(ex.Message, "warn");
        }
    }

    private void AttachDevice(IWbbDevice device)
    {
        _device = device;
        device.FrameReceived += OnFrame;
    }

    private void DetachDevice()
    {
        IWbbDevice? old;

        lock (_gate)
        {
            old = _device;
            _device = null;
        }

        if (old is null) return;

        try { old.FrameReceived -= OnFrame; } catch { }
        try { old.Disconnect(); } catch { }
        try { old.Dispose(); } catch { }
    }

    private void ResetProcessingLocked()
    {
        _zero.LF = _zero.RF = _zero.LB = _zero.RB = 0;
        _center = null;
        _zeroApplied = false;
        _displayKg = null;
        _lastFrame = null;
        _lastRawMetrics = new Metrics();
        _recent.Clear();
        _zeroState = "Not set";
        _centerState = "Not set";
    }

    private void OnFrame(WbbFrame frame)
    {
        lock (_gate)
        {
            _lastFrame = frame;

            var now = Environment.TickCount64;
            _recent.Add((now, frame.Kg.Clone()));
            while (_recent.Count > 0 && now - _recent[0].Tick > 3000)
                _recent.RemoveAt(0);

            var adjusted = Adjust(frame.Kg);
            var rawMetrics = MetricsCalculator.Calculate(adjusted, WeightThresholdKg);
            _lastRawMetrics = rawMetrics;

            if (_displayKg is null)
            {
                _displayKg = adjusted.Clone();
            }
            else
            {
                Ema(_displayKg, adjusted, _displayAlpha);
            }

            if (_logger.Active)
            {
                var relX = rawMetrics.WeightPresent && _center.HasValue
                    ? rawMetrics.CopX - _center.Value.X : 0;
                var relY = rawMetrics.WeightPresent && _center.HasValue
                    ? rawMetrics.CopY - _center.Value.Y : 0;

                _logger.Write(frame, adjusted, rawMetrics, relX, relY);
                _logState = $"{_logger.SampleCount} samples";
            }
        }
    }

    private CornerValues Adjust(CornerValues c) => new()
    {
        LF = Math.Max(0, c.LF - _zero.LF),
        RF = Math.Max(0, c.RF - _zero.RF),
        LB = Math.Max(0, c.LB - _zero.LB),
        RB = Math.Max(0, c.RB - _zero.RB),
    };

    private static void Ema(CornerValues target, CornerValues value, double alpha)
    {
        target.LF += (value.LF - target.LF) * alpha;
        target.RF += (value.RF - target.RF) * alpha;
        target.LB += (value.LB - target.LB) * alpha;
        target.RB += (value.RB - target.RB) * alpha;
    }

    public async Task ZeroAsync()
    {
        long started;

        lock (_gate)
        {
            if (_lastFrame is null)
            {
                AlertLocked("センサーデータがまだありません。", "warn");
                return;
            }

            var total = _lastFrame.Kg.LF + _lastFrame.Kg.RF + _lastFrame.Kg.LB + _lastFrame.Kg.RB;

            if (total >= WeightThresholdKg)
            {
                AlertLocked($"ZEROはボードから降りた状態で実行してください。現在の総荷重: {total:0.0} kg", "warn");
                return;
            }

            _zeroState = "Sampling…";
            started = Environment.TickCount64;
            AlertLocked("ZEROを取得しています。約1秒、ボードには触れないでください。", "ok");
        }

        await Task.Delay(1000);

        lock (_gate)
        {
            var samples = _recent.Where(s => s.Tick >= started).Select(s => s.Kg).ToArray();

            if (samples.Length < 3)
            {
                _zeroState = "Failed";
                AlertLocked("ZERO用サンプルが不足しました。", "warn");
                return;
            }

            _zero.LF = samples.Average(s => s.LF);
            _zero.RF = samples.Average(s => s.RF);
            _zero.LB = samples.Average(s => s.LB);
            _zero.RB = samples.Average(s => s.RB);

            _zeroApplied = true;
            _zeroState = "Applied";
            _displayKg = null;
            AlertLocked("ZEROを設定しました。", "ok");
        }
    }

    public void SetCenter()
    {
        lock (_gate)
        {
            if (!_lastRawMetrics.WeightPresent)
            {
                AlertLocked("SET CENTERはボード上に立った状態で実行してください。", "warn");
                return;
            }

            _center = (_lastRawMetrics.CopX, _lastRawMetrics.CopY);
            _centerState = "Applied";
            AlertLocked("現在のCoP位置を基準CENTERとして登録しました。", "ok");
        }
    }

    public void ToggleLog()
    {
        lock (_gate)
        {
            if (!_logger.Active)
            {
                _logger.Start(WeightThresholdKg, _zeroApplied, _center.HasValue);
                _logState = _logger.SessionId;
                AlertLocked("センサーログの記録を開始しました。", "ok");
            }
            else
            {
                _logger.Stop(WeightThresholdKg, _zeroApplied, _center.HasValue);
                _logState = $"{_logger.SampleCount} samples";
                AlertLocked($"ログを保存しました: {_logger.LastSessionDirectory}", "ok");
            }
        }
    }

    public void SetMockWeight(double value)
    {
        lock (_gate)
        {
            if (_device is MockWbbDevice mock) mock.SetWeight(value);
        }
    }

    public void SetMockPose(double x, double y)
    {
        lock (_gate)
        {
            if (_device is MockWbbDevice mock) mock.SetPose(x, y);
        }
    }

    public UiSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var kg = _displayKg?.Clone() ?? new CornerValues();
            var m = MetricsCalculator.Calculate(kg, WeightThresholdKg);

            double? relX = null;
            double? relY = null;

            if (m.WeightPresent && _center.HasValue)
            {
                relX = m.CopX - _center.Value.X;
                relY = m.CopY - _center.Value.Y;
            }

            return new UiSnapshot
            {
                Mode = _mode,
                Connected = _device?.Connected == true,
                ConnectionText = _connectionText,
                ConnectionState = _connectionState,
                Lf = kg.LF,
                Rf = kg.RF,
                Lb = kg.LB,
                Rb = kg.RB,
                Total = m.TotalKg,
                LeftPct = m.WeightPresent ? m.LeftPct : null,
                RightPct = m.WeightPresent ? m.RightPct : null,
                FrontPct = m.WeightPresent ? m.FrontPct : null,
                BackPct = m.WeightPresent ? m.BackPct : null,
                CopX = m.WeightPresent ? m.CopX : null,
                CopY = m.WeightPresent ? m.CopY : null,
                RelativeX = relX,
                RelativeY = relY,
                WeightPresent = m.WeightPresent,
                ZeroApplied = _zeroApplied,
                CenterApplied = _center.HasValue,
                CenterX = _center?.X,
                CenterY = _center?.Y,
                Logging = _logger.Active,
                LogState = _logState,
                ZeroState = _zeroState,
                CenterState = _centerState,
                SampleCount = (int)Math.Min(int.MaxValue, _logger.SampleCount),
                AlertSerial = _alertSerial,
                AlertText = _alertText,
                AlertType = _alertType,
            };
        }
    }

    public string LogsDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

    private void Alert(string text, string type)
    {
        lock (_gate) AlertLocked(text, type);
    }

    private void AlertLocked(string text, string type)
    {
        _alertSerial++;
        _alertText = text;
        _alertType = type;
    }

    public void Dispose()
    {
        try
        {
            lock (_gate)
            {
                if (_logger.Active)
                    _logger.Stop(WeightThresholdKg, _zeroApplied, _center.HasValue);
            }
        }
        catch { }

        DetachDevice();
        _logger.Dispose();
    }
}

public sealed class SessionLogger : IDisposable
{
    private readonly object _gate = new();
    private StreamWriter? _writer;
    private string? _sessionDir;
    private string? _sessionId;
    private long _count;

    public bool Active { get { lock (_gate) return _writer is not null; } }
    public string SessionId { get { lock (_gate) return _sessionId ?? ""; } }
    public long SampleCount { get { lock (_gate) return _count; } }
    public string LastSessionDirectory { get { lock (_gate) return _sessionDir ?? ""; } }

    public void Start(double threshold, bool zeroApplied, bool centerApplied)
    {
        lock (_gate)
        {
            if (_writer is not null) return;

            _sessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var logs = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            _sessionDir = Path.Combine(logs, _sessionId);
            Directory.CreateDirectory(_sessionDir);

            _writer = new StreamWriter(Path.Combine(_sessionDir, "sensor_log.csv"), false, new UTF8Encoding(true));
            _writer.WriteLine("timestamp_ms,session_id,raw_lf,raw_rf,raw_lb,raw_rb,lf_kg,rf_kg,lb_kg,rb_kg,total_kg,left_pct,right_pct,front_pct,back_pct,cop_x_norm,cop_y_norm,relative_cop_x,relative_cop_y,weight_present");
            _writer.AutoFlush = true;
            _count = 0;
            WriteSession(threshold, zeroApplied, centerApplied);
        }
    }

    public void Write(WbbFrame frame, CornerValues adjusted, Metrics metrics, double relativeX, double relativeY)
    {
        lock (_gate)
        {
            if (_writer is null) return;
            string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
            var line = string.Join(",", new[]
            {
                frame.TimestampMs.ToString(CultureInfo.InvariantCulture), _sessionId ?? "",
                frame.Raw.LF.ToString(CultureInfo.InvariantCulture), frame.Raw.RF.ToString(CultureInfo.InvariantCulture),
                frame.Raw.LB.ToString(CultureInfo.InvariantCulture), frame.Raw.RB.ToString(CultureInfo.InvariantCulture),
                F(adjusted.LF), F(adjusted.RF), F(adjusted.LB), F(adjusted.RB), F(metrics.TotalKg),
                metrics.WeightPresent ? F(metrics.LeftPct) : "", metrics.WeightPresent ? F(metrics.RightPct) : "",
                metrics.WeightPresent ? F(metrics.FrontPct) : "", metrics.WeightPresent ? F(metrics.BackPct) : "",
                metrics.WeightPresent ? F(metrics.CopX) : "", metrics.WeightPresent ? F(metrics.CopY) : "",
                metrics.WeightPresent ? F(relativeX) : "", metrics.WeightPresent ? F(relativeY) : "",
                metrics.WeightPresent ? "true" : "false"
            });
            _writer.WriteLine(line);
            _count++;
        }
    }

    public void Stop(double threshold, bool zeroApplied, bool centerApplied)
    {
        lock (_gate)
        {
            if (_writer is null) return;
            _writer.Flush();
            _writer.Close();
            _writer = null;
            WriteSession(threshold, zeroApplied, centerApplied);
        }
    }

    private void WriteSession(double threshold, bool zeroApplied, bool centerApplied)
    {
        if (_sessionDir is null || _sessionId is null) return;
        var payload = new
        {
            session_id = _sessionId,
            application = "Therabby Load Balance Viewer",
            version = "0.3",
            zero_applied = zeroApplied,
            center_applied = centerApplied,
            weight_threshold_kg = threshold,
            sample_count = _count,
            display_smoothing = "EMA alpha 0.26; CSV stores unsmoothed adjusted sensor data",
            created_at = DateTimeOffset.Now
        };
        File.WriteAllText(Path.Combine(_sessionDir, "session.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(true));
    }

    public void Dispose()
    {
        try { Stop(5, false, false); } catch { }
    }
}
