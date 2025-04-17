using System.Text;
using System.Diagnostics;
using Docker.DotNet;
using GZCTF.Models.Internal;
using GZCTF.Services.Container.Provider;
using ContainerStatus = GZCTF.Utils.ContainerStatus;

namespace GZCTF.Services.Container.Manager;

public class DockerComposeManager : IContainerManager
{
    readonly DockerClient _client;
    readonly ILogger<DockerComposeManager> _logger;
    readonly DockerMetadata _meta;
    readonly DockerManager _fallbackManager;

    public DockerComposeManager(IContainerProvider<DockerClient, DockerMetadata> provider, ILogger<DockerComposeManager> logger, ILogger<DockerManager> fallbackLogger)
    {
        _logger = logger;
        _meta = provider.GetMetadata();
        _client = provider.GetProvider();
        _fallbackManager = new DockerManager(provider, fallbackLogger);

        logger.SystemLog(StaticLocalizer[nameof(Resources.Program.ContainerManager_DockerComposeMode)], TaskStatus.Success, LogLevel.Debug);
    }


    public async Task DestroyContainerAsync(Models.Data.Container container, CancellationToken token = default)
    {
        if (!container.Image.Trim('\n', '\r').Contains('\n'))
        {
            await _fallbackManager.DestroyContainerAsync(container, token);
            return;
        }

        //TODO: Exception handling
        await LaunchHelper("docker",
            ["compose", "--file", "-", "--project-name", container.ContainerId, "--progress", "plain", "down", "--remove-orphans", "--volumes"],
            new Dictionary<string, string>
            {
                { "DOCKER_HOST", _client.Configuration.EndpointBaseUri.ToString() },
                //TODO: Pass resource limit, the token/flag variables and maybe the desired port to be exposed
            },
            container.Image);

        container.Status = ContainerStatus.Destroyed;
    }

    public async Task<Models.Data.Container?> CreateContainerAsync(ContainerConfig config, CancellationToken token = default)
    {
        // if the image is just a single line, assume it's a classic docker image and use the normal manager
        if (!config.Image.Trim('\n', '\r').Contains('\n'))
            return await _fallbackManager.CreateContainerAsync(config, token);

        string name = $"{config.TeamId}_{config.ChallengeId}_{(config.Flag ?? Guid.NewGuid().ToString("N")).ToMD5String()[..16]}";

        //TODO: sign into registries

        //TODO: Exception handling
        //TODO: Move this to a safe workdir
        await LaunchHelper("docker",
            ["compose", "--file", "-", "--project-name", name, "--progress", "plain", "up", "-d", "--wait", "--pull", "missing"],
            new Dictionary<string, string>
            {
                { "DOCKER_HOST", _client.Configuration.EndpointBaseUri.ToString() },
            },
            config.Image);

        //TODO: test if service with name "main" exists, since further info will be drawn from it. Or try to parse first via "docker compose config"

        Models.Data.Container container = new Models.Data.Container
        {
            ContainerId = name,
            Image = config.Image,
            StartedAt = DateTimeOffset.UtcNow, // Has to be UTC for Postgres
            IP = "127.0.0.42", //TODO
            Port = config.ExposedPort,
            IsProxy = !_meta.ExposePort,
            Status = ContainerStatus.Running,
        };

        container.ExpectStopAt = container.StartedAt + TimeSpan.FromHours(2);

        if (!_meta.ExposePort)
            return container;

        container.PublicPort = 1234; // TODO

        if (!string.IsNullOrEmpty(_meta.PublicEntry))
            container.PublicIP = _meta.PublicEntry;

        return container;
    }

    private async Task<string> LaunchHelper(string command, string[] arguments, Dictionary<string, string> env, string? input = null)
    {
        command = ResolvePath(command);

        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };

        foreach (string arg in arguments)
            proc.StartInfo.ArgumentList.Add(arg);

        foreach (var pair in env)
            proc.StartInfo.Environment.Add(pair.Key, pair.Value);

        proc.EnableRaisingEvents = true;

        StringBuilder resBuilder = new StringBuilder();
        DataReceivedEventHandler handler = (sender, data) => { resBuilder.Append(data.Data).Append('\n'); };
        proc.OutputDataReceived += handler;
        proc.ErrorDataReceived += handler;

        try
        {
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            using (var writer = proc.StandardInput)
                if (input != null)
                    await writer.WriteAsync(input);

            await proc.WaitForExitAsync();

            if (proc.ExitCode != 0)
                throw new LaunchException(proc.ExitCode, resBuilder.ToString());

            return resBuilder.ToString();
        }
        finally
        {
            proc.OutputDataReceived -= handler;
            proc.ErrorDataReceived -= handler;
        }
    }

    private Dictionary<string, string> _resolvedCommands = [];

    private string ResolvePath(string command)
    {
        if (_resolvedCommands.TryGetValue(command, out var res))
            return res;

        if (!Path.IsPathRooted(command) && !command.Contains(Path.DirectorySeparatorChar) && !command.Contains(Path.AltDirectorySeparatorChar) && !File.Exists(command))
        {
            string[] path = Environment.GetEnvironmentVariable("PATH")!.Split(Path.PathSeparator);
            string[] exts = Environment.GetEnvironmentVariable("PATHEXT")?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();

            foreach (string dir in path)
            {
                string fullCommand = Path.Combine(dir, command);
                if (File.Exists(fullCommand))
                {
                    _resolvedCommands.Add(command, fullCommand);
                    return fullCommand;
                }

                foreach (string ext in exts)
                {
                    string fullCommandExt = fullCommand + ext;
                    if (File.Exists(fullCommandExt))
                    {
                        _resolvedCommands.Add(command, fullCommandExt);
                        return fullCommandExt;
                    }
                }
            }
        }

        return command;
    }

    private class LaunchException : Exception
    {
        public int ExitCode { get; private set; }

        public LaunchException(int code, string message)
            : base(message)
        {
            ExitCode = code;
        }
    }
}
