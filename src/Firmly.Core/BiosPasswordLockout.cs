namespace Firmly.Core;

/// <summary>
/// How a firmware locks out wrong BIOS passwords: after <paramref name="MaxConsecutiveFailures"/> wrong ones in a row it
/// refuses every change, even with the right password, until the PC restarts. A change it accepts resets the count.
/// </summary>
/// <remarks>
/// A locked firmware answers exactly as it does to a wrong password, so only counting tells the two apart. Firmly stops
/// sending changes at the limit, rather than have a correct password refused as if it were wrong.
/// </remarks>
public sealed record BiosPasswordLockout(int MaxConsecutiveFailures);
