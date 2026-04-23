using System.Net;
using System.Net.Http;
using System.Reflection;
using RetroGameCoverDownloader.Models;
using RetroGameCoverDownloader.Services;
using Xunit;

namespace RetroGameCoverDownloader.Tests.Services;

public class GitHubServiceTests
{
    #region ParseGitmodules Tests

    [Fact]
    public void ParseGitmodules_ValidInput_ReturnsCorrectMap()
    {
        var input = "[submodule \"Nintendo - NES\"]\n" +
                    "\tpath = Nintendo - NES\n" +
                    "\turl = https://github.com/libretro-thumbnails/Nintendo_-_Nintendo_Entertainment_System.git\n" +
                    "[submodule \"Nintendo - SNES\"]\n" +
                    "\tpath = Nintendo - SNES\n" +
                    "\turl = https://github.com/libretro-thumbnails/Nintendo_-_Super_Nintendo_Entertainment_System.git\n";

        var method = typeof(GitHubService).GetMethod("ParseGitmodules", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = (Dictionary<string, string>)method.Invoke(null, [input])!;

        Assert.Equal(2, result.Count);
        Assert.Equal("Nintendo_-_Nintendo_Entertainment_System", result["Nintendo - NES"]);
        Assert.Equal("Nintendo_-_Super_Nintendo_Entertainment_System", result["Nintendo - SNES"]);
    }

    [Fact]
    public void ParseGitmodules_EmptyInput_ThrowsException()
    {
        var input = "";

        var method = typeof(GitHubService).GetMethod("ParseGitmodules", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [input]));
    }

    [Fact]
    public void ParseGitmodules_MalformedLines_SkipsInvalidEntries()
    {
        var input = "[submodule \"Bad\"]\n" +
                    "\tpath = Bad\n" +
                    "\turl = /\n" +
                    "[submodule \"Good\"]\n" +
                    "\tpath = Good\n" +
                    "\turl = https://github.com/libretro-thumbnails/Good_System.git\n";

        var method = typeof(GitHubService).GetMethod("ParseGitmodules", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = (Dictionary<string, string>)method.Invoke(null, [input])!;

        Assert.Single(result);
        Assert.Equal("Good_System", result["Good"]);
    }

    #endregion

    #region Disposal Tests

    [Fact]
    public void Dispose_DisposesHttpClient()
    {
        var handler = new TrackingHttpMessageHandler();
        var client = new HttpClient(handler);
        var service = new GitHubService(client);

        service.Dispose();

        Assert.True(handler.IsDisposed, "Expected the HttpMessageHandler to be disposed when GitHubService is disposed.");
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var handler = new TrackingHttpMessageHandler();
        var client = new HttpClient(handler);
        var service = new GitHubService(client);

        service.Dispose();
        var exception = Record.Exception(() => service.Dispose());

        Assert.Null(exception);
        Assert.True(handler.IsDisposed);
    }

    #endregion

    #region HttpResponseMessage Leak Tests

    [Fact]
    public async Task GetSystemFilesAsync_DisposesHttpResponseMessage_OnNotFound()
    {
        // Arrange: both branches return 404 NotFound, causing 'continue' in the loop.
        var responseMain = new TrackingHttpResponseMessage(HttpStatusCode.NotFound);
        var responseMaster = new TrackingHttpResponseMessage(HttpStatusCode.NotFound);
        var handler = new TestHttpMessageHandler(request =>
        {
            if (request.RequestUri?.ToString().Contains("/master?recursive=1") == true)
                return responseMaster;
            return responseMain;
        });

        var client = new HttpClient(handler);
        var service = new GitHubService(client);
        var system = new SystemConfig("TestSystem", "test-owner", "test-repo", "Named_Boxarts");

        // Act
        await service.GetSystemFilesAsync(system, _ => { });

        // Assert: both responses should be disposed by the 'using var' statements
        Assert.True(responseMain.IsDisposed, "Expected the 404 response for 'main' branch to be disposed.");
        Assert.True(responseMaster.IsDisposed, "Expected the 404 response for 'master' branch to be disposed.");
    }

    [Fact]
    public async Task GetSystemFilesAsync_DisposesHttpResponseMessage_OnInternalServerError()
    {
        // Arrange: first branch returns 500, triggering early return via fallback.
        var response500 = new TrackingHttpResponseMessage(HttpStatusCode.InternalServerError);
        var handler = new TestHttpMessageHandler(request =>
        {
            if (request.RequestUri?.ToString().Contains("?recursive=1") == true)
                return response500;

            // Fallback calls: return minimal valid JSON trees so the method completes gracefully
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"tree\":[]}")
            };
        });

        var client = new HttpClient(handler);
        var service = new GitHubService(client);
        var system = new SystemConfig("TestSystem", "test-owner", "test-repo", "Named_Boxarts");

        // Act
        await service.GetSystemFilesAsync(system, _ => { });

        // Assert: the 500 response should be disposed despite the early return path
        Assert.True(response500.IsDisposed, "Expected the 500 InternalServerError response to be disposed.");
    }

    #endregion

    #region Test Helpers

    private class TrackingHttpMessageHandler : HttpMessageHandler
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private class TrackingHttpResponseMessage : HttpResponseMessage
    {
        public bool IsDisposed { get; private set; }

        public TrackingHttpResponseMessage(HttpStatusCode statusCode) : base(statusCode) { }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }

    #endregion
}
