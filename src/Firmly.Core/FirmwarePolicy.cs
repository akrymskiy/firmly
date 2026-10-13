namespace Firmly.Core;

/// <summary>
/// Allowlists of settings Firmly may expose and change. Anything not listed is invisible to the app
/// and can never be written, which keeps irreversible or security-critical settings out of reach.
/// </summary>
public sealed class FirmwarePolicy
{
    private readonly HashSet<string> _readable;
    private readonly HashSet<string> _writable;
    private readonly HashSet<string> _adminWritable;

    /// <param name="readable">Settings the app may read.</param>
    /// <param name="writable">Settings any signed-in user may change; these are implicitly readable.</param>
    /// <param name="adminWritable">Settings only administrators may change; these are implicitly readable.</param>
    public FirmwarePolicy(IEnumerable<string> readable, IEnumerable<string> writable, IEnumerable<string>? adminWritable = null)
    {
        _adminWritable = new HashSet<string>(adminWritable ?? [], StringComparer.Ordinal);
        _writable = new HashSet<string>(writable, StringComparer.Ordinal);
        if (_writable.Overlaps(_adminWritable))
            throw new ArgumentException("A setting cannot be both user-writable and admin-writable.", nameof(adminWritable));

        _writable.UnionWith(_adminWritable);
        _readable = new HashSet<string>(readable, StringComparer.Ordinal);
        _readable.UnionWith(_writable);
    }

    public bool IsReadable(string name) => _readable.Contains(name);

    public bool IsWritable(string name) => _writable.Contains(name);

    public bool RequiresAdmin(string name) => _adminWritable.Contains(name);

    public IReadOnlyList<FirmwareSetting> FilterReadable(IEnumerable<FirmwareSetting> settings) =>
        settings.Where(s => IsReadable(s.Name)).ToList();

    /// <exception cref="FirmwarePolicyException">The change is not allowed.</exception>
    public void EnsureCanWrite(FirmwareSetting current, string newValue)
    {
        if (!IsWritable(current.Name))
            throw new FirmwarePolicyException($"'{current.Name}' is not on the write allowlist.");

        if (current.IsReadOnly)
            throw new FirmwarePolicyException($"'{current.Name}' is read-only in firmware.");

        // Only enumerations are writable for now: the firmware's own list of values is the validation.
        if (current.Kind != FirmwareSettingKind.Enumeration)
            throw new FirmwarePolicyException($"'{current.Name}' is a {current.Kind} setting; only enumerations can be written.");

        if (!current.PossibleValues.Contains(newValue, StringComparer.Ordinal))
            throw new FirmwarePolicyException($"'{newValue}' is not a valid value for '{current.Name}'.");
    }
}

public class FirmwarePolicyException(string message) : InvalidOperationException(message);

/// <summary>The setting may be changed, but not by this caller.</summary>
public sealed class FirmwareAccessDeniedException(string message) : FirmwarePolicyException(message);
