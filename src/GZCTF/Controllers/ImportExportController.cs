using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using GZCTF.Extensions;
using GZCTF.Middlewares;
using GZCTF.Models.Export;
using GZCTF.Models.Request.Edit;
using GZCTF.Models.Request.Game;
using GZCTF.Models.Request.Info;
using GZCTF.Repositories;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Cache;
using GZCTF.Services.Container.Manager;
using GZCTF.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using NSwag.Annotations;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.Converters;
using YamlDotNet.Serialization.NamingConventions;

namespace GZCTF.Controllers;

/// <summary>
/// Import/Export APIs
/// </summary>
[RequireAdmin]
[ApiController]
[Route("api")]
[Produces(MediaTypeNames.Application.Json)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class ImportExportController(
    CacheHelper cacheHelper,
    IGameChallengeRepository challengeRepository,
    IGameRepository gameRepository,
    IBlobStorage blobStorage,
    IBlobRepository blobService,
    IStringLocalizer<Program> localizer) : Controller
{
    /// <summary>
    /// Get Game YAML export
    /// </summary>
    /// <remarks>
    /// Retrieving a game requires administrator privileges
    /// </remarks>
    /// <param name="id"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game</response>
    [HttpGet("Export/Game/{id:int}")]
    [ProducesResponseType(typeof(DataExportModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportGame([FromRoute] int id, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        await gameRepository.LoadChallenges(game, token);
        foreach (var challenge in game.Challenges)
            await challengeRepository.LoadFlags(challenge, token);

        var dataObj = new
        {
            Game = await GameExportModel.FromGame(game, blobStorage, token)
        };

        var yaml = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
            .WithTypeConverter(new DateTimeOffsetConverter())
            .Build();

        return Ok(new DataExportModel { Data = yaml.Serialize(dataObj) });
    }

    /// <summary>
    /// Get Game Challenge as Data Export
    /// </summary>
    /// <remarks>
    /// Retrieving a game challenge requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="cId">Challenge ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully exported game challenge</response>
    [HttpGet("Export/Game/{id:int}/Challenge/{cId:int}")]
    [ProducesResponseType(typeof(DataExportModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportGameChallenge([FromRoute] int id, [FromRoute] int cId, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        var challenge = await challengeRepository.GetChallenge(id, cId, token);

        if (challenge is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        // Do not load flags for dynamic containers
        if (challenge.Type != ChallengeType.DynamicContainer)
            await challengeRepository.LoadFlags(challenge, token);

        var dataObj = new
        {
            Challenge = await ChallengeExportModel.FromChallenge(challenge, blobStorage, token)
        };

        var yaml = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
            .Build();

        return Ok(new DataExportModel { Data = yaml.Serialize(dataObj) });
    }

    /// <summary>
    /// Add Game
    /// </summary>
    /// <remarks>
    /// Adding a game requires administrator privileges
    /// </remarks>
    /// <param name="model"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully added game</response>
    [HttpPost("Import/Game")]
    [ProducesResponseType(typeof(GameInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ImportGame([FromBody] DataExportModel model, CancellationToken token)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .WithTypeConverter(new DateTimeConverter())
            .Build();

        Dictionary<string, GameExportModel> data;
        try
        {
            data = deserializer.Deserialize<Dictionary<string, GameExportModel>>(model.Data);
        }
        catch (YamlException e)
        {
            return UnprocessableEntity(new RequestResponse(e.Message, StatusCodes.Status422UnprocessableEntity));
        }

        GameExportModel gameModel;
        try
        {
            gameModel = data.Single(k => string.Equals(k.Key, "game", StringComparison.OrdinalIgnoreCase)).Value;
        }
        catch (InvalidOperationException)
        {
            return UnprocessableEntity(new RequestResponse("No singular game item in imported data.", StatusCodes.Status422UnprocessableEntity));
        }

        var trans = await gameRepository.BeginTransactionAsync(token);

        try
        {
            var game = await gameRepository.CreateGame(gameModel.ToGame(), token);
            if (game is null)
            {
                await trans.RollbackAsync(token);
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_CreationFailed)]));
            }

            foreach (var challengeModel in gameModel.Challenges ?? [])
            {
                var challenge = await challengeRepository.CreateChallenge(game, challengeModel.ToChallenge(), token);

                await ProcessChallengeAttachments(challengeModel, challenge);
            }

            if (!string.IsNullOrWhiteSpace(gameModel.Poster))
            {
                using var stream = new MemoryStream(Convert.FromBase64String(gameModel.Poster));
                var file = await blobService.CreateOrUpdateBlob(stream, "poster", token);

                game.PosterHash = file.Hash;
            }

            await gameRepository.SaveAsync(token);

            await trans.CommitAsync(token);

            await cacheHelper.FlushRecentGamesCache(token);

            return Ok(GameInfoModel.FromGame(game));
        }
        catch
        {
            await trans.RollbackAsync(token);
            throw;
        }
    }


    /// <summary>
    /// Import a game challenge from yaml
    /// </summary>
    /// <remarks>
    /// Adding a game challenge requires administrator privileges
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="model"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully added game challenge</response>
    /// <response code="422">Invalid YAML input</response>
    [HttpPost("Import/Game/{id:int}/Challenge")]
    [ProducesResponseType(typeof(ChallengeInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ImportGameChallenge([FromRoute] int id, [FromBody] DataExportModel model,
        CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        Dictionary<string, ChallengeExportModel> data;
        try
        {
            data = deserializer.Deserialize<Dictionary<string, ChallengeExportModel>>(model.Data);
        }
        catch (YamlException e)
        {
            return UnprocessableEntity(new RequestResponse(e.Message, StatusCodes.Status422UnprocessableEntity));
        }

        ChallengeExportModel challengeModel;
        try
        {
            challengeModel = data.Single(k => string.Equals(k.Key, "challenge", StringComparison.OrdinalIgnoreCase)).Value;
        }
        catch (InvalidOperationException)
        {
            return UnprocessableEntity(new RequestResponse("No singular challenge item in imported data.", StatusCodes.Status422UnprocessableEntity));
        }

        if (string.IsNullOrWhiteSpace(challengeModel.Title) || challengeModel.Type is null || challengeModel.Category is null)
            return UnprocessableEntity(new RequestResponse("Incomplete challenge in imported data.", StatusCodes.Status422UnprocessableEntity));

        var trans = await challengeRepository.BeginTransactionAsync(token);

        try
        {
            var challenge = await challengeRepository.CreateChallenge(game, challengeModel.ToChallenge(), token);

            await ProcessChallengeAttachments(challengeModel, challenge);

            await trans.CommitAsync(token);

            return Ok(ChallengeInfoModel.FromChallenge(challenge));
        }
        catch
        {
            await trans.RollbackAsync(token);
            throw;
        }
    }

    private async Task ProcessChallengeAttachments(ChallengeExportModel challengeModel, GameChallenge challenge, CancellationToken token = default)
    {
        if (challengeModel.Attachment is not null && challenge.Attachment?.Type != FileType.None)
        {
            var attachment = new AttachmentCreateModel { AttachmentType = challengeModel.Attachment.Type };

            if (attachment.AttachmentType == FileType.Remote)
            {
                attachment.RemoteUrl = challengeModel.Attachment.Url;
            }
            else if (attachment.AttachmentType == FileType.Local)
            {
                using var stream = new MemoryStream(Convert.FromBase64String(challengeModel.Attachment.Data ?? ""));
                var file = await blobService.CreateOrUpdateBlob(stream, challengeModel.Attachment.Name ?? "UNNAMED_IMPORTED_FILE", token);

                attachment.FileHash = file.Hash;
            }

            await challengeRepository.UpdateAttachment(challenge, attachment, token);
        }

        FlagCreateModel[] flags = new FlagCreateModel[challengeModel.Flags?.Count ?? 0];
        for (int i = 0; i < flags.Length; ++i)
        {
            var flagModel = challengeModel.Flags?[i];
            if (flagModel is null)
                continue;

            var flag = flags[i] = new FlagCreateModel { Flag = flagModel.Flag ?? "UNNAMED_IMPORTED_FLAG" };

            if (flagModel.Attachment?.Type == FileType.Remote)
            {
                flag.AttachmentType = FileType.Remote;
                flag.RemoteUrl = flagModel.Attachment.Url;
            }
            else if (flagModel.Attachment?.Type == FileType.Local)
            {
                using var stream = new MemoryStream(Convert.FromBase64String(flagModel.Attachment.Data ?? ""));
                var file = await blobService.CreateOrUpdateBlob(stream, flagModel.Attachment.Name ?? "UNNAMED_IMPORTED_FILE", token);

                flag.AttachmentType = FileType.Local;
                flag.FileHash = file.Hash;
            }
        }

        if (flags.Length > 0)
            await challengeRepository.AddFlags(challenge, flags, token);
    }
}
