using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GitHub.DistributedTask.WebApi;
using GitHub.Runner.Common;
using GitHub.Runner.Sdk;
using Newtonsoft.Json;

namespace GitHub.Runner.Listener
{
    [ServiceLocator(Default = typeof(ReleaseUpdatePoller))]
    public interface IReleaseUpdatePoller : IRunnerService
    {
        bool Enabled { get; }
        bool Busy { get; }
        Task<bool> UpdateReady { get; }
        void Start(IJobDispatcher jobDispatcher, bool restartInteractiveRunner, CancellationToken token);
    }

    public sealed class ReleaseUpdatePoller : RunnerService, IReleaseUpdatePoller
    {
        public const string UpdateSourceFileName = ".runner-release-source";
        private const string PollIntervalVariable = "GITHUB_ACTIONS_RUNNER_RELEASE_UPDATE_POLL_INTERVAL";
        private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromHours(1);
        private static readonly TimeSpan MinimumPollInterval = TimeSpan.FromMinutes(1);
        private static readonly Regex Sha256Regex = new(@"\b[a-fA-F0-9]{64}\b", RegexOptions.Compiled);

        private readonly TaskCompletionSource<bool> _updateReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _started;

        public bool Enabled { get; private set; }
        public bool Busy { get; private set; }
        public Task<bool> UpdateReady => _updateReady.Task;

        public void Start(IJobDispatcher jobDispatcher, bool restartInteractiveRunner, CancellationToken token)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            string releaseSource = ReadReleaseSource();
            if (string.IsNullOrEmpty(releaseSource))
            {
                Trace.Info($"Release update source file '{UpdateSourceFileName}' was not found or was empty. Release polling updates are disabled.");
                return;
            }

            Enabled = true;
            TimeSpan pollInterval = GetPollInterval();
            Trace.Info($"Release update polling enabled for '{releaseSource}' every {pollInterval.TotalSeconds} seconds.");
            _ = PollLoopAsync(releaseSource, pollInterval, jobDispatcher, restartInteractiveRunner, token);
        }

        private string ReadReleaseSource()
        {
            string updateSourcePath = Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Root), UpdateSourceFileName);
            if (!File.Exists(updateSourcePath))
            {
                return null;
            }

            string releaseSource = File.ReadAllText(updateSourcePath).Trim();
            string[] parts = releaseSource.Split('/');
            if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
            {
                Trace.Warning($"Ignoring invalid release update source '{releaseSource}' from '{updateSourcePath}'. Expected owner/repo.");
                return null;
            }

            return releaseSource;
        }

        private static TimeSpan GetPollInterval()
        {
            string configuredInterval = Environment.GetEnvironmentVariable(PollIntervalVariable);
            if (int.TryParse(configuredInterval, out int seconds) && seconds > 0)
            {
                return TimeSpan.FromSeconds(Math.Max(seconds, (int)MinimumPollInterval.TotalSeconds));
            }

            return DefaultPollInterval;
        }

        private async Task PollLoopAsync(string releaseSource, TimeSpan pollInterval, IJobDispatcher jobDispatcher, bool restartInteractiveRunner, CancellationToken token)
        {
            bool firstPoll = true;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!firstPoll)
                    {
                        await HostContext.Delay(pollInterval, token);
                    }

                    firstPoll = false;
                    ReleaseUpdate update = await TryGetLatestReleaseUpdateAsync(releaseSource, token);
                    if (update == null)
                    {
                        continue;
                    }

                    try
                    {
                        Busy = true;
                        Trace.Info($"Starting release update to {update.TargetVersion} from {releaseSource}.");
                        var selfUpdater = HostContext.GetService<ISelfUpdaterV2>();
                        bool updateReady = await selfUpdater.SelfUpdate(
                            update.TargetVersion,
                            update.DownloadUrl,
                            update.Sha256Checksum,
                            update.Platform,
                            jobDispatcher,
                            restartInteractiveRunner,
                            token);

                        if (updateReady)
                        {
                            _updateReady.TrySetResult(true);
                            return;
                        }
                    }
                    finally
                    {
                        Busy = false;
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Trace.Error("Release update poll failed.");
                    Trace.Error(ex);
                }
            }
        }

        private async Task<ReleaseUpdate> TryGetLatestReleaseUpdateAsync(string releaseSource, CancellationToken token)
        {
            string releaseUrl = $"https://api.github.com/repos/{releaseSource}/releases/latest";
            using HttpClient httpClient = new(HostContext.CreateHttpClientHandler());
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"actions-runner/{BuildConstants.RunnerPackage.Version}");
            httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            using HttpResponseMessage response = await httpClient.GetAsync(releaseUrl, token);
            if (!response.IsSuccessStatusCode)
            {
                Trace.Warning($"Release update poll for '{releaseSource}' returned HTTP {(int)response.StatusCode}.");
                return null;
            }

            string responseBody = await response.Content.ReadAsStringAsync(token);
            GitHubRelease latestRelease = GitHub.Runner.Sdk.StringUtil.ConvertFromJson<GitHubRelease>(responseBody);
            string targetVersion = latestRelease.TagName?.Trim();
            if (string.IsNullOrEmpty(targetVersion))
            {
                Trace.Warning($"Release update poll for '{releaseSource}' returned a release without a tag.");
                return null;
            }

            if (targetVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                targetVersion = targetVersion.Substring(1);
            }

            PackageVersion latestVersion = new(targetVersion);
            PackageVersion runnerVersion = new(BuildConstants.RunnerPackage.Version);
            if (latestVersion.CompareTo(runnerVersion) <= 0)
            {
                Trace.Verbose($"Latest release version '{targetVersion}' is not newer than current runner version '{BuildConstants.RunnerPackage.Version}'.");
                return null;
            }

            string platform = BuildConstants.RunnerPackage.PackageName;
            string extension = platform.StartsWith("win", StringComparison.OrdinalIgnoreCase) ? ".zip" : ".tar.gz";
            string assetName = $"actions-runner-{platform}-{targetVersion}{extension}";
            GitHubReleaseAsset asset = latestRelease.Assets?.FirstOrDefault(x => string.Equals(x.Name, assetName, StringComparison.OrdinalIgnoreCase));
            if (asset == null || string.IsNullOrEmpty(asset.BrowserDownloadUrl))
            {
                Trace.Warning($"Release '{latestRelease.TagName}' from '{releaseSource}' does not contain asset '{assetName}'.");
                return null;
            }

            return new ReleaseUpdate
            {
                TargetVersion = targetVersion,
                Platform = platform,
                DownloadUrl = asset.BrowserDownloadUrl,
                Sha256Checksum = GetSha256Checksum(asset, latestRelease.Body, assetName),
            };
        }

        private static string GetSha256Checksum(GitHubReleaseAsset asset, string releaseBody, string assetName)
        {
            if (!string.IsNullOrEmpty(asset.Digest) &&
                asset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                return asset.Digest.Substring("sha256:".Length);
            }

            if (!string.IsNullOrEmpty(releaseBody))
            {
                foreach (string line in releaseBody.Split('\n'))
                {
                    if (line.Contains(assetName, StringComparison.OrdinalIgnoreCase))
                    {
                        Match match = Sha256Regex.Match(line);
                        if (match.Success)
                        {
                            return match.Value;
                        }
                    }
                }
            }

            return null;
        }

        private sealed class ReleaseUpdate
        {
            public string TargetVersion { get; set; }
            public string Platform { get; set; }
            public string DownloadUrl { get; set; }
            public string Sha256Checksum { get; set; }
        }

        private sealed class GitHubRelease
        {
            [JsonProperty("tag_name")]
            public string TagName { get; set; }

            [JsonProperty("body")]
            public string Body { get; set; }

            [JsonProperty("assets")]
            public List<GitHubReleaseAsset> Assets { get; set; }
        }

        private sealed class GitHubReleaseAsset
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("browser_download_url")]
            public string BrowserDownloadUrl { get; set; }

            [JsonProperty("digest")]
            public string Digest { get; set; }
        }
    }
}
