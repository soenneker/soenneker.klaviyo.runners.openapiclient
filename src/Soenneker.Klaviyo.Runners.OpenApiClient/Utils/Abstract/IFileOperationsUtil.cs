using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Klaviyo.Runners.OpenApiClient.Utils.Abstract;

/// <summary>Regenerates the API client from Klaviyo's published OpenAPI specification.</summary>
public interface IFileOperationsUtil
{
    /// <summary>
    /// Downloads the specification, regenerates the client, and verifies a Release build.
    /// Set Klaviyo:LocalRepositoryPath to regenerate a local client repository without pushing by default.
    /// Klaviyo:PushChanges explicitly controls publication; Klaviyo:ClientGenerationUrl overrides the specification source.
    /// Throws when generation or compilation fails.
    /// </summary>
    ValueTask Process(CancellationToken cancellationToken = default);
}
