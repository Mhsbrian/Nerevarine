using System.Net;
using Mri.Core.Umo;

namespace Mri.Core.Tests.Umo;

public class EphemeralUrlResolverTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Requested.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }
    }

    private static EphemeralUrlResolver Resolver(
        FakeHandler handler) => new(new HttpClient(handler));

    [Fact]
    public async Task MediafireLandingIsSwappedForTokenUrl()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """<a href="https://download1507.mediafire.com/abc123/g9yn/Simple+Sleeping.7z" id="downloadButton">"""),
        });
        var json =
            """{"download_info":[{"direct_download":"https://www.mediafire.com/file/g9yn/Simple_Sleeping.7z/file"}]}""";

        var resolved = await Resolver(handler).ResolveAsync(json);

        Assert.Contains("https://download1507.mediafire.com/abc123/g9yn/Simple+Sleeping.7z", resolved);
        Assert.DoesNotContain("mediafire.com/file/", resolved);
        Assert.Equal(["https://www.mediafire.com/file/g9yn/Simple_Sleeping.7z/file"], handler.Requested);
    }

    [Fact]
    public async Task NonMediafireListPassesThroughWithoutNetwork()
    {
        var handler = new FakeHandler(_ => throw new InvalidOperationException("no requests expected"));
        var json =
            """{"download_info":[{"direct_download":"https://www.dropbox.com/scl/fi/x/y.esp?dl=1"}]}""";

        Assert.Equal(json, await Resolver(handler).ResolveAsync(json));
        Assert.Empty(handler.Requested);
    }

    [Fact]
    public async Task MissingTokenOnLandingPageThrows()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>file removed</html>"),
        });
        var json =
            """{"download_info":[{"direct_download":"https://www.mediafire.com/file/gone/x.7z/file"}]}""";

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Resolver(handler).ResolveAsync(json));
    }
}
