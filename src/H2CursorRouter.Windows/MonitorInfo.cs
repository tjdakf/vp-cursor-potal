using H2CursorRouter.Core.Geometry;

namespace H2CursorRouter.Windows;

public sealed record MonitorInfo(
    string DeviceName,
    IntRect Bounds,
    bool IsPrimary,
    MonitorIdentityInfo? Identity = null);

public sealed record MonitorIdentityInfo(
    string SourceAdapterId,
    uint SourceId,
    string TargetAdapterId,
    uint TargetId,
    int OutputTechnology,
    ushort EdidManufactureId,
    ushort EdidProductCodeId,
    uint ConnectorInstance,
    string MonitorFriendlyName,
    string MonitorDevicePath);
