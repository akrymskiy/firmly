namespace Firmly.Core;

/// <summary>
/// Allowlists of settings Firmly may expose and change. Anything not listed is invisible to the app
/// and can never be written, which keeps irreversible or security-critical settings out of reach.
/// </summary>
public sealed class FirmwarePolicy
{
    private readonly HashSet<string> _readable;
    private readonly HashSet<string> _writable;

    /// <param name="readable">Settings the app may read.</param>
    /// <param name="writable">Settings the app may change; these are implicitly readable.</param>
    public FirmwarePolicy(IEnumerable<string> readable, IEnumerable<string> writable)
    {
        _writable = new HashSet<string>(writable, StringComparer.Ordinal);
        _readable = new HashSet<string>(readable, StringComparer.Ordinal);
        _readable.UnionWith(_writable);
    }

    public bool IsReadable(string name) => _readable.Contains(name);

    public bool IsWritable(string name) => _writable.Contains(name);

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

public sealed class FirmwarePolicyException(string message) : InvalidOperationException(message);
