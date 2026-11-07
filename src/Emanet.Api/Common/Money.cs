using Emanet.Core;

namespace Emanet.Api.Common;

/// <summary>Amounts travel as lira with kuruş (12.50) and are kept as whole kuruş, so nothing is ever rounded twice.</summary>
internal static class Money
{
    public static long ToMinor(decimal amount, string what)
    {
        if (amount < 0 || decimal.Round(amount, 2) != amount)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"{what} is a positive amount with at most two decimals.");
        }

        return (long)(amount * 100);
    }

    public static decimal FromMinor(long minor) => minor / 100m;
}
