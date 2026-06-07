using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using ServiceDefault;
using Shouldly;
using Xunit;

namespace Service.IntegrationTests;

public sealed class ConfigurationValidationTests
{
    [Fact]
    public void Api_defaults_reject_wildcard_allowed_hosts_in_production()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production
        });
        builder.Configuration["AllowedHosts"] = "*";

        Should.Throw<InvalidOperationException>(() => builder.AddApiServiceDefaults())
            .Message.ShouldContain("AllowedHosts");
    }

    [Fact]
    public void Api_defaults_reject_loopback_cors_origin_in_production()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production
        });
        builder.Configuration["AllowedHosts"] = "api.example.com";
        builder.Configuration["Cors:AllowedOrigins:0"] = "http://localhost:4200";

        Should.Throw<InvalidOperationException>(() => builder.AddApiServiceDefaults())
            .Message.ShouldContain("Cors:AllowedOrigins");
    }

    [Fact]
    public void Required_configuration_value_rejects_missing_or_whitespace_values()
    {
        var configuration = BuildConfiguration(("Present", "value"), ("Whitespace", "   "));

        configuration.GetRequiredConfigurationValue("Present").ShouldBe("value");
        Should.Throw<InvalidOperationException>(
                () => configuration.GetRequiredConfigurationValue("Missing"))
            .Message.ShouldContain("Missing");
        Should.Throw<InvalidOperationException>(
                () => configuration.GetRequiredConfigurationValue("Whitespace"))
            .Message.ShouldContain("Whitespace");
    }

    [Fact]
    public void Required_connection_string_uri_rejects_missing_or_invalid_values()
    {
        var configuration = BuildConfiguration(
            ("ConnectionStrings:rabbitmq", "amqp://user:password@rabbitmq:5672/"),
            ("ConnectionStrings:invalid", "not a uri"));

        configuration.GetRequiredConnectionStringUri("rabbitmq")
            .ShouldBe(new Uri("amqp://user:password@rabbitmq:5672/"));

        Should.Throw<InvalidOperationException>(
                () => configuration.GetRequiredConnectionStringUri("missing"))
            .Message.ShouldContain("missing");
        Should.Throw<InvalidOperationException>(
                () => configuration.GetRequiredConnectionStringUri("invalid"))
            .Message.ShouldContain("valid absolute URI");
    }

    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(x => x.Key, x => (string?)x.Value))
            .Build();
}
