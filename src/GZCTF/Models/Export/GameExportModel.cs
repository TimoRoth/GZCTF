using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FluentStorage.Blobs;
using YamlDotNet.Serialization;
using FluentStorage;
using System.Security.Cryptography;
using System.Text;

namespace GZCTF.Models.Export
{
    public class GameExportModel
    {
        public string? Title { get; set; }

        public bool? Hidden { get; set; }

        public string? Poster { get; set; }

        public string? Summary { get; set; }

        public string? Content { get; set; }

        public bool? AcceptWithoutReview { get; set; }

        public bool? WriteupRequired { get; set; }

        public string? InviteCode { get; set; }

        public HashSet<string>? Divisions { get; set; }

        public int? TeamMemberCountLimit { get; set; }

        public int? ContainerCountLimit { get; set; }

        [YamlMember(Alias = "start")]
        public DateTimeOffset? StartTimeUtc { get; set; }

        [YamlMember(Alias = "end")]
        public DateTimeOffset? EndTimeUtc { get; set; }

        public DateTimeOffset? WriteupDeadline { get; set; }

        public string? WriteupNote { get; set; }

        public long? BloodBonusValue { get; set; }

        public bool? PracticeMode { get; set; }

        public List<ChallengeExportModel>? Challenges { get; set; }

        internal static async Task<GameExportModel> FromGame(Game game, IBlobStorage blobStorage, CancellationToken token)
        {
            var res = new GameExportModel
            {
                Title = game.Title,
                Hidden = game.Hidden,
                Summary = game.Summary,
                Content = game.Content,
                AcceptWithoutReview = game.AcceptWithoutReview,
                WriteupRequired = game.WriteupRequired,
                InviteCode = game.InviteCode,
                Divisions = game.Divisions,
                TeamMemberCountLimit = game.TeamMemberCountLimit,
                ContainerCountLimit = game.ContainerCountLimit,
                StartTimeUtc = game.StartTimeUtc,
                EndTimeUtc = game.EndTimeUtc,
                WriteupDeadline = game.WriteupDeadline,
                WriteupNote = game.WriteupNote,
                BloodBonusValue = game.BloodBonusValue,
                PracticeMode = game.PracticeMode,
            };

            if (game.Challenges.Count > 0)
            {
                res.Challenges = [];
                foreach (var challenge in game.Challenges)
                {
                    res.Challenges.Add(await ChallengeExportModel.FromChallenge(challenge, blobStorage, token));
                }
            }

            if (!string.IsNullOrWhiteSpace(game.PosterHash))
            {
                var posterPath = StoragePath.Combine(PathHelper.Uploads, game.PosterHash[..2], game.PosterHash[2..4], game.PosterHash);

                using var stream = await blobStorage.OpenReadAsync(posterPath, token);
                using var base64stream = new CryptoStream(stream, new ToBase64Transform(), CryptoStreamMode.Read);
                using var reader = new StreamReader(base64stream, Encoding.UTF8);

                res.Poster = await reader.ReadToEndAsync(token);
            }

            return res;
        }

        internal Game ToGame() =>
            new Game
            {
                Title = Title ?? string.Empty,
                Hidden = Hidden ?? true,
                Summary = Summary ?? string.Empty,
                Content = Content ?? string.Empty,
                AcceptWithoutReview = AcceptWithoutReview ?? false,
                WriteupRequired = WriteupRequired ?? false,
                InviteCode = InviteCode,
                Divisions = Divisions,
                TeamMemberCountLimit = TeamMemberCountLimit ?? 0,
                ContainerCountLimit = ContainerCountLimit ?? 3,
                StartTimeUtc = StartTimeUtc?.ToUniversalTime() ?? DateTime.UtcNow,
                EndTimeUtc = EndTimeUtc?.ToUniversalTime() ?? (DateTime.UtcNow + TimeSpan.FromHours(2)),
                WriteupDeadline = WriteupDeadline?.ToUniversalTime() ?? ((EndTimeUtc?.ToUniversalTime() ?? DateTime.UtcNow) + TimeSpan.FromHours(2)),
                WriteupNote = WriteupNote ?? string.Empty,
                BloodBonusValue = BloodBonusValue ?? BloodBonus.DefaultValue,
                PracticeMode = PracticeMode ?? false,
                // Challenges and Poster need to be handled externally
            };
    }
}
