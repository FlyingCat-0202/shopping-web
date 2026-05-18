namespace Payment.API.PaymentProviders;

public sealed class PaymentProviderCatalog(IEnumerable<IPaymentProvider> providers)
{
    private readonly IReadOnlyList<IPaymentProvider> _providers = providers.ToList();

    public IReadOnlyList<PaymentProviderSummary> GetAll()
        => [.. _providers
            .OrderBy(p => p.Name)
            .Select(p => new PaymentProviderSummary(p.Name, p.RouteName))];

    public IPaymentProvider? FindByRoute(string provider)
        => _providers.FirstOrDefault(p =>
            string.Equals(p.RouteName, provider, StringComparison.OrdinalIgnoreCase));

    public IPaymentProvider? FindByPaymentMethod(string paymentMethod)
        => _providers.FirstOrDefault(p => p.SupportsPaymentMethod(paymentMethod));
}
