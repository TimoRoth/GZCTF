using System.Text;
using System.Diagnostics;
using Docker.DotNet;
using GZCTF.Models.Internal;
using GZCTF.Services.Container.Provider;
using ContainerStatus = GZCTF.Utils.ContainerStatus;

namespace GZCTF.Services.Container.Manager;

public class DockerComposeManager : IContainerManager
{
    readonly ILogger<DockerComposeManager> _logger;
    readonly DockerMetadata _meta;
    readonly DockerManager _fallbackManager;

    public DockerComposeManager(IContainerProvider<DockerClient, DockerMetadata> provider, ILogger<DockerComposeManager> logger, ILogger<DockerManager> fallbackLogger)
    {
        _logger = logger;
        _meta = provider.GetMetadata();
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

        container.Status = ContainerStatus.Destroyed;
    }

    public async Task<Models.Data.Container?> CreateContainerAsync(ContainerConfig config, CancellationToken token = default)
    {
        // if the image is just a single line, assume it's a classic docker image and use the normal manager
        if (!config.Image.Trim('\n', '\r').Contains('\n'))
            return await _fallbackManager.CreateContainerAsync(config, token);


        Models.Data.Container res = new Models.Data.Container();
        return res;
    }
}
