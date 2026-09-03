using Azure.Core;
using Azure.Identity;
using Mcp.Dataverse.Core.Prompts;
using Mcp.Dataverse.Core.Tools;
using MarkMpn.Sql4Cds.Engine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.PowerPlatform.Dataverse.Client.Model;
using ModelContextProtocol;
using System;
namespace Mcp.Dataverse.Core.Extensions;

public static class BuilderExtensions
{
    private enum AuthMode { Delegated, S2S }

    public static void AddDataverse(this IHostApplicationBuilder builder)
    {
        var environmentUrl = Environment.GetEnvironmentVariable("DATAVERSE_ENVIRONMENT_URL");
        if (string.IsNullOrEmpty(environmentUrl))
        {
            throw new McpException("DATAVERSE_ENVIRONMENT_URL environment variable is not set.", McpErrorCode.InvalidRequest);
        }

        var mode = ResolveAuthMode(
            Environment.GetEnvironmentVariable("DATAVERSE_AUTH_MODE"),
            Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET"));

        var credential = mode switch
        {
            AuthMode.Delegated => CreateDelegatedCredential(),
            AuthMode.S2S => CreateS2SCredential(),
            _ => throw new InvalidOperationException("unreachable")
        };

        builder.Services.AddSingleton(credential);
        builder.Services.AddSingleton(sp =>
        {
            try
            {
                // single-flight: concurrent tool calls must not each open an interactive login window
                var tokenLock = new SemaphoreSlim(1, 1);
                var options = new ConnectionOptions
                {
                    ServiceUri = new Uri(environmentUrl),
                    AuthenticationType = AuthenticationType.ExternalTokenManagement,
                    AccessTokenProviderFunctionAsync = async instanceUri =>
                    {
                        // instanceUri is the full Organization.svc endpoint (incl. query string) - the
                        // token resource must be the bare origin, e.g. https://yourorg-dev.crm4.dynamics.com/.default
                        var origin = new Uri(instanceUri).GetLeftPart(UriPartial.Authority);
                        await tokenLock.WaitAsync(CancellationToken.None);
                        try
                        {
                            var token = await sp.GetRequiredService<TokenCredential>()
                                .GetTokenAsync(new TokenRequestContext(new[] { origin + "/.default" }), CancellationToken.None);
                            return token.Token;
                        }
                        finally
                        {
                            tokenLock.Release();
                        }
                    }
                };
                return new ServiceClient(options);
            }
            catch (Exception ex)
            {
                throw new McpException(ex.Message, ex.InnerException);
            }
        });

        builder.Services.AddSingleton(sp =>
        {
            var dataverseClient = sp.GetRequiredService<ServiceClient>();
            return new Sql4CdsConnection(dataverseClient) { UseLocalTimeZone = true };
        });
    }

    private static AuthMode ResolveAuthMode(string? configured, string? clientSecret)
    {
        return configured?.ToLowerInvariant() switch
        {
            null or "" or "auto" => string.IsNullOrEmpty(clientSecret) ? AuthMode.Delegated : AuthMode.S2S,
            "delegated" => AuthMode.Delegated,
            "s2s" => AuthMode.S2S,
            var other => throw new McpException($"Unknown DATAVERSE_AUTH_MODE '{other}'. Valid values: auto, delegated, s2s.")
        };
    }

    // ponytail: default client = Microsoft first-party "Dynamics 365 Example Client Application"
    // (learn.microsoft.com/power-platform/admin/apps-to-allow) so interactive login works without
    // an own app registration. Set DATAVERSE_APP_ID to use your own app reg. If a tenant blocks
    // user consent for first-party apps or the redirect isn't accepted, an own app reg is required.
    private const string DefaultDelegatedClientId = "51f81489-12ee-4a9e-aaae-a2591f45987d";

    private static TokenCredential CreateDelegatedCredential()
    {
        return new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
        {
            ClientId = Environment.GetEnvironmentVariable("DATAVERSE_APP_ID") ?? DefaultDelegatedClientId,
            TenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID"), // optional, defaults to 'organizations'
            RedirectUri = new Uri("http://localhost"),
            TokenCachePersistenceOptions = new TokenCachePersistenceOptions()
        });
    }

    private static TokenCredential CreateS2SCredential()
    {
        var clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");
        var tenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID");
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(tenantId))
        {
            throw new McpException("S2S auth needs AZURE_CLIENT_ID, AZURE_CLIENT_SECRET and AZURE_TENANT_ID. See docs/auth.md.");
        }
        return new ClientSecretCredential(tenantId, clientId, clientSecret);
    }
}
