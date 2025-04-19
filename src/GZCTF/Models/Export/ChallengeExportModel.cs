using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using FluentStorage;
using FluentStorage.Blobs;
using FluentStorage.Utils.Extensions;
using GZCTF.Models.Data;

namespace GZCTF.Models.Export
{
    public class ChallengeExportModel
    {
        [MinLength(1, ErrorMessageResourceName = nameof(Resources.Program.Model_TitleTooShort), ErrorMessageResourceType = typeof(Resources.Program))]
        public string Title { get; set; } = "~~~~UNSET~~~~";

        public string? Content { get; set; }

        [MaxLength(Limits.MaxFlagTemplateLength, ErrorMessageResourceName = nameof(Resources.Program.Model_FlagTooLong), ErrorMessageResourceType = typeof(Resources.Program))]
        public string? FlagTemplate { get; set; }

        public ChallengeCategory Category { get; set; }

        public ChallengeType Type { get; set; }

        public List<string>? Hints { get; set; }

        public bool? IsEnabled { get; set; }

        public string? FileName { get; set; }

        public string? ContainerImage { get; set; }

        [Range(32, 1048576, ErrorMessageResourceName = nameof(Resources.Program.Model_OutOfRange), ErrorMessageResourceType = typeof(Resources.Program))]
        public int? MemoryLimit { get; set; }

        [Range(1, 1024, ErrorMessageResourceName = nameof(Resources.Program.Model_OutOfRange), ErrorMessageResourceType = typeof(Resources.Program))]
        public int? CpuCount { get; set; }

        [Range(128, 1048576, ErrorMessageResourceName = nameof(Resources.Program.Model_OutOfRange), ErrorMessageResourceType = typeof(Resources.Program))]
        public int? StorageLimit { get; set; }

        public int? ContainerExposePort { get; set; }

        public bool? EnableTrafficCapture { get; set; }

        public bool? DisableBloodBonus { get; set; }

        public int? OriginalScore { get; set; }

        [Range(0, 1)]
        public double? MinScoreRate { get; set; }

        public double? Difficulty { get; set; }

        public AttachmentExportModel? Attachment { get; set; }

        public List<FlagExportModel>? Flags { get; set; }

        internal GameChallenge ToChallenge() =>
            new GameChallenge
            {
                Title = Title,
                Content = Content ?? String.Empty,
                Category = Category,
                Type = Type,
                FlagTemplate = FlagTemplate,
                Hints = Hints,
                IsEnabled = IsEnabled ?? false,
                FileName = FileName,
                ContainerImage = ContainerImage,
                MemoryLimit = MemoryLimit,
                CPUCount = CpuCount,
                StorageLimit = StorageLimit,
                ContainerExposePort = ContainerExposePort,
                EnableTrafficCapture = EnableTrafficCapture ?? false,
                DisableBloodBonus = DisableBloodBonus ?? false,
                OriginalScore = OriginalScore ?? 1000,
                MinScoreRate = MinScoreRate ?? 0.25,
                Difficulty = Difficulty ?? 5,
                // Attachments and Flags need external handling
            };

        internal static async Task<ChallengeExportModel> FromChallenge(GameChallenge chal, IBlobStorage storage, CancellationToken token = default)
        {
            var res = new ChallengeExportModel()
            {
                Title = chal.Title,
                Content = chal.Content,
                Category = chal.Category,
                Type = chal.Type,
                FlagTemplate = chal.FlagTemplate,
                Hints = chal.Hints ?? [],
                IsEnabled = chal.IsEnabled,
                FileName = chal.FileName,
                ContainerImage = chal.ContainerImage,
                MemoryLimit = chal.MemoryLimit,
                CpuCount = chal.CPUCount,
                StorageLimit = chal.StorageLimit,
                ContainerExposePort = chal.ContainerExposePort,
                EnableTrafficCapture = chal.EnableTrafficCapture,
                DisableBloodBonus = chal.DisableBloodBonus,
                OriginalScore = chal.OriginalScore,
                MinScoreRate = chal.MinScoreRate,
                Difficulty = chal.Difficulty,
            };

            if (chal.Attachment is not null)
                res.Attachment = await AttachmentExportModel.FromAttachment(chal.Attachment, storage, token);

            if (chal.Flags.Count > 0)
            {
                res.Flags = new List<FlagExportModel>();
                foreach (var flag in chal.Flags)
                {
                    res.Flags.Add(new FlagExportModel {
                        Flag = flag.Flag,
                        Attachment = flag.Attachment is null
                            ? null
                            : await AttachmentExportModel.FromAttachment(flag.Attachment, storage, token)
                    });
                }
            }

            return res;
        }
    }

    public class AttachmentExportModel
    {
        public FileType Type { get; set; } = FileType.None;

        public string? Url { get; set; }

        public string? Data { get; set; }

        public string? Name { get; set; }

        internal static async Task<AttachmentExportModel> FromAttachment(Attachment attachment, IBlobStorage storage, CancellationToken token = default)
        {
            var res = new AttachmentExportModel();

            if (attachment.Type == FileType.Remote && attachment.Url is not null)
            {
                res.Type = FileType.Remote;
                res.Url = attachment.Url;
            }
            else if (attachment.Type == FileType.Local && attachment.LocalFile is not null)
            {
                res.Type = FileType.Local;

                var path = StoragePath.Combine(PathHelper.Uploads, attachment.LocalFile.Location, attachment.LocalFile.Hash);
                using var stream = await storage.OpenReadAsync(path, token);
                using var base64stream = new CryptoStream(stream, new ToBase64Transform(), CryptoStreamMode.Read);
                using var reader = new StreamReader(base64stream, Encoding.UTF8);

                res.Data = await reader.ReadToEndAsync(token);
                res.Name = attachment.LocalFile.Name;
            }

            return res;
        }
    }

    public class FlagExportModel
    {
        public string? Flag { get; set; }

        public AttachmentExportModel? Attachment { get; set; }
    }
}
