using System.Net;
using System.Net.Http.Json;
using AutoFixture;
using Bogus;
using Shouldly;
using Xunit;

namespace Identity.ApiTests;

public sealed class AuthEndpointsTests
{
    private readonly Fixture _fixture = new();
    private readonly Faker _faker = new();

    [SkippableFact]
    public async Task Register_returns_tokens_and_can_logout_session()
    {
        using var factory = await StartFactoryAsync();
        using var client = factory.CreateClient();
        var request = NewRegisterRequest();

        var response = await client.PostAsJsonAsync("/api/auth/register", request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        auth.ShouldNotBeNull();
        auth.Token.ShouldNotBeNullOrWhiteSpace();
        auth.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        auth.Email.ShouldBe(request.Email.ToLowerInvariant());
        auth.Role.ShouldBe("Customer");

        var logoutResponse = await client.PostAsJsonAsync("/api/auth/logout", new
        {
            auth.RefreshToken
        });

        logoutResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var refreshAfterLogoutResponse = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            auth.RefreshToken
        });

        refreshAfterLogoutResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task Login_with_registered_user_returns_new_refresh_token()
    {
        using var factory = await StartFactoryAsync();
        using var client = factory.CreateClient();
        var user = NewRegisterRequest();
        var registered = await RegisterAsync(client, user);

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            EmailOrPhone = user.Email,
            user.Password
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var loggedIn = await response.Content.ReadFromJsonAsync<AuthResponse>();

        loggedIn.ShouldNotBeNull();
        loggedIn.Token.ShouldNotBeNullOrWhiteSpace();
        loggedIn.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        loggedIn.RefreshToken.ShouldNotBe(registered.RefreshToken);
    }

    [SkippableFact]
    public async Task Refresh_rotates_refresh_token_and_rejects_old_token()
    {
        using var factory = await StartFactoryAsync();
        using var client = factory.CreateClient();
        var user = NewRegisterRequest();
        var registered = await RegisterAsync(client, user);

        var refreshResponse = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            registered.RefreshToken
        });

        refreshResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var refreshed = await refreshResponse.Content.ReadFromJsonAsync<AuthResponse>();

        refreshed.ShouldNotBeNull();
        refreshed.Token.ShouldNotBeNullOrWhiteSpace();
        refreshed.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        refreshed.RefreshToken.ShouldNotBe(registered.RefreshToken);

        var replayResponse = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            registered.RefreshToken
        });

        replayResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task<IdentityApiFactory> StartFactoryAsync()
    {
        Skip.IfNot(IdentityApiFactory.IsDockerAvailable(), "Docker is not running; skipping Testcontainers integration test.");

        var factory = await IdentityApiFactory.StartAsync();
        await factory.ResetDatabaseAsync();
        return factory;
    }

    private async Task<AuthResponse> RegisterAsync(HttpClient client, RegisterRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", request);
        response.EnsureSuccessStatusCode();

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        auth.ShouldNotBeNull();

        return auth;
    }

    private RegisterRequest NewRegisterRequest()
    {
        var phoneSuffix = _faker.Random.ReplaceNumbers("#########");
        var email = $"user-{_fixture.Create<Guid>():N}@example.test";

        return new RegisterRequest(
            email,
            "Password123",
            _faker.Name.FullName(),
            $"0{phoneSuffix}");
    }

    private sealed record RegisterRequest(
        string Email,
        string Password,
        string FullName,
        string PhoneNumber);

    private sealed record AuthResponse(
        string Token,
        string RefreshToken,
        string FullName,
        string Email,
        string Role);

}
