using System.Net;

using BarkFluff.Proto.Configuration;

namespace BarkFluff.GrpcServer;

internal static class ConfigurationStartupLoader
{
    internal static async Task<GetConfigurationResponse> LoadAsync(
        HttpClient client,
        Func<Task<GetConfigurationResponse>> getConfiguration,
        bool requireValidSettings,
        Func<TimeSpan, Task>? delay = null)
    {
        delay ??= duration => Task.Delay(duration);
        var path = "internal/configuration/ready"
                   + (requireValidSettings ? string.Empty : "?requireValidSettings=false");

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            string reason;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, path)
                {
                    Version = HttpVersion.Version20,
                    VersionPolicy = HttpVersionPolicy.RequestVersionExact
                };
                using var response = await client.SendAsync(request).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    Console.Error.WriteLine($"Settings readiness check {attempt}/5 succeeded; loading configuration.");
                    break;
                }

                if ((int)response.StatusCode < 500 || (int)response.StatusCode > 599)
                {
                    Console.Error.WriteLine($"Settings readiness check {attempt}/5 failed: HTTP {(int)response.StatusCode}.");
                    throw new InvalidOperationException($"Settings readiness endpoint returned HTTP {(int)response.StatusCode}.");
                }

                reason = $"HTTP {(int)response.StatusCode}";
            }
            catch (HttpRequestException)
            {
                reason = "network error";
            }
            catch (OperationCanceledException)
            {
                reason = "request timed out";
            }

            Console.Error.WriteLine($"Settings readiness check {attempt}/5 failed: {reason}.");
            if (attempt == 5)
                throw new InvalidOperationException("Settings is not ready after 5 checks; configuration was not loaded.");

            Console.Error.WriteLine("Waiting 5 seconds before checking Settings again.");
            await delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }

        return await getConfiguration().ConfigureAwait(false);
    }
}
