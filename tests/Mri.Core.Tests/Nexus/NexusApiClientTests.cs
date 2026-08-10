using System.Net;
using System.Text;
using Mri.Core.Nexus;

namespace Mri.Core.Tests.Nexus;

public class NexusApiClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    [Fact]
    public async Task ValidKeyReturnsUserWithPremiumFlag()
    {
        HttpRequestMessage? seen = null;
        var client = new NexusApiClient(new HttpClient(new StubHandler(req =>
        {
            seen = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"user_id":123,"name":"brian","is_premium":true,"is_supporter":false,"profile_url":"https://example/avatar.png"}""",
                    Encoding.UTF8, "application/json"),
            };
        })));

        var user = await client.ValidateKeyAsync("  key-with-whitespace  ");

        Assert.NotNull(user);
        Assert.Equal("brian", user!.Name);
        Assert.True(user.IsPremium);
        Assert.False(user.IsSupporter);

        // AUP-required headers and trimmed key.
        Assert.Equal("key-with-whitespace", seen!.Headers.GetValues("apikey").Single());
        Assert.Equal("MorrowindRemakeInstaller", seen.Headers.GetValues("Application-Name").Single());
        Assert.NotEmpty(seen.Headers.GetValues("Application-Version").Single());
    }

    [Fact]
    public async Task UnauthorizedReturnsNull()
    {
        var client = new NexusApiClient(new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized))));

        Assert.Null(await client.ValidateKeyAsync("bad-key"));
    }

    [Fact]
    public async Task ServerErrorThrows()
    {
        var client = new NexusApiClient(new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError))));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.ValidateKeyAsync("key"));
    }
}
