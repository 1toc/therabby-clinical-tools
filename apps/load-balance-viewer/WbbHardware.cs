using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Therabby.LoadBalanceViewer;

public sealed class CornerValues
{
    public double LF { get; set; }
    public double RF { get; set; }
    public double LB { get; set; }
    public double RB { get; set; }

    public CornerValues Clone() => new() { LF = LF, RF = RF, LB = LB, RB = RB };
}

public sealed class RawValues
{
    public uint LF { get; set; }
    public uint RF { get; set; }
    public uint LB { get; set; }
    public uint RB { get; set; }
}

public sealed class WbbFrame
{
    public long TimestampMs { get; set; }
    public RawValues Raw { get; set; } = new();
    public CornerValues Kg { get; set; } = new();
}

public sealed class Metrics
{
    public double TotalKg { get; set; }
    public double LeftPct { get; set; }
    public double RightPct { get; set; }
    public double FrontPct { get; set; }
    public double BackPct { get; set; }
    public double CopX { get; set; }
    public double CopY { get; set; }
    public bool WeightPresent { get; set; }
}

public static class MetricsCalculator
{
    public static Metrics Calculate(CornerValues values, double thresholdKg)
    {
        var lf = Math.Max(0, values.LF);
        var rf = Math.Max(0, values.RF);
        var lb = Math.Max(0, values.LB);
        var rb = Math.Max(0, values.RB);
        var total = lf + rf + lb + rb;

        var m = new Metrics
        {
            TotalKg = total,
            WeightPresent = total >= thresholdKg
        };

        if (!m.WeightPresent || total <= 0.0001) return m;

        var left = lf + lb;
        var right = rf + rb;
        var front = lf + rf;
        var back = lb + rb;

        m.LeftPct = left / total * 100.0;
        m.RightPct = right / total * 100.0;
        m.FrontPct = front / total * 100.0;
        m.BackPct = back / total * 100.0;
        m.CopX = (right - left) / total;
        m.CopY = (front - back) / total;
        return m;
    }
}

public interface IWbbDevice : IDisposable
{
    event Action<WbbFrame>? FrameReceived;
    bool Connected { get; }
    string Name { get; }
    void Connect();
    void Disconnect();
}

public sealed class MockWbbDevice : IWbbDevice
{
    public event Action<WbbFrame>? FrameReceived;

    private System.Threading.Timer? _timer;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly object _gate = new();
    private double _total = 70.0;
    private double _x;
    private double _y;

    public bool Connected { get; private set; }
    public string Name => "Mock Wii Balance Board";

    public void SetPose(double x, double y)
    {
        lock (_gate)
        {
            _x = Math.Clamp(x, -0.88, 0.88);
            _y = Math.Clamp(y, -0.88, 0.88);
        }
        Emit();
    }

    public void SetWeight(double total)
    {
        lock (_gate) _total = Math.Max(0, total);
        Emit();
    }

    public void Connect()
    {
        if (Connected) return;
        Connected = true;
        _timer = new System.Threading.Timer(_ => Emit(), null, 0, 16);
    }

    public void Disconnect()
    {
        Connected = false;
        _timer?.Dispose();
        _timer = null;
    }

    private void Emit()
    {
        if (!Connected) return;

        double t, x, y;
        lock (_gate) { t = _total; x = _x; y = _y; }

        var wave = Math.Sin(_stopwatch.Elapsed.TotalSeconds * 2.1) * 0.025;

        var kg = new CornerValues
        {
            LF = Math.Max(0, t * (1 - x) * (1 + y) / 4 + wave),
            RF = Math.Max(0, t * (1 + x) * (1 + y) / 4 - wave * .8),
            LB = Math.Max(0, t * (1 - x) * (1 - y) / 4 + wave * .5),
            RB = Math.Max(0, t * (1 + x) * (1 - y) / 4 - wave * .4),
        };

        var f = new WbbFrame
        {
            TimestampMs = _stopwatch.ElapsedMilliseconds,
            Kg = kg,
            Raw = new RawValues
            {
                LF = (uint)(1000 + kg.LF * 100),
                RF = (uint)(1000 + kg.RF * 100),
                LB = (uint)(1000 + kg.LB * 100),
                RB = (uint)(1000 + kg.RB * 100),
            }
        };

        FrameReceived?.Invoke(f);
    }

    public void Dispose() => Disconnect();
}

public sealed class Calibration
{
    public CornerValues Kg0 { get; } = new();
    public CornerValues Kg17 { get; } = new();
    public CornerValues Kg34 { get; } = new();
    public bool Valid { get; set; }
}

public sealed class RealWbbDevice : IWbbDevice
{
    public event Action<WbbFrame>? FrameReceived;

    private IntPtr _hid = NativeMethods.INVALID_HANDLE_VALUE;
    private CancellationTokenSource? _cancellation;
    private Calibration? _calibration;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public bool Connected { get; private set; }
    public string Name => "Nintendo Wii Balance Board";

    public void Connect()
    {
        if (Connected) return;

        _hid = OpenBalanceBoard();
        if (_hid == NativeMethods.INVALID_HANDLE_VALUE)
            throw new InvalidOperationException(
                "Wii Balance Board HIDが見つかりません。WindowsでBluetoothペアリングし、ボードのPOWERを押してください。");

        try
        {
            InitializeStream();
            _calibration = LoadCalibration();

            if (_calibration is null || !_calibration.Valid)
                throw new InvalidOperationException("Wii Balance Boardの工場校正値を読み込めませんでした。");

            Connected = true;
            _cancellation = new CancellationTokenSource();
            _ = Task.Run(() => ReadLoop(_cancellation.Token));
        }
        catch
        {
            Disconnect();
            throw;
        }
    }

    public void Disconnect()
    {
        Connected = false;

        if (_cancellation is not null)
        {
            try { _cancellation.Cancel(); } catch { }
            _cancellation.Dispose();
            _cancellation = null;
        }

        if (_hid != NativeMethods.INVALID_HANDLE_VALUE && _hid != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_hid);
            _hid = NativeMethods.INVALID_HANDLE_VALUE;
        }
    }

    private IntPtr OpenBalanceBoard()
    {
        NativeMethods.HidD_GetHidGuid(out var hidGuid);

        var info = NativeMethods.SetupDiGetClassDevs(
            ref hidGuid, IntPtr.Zero, IntPtr.Zero,
            NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);

        if (info == NativeMethods.INVALID_HANDLE_VALUE)
            return NativeMethods.INVALID_HANDLE_VALUE;

        try
        {
            uint index = 0;

            while (true)
            {
                var data = new NativeMethods.SP_DEVICE_INTERFACE_DATA
                {
                    cbSize = Marshal.SizeOf<NativeMethods.SP_DEVICE_INTERFACE_DATA>()
                };

                if (!NativeMethods.SetupDiEnumDeviceInterfaces(info, IntPtr.Zero, ref hidGuid, index, ref data))
                    break;

                index++;

                NativeMethods.SetupDiGetDeviceInterfaceDetail(
                    info, ref data, IntPtr.Zero, 0, out var required, IntPtr.Zero);

                if (required <= 0 || required > 16384) continue;

                var detail = Marshal.AllocHGlobal(required);

                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);

                    if (!NativeMethods.SetupDiGetDeviceInterfaceDetail(
                            info, ref data, detail, required, out required, IntPtr.Zero))
                        continue;

                    var pathPtr = IntPtr.Add(detail, 4);
                    var path = Marshal.PtrToStringUni(pathPtr);
                    if (string.IsNullOrEmpty(path)) continue;

                    var meta = NativeMethods.CreateFile(
                        path, 0,
                        NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                        IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);

                    if (meta == NativeMethods.INVALID_HANDLE_VALUE) continue;

                    try
                    {
                        var attr = new NativeMethods.HIDD_ATTRIBUTES
                        {
                            Size = Marshal.SizeOf<NativeMethods.HIDD_ATTRIBUTES>()
                        };

                        var ok = NativeMethods.HidD_GetAttributes(meta, ref attr);
                        var match = ok
                            && attr.VendorID == 0x057e
                            && (attr.ProductID == 0x0306 || attr.ProductID == 0x0330);

                        if (!match) continue;
                    }
                    finally
                    {
                        NativeMethods.CloseHandle(meta);
                    }

                    var h = NativeMethods.CreateFile(
                        path,
                        NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
                        NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                        IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);

                    if (h != NativeMethods.INVALID_HANDLE_VALUE) return h;
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(info);
        }

        return NativeMethods.INVALID_HANDLE_VALUE;
    }

    private bool SendReport(byte reportId, byte[] payload)
    {
        var report = new byte[22];
        report[0] = reportId;
        Array.Copy(payload, 0, report, 1, Math.Min(payload.Length, 21));
        return NativeMethods.HidD_SetOutputReport(_hid, report, report.Length);
    }

    private void WriteRegister(uint address, byte[] bytes)
    {
        var p = new byte[21];
        p[0] = 0x04;
        p[1] = (byte)((address >> 16) & 0xff);
        p[2] = (byte)((address >> 8) & 0xff);
        p[3] = (byte)(address & 0xff);
        p[4] = (byte)Math.Min(bytes.Length, 16);
        Array.Copy(bytes, 0, p, 5, Math.Min(bytes.Length, 16));

        if (!SendReport(0x16, p))
            throw new IOException("WBB register write failed.");
    }

    private void SetStreamMode()
    {
        if (!SendReport(0x12, new byte[] { 0x04, 0x32 }))
            throw new IOException("WBB stream mode setup failed.");
    }

    private void InitializeStream()
    {
        WriteRegister(0xA400F0, new byte[] { 0x55 });
        Thread.Sleep(40);
        WriteRegister(0xA400FB, new byte[] { 0x00 });
        Thread.Sleep(60);
        SendReport(0x11, new byte[] { 0x10 });
        Thread.Sleep(20);
        SetStreamMode();
    }

    private byte[] ReadRegister(uint address, int size)
    {
        var output = new byte[size];
        var seen = new byte[size];

        var p = new byte[]
        {
            0x04,
            (byte)((address >> 16) & 0xff),
            (byte)((address >> 8) & 0xff),
            (byte)(address & 0xff),
            (byte)((size >> 8) & 0xff),
            (byte)(size & 0xff)
        };

        if (!SendReport(0x17, p))
            throw new IOException("WBB register read request failed.");

        var received = 0;
        var baseAddress = (int)(address & 0xffff);

        for (var attempts = 0; received < size && attempts < 120; attempts++)
        {
            var report = new byte[22];

            if (!NativeMethods.ReadFile(_hid, report, report.Length, out var got, IntPtr.Zero))
                throw new IOException("WBB register read failed.");

            if (got < 7 || report[0] != 0x21) continue;

            var status = report[3];
            if ((status & 0x0f) != 0)
                throw new IOException("WBB register read returned an error.");

            var packet = (status >> 4) + 1;
            if (packet > 16) packet = 16;

            var offset = (report[4] << 8) | report[5];
            var dest = offset - baseAddress;
            if (dest < 0 || dest >= size) continue;

            for (var i = 0; i < packet && dest + i < size; i++)
            {
                output[dest + i] = report[6 + i];
                if (seen[dest + i] == 0)
                {
                    seen[dest + i] = 1;
                    received++;
                }
            }
        }

        if (received < size)
            throw new IOException("WBB calibration read timed out.");

        Thread.Sleep(10);
        SetStreamMode();
        Thread.Sleep(20);
        return output;
    }

    private Calibration LoadCalibration()
    {
        var b = ReadRegister(0xA40020, 32);
        var c = new Calibration();

        c.Kg0.RF = U16(b, 4);
        c.Kg0.RB = U16(b, 6);
        c.Kg0.LF = U16(b, 8);
        c.Kg0.LB = U16(b, 10);

        c.Kg17.RF = U16(b, 12);
        c.Kg17.RB = U16(b, 14);
        c.Kg17.LF = U16(b, 16);
        c.Kg17.LB = U16(b, 18);

        c.Kg34.RF = U16(b, 20);
        c.Kg34.RB = U16(b, 22);
        c.Kg34.LF = U16(b, 24);
        c.Kg34.LB = U16(b, 26);

        c.Valid =
            c.Kg0.LF < c.Kg17.LF && c.Kg17.LF < c.Kg34.LF &&
            c.Kg0.RF < c.Kg17.RF && c.Kg17.RF < c.Kg34.RF &&
            c.Kg0.LB < c.Kg17.LB && c.Kg17.LB < c.Kg34.LB &&
            c.Kg0.RB < c.Kg17.RB && c.Kg17.RB < c.Kg34.RB;

        return c;
    }

    private static ushort U16(byte[] b, int i)
        => (ushort)((b[i] << 8) | b[i + 1]);

    private static double RawToKg(uint raw, double k0, double k17, double k34)
    {
        if (k17 <= k0 + 1 || k34 <= k17 + 1 || raw <= k0)
            return 0;

        if (raw < k17)
            return 17.0 * (raw - k0) / (k17 - k0);

        return 17.0 + 17.0 * (raw - k17) / (k34 - k17);
    }

    private void ReadLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && Connected)
            {
                var report = new byte[22];

                if (!NativeMethods.ReadFile(_hid, report, report.Length, out var got, IntPtr.Zero))
                    throw new IOException("Wii Balance Boardとの読み取りが停止しました。");

                if (got < 11) continue;

                if (report[0] == 0x20)
                {
                    try { SetStreamMode(); } catch { }
                    continue;
                }

                if (report[0] != 0x32) continue;

                var f = new WbbFrame
                {
                    TimestampMs = _stopwatch.ElapsedMilliseconds
                };

                f.Raw.RF = U16(report, 3);
                f.Raw.RB = U16(report, 5);
                f.Raw.LF = U16(report, 7);
                f.Raw.LB = U16(report, 9);

                var cal = _calibration!;
                f.Kg.LF = RawToKg(f.Raw.LF, cal.Kg0.LF, cal.Kg17.LF, cal.Kg34.LF);
                f.Kg.RF = RawToKg(f.Raw.RF, cal.Kg0.RF, cal.Kg17.RF, cal.Kg34.RF);
                f.Kg.LB = RawToKg(f.Raw.LB, cal.Kg0.LB, cal.Kg17.LB, cal.Kg34.LB);
                f.Kg.RB = RawToKg(f.Raw.RB, cal.Kg0.RB, cal.Kg17.RB, cal.Kg34.RB);

                FrameReceived?.Invoke(f);
            }
        }
        catch
        {
            Connected = false;
        }
    }

    public void Dispose() => Disconnect();
}

internal static class NativeMethods
{
    public static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    public const int DIGCF_PRESENT = 0x02;
    public const int DIGCF_DEVICEINTERFACE = 0x10;
    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 0x1;
    public const uint FILE_SHARE_WRITE = 0x2;
    public const uint OPEN_EXISTING = 3;

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [DllImport("hid.dll")]
    public static extern void HidD_GetHidGuid(out Guid HidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool HidD_GetAttributes(IntPtr HidDeviceObject, ref HIDD_ATTRIBUTES Attributes);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool HidD_SetOutputReport(IntPtr HidDeviceObject, byte[] ReportBuffer, int ReportBufferLength);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevs(
        ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, int Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr DeviceInfoSet, IntPtr DeviceInfoData,
        ref Guid InterfaceClassGuid, uint MemberIndex,
        ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr DeviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
        IntPtr DeviceInterfaceDetailData,
        int DeviceInterfaceDetailDataSize,
        out int RequiredSize,
        IntPtr DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReadFile(
        IntPtr hFile, byte[] lpBuffer, int nNumberOfBytesToRead,
        out int lpNumberOfBytesRead, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);
}
