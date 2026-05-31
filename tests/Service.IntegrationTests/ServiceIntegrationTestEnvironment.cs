using DotNet.Testcontainers.Builders;

namespace Service.IntegrationTests;

internal static class ServiceIntegrationTestEnvironment
{
    public static bool IsDockerAvailable()
    {
        try
        {
            var container = new ContainerBuilder()
                .WithImage("redis:7-alpine")
                .Build();

            container.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return true;
        }
        catch (DockerUnavailableException)
        {
            return false;
        }
    }
}
