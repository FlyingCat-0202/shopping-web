namespace Payment.API.PaymentProviders;

public sealed class MeilyMeilyPaymentProvider(
    IConfiguration configuration,
    IWebHostEnvironment environment) : FakeWalletPaymentProvider(configuration, environment)
{
    public override string Name => "MeilyMeily";
    public override string RouteName => "meilymeily";
    protected override string AccentColor => "#0f766e";
    protected override string PageBackgroundColor => "#ecfeff";
    protected override string BorderColor => "#bae6fd";
    protected override string ShadowColor => "rgba(15, 118, 110, 0.16)";
}
