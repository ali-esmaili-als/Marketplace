namespace Marketplace.Domain.Catalog;

public static class ProductDiscountRules
{
    public const byte Percent = 1;
    public const byte Fixed = 2;

    public static long Apply(long baseAmount, byte discountType, long discountValue)
    {
        if (baseAmount < 0 || discountValue < 0) throw new ArgumentOutOfRangeException();
        return discountType switch
        {
            Percent => Math.Min(baseAmount, baseAmount * discountValue / 100),
            Fixed => Math.Min(baseAmount, discountValue),
            _ => throw new ArgumentOutOfRangeException(nameof(discountType))
        };
    }
}
