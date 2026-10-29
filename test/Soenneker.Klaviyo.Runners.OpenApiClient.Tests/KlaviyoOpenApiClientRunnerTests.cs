using Soenneker.Tests.HostedUnit;

namespace Soenneker.Klaviyo.Runners.OpenApiClient.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public sealed class KlaviyoOpenApiClientRunnerTests : HostedUnitTest
{
    public KlaviyoOpenApiClientRunnerTests(Host host) : base(host)
    {
    }

    [Test]
    public void Default()
    {

    }
}
