using System.Security.Claims;
using Emanet.Api.Common;
using Emanet.Core;
using Emanet.Core.Ledger;
using Emanet.Core.Payments;
using Emanet.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Emanet.Api.Features.Bookings;

internal static class LedgerEndpoints
{
    public static IEndpointRouteBuilder MapLedgerEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me/earnings", Earnings).WithTags("Money").RequireAuthorization()
            .WithSummary("What you have earned renting things out, from the ledger");
        app.MapGet("/api/admin/books", Books).WithTags("Money").RequireAuthorization()
            .WithSummary("Check the books (admins)")
            .WithDescription("Every entry must balance, the ledger must sum to zero, and the provider's net captures must equal the ledger's provider account.");
        return app;
    }

    private static async Task<Ok<EarningsResponse>> Earnings(ClaimsPrincipal user, EmanetDbContext db, CancellationToken ct)
    {
        var account = Accounts.OwnerPayable(user.UserId());
        var lines = await (
            from l in db.LedgerLines.AsNoTracking()
            join e in db.Journal.AsNoTracking() on l.EntryId equals e.Id
            where l.Account == account
            orderby l.Id descending
            select new { e.Id, e.Kind, e.BookingId, e.Memo, l.Amount, e.PostedAt })
            .Take(200).ToListAsync(ct);
        var totals = await db.LedgerLines.AsNoTracking().Where(l => l.Account == account)
            .GroupBy(_ => 1).Select(g => new { Owed = -g.Sum(l => l.Amount), Earned = -g.Where(l => l.Amount < 0).Sum(l => l.Amount), Refunded = g.Where(l => l.Amount > 0).Sum(l => l.Amount) })
            .SingleOrDefaultAsync(ct);
        // The owner's account is a liability: credits (negative) are what they are owed.
        return TypedResults.Ok(new EarningsResponse(
            Money.FromMinor(totals?.Owed ?? 0), Money.FromMinor(totals?.Earned ?? 0), Money.FromMinor(totals?.Refunded ?? 0),
            lines.Select(l => new LedgerLineResponse(l.Id, l.Kind.ToString(), l.BookingId, l.Memo, Money.FromMinor(-l.Amount), l.PostedAt)).ToList()));
    }

    private static async Task<Ok<BooksResponse>> Books(ClaimsPrincipal user, EmanetDbContext db, IPaymentProvider payments, IConfiguration configuration, CancellationToken ct)
    {
        await user.RequireAdminAsync(db, configuration, ct);

        var entries = await db.Journal.CountAsync(ct);
        var unbalanced = await db.LedgerLines.AsNoTracking().GroupBy(l => l.EntryId).CountAsync(g => g.Sum(l => l.Amount) != 0, ct);
        var total = await db.LedgerLines.SumAsync(l => (long?)l.Amount, ct) ?? 0;
        var balances = await db.LedgerLines.AsNoTracking()
            .GroupBy(l => l.Account.StartsWith("owner:") ? "owners:payable" : l.Account)
            .Select(g => new { Account = g.Key, Balance = g.Sum(l => l.Amount) })
            .OrderBy(g => g.Account)
            .ToListAsync(ct);
        var clearing = balances.SingleOrDefault(b => b.Account == Accounts.ProviderClearing)?.Balance ?? 0;
        var provider = await payments.NetCapturedAsync(ct);
        return TypedResults.Ok(new BooksResponse(
            unbalanced == 0 && total == 0, entries, unbalanced, Money.FromMinor(total), Money.FromMinor(clearing), Money.FromMinor(provider), clearing == provider,
            balances.Select(b => new AccountBalance(b.Account, Money.FromMinor(b.Balance))).ToList()));
    }
}
