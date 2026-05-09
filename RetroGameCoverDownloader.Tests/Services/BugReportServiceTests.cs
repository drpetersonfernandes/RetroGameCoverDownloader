using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using RetroGameCoverDownloader.Services;
using Xunit;

namespace RetroGameCoverDownloader.Tests.Services;

public class BugReportServiceTests
{
    [Fact]
    public void LogErrorSyncWithSingleThreadedSynchronizationContextDoesNotDeadlock()
    {
        var originalFactory = BugReportService.HttpClientFactory;
        BugReportService.InvalidateHttpClient();
        BugReportService.HttpClientFactory = static () => new HttpClient(new ImmediateOkHandler());

        var syncContext = new SingleThreadSynchronizationContext();
        Exception? caughtException = null;
        var completed = false;

        try
        {
            var worker = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(syncContext);
                syncContext.Run(() =>
                {
                    try
                    {
                        BugReportService.LogErrorSync(
                            new InvalidOperationException("test exception"),
                            "deadlock scenario test");
                        completed = true;
                    }
                    catch (Exception ex)
                    {
                        caughtException = ex;
                    }
                });
            })
            {
                IsBackground = true
            };

            worker.Start();
            var finished = worker.Join(TimeSpan.FromSeconds(5));

            Assert.True(
                finished,
                "LogErrorSync deadlocked when executed on a thread with a single-threaded SynchronizationContext.");
            Assert.True(completed, "LogErrorSync did not complete successfully.");
            Assert.Null(caughtException);
        }
        finally
        {
            BugReportService.HttpClientFactory = originalFactory;
            BugReportService.InvalidateHttpClient();
        }
    }

    private class ImmediateOkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    /// <summary>
    /// A SynchronizationContext that executes posted callbacks on the thread that calls <see cref="Run"/>.
    /// This closely mimics a WPF Dispatcher or Windows Forms message loop.
    /// </summary>
    private class SingleThreadSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state)
        {
            _queue.Add((d, state));
        }

        public void Run(Action action)
        {
            // Queue the initial action and pump until completion.
            Post(_ =>
            {
                try
                {
                    action();
                }
                finally
                {
                    _queue.CompleteAdding();
                }
            }, null);

            foreach (var item in _queue.GetConsumingEnumerable())
            {
                item.Callback(item.State);
            }
        }

        public void Dispose()
        {
            _queue.Dispose();
        }
    }
}
