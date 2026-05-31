using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GitHub.Runner.Common;
using GitHub.Runner.Sdk;

namespace GitHub.Runner.Listener
{
    [ServiceLocator(Default = typeof(RunnerHookProvider))]
    public interface IRunnerHookProvider : IRunnerService
    {
        Task RunHook(string displayName, string path, CancellationToken cancellationToken);
    }

    public sealed class RunnerHookProvider : RunnerService, IRunnerHookProvider
    {
        private ITerminal _term;

        public override void Initialize(IHostContext hostContext)
        {
            base.Initialize(hostContext);
            _term = HostContext.GetService<ITerminal>();
        }

        public async Task RunHook(string displayName, string path, CancellationToken cancellationToken)
        {
            _term.WriteLine($"A {displayName} has been configured by the self-hosted runner administrator");

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("File doesn't exist");
            }

            var scriptDirectory = Path.GetDirectoryName(path);
            var shell = HostContext.GetDefaultShellForScript(path, prependPath: null);
            var command = GetScriptCommand(path, shell);

            using (var processInvoker = HostContext.CreateService<IProcessInvoker>())
            {
                processInvoker.OutputDataReceived += OnOutputDataReceived;
                processInvoker.ErrorDataReceived += OnErrorDataReceived;

                int exitCode = await processInvoker.ExecuteAsync(
                    workingDirectory: scriptDirectory,
                    fileName: command.FileName,
                    arguments: command.Arguments,
                    environment: new Dictionary<string, string>(),
                    requireExitCodeZero: false,
                    outputEncoding: null,
                    killProcessOnCancel: true,
                    cancellationToken: cancellationToken);

                if (exitCode != 0)
                {
                    throw new Exception($"Process completed with exit code {exitCode}.");
                }
            }
        }

        private void OnOutputDataReceived(object sender, ProcessDataReceivedEventArgs stdout)
        {
            if (!string.IsNullOrEmpty(stdout.Data))
            {
                _term.WriteLine(stdout.Data);
            }
        }

        private void OnErrorDataReceived(object sender, ProcessDataReceivedEventArgs stderr)
        {
            if (!string.IsNullOrEmpty(stderr.Data))
            {
                _term.WriteError(stderr.Data);
            }
        }

        private static (string FileName, string Arguments) GetScriptCommand(string path, string shell)
        {
            var scriptPath = path.Replace("\"", "\\\"");
            if (shell.Contains("{0}"))
            {
                var shellParts = shell.Split(" ", 2);
                if (shellParts.Length != 2)
                {
                    throw new ArgumentException($"Invalid shell option. Shell must be a valid built-in (bash, sh, powershell, pwsh) or a format string containing '{{0}}'");
                }

                return (shellParts[0], string.Format(shellParts[1], scriptPath));
            }

            return shell switch
            {
                "bash" => (shell, $"-e {scriptPath}"),
                "sh" => (shell, $"-e {scriptPath}"),
                "pwsh" => (shell, $"-command \". '{scriptPath}'\""),
                "powershell" => (shell, $"-command \". '{scriptPath}'\""),
                _ => throw new ArgumentException($"Invalid shell option. Shell must be a valid built-in (bash, sh, powershell, pwsh) or a format string containing '{{0}}'")
            };
        }
    }
}
