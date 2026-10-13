namespace Firmly.Core.Vendors.Hp;

/// <summary>
/// Settings Firmly exposes on HP business notebooks. Names must match the BIOS exactly.
/// Keep irreversible settings (e.g. "Permanently Disable ... (Set Once)"), Secure Boot/TPM controls,
/// and factory-reset actions off these lists.
/// </summary>
public static class HpFirmwarePolicy
{
    public static FirmwarePolicy Default { get; } = new(
        readable:
        [
            // Read-only; firmware enables it when Battery Health Manager is "Let HP Manage My Battery Health".
            "Adaptive Battery Optimizer",
            "Adaptive Battery Optimizer Status",
            "Fast Charge",
            "Product Name",
            "System BIOS Version",
        ],
        // Power and charging behaviour: any signed-in user, as with Windows power settings.
        writable:
        [
            "Battery Health Manager",
        ],
        // Settings that affect other users or device security posture.
        adminWritable:
        [
        ]);
}
