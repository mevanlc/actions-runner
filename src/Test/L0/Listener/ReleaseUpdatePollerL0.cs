using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GitHub.Runner.Listener;
using GitHub.Runner.Sdk;
using Moq;
using Moq.Protected;
using Xunit;

namespace GitHub.Runner.Common.Tests.Listener
{
    public sealed class ReleaseUpdatePollerL0
    {
        private static string GetReleaseSourcePath(TestHostContext hc)
        {
            return Path.Combine(hc.GetDirectory(WellKnownDirectory.Root), ReleaseUpdatePoller.UpdateSourceFileName);
        }

        private static void DeleteReleaseSource(TestHostContext hc)
        {
            string releaseSourcePath = GetReleaseSourcePath(hc);
            if (File.Exists(releaseSourcePath))
            {
                File.Delete(releaseSourcePath);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Runner")]
        public void StartWithoutReleaseSourceDisablesPolling()
        {
            using (var hc = new TestHostContext(this))
            {
                DeleteReleaseSource(hc);

                var poller = new ReleaseUpdatePoller();
                poller.Initialize(hc);

                poller.Start(Mock.Of<IJobDispatcher>(), false, hc.RunnerShutdownToken);

                Assert.False(poller.Enabled);
                Assert.False(poller.Busy);
                Assert.False(poller.UpdateReady.IsCompleted);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Runner")]
        public async Task StartWithNewerReleaseInvokesSelfUpdater()
        {
            using (var hc = new TestHostContext(this))
            {
                try
                {
                    string releaseSource = "mevanlc/actions-runner";
                    string targetVersion = "99.999.0";
                    string platform = BuildConstants.RunnerPackage.PackageName;
                    string extension = platform.StartsWith("win", StringComparison.OrdinalIgnoreCase) ? ".zip" : ".tar.gz";
                    string assetName = $"actions-runner-{platform}-{targetVersion}{extension}";
                    string downloadUrl = $"https://github.com/{releaseSource}/releases/download/v{targetVersion}/{assetName}";
                    string sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

                    DeleteReleaseSource(hc);
                    File.WriteAllText(GetReleaseSourcePath(hc), releaseSource);

                    var mockClientHandler = new Mock<HttpClientHandler>();
                    mockClientHandler.Protected()
                        .Setup<Task<HttpResponseMessage>>(
                            "SendAsync",
                            ItExpr.Is<HttpRequestMessage>(m => m.RequestUri == new Uri($"https://api.github.com/repos/{releaseSource}/releases/latest")),
                            ItExpr.IsAny<CancellationToken>())
                        .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent($@"{{
  ""tag_name"": ""v{targetVersion}"",
  ""assets"": [
    {{
      ""name"": ""{assetName}"",
      ""browser_download_url"": ""{downloadUrl}"",
      ""digest"": ""sha256:{sha256}""
    }}
  ]
}}")
                        });

                    var handlerFactory = new Mock<IHttpClientHandlerFactory>();
                    handlerFactory.Setup(x => x.CreateClientHandler(It.IsAny<RunnerWebProxy>()))
                        .Returns(mockClientHandler.Object);
                    hc.SetSingleton<IHttpClientHandlerFactory>(handlerFactory.Object);

                    var jobDispatcher = Mock.Of<IJobDispatcher>();
                    var updater = new Mock<ISelfUpdaterV2>();
                    var selfUpdateEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var finishSelfUpdate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    updater.Setup(x => x.SelfUpdate(
                            targetVersion,
                            downloadUrl,
                            sha256,
                            platform,
                            jobDispatcher,
                            false,
                            It.IsAny<CancellationToken>()))
                        .Returns(async () =>
                        {
                            selfUpdateEntered.TrySetResult(true);
                            await finishSelfUpdate.Task;
                            return true;
                        });
                    hc.SetSingleton<ISelfUpdaterV2>(updater.Object);

                    var poller = new ReleaseUpdatePoller();
                    poller.Initialize(hc);

                    poller.Start(jobDispatcher, false, hc.RunnerShutdownToken);

                    Assert.True(poller.Enabled);
                    Assert.Same(selfUpdateEntered.Task, await Task.WhenAny(selfUpdateEntered.Task, Task.Delay(5000)));
                    Assert.True(poller.Busy);

                    finishSelfUpdate.SetResult(true);
                    Assert.Same(poller.UpdateReady, await Task.WhenAny(poller.UpdateReady, Task.Delay(5000)));
                    Assert.True(await poller.UpdateReady);
                    Assert.False(poller.Busy);

                    updater.VerifyAll();
                    mockClientHandler.Protected().Verify(
                        "SendAsync",
                        Times.Once(),
                        ItExpr.IsAny<HttpRequestMessage>(),
                        ItExpr.IsAny<CancellationToken>());
                }
                finally
                {
                    DeleteReleaseSource(hc);
                }
            }
        }
    }
}
