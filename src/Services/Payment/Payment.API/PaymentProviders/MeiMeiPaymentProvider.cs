namespace Payment.API.PaymentProviders;

public sealed class MeiMeiPaymentProvider(
    IConfiguration configuration,
    IWebHostEnvironment environment) : FakeWalletPaymentProvider(configuration, environment)
{
    public override string Name => "MeiMei";
    public override string RouteName => "meimei";
    protected override string AccentColor => "#7c3aed";
    protected override string PageBackgroundColor => "#f7f3ff";
    protected override string BorderColor => "#e7defc";
    protected override string ShadowColor => "rgba(68, 45, 122, 0.16)";
}
