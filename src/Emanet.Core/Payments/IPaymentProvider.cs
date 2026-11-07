namespace Emanet.Core.Payments;

public enum HoldStatus
{
    /// <summary>The money is reserved on the card and can be captured.</summary>
    Held,

    /// <summary>Some or all of it was taken.</summary>
    Captured,

    /// <summary>Let go without being taken, or the remainder after a capture.</summary>
    Released,
}

public sealed record Hold(string Id, long Amount, long Captured, long Refunded, HoldStatus Status);

/// <summary>
/// A card payment provider, in the shape most of them share: reserve an amount, take some or all of it later, let the
/// rest go, refund what was taken. Every call carries a key; repeating a call with the same key returns the first
/// result and moves no money twice. Payment methods are the provider's tokens, never card numbers.
/// </summary>
public interface IPaymentProvider
{
    Task<Hold> HoldAsync(long amount, string paymentMethod, string key, CancellationToken ct);

    Task<Hold> CaptureAsync(string holdId, long amount, string key, CancellationToken ct);

    /// <summary>Lets go of what is still held. Releasing twice, or after a full capture, changes nothing.</summary>
    Task<Hold> ReleaseAsync(string holdId, string key, CancellationToken ct);

    Task<Hold> RefundAsync(string holdId, long amount, string key, CancellationToken ct);

    /// <summary>What the provider says it has taken, net of refunds: compared with the books in a reconciliation.</summary>
    Task<long> NetCapturedAsync(CancellationToken ct);
}

/// <summary>The card was refused. <see cref="Reason"/> is safe to show to the renter.</summary>
public sealed class PaymentDeclinedException(string reason) : Exception(reason)
{
    public string Reason { get; } = reason;
}
