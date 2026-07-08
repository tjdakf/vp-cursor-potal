using H2CursorRouter.Core.Domain;
using H2CursorRouter.Core.Geometry;
using H2CursorRouter.Core.Profiles;
using H2CursorRouter.Core.Configuration;
using H2CursorRouter.App.Services;
using H2CursorRouter.Windows;

namespace H2CursorRouter.App.ViewModels;

public sealed class DeviceRow : ViewModelBase
{
    private bool _isOnline;

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = H2DeviceConfig.DefaultPort;
    public int DeviceId { get; set; }
    public int PresetEnumScreenId { get; set; }
    public int TimeoutMs { get; set; } = 1000;
    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            if (SetProperty(ref _isOnline, value))
            {
                OnPropertyChanged(nameof(IsH2Online));
            }
        }
    }

    public bool IsH2Online => IsOnline;

    public static DeviceRow FromModel(H2DeviceConfig device) => new()
    {
        Id = device.Id,
        Name = device.Name,
        Host = device.Host,
        Port = device.Port,
        DeviceId = device.DeviceId,
        TimeoutMs = (int)device.Timeout.TotalMilliseconds
    };

    public H2DeviceConfig ToModel() => new(
        Id,
        Name,
        Host,
        Port,
        DeviceId,
        TimeSpan.FromMilliseconds(TimeoutMs));
}

public sealed class PresetRow
{
    public string DeviceConfigId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public int H2DeviceId { get; set; }
    public int ScreenId { get; set; }
    public int FriendlyPresetNumber { get; set; }
    public int PresetId { get; set; }
    public string DisplayName { get; set; } = "";
    public DateTimeOffset? LastFetchedAtUtc { get; set; }

    public static PresetRow FromCachedPreset(CachedH2Preset preset) => new()
    {
        DeviceConfigId = preset.DeviceConfigId,
        DeviceName = preset.DeviceName,
        H2DeviceId = preset.H2DeviceId,
        ScreenId = preset.ScreenId,
        FriendlyPresetNumber = preset.FriendlyPresetNumber,
        PresetId = preset.PresetId,
        DisplayName = preset.DisplayName,
        LastFetchedAtUtc = preset.LastFetchedAtUtc
    };

    public CachedH2Preset ToCachedPreset() => new(
        DeviceConfigId,
        DeviceName,
        H2DeviceId,
        ScreenId,
        FriendlyPresetNumber,
        PresetId,
        DisplayName,
        LastFetchedAtUtc);
}

public sealed class LayoutRow : ViewModelBase
{
    private string _displays = "";

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int? DefaultStartX { get; set; }
    public int? DefaultStartY { get; set; }
    public string Displays
    {
        get => _displays;
        set => SetProperty(ref _displays, value);
    }

    public static LayoutRow FromModel(CursorLayout layout) => new()
    {
        Id = layout.Id,
        Name = layout.Name,
        Description = layout.Description ?? "",
        DefaultStartX = layout.DefaultStartPosition?.X,
        DefaultStartY = layout.DefaultStartPosition?.Y,
        Displays = FormatDisplays(layout.Zones)
    };

    public static string FormatDisplays(IEnumerable<CursorZone> zones)
    {
        var labels = zones
            .Where(zone => zone.IsVisible)
            .Select(zone => DisplayLabelFormatter.Format(zone.DisplayName, zone.Id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return labels.Length == 0 ? "No displays" : string.Join(", ", labels);
    }
}

public sealed class ZoneRow : ViewModelBase, IVisualLayoutZone
{
    private string _id = "";
    private string _displayName = "";
    private string _displayAlias = "";
    private double _visualLeft;
    private double _visualTop;
    private double _visualRight;
    private double _visualBottom;
    private bool _isSelected;

    public string LayoutId { get; set; } = "";
    public string Id
    {
        get => _id;
        set
        {
            if (SetProperty(ref _id, value))
            {
                OnPropertyChanged(nameof(DisplayLabel));
                OnPropertyChanged(nameof(DisplayIdHint));
            }
        }
    }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (SetProperty(ref _displayName, value))
            {
                OnPropertyChanged(nameof(DisplayLabel));
                OnPropertyChanged(nameof(DisplayIdHint));
            }
        }
    }

    public string DisplayAlias
    {
        get => _displayAlias;
        set
        {
            if (SetProperty(ref _displayAlias, value))
            {
                OnPropertyChanged(nameof(DisplayLabel));
                OnPropertyChanged(nameof(DisplayIdHint));
            }
        }
    }

    public string DisplayLabel => string.IsNullOrWhiteSpace(DisplayAlias)
        ? DisplayLabelFormatter.Format(DisplayName, Id)
        : DisplayAlias.Trim();
    public string DisplayIdHint => string.IsNullOrWhiteSpace(DisplayAlias) && DisplayLabelFormatter.ShouldShowId(DisplayName, Id) ? Id : "";
    public int WindowsLeft { get; set; }
    public int WindowsTop { get; set; }
    public int WindowsRight { get; set; }
    public int WindowsBottom { get; set; }
    public double VisualLeft
    {
        get => _visualLeft;
        set
        {
            var width = VisualWidth;
            if (SetProperty(ref _visualLeft, value))
            {
                _visualRight = _visualLeft + width;
                OnPropertyChanged(nameof(VisualRight));
                OnPropertyChanged(nameof(VisualWidth));
            }
        }
    }

    public double VisualTop
    {
        get => _visualTop;
        set
        {
            var height = VisualHeight;
            if (SetProperty(ref _visualTop, value))
            {
                _visualBottom = _visualTop + height;
                OnPropertyChanged(nameof(VisualBottom));
                OnPropertyChanged(nameof(VisualHeight));
            }
        }
    }

    public double VisualRight
    {
        get => _visualRight;
        set
        {
            if (SetProperty(ref _visualRight, value))
            {
                OnPropertyChanged(nameof(VisualWidth));
            }
        }
    }

    public double VisualBottom
    {
        get => _visualBottom;
        set
        {
            if (SetProperty(ref _visualBottom, value))
            {
                OnPropertyChanged(nameof(VisualHeight));
            }
        }
    }

    public double VisualWidth
    {
        get => Math.Max(1, VisualRight - VisualLeft);
        set
        {
            VisualRight = VisualLeft + Math.Max(1, value);
            OnPropertyChanged();
        }
    }

    public double VisualHeight
    {
        get => Math.Max(1, VisualBottom - VisualTop);
        set
        {
            VisualBottom = VisualTop + Math.Max(1, value);
            OnPropertyChanged();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsVisible { get; set; } = true;

    public static ZoneRow FromModel(string layoutId, CursorZone zone) => new()
    {
        LayoutId = layoutId,
        Id = zone.Id,
        DisplayName = zone.DisplayName,
        WindowsLeft = zone.WindowsRect.Left,
        WindowsTop = zone.WindowsRect.Top,
        WindowsRight = zone.WindowsRect.Right,
        WindowsBottom = zone.WindowsRect.Bottom,
        VisualLeft = zone.VisualRect.Left,
        VisualTop = zone.VisualRect.Top,
        VisualRight = zone.VisualRect.Right,
        VisualBottom = zone.VisualRect.Bottom,
        IsVisible = zone.IsVisible
    };

    public CursorZone ToModel() => new(
        Id,
        DisplayName,
        new IntRect(WindowsLeft, WindowsTop, WindowsRight, WindowsBottom),
        new VisualRect(VisualLeft, VisualTop, VisualRight, VisualBottom),
        IsVisible);
}

public sealed class PortalRow
{
    public string LayoutId { get; set; } = "";
    public string FromZoneId { get; set; } = "";
    public Edge FromEdge { get; set; } = Edge.Right;
    public double FromStartRatio { get; set; }
    public double FromEndRatio { get; set; } = 1.0;
    public string ToZoneId { get; set; } = "";
    public Edge ToEdge { get; set; } = Edge.Left;
    public double ToStartRatio { get; set; }
    public double ToEndRatio { get; set; } = 1.0;

    public static PortalRow FromModel(string layoutId, CursorPortal portal) => new()
    {
        LayoutId = layoutId,
        FromZoneId = portal.FromZoneId,
        FromEdge = portal.FromEdge,
        FromStartRatio = portal.FromRange.StartRatio,
        FromEndRatio = portal.FromRange.EndRatio,
        ToZoneId = portal.ToZoneId,
        ToEdge = portal.ToEdge,
        ToStartRatio = portal.ToRange.StartRatio,
        ToEndRatio = portal.ToRange.EndRatio
    };

    public CursorPortal ToModel() => new(
        FromZoneId,
        FromEdge,
        new EdgeRange(FromStartRatio, FromEndRatio),
        ToZoneId,
        ToEdge,
        new EdgeRange(ToStartRatio, ToEndRatio));
}

public sealed class ProfileRow : ViewModelBase
{
    private string _id = "";
    private string _name = "";
    private string? _hotkey;
    private string? _deviceId;
    private int? _screenId;
    private int? _presetId;
    private string? _presetDisplayName;
    private string? _cursorLayoutId;
    private string? _cursorLayoutName;
    private int? _startX;
    private int? _startY;
    private int _postAckDelayMs = 500;
    private bool _requireH2AckBeforeCursorLayout = true;

    public string Id { get => _id; set => SetProperty(ref _id, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string? Hotkey { get => _hotkey; set => SetProperty(ref _hotkey, value); }
    public string? DeviceId
    {
        get => _deviceId;
        set
        {
            if (SetProperty(ref _deviceId, value))
            {
                OnPropertyChanged(nameof(PresetSummary));
                OnPropertyChanged(nameof(Description));
            }
        }
    }

    public int? ScreenId
    {
        get => _screenId;
        set
        {
            if (SetProperty(ref _screenId, value))
            {
                OnPropertyChanged(nameof(PresetSummary));
                OnPropertyChanged(nameof(Description));
            }
        }
    }

    public int? PresetId
    {
        get => _presetId;
        set
        {
            if (SetProperty(ref _presetId, value))
            {
                OnPropertyChanged(nameof(PresetSummary));
                OnPropertyChanged(nameof(Description));
            }
        }
    }

    public string? PresetDisplayName
    {
        get => _presetDisplayName;
        set
        {
            if (SetProperty(ref _presetDisplayName, value))
            {
                OnPropertyChanged(nameof(PresetSummary));
                OnPropertyChanged(nameof(Description));
            }
        }
    }

    public string? CursorLayoutId
    {
        get => _cursorLayoutId;
        set
        {
            if (SetProperty(ref _cursorLayoutId, value))
            {
                OnPropertyChanged(nameof(LayoutSummary));
                OnPropertyChanged(nameof(Description));
            }
        }
    }

    public string? CursorLayoutName
    {
        get => _cursorLayoutName;
        set
        {
            if (SetProperty(ref _cursorLayoutName, value))
            {
                OnPropertyChanged(nameof(LayoutSummary));
                OnPropertyChanged(nameof(Description));
            }
        }
    }

    public int? StartX
    {
        get => _startX;
        set
        {
            if (SetProperty(ref _startX, value))
            {
                OnPropertyChanged(nameof(StartSummary));
            }
        }
    }

    public int? StartY
    {
        get => _startY;
        set
        {
            if (SetProperty(ref _startY, value))
            {
                OnPropertyChanged(nameof(StartSummary));
            }
        }
    }

    public int PostAckDelayMs { get => _postAckDelayMs; set => SetProperty(ref _postAckDelayMs, value); }
    public bool RequireH2AckBeforeCursorLayout { get => _requireH2AckBeforeCursorLayout; set => SetProperty(ref _requireH2AckBeforeCursorLayout, value); }
    public string PresetSummary
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(PresetDisplayName))
            {
                return PresetDisplayName!;
            }

            return PresetId is not null
                ? $"Preset {PresetId.Value + 1} / presetId {PresetId.Value}"
                : "Cursor layout only";
        }
    }

    public string LayoutSummary => string.IsNullOrWhiteSpace(CursorLayoutId)
        ? "H2 preset only"
        : string.IsNullOrWhiteSpace(CursorLayoutName) ? "Missing cursor layout" : CursorLayoutName!;

    public string Description => $"{PresetSummary} - {LayoutSummary}";

    public string StartSummary => StartX is not null && StartY is not null
        ? $"{StartX}, {StartY}"
        : "Layout default";

    public static ProfileRow FromModel(ExecutionProfile profile) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        Hotkey = profile.Hotkey,
        DeviceId = profile.H2Preset?.DeviceId,
        ScreenId = profile.H2Preset?.ScreenId,
        PresetId = profile.H2Preset?.PresetId,
        PresetDisplayName = profile.H2Preset?.DisplayName,
        CursorLayoutId = profile.CursorLayoutId,
        StartX = profile.StartPosition?.X,
        StartY = profile.StartPosition?.Y,
        PostAckDelayMs = profile.PostAckDelayMs,
        RequireH2AckBeforeCursorLayout = profile.RequireH2AckBeforeCursorLayout
    };

    public ExecutionProfile ToModel()
    {
        H2PresetRef? preset = null;
        if (!string.IsNullOrWhiteSpace(DeviceId) && ScreenId is not null && PresetId is not null)
        {
            preset = new H2PresetRef(DeviceId, ScreenId.Value, PresetId.Value, PresetDisplayName);
        }

        var start = StartX is not null && StartY is not null
            ? new CursorPoint(StartX.Value, StartY.Value)
            : (CursorPoint?)null;

        return new ExecutionProfile(
            Id,
            Name,
            Hotkey,
            preset,
            string.IsNullOrWhiteSpace(CursorLayoutId) ? null : CursorLayoutId,
            start,
            PostAckDelayMs,
            RequireH2AckBeforeCursorLayout);
    }
}

public sealed class MonitorRow : ViewModelBase
{
    private string _displayAlias = "";
    private double _previewLeft;
    private double _previewTop;
    private double _previewWidth;
    private double _previewHeight;

    public string DeviceName { get; init; } = "";
    public string DisplayAlias
    {
        get => _displayAlias;
        set
        {
            if (SetProperty(ref _displayAlias, value))
            {
                OnPropertyChanged(nameof(DisplayLabel));
            }
        }
    }

    public int Left { get; init; }
    public int Top { get; init; }
    public int Right { get; init; }
    public int Bottom { get; init; }
    public bool IsPrimary { get; init; }
    public MonitorIdentityInfo? Identity { get; init; }
    public double PreviewLeft
    {
        get => _previewLeft;
        set => SetProperty(ref _previewLeft, value);
    }

    public double PreviewTop
    {
        get => _previewTop;
        set => SetProperty(ref _previewTop, value);
    }

    public double PreviewWidth
    {
        get => _previewWidth;
        set => SetProperty(ref _previewWidth, value);
    }

    public double PreviewHeight
    {
        get => _previewHeight;
        set => SetProperty(ref _previewHeight, value);
    }

    public string BoundsText => $"{Left},{Top} -> {Right},{Bottom}";
    public string PrimaryText => IsPrimary ? "Primary" : "";
    public string DisplayLabel => DisplayLabelFormatter.FormatAlias(DisplayAlias, DeviceName);
    public int Width => Right - Left;
    public int Height => Bottom - Top;

    public static MonitorRow FromModel(MonitorInfo monitor) => new()
    {
        DeviceName = monitor.DeviceName.Replace(@"\\.\", "", StringComparison.OrdinalIgnoreCase),
        Left = monitor.Bounds.Left,
        Top = monitor.Bounds.Top,
        Right = monitor.Bounds.Right,
        Bottom = monitor.Bounds.Bottom,
        IsPrimary = monitor.IsPrimary,
        Identity = monitor.Identity
    };
}

public sealed class DisplayAliasRow : ViewModelBase
{
    private string _deviceName = "";
    private string _alias = "";
    private bool _isConnected;
    private DateTimeOffset? _lastSeenAtUtc;

    public string DeviceName
    {
        get => _deviceName;
        set
        {
            if (SetProperty(ref _deviceName, value))
            {
                OnPropertyChanged(nameof(DisplayLabel));
            }
        }
    }

    public string Alias
    {
        get => _alias;
        set
        {
            if (SetProperty(ref _alias, value))
            {
                OnPropertyChanged(nameof(DisplayLabel));
            }
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(DeleteActionText));
            }
        }
    }

    public DateTimeOffset? LastSeenAtUtc
    {
        get => _lastSeenAtUtc;
        set
        {
            if (SetProperty(ref _lastSeenAtUtc, value))
            {
                OnPropertyChanged(nameof(LastSeenText));
            }
        }
    }

    public string StatusText => IsConnected ? "Connected" : "Not detected";
    public string DisplayLabel => DisplayLabelFormatter.FormatAlias(Alias, DeviceName);
    public string LastSeenText => LastSeenAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "";
    public string DeleteActionText => IsConnected ? "Clear alias" : "Remove";

    public static DisplayAliasRow FromModel(DisplayAlias alias) => new()
    {
        DeviceName = alias.DeviceName,
        Alias = alias.Alias,
        LastSeenAtUtc = alias.LastSeenAtUtc
    };

    public DisplayAlias ToModel() => new(
        DeviceName,
        Alias.Trim(),
        LastSeenAtUtc);
}

internal static class DisplayLabelFormatter
{
    public static string FormatAlias(string? alias, string fallback)
    {
        var name = alias?.Trim();
        return string.IsNullOrWhiteSpace(name) ? fallback.Trim() : name;
    }

    public static string Format(string? displayName, string id)
    {
        var name = displayName?.Trim();
        var normalizedId = id.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return normalizedId;
        }

        return ShouldShowId(name, normalizedId)
            ? $"{name} ({normalizedId})"
            : name;
    }

    public static bool ShouldShowId(string? displayName, string id) =>
        !string.IsNullOrWhiteSpace(id) &&
        !string.Equals(displayName?.Trim(), id.Trim(), StringComparison.OrdinalIgnoreCase);
}
