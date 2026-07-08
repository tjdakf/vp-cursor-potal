using System.Runtime.InteropServices;
using System.Text;
using H2CursorRouter.Core.Geometry;

namespace H2CursorRouter.Windows;

public sealed class Win32MonitorTopologyService : IMonitorTopologyService
{
    private System.Threading.Timer? _timer;
    private string? _lastSignature;

    public event EventHandler? TopologyChanged;

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var displayConfigMonitors = GetMonitorsFromDisplayConfig();
        var monitorHandleMonitors = GetMonitorsFromMonitorHandles();
        return displayConfigMonitors.Count >= monitorHandleMonitors.Count
            ? displayConfigMonitors
            : monitorHandleMonitors;
    }

    private static IReadOnlyList<MonitorInfo> GetMonitorsFromDisplayConfig()
    {
        if (GetDisplayConfigBufferSizes(QueryDisplayConfigFlags.OnlyActivePaths, out var pathCount, out var modeCount) != ErrorSuccess ||
            pathCount == 0 ||
            modeCount == 0)
        {
            return Array.Empty<MonitorInfo>();
        }

        var paths = new DisplayConfigPathInfo[pathCount];
        var modes = new DisplayConfigModeInfo[modeCount];
        var result = QueryDisplayConfig(
            QueryDisplayConfigFlags.OnlyActivePaths,
            ref pathCount,
            paths,
            ref modeCount,
            modes,
            IntPtr.Zero);
        if (result != ErrorSuccess)
        {
            return Array.Empty<MonitorInfo>();
        }

        var monitors = new List<MonitorInfo>();
        var seenSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.Take((int)pathCount))
        {
            if (!TryGetSourceMode(path, modes, modeCount, out var sourceMode) ||
                sourceMode.SourceMode.Width == 0 ||
                sourceMode.SourceMode.Height == 0)
            {
                continue;
            }

            var deviceName = GetSourceDeviceName(path.SourceInfo.AdapterId, path.SourceInfo.Id);
            var left = sourceMode.SourceMode.Position.X;
            var top = sourceMode.SourceMode.Position.Y;
            var sourceKey = $"{deviceName}:{left},{top},{sourceMode.SourceMode.Width},{sourceMode.SourceMode.Height}";
            if (!seenSources.Add(sourceKey))
            {
                continue;
            }

            monitors.Add(new MonitorInfo(
                deviceName,
                new IntRect(
                    left,
                    top,
                    left + (int)sourceMode.SourceMode.Width,
                    top + (int)sourceMode.SourceMode.Height),
                left == 0 && top == 0,
                CreateIdentityInfo(path)));
        }

        return monitors;
    }

    private static bool TryGetSourceMode(
        DisplayConfigPathInfo path,
        IReadOnlyList<DisplayConfigModeInfo> modes,
        uint modeCount,
        out DisplayConfigModeInfo sourceMode)
    {
        if (path.SourceInfo.ModeInfoIdx != InvalidModeInfoIdx &&
            path.SourceInfo.ModeInfoIdx < modeCount)
        {
            var indexedMode = modes[(int)path.SourceInfo.ModeInfoIdx];
            if (indexedMode.InfoType == DisplayConfigModeInfoType.Source)
            {
                sourceMode = indexedMode;
                return true;
            }
        }

        sourceMode = modes.Take((int)modeCount).FirstOrDefault(mode =>
            mode.InfoType == DisplayConfigModeInfoType.Source &&
            mode.Id == path.SourceInfo.Id &&
            mode.AdapterId.Equals(path.SourceInfo.AdapterId));
        return sourceMode.SourceMode.Width > 0 && sourceMode.SourceMode.Height > 0;
    }

    private static string GetSourceDeviceName(Luid adapterId, uint sourceId)
    {
        var sourceName = new DisplayConfigSourceDeviceName
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = DisplayConfigDeviceInfoType.GetSourceName,
                Size = (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
                AdapterId = adapterId,
                Id = sourceId
            }
        };

        return DisplayConfigGetDeviceInfo(ref sourceName) == ErrorSuccess &&
               !string.IsNullOrWhiteSpace(sourceName.ViewGdiDeviceName)
            ? sourceName.ViewGdiDeviceName.TrimEnd('\0')
            : $"DISPLAY{sourceId + 1}";
    }

    private static MonitorIdentityInfo CreateIdentityInfo(DisplayConfigPathInfo path)
    {
        var targetName = GetTargetDeviceName(path.TargetInfo.AdapterId, path.TargetInfo.Id);
        return new MonitorIdentityInfo(
            FormatLuid(path.SourceInfo.AdapterId),
            path.SourceInfo.Id,
            FormatLuid(path.TargetInfo.AdapterId),
            path.TargetInfo.Id,
            path.TargetInfo.OutputTechnology,
            targetName?.EdidManufactureId ?? 0,
            targetName?.EdidProductCodeId ?? 0,
            targetName?.ConnectorInstance ?? 0,
            CleanNullTerminated(targetName?.MonitorFriendlyDeviceName),
            CleanNullTerminated(targetName?.MonitorDevicePath));
    }

    private static DisplayConfigTargetDeviceName? GetTargetDeviceName(Luid adapterId, uint targetId)
    {
        var targetName = new DisplayConfigTargetDeviceName
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = DisplayConfigDeviceInfoType.GetTargetName,
                Size = (uint)Marshal.SizeOf<DisplayConfigTargetDeviceName>(),
                AdapterId = adapterId,
                Id = targetId
            }
        };

        return DisplayConfigGetDeviceInfo(ref targetName) == ErrorSuccess
            ? targetName
            : null;
    }

    private static string FormatLuid(Luid luid) => $"{luid.HighPart:x8}:{luid.LowPart:x8}";

    private static string CleanNullTerminated(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : value.TrimEnd('\0').Trim();

    private static IReadOnlyList<MonitorInfo> GetMonitorsFromMonitorHandles()
    {
        var monitors = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MonitorInfoEx();
            info.Size = Marshal.SizeOf<MonitorInfoEx>();
            if (GetMonitorInfo(monitor, ref info))
            {
                monitors.Add(new MonitorInfo(
                    (info.DeviceName ?? string.Empty).TrimEnd('\0'),
                    new IntRect(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom),
                    (info.Flags & 1) == 1));
            }

            return true;
        }, IntPtr.Zero);
        return monitors;
    }

    public string GetTopologySignature()
    {
        var builder = new StringBuilder();
        foreach (var monitor in GetMonitors().OrderBy(monitor => monitor.DeviceName, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(monitor.DeviceName)
                .Append(':')
                .Append(monitor.Bounds.Left)
                .Append(',')
                .Append(monitor.Bounds.Top)
                .Append(',')
                .Append(monitor.Bounds.Right)
                .Append(',')
                .Append(monitor.Bounds.Bottom)
                .Append('|');
        }

        return builder.ToString();
    }

    public void StartWatching(TimeSpan interval)
    {
        _lastSignature = GetTopologySignature();
        _timer?.Dispose();
        _timer = new System.Threading.Timer(_ =>
        {
            var current = GetTopologySignature();
            if (!string.Equals(current, _lastSignature, StringComparison.Ordinal))
            {
                _lastSignature = current;
                TopologyChanged?.Invoke(this, EventArgs.Empty);
            }
        }, null, interval, interval);
    }

    public void Dispose() => _timer?.Dispose();

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    private const int ErrorSuccess = 0;
    private const uint InvalidModeInfoIdx = 0xffffffff;

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(QueryDisplayConfigFlags flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        QueryDisplayConfigFlags flags,
        ref uint pathCount,
        [Out] DisplayConfigPathInfo[] paths,
        ref uint modeCount,
        [Out] DisplayConfigModeInfo[] modes,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName requestPacket);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName requestPacket);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clipRect, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfoEx
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    private enum QueryDisplayConfigFlags : uint
    {
        OnlyActivePaths = 0x00000002
    }

    private enum DisplayConfigModeInfoType : uint
    {
        Source = 1,
        Target = 2,
        DesktopImage = 3
    }

    private enum DisplayConfigDeviceInfoType : uint
    {
        GetSourceName = 1,
        GetTargetName = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid : IEquatable<Luid>
    {
        public uint LowPart;
        public int HighPart;

        public bool Equals(Luid other) => LowPart == other.LowPart && HighPart == other.HighPart;
        public override bool Equals(object? obj) => obj is Luid other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(LowPart, HighPart);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo SourceInfo;
        public DisplayConfigPathTargetInfo TargetInfo;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public int OutputTechnology;
        public int Rotation;
        public int Scaling;
        public DisplayConfigRational RefreshRate;
        public int ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigRational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigModeInfo
    {
        public DisplayConfigModeInfoType InfoType;
        public uint Id;
        public Luid AdapterId;
        public DisplayConfigModeInfoUnion ModeInfo;

        public DisplayConfigSourceMode SourceMode => ModeInfo.SourceMode;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct DisplayConfigModeInfoUnion
    {
        [FieldOffset(0)]
        public DisplayConfigTargetMode TargetMode;

        [FieldOffset(0)]
        public DisplayConfigSourceMode SourceMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigTargetMode
    {
        public DisplayConfigVideoSignalInfo TargetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigVideoSignalInfo
    {
        public ulong PixelRate;
        public DisplayConfigRational HSyncFreq;
        public DisplayConfigRational VSyncFreq;
        public DisplayConfig2DRegion ActiveSize;
        public DisplayConfig2DRegion TotalSize;
        public uint VideoStandard;
        public int ScanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfig2DRegion
    {
        public uint Cx;
        public uint Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigSourceMode
    {
        public uint Width;
        public uint Height;
        public int PixelFormat;
        public PointL Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointL
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDeviceInfoHeader
    {
        public DisplayConfigDeviceInfoType Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigSourceDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigTargetDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Flags;
        public int OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string MonitorFriendlyDeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string MonitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
