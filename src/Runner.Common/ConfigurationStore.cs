using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using GitHub.Runner.Sdk;

namespace GitHub.Runner.Common
{
    //
    // Settings are persisted in this structure
    //
    [DataContract]
    public sealed class RunnerSettings
    {
        [DataMember(Name = "IsHostedServer", EmitDefaultValue = false)]
        private bool? _isHostedServer;

        [DataMember(EmitDefaultValue = false)]
        public ulong AgentId { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string AgentName { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public bool SkipSessionRecover { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public int PoolId { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string PoolName { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public bool DisableUpdate { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public bool Ephemeral { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string ServerUrl { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string GitHubUrl { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string WorkFolder { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string MonitorSocketAddress { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public bool UseV2Flow { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public bool UseRunnerAdminFlow { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public string ServerUrlV2 { get; set; }

        [IgnoreDataMember]
        public bool IsHostedServer
        {
            get
            {
                // If the value has been explicitly set, return it.
                if (_isHostedServer.HasValue)
                {
                    return _isHostedServer.Value;
                }

                // Otherwise, try to infer it from the GitHubUrl.
                if (!string.IsNullOrEmpty(GitHubUrl))
                {
                    return UrlUtil.IsHostedServer(new UriBuilder(GitHubUrl));
                }
                else
                {
                    // feature flag env in case the new logic is wrong.
                    if (StringUtil.ConvertToBoolean(Environment.GetEnvironmentVariable("GITHUB_ACTIONS_RUNNER_FORCE_EMPTY_GITHUB_URL_IS_HOSTED")))
                    {
                        return true;
                    }

                    // GitHubUrl will be empty for jit configured runner
                    // We will try to infer it from the ServerUrl/ServerUrlV2
                    if (StringUtil.ConvertToBoolean(Environment.GetEnvironmentVariable("GITHUB_ACTIONS_RUNNER_FORCE_GHES")))
                    {
                        // Allow env to override and force GHES in case the inference logic is wrong.
                        return false;
                    }

                    if (!string.IsNullOrEmpty(ServerUrl))
                    {
                        // pipelines services
                        var serverUrl = new UriBuilder(ServerUrl);
                        return serverUrl.Host.EndsWith(".actions.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
                                || serverUrl.Host.EndsWith(".codedev.ms", StringComparison.OrdinalIgnoreCase);
                    }

                    if (!string.IsNullOrEmpty(ServerUrlV2))
                    {
                        // broker-listener
                        var serverUrlV2 = new UriBuilder(ServerUrlV2);
                        return serverUrlV2.Host.EndsWith(".actions.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
                                || serverUrlV2.Host.EndsWith(".githubapp.com", StringComparison.OrdinalIgnoreCase)
                                || serverUrlV2.Host.EndsWith(".ghe.com", StringComparison.OrdinalIgnoreCase)
                                || serverUrlV2.Host.EndsWith(".actions.localhost", StringComparison.OrdinalIgnoreCase)
                                || serverUrlV2.Host.EndsWith(".ghe.localhost", StringComparison.OrdinalIgnoreCase);
                    }
                }

                // Default to true since Hosted runners likely don't have this property set.
                return true;
            }

            set
            {
                _isHostedServer = value;
            }
        }

        /// <summary>
        // Computed property for convenience. Can either return:
        // 1. If runner was configured at the repo level, returns something like: "myorg/myrepo"
        // 2. If runner was configured at the org level, returns something like: "myorg"
        /// </summary>
        public string RepoOrOrgName
        {
            get
            {
                Uri accountUri = new(this.ServerUrl);
                string repoOrOrgName = string.Empty;

                if (accountUri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(this.GitHubUrl))
                {
                    Uri gitHubUrl = new(this.GitHubUrl);

                    // Use the "NWO part" from the GitHub URL path
                    repoOrOrgName = gitHubUrl.AbsolutePath.Trim('/');
                }

                if (string.IsNullOrEmpty(repoOrOrgName))
                {
                    repoOrOrgName = accountUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                }

                return repoOrOrgName;
            }
        }

        [OnSerializing]
        private void OnSerializing(StreamingContext context)
        {
            if (_isHostedServer.HasValue && _isHostedServer.Value)
            {
                _isHostedServer = null;
            }
        }
    }

    [DataContract]
    public sealed class MultiRunnerSettings
    {
        [DataMember(Name = "schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        [DataMember(Name = "workFolder", EmitDefaultValue = false)]
        public string WorkFolder { get; set; } = Constants.Path.WorkDirectory;

        [DataMember(Name = "executionSlots")]
        public int ExecutionSlots { get; set; } = 1;

        [DataMember(Name = "associations")]
        public List<RunnerAssociation> Associations { get; set; } = new();
    }

    [DataContract]
    public sealed class RunnerAssociation
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }

        [DataMember(Name = "url", EmitDefaultValue = false)]
        public string Url { get; set; }

        [DataMember(Name = "agentId")]
        public ulong AgentId { get; set; }

        [DataMember(Name = "agentName")]
        public string AgentName { get; set; }

        [DataMember(Name = "poolId")]
        public int PoolId { get; set; }

        [DataMember(Name = "poolName")]
        public string PoolName { get; set; }

        [DataMember(Name = "serverUrl", EmitDefaultValue = false)]
        public string ServerUrl { get; set; }

        [DataMember(Name = "serverUrlV2", EmitDefaultValue = false)]
        public string ServerUrlV2 { get; set; }

        [DataMember(Name = "useV2Flow")]
        public bool UseV2Flow { get; set; }

        [DataMember(Name = "useRunnerAdminFlow")]
        public bool UseRunnerAdminFlow { get; set; }

        [DataMember(Name = "labels")]
        public List<string> Labels { get; set; } = new();

        [DataMember(Name = "disableUpdate")]
        public bool DisableUpdate { get; set; }

        [DataMember(Name = "ephemeral")]
        public bool Ephemeral { get; set; }

        [DataMember(Name = "monitorSocketAddress", EmitDefaultValue = false)]
        public string MonitorSocketAddress { get; set; }

        [DataMember(Name = "credentialRef")]
        public string CredentialRef { get; set; }

        public RunnerSettings ToRunnerSettings(string workFolder)
        {
            return new RunnerSettings
            {
                AgentId = AgentId,
                AgentName = AgentName,
                PoolId = PoolId,
                PoolName = PoolName,
                DisableUpdate = DisableUpdate,
                Ephemeral = Ephemeral,
                ServerUrl = ServerUrl,
                GitHubUrl = Url,
                WorkFolder = workFolder,
                MonitorSocketAddress = MonitorSocketAddress,
                UseV2Flow = UseV2Flow,
                UseRunnerAdminFlow = UseRunnerAdminFlow,
                ServerUrlV2 = ServerUrlV2,
            };
        }

        public static RunnerAssociation FromRunnerSettings(RunnerSettings settings, ISet<string> labels, string id)
        {
            ArgUtil.NotNull(settings, nameof(settings));
            ArgUtil.NotNullOrEmpty(id, nameof(id));

            return new RunnerAssociation
            {
                Id = id,
                Url = settings.GitHubUrl,
                AgentId = settings.AgentId,
                AgentName = settings.AgentName,
                PoolId = settings.PoolId,
                PoolName = settings.PoolName,
                ServerUrl = settings.ServerUrl,
                ServerUrlV2 = settings.ServerUrlV2,
                UseV2Flow = settings.UseV2Flow,
                UseRunnerAdminFlow = settings.UseRunnerAdminFlow,
                Labels = labels?.Where(x => !string.IsNullOrWhiteSpace(x)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>(),
                DisableUpdate = settings.DisableUpdate,
                Ephemeral = settings.Ephemeral,
                MonitorSocketAddress = settings.MonitorSocketAddress,
                CredentialRef = id,
            };
        }
    }

    public sealed class MultiCredentialStore
    {
        public int SchemaVersion { get; set; } = 1;
        public Dictionary<string, CredentialData> Credentials { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    [ServiceLocator(Default = typeof(ConfigurationStore))]
    public interface IConfigurationStore : IRunnerService
    {
        bool IsConfigured();
        bool IsServiceConfigured();
        bool HasCredentials();
        bool IsMigratedConfigured();
        CredentialData GetCredentials();
        CredentialData GetMigratedCredentials();
        CredentialData GetCredential(string credentialRef);
        RunnerSettings GetSettings();
        RunnerSettings GetMigratedSettings();
        MultiRunnerSettings GetMultiSettings();
        void SaveCredential(CredentialData credential);
        void SaveCredential(string credentialRef, CredentialData credential);
        void SaveMigratedCredential(CredentialData credential);
        void SaveSettings(RunnerSettings settings);
        void SaveMultiSettings(MultiRunnerSettings settings);
        void SaveMigratedSettings(RunnerSettings settings);
        void DeleteCredential();
        void DeleteCredential(string credentialRef);
        void DeleteMigratedCredential();
        void DeleteSettings();
    }

    public sealed class ConfigurationStore : RunnerService, IConfigurationStore
    {
        private string _binPath;
        private string _configFilePath;
        private string _migratedConfigFilePath;
        private string _credFilePath;
        private string _migratedCredFilePath;
        private string _serviceConfigFilePath;

        private CredentialData _creds;
        private CredentialData _migratedCreds;
        private RunnerSettings _settings;
        private RunnerSettings _migratedSettings;
        private MultiRunnerSettings _multiSettings;
        private MultiCredentialStore _multiCreds;

        public override void Initialize(IHostContext hostContext)
        {
            base.Initialize(hostContext);

            var currentAssemblyLocation = System.Reflection.Assembly.GetEntryAssembly().Location;
            Trace.Info("currentAssemblyLocation: {0}", currentAssemblyLocation);

            _binPath = HostContext.GetDirectory(WellKnownDirectory.Bin);
            Trace.Info("binPath: {0}", _binPath);

            RootFolder = HostContext.GetDirectory(WellKnownDirectory.Root);
            Trace.Info("RootFolder: {0}", RootFolder);

            _configFilePath = hostContext.GetConfigFile(WellKnownConfigFile.Runner);
            Trace.Info("ConfigFilePath: {0}", _configFilePath);

            _migratedConfigFilePath = hostContext.GetConfigFile(WellKnownConfigFile.MigratedRunner);
            Trace.Info("MigratedConfigFilePath: {0}", _migratedConfigFilePath);

            _credFilePath = hostContext.GetConfigFile(WellKnownConfigFile.Credentials);
            Trace.Info("CredFilePath: {0}", _credFilePath);

            _migratedCredFilePath = hostContext.GetConfigFile(WellKnownConfigFile.MigratedCredentials);
            Trace.Info("MigratedCredFilePath: {0}", _migratedCredFilePath);

            _serviceConfigFilePath = hostContext.GetConfigFile(WellKnownConfigFile.Service);
            Trace.Info("ServiceConfigFilePath: {0}", _serviceConfigFilePath);
        }

        public string RootFolder { get; private set; }

        public bool HasCredentials()
        {
            Trace.Info("HasCredentials()");
            bool credsStored = new FileInfo(_credFilePath).Exists || new FileInfo(_migratedCredFilePath).Exists;
            Trace.Info("stored {0}", credsStored);
            return credsStored;
        }

        public bool IsConfigured()
        {
            Trace.Info("IsConfigured()");
            bool configured = new FileInfo(_configFilePath).Exists || new FileInfo(_migratedConfigFilePath).Exists;
            Trace.Info("IsConfigured: {0}", configured);
            return configured;
        }

        public bool IsServiceConfigured()
        {
            Trace.Info("IsServiceConfigured()");
            bool serviceConfigured = new FileInfo(_serviceConfigFilePath).Exists;
            Trace.Info($"IsServiceConfigured: {serviceConfigured}");
            return serviceConfigured;
        }

        public bool IsMigratedConfigured()
        {
            Trace.Info("IsMigratedConfigured()");
            bool configured = new FileInfo(_migratedConfigFilePath).Exists;
            Trace.Info("IsMigratedConfigured: {0}", configured);
            return configured;
        }

        public CredentialData GetCredentials()
        {
            if (_creds == null)
            {
                var multi = GetMultiCredentialStore(required: false);
                if (multi?.Credentials?.Count > 0)
                {
                    var settings = GetMultiSettings();
                    var firstRef = settings.Associations.FirstOrDefault()?.CredentialRef ?? multi.Credentials.Keys.First();
                    _creds = multi.Credentials[firstRef];
                }
                else
                {
                    _creds = IOUtil.LoadObject<CredentialData>(_credFilePath);
                }
            }

            return _creds;
        }

        public CredentialData GetCredential(string credentialRef)
        {
            ArgUtil.NotNullOrEmpty(credentialRef, nameof(credentialRef));
            var multi = GetMultiCredentialStore(required: true);
            if (multi.Credentials.TryGetValue(credentialRef, out var credential))
            {
                return credential;
            }

            throw new InvalidOperationException($"Credential '{credentialRef}' not found.");
        }

        public CredentialData GetMigratedCredentials()
        {
            if (_migratedCreds == null && File.Exists(_migratedCredFilePath))
            {
                _migratedCreds = IOUtil.LoadObject<CredentialData>(_migratedCredFilePath);
            }

            return _migratedCreds;
        }

        public RunnerSettings GetSettings()
        {
            if (_settings == null)
            {
                var multiSettings = GetMultiSettings();
                var association = multiSettings.Associations.FirstOrDefault();
                ArgUtil.NotNull(association, nameof(association));
                _settings = association.ToRunnerSettings(multiSettings.WorkFolder);
            }

            return _settings;
        }

        public MultiRunnerSettings GetMultiSettings()
        {
            if (_multiSettings == null)
            {
                MultiRunnerSettings configuredSettings = null;
                if (File.Exists(_configFilePath))
                {
                    string json = File.ReadAllText(_configFilePath, Encoding.UTF8);
                    Trace.Info($"Read multi-runner setting file: {json.Length} chars");
                    configuredSettings = StringUtil.ConvertFromJson<MultiRunnerSettings>(json);
                }

                ArgUtil.NotNull(configuredSettings, nameof(configuredSettings));
                configuredSettings.Associations ??= new List<RunnerAssociation>();
                if (configuredSettings.ExecutionSlots < 1)
                {
                    configuredSettings.ExecutionSlots = 1;
                }

                if (string.IsNullOrEmpty(configuredSettings.WorkFolder))
                {
                    configuredSettings.WorkFolder = Constants.Path.WorkDirectory;
                }

                _multiSettings = configuredSettings;
            }

            return _multiSettings;
        }

        public RunnerSettings GetMigratedSettings()
        {
            if (_migratedSettings == null)
            {
                RunnerSettings configuredSettings = null;
                if (File.Exists(_migratedConfigFilePath))
                {
                    string json = File.ReadAllText(_migratedConfigFilePath, Encoding.UTF8);
                    Trace.Info($"Read migrated setting file: {json.Length} chars");
                    configuredSettings = StringUtil.ConvertFromJson<RunnerSettings>(json);
                }

                ArgUtil.NotNull(configuredSettings, nameof(configuredSettings));
                _migratedSettings = configuredSettings;
            }

            return _migratedSettings;
        }

        public void SaveCredential(CredentialData credential)
        {
            SaveCredential(CreateAssociationId(GetSettings().GitHubUrl ?? GetSettings().ServerUrl, GetSettings().AgentName, GetSettings().AgentId), credential);
        }

        public void SaveCredential(string credentialRef, CredentialData credential)
        {
            ArgUtil.NotNullOrEmpty(credentialRef, nameof(credentialRef));
            ArgUtil.NotNull(credential, nameof(credential));
            Trace.Info("Saving {0} credential '{1}' @ {2}", credential.Scheme, credentialRef, _credFilePath);
            var multi = GetMultiCredentialStore(required: false) ?? new MultiCredentialStore();
            if (File.Exists(_credFilePath))
            {
                // Delete existing credential file first, since the file is hidden and not able to overwrite.
                Trace.Info("Delete exist runner credential file.");
                IOUtil.DeleteFile(_credFilePath);
            }

            multi.Credentials[credentialRef] = credential;
            IOUtil.SaveObject(multi, _credFilePath);
            _multiCreds = multi;
            _creds = credential;
            Trace.Info("Credentials Saved.");
            File.SetAttributes(_credFilePath, File.GetAttributes(_credFilePath) | FileAttributes.Hidden);
        }

        public void SaveMigratedCredential(CredentialData credential)
        {
            Trace.Info("Saving {0} migrated credential @ {1}", credential.Scheme, _migratedCredFilePath);
            if (File.Exists(_migratedCredFilePath))
            {
                // Delete existing credential file first, since the file is hidden and not able to overwrite.
                Trace.Info("Delete exist runner migrated credential file.");
                IOUtil.DeleteFile(_migratedCredFilePath);
            }

            IOUtil.SaveObject(credential, _migratedCredFilePath);
            Trace.Info("Migrated Credentials Saved.");
            File.SetAttributes(_migratedCredFilePath, File.GetAttributes(_migratedCredFilePath) | FileAttributes.Hidden);
        }

        public void SaveSettings(RunnerSettings settings)
        {
            ArgUtil.NotNull(settings, nameof(settings));
            var id = CreateAssociationId(settings.GitHubUrl ?? settings.ServerUrl, settings.AgentName, settings.AgentId);
            SaveMultiSettings(new MultiRunnerSettings
            {
                WorkFolder = settings.WorkFolder,
                ExecutionSlots = 1,
                Associations = new List<RunnerAssociation>
                {
                    RunnerAssociation.FromRunnerSettings(settings, labels: null, id),
                },
            });
        }

        public void SaveMultiSettings(MultiRunnerSettings settings)
        {
            ArgUtil.NotNull(settings, nameof(settings));
            Trace.Info("Saving multi-runner settings.");
            if (File.Exists(_configFilePath))
            {
                // Delete existing runner settings file first, since the file is hidden and not able to overwrite.
                Trace.Info("Delete exist runner settings file.");
                IOUtil.DeleteFile(_configFilePath);
            }

            IOUtil.SaveObject(settings, _configFilePath);
            _multiSettings = settings;
            _settings = settings.Associations.FirstOrDefault()?.ToRunnerSettings(settings.WorkFolder);
            Trace.Info("Settings Saved.");
            File.SetAttributes(_configFilePath, File.GetAttributes(_configFilePath) | FileAttributes.Hidden);
        }

        public void SaveMigratedSettings(RunnerSettings settings)
        {
            Trace.Info("Saving runner migrated settings");
            if (File.Exists(_migratedConfigFilePath))
            {
                // Delete existing settings file first, since the file is hidden and not able to overwrite.
                Trace.Info("Delete exist runner migrated settings file.");
                IOUtil.DeleteFile(_migratedConfigFilePath);
            }

            IOUtil.SaveObject(settings, _migratedConfigFilePath);
            Trace.Info("Migrated Settings Saved.");
            File.SetAttributes(_migratedConfigFilePath, File.GetAttributes(_migratedConfigFilePath) | FileAttributes.Hidden);
        }

        public void DeleteCredential()
        {
            IOUtil.Delete(_credFilePath, default(CancellationToken));
            IOUtil.Delete(_migratedCredFilePath, default(CancellationToken));
            _creds = null;
            _multiCreds = null;
        }

        public void DeleteCredential(string credentialRef)
        {
            ArgUtil.NotNullOrEmpty(credentialRef, nameof(credentialRef));
            var multi = GetMultiCredentialStore(required: false);
            if (multi?.Credentials?.Remove(credentialRef) == true)
            {
                if (multi.Credentials.Count == 0)
                {
                    IOUtil.Delete(_credFilePath, default(CancellationToken));
                }
                else
                {
                    if (File.Exists(_credFilePath))
                    {
                        IOUtil.DeleteFile(_credFilePath);
                    }

                    IOUtil.SaveObject(multi, _credFilePath);
                    File.SetAttributes(_credFilePath, File.GetAttributes(_credFilePath) | FileAttributes.Hidden);
                }
            }

            _creds = null;
        }

        public void DeleteMigratedCredential()
        {
            IOUtil.Delete(_migratedCredFilePath, default(CancellationToken));
        }

        public void DeleteSettings()
        {
            IOUtil.Delete(_configFilePath, default(CancellationToken));
            IOUtil.Delete(_migratedConfigFilePath, default(CancellationToken));
            _settings = null;
            _multiSettings = null;
        }

        public void DeleteMigratedSettings()
        {
            IOUtil.Delete(_migratedConfigFilePath, default(CancellationToken));
        }

        public static string CreateAssociationId(string url, string agentName, ulong agentId)
        {
            string raw = $"{url ?? string.Empty}\n{agentName ?? string.Empty}\n{agentId}";
            using SHA256 sha256 = SHA256.Create();
            byte[] data = sha256.ComputeHash(Encoding.UTF8.GetBytes(raw.ToLowerInvariant()));
            return BitConverter.ToString(data, 0, 16).Replace("-", string.Empty).ToLowerInvariant();
        }

        private MultiCredentialStore GetMultiCredentialStore(bool required)
        {
            if (_multiCreds == null && File.Exists(_credFilePath))
            {
                string json = File.ReadAllText(_credFilePath, Encoding.UTF8);
                if (!string.IsNullOrWhiteSpace(json) && json.Contains("\"credentials\"", StringComparison.OrdinalIgnoreCase))
                {
                    _multiCreds = StringUtil.ConvertFromJson<MultiCredentialStore>(json);
                    _multiCreds.Credentials ??= new Dictionary<string, CredentialData>(StringComparer.OrdinalIgnoreCase);
                }
            }

            if (required && _multiCreds == null)
            {
                throw new InvalidOperationException("Credentials not stored. Must reconfigure.");
            }

            return _multiCreds;
        }
    }
}
