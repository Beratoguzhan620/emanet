using Emanet.Core.Payments;
using Emanet.Infrastructure.Payments;

namespace Emanet.IntegrationTests.Support;

/// <summary>The stand-in provider, with a switch that makes captures fail as if the provider were down.</summary>
public sealed class FlakyPayments(FakePaymentProvider inner) : IPaymentProvider
{
    public bool CapturesFail { get; set; }

    public Task<Hold> HoldAsync(long amount, string paymentMethod, string key, CancellationToken ct) => inner.HoldAsync(amount, paymentMethod, key, ct);

    public Task<Hold> CaptureAsync(string holdId, long amount, string key, CancellationToken ct) =>
        CapturesFail ? throw new HttpRequestException("The payment provider is not answering.") : inner.CaptureAsync(holdId, amount, key, ct);

    public Task<Hold> ReleaseAsync(string holdId, string key, CancellationToken ct) => inner.ReleaseAsync(holdId, key, ct);

    public Task<Hold> RefundAsync(string holdId, long amount, string key, CancellationToken ct) => inner.RefundAsync(holdId, amount, key, ct);

    public Task<long> NetCapturedAsync(CancellationToken ct) => inner.NetCapturedAsync(ct);
}
