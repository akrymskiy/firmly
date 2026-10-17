
namespace Firmly.Ipc;

/// <summary>An answer to "what's the BIOS administrator password?".</summary>
/// <param name="Remember">Save it, protected by Windows Hello, once the BIOS has accepted it.</param>
/// <param name="FromVault">It's the saved one, so a refusal means the saved one is out of date.</param>
public sealed record BiosPasswordAnswer(string Password, bool Remember = false, bool FromVault = false)
{
    // Records print their properties; this one must never show up in a log or the debugger's summary.
    public override string ToString() => FromVault ? "saved BIOS password" : "typed BIOS password";
}

/// <summary>Gets the BIOS administrator password: unlocked with Windows Hello, or asked for.</summary>
public interface IBiosPasswordSource
{
    /// <param name="rejected">The answer the BIOS just refused; null the first time.</param>
    /// <returns>Null when the user cancelled.</returns>
    Task<BiosPasswordAnswer?> GetAsync(BiosPasswordAnswer? rejected);

    /// <summary>The BIOS accepted <paramref name="answer"/>: saves it if the user asked to.</summary>
    Task AcceptedAsync(BiosPasswordAnswer answer);
}

/// <summary>
/// Sends changes with the BIOS administrator password when one is set: gets it first, and again while the BIOS
/// refuses it, a few times at most.
/// </summary>
public sealed class BiosPasswordGate
{
    // A BIOS may count wrong passwords and lock changes until restarted after a few.
    private const int MaxTries = 3;

    /// <summary>Set at start-up; without one, changes are sent without a password.</summary>
    public IBiosPasswordSource? Source { get; set; }

    /// <summary>The BIOS has an administrator password, as of the last reading.</summary>
    public bool IsSet { get; set; }

    /// <summary>The machine policy lets Firmly keep the password: with Windows Hello, or in the service for a timed charge.</summary>
    public bool AllowSaved { get; set; } = true;

    /// <summary>Runs <paramref name="change"/>, with the BIOS password when one is needed.</summary>
    /// <exception cref="OperationCanceledException">The user didn't give the password.</exception>
    /// <exception cref="FirmlyServiceException">The change failed, including the password being refused too often.</exception>
    public async Task<T> CallAsync<T>(Func<string?, Task<T>> change)
    {
        BiosPasswordAnswer? answer = null;
        if (IsSet && Source is { } first)
            answer = await first.GetAsync(null) ?? throw new OperationCanceledException();

        for (var tries = 1; ; tries++)
        {
            try
            {
                var result = await change(answer?.Password);
                if (answer is not null && Source is { } source)
                    await source.AcceptedAsync(answer);
                return result;
            }
            catch (FirmlyServiceException ex) when (ex.Error == IpcError.BiosPasswordRequired && tries < MaxTries && Source is { } source)
            {
                // Also when Firmly didn't know of a password: one was set since, or the firmware couldn't say.
                IsSet = true;
                answer = await source.GetAsync(answer) ?? throw new OperationCanceledException();
            }
        }
    }
}
