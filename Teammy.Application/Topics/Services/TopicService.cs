using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Teammy.Application.Common.Interfaces;
using Teammy.Application.Topics.Dtos;

namespace Teammy.Application.Topics.Services
{
    public sealed class TopicsService
    {
        private readonly ITopicReadOnlyQueries _read;
        private readonly ITopicWriteRepository _write;
        private readonly ITopicImportService _excel;
        private readonly IMentorLookupService _mentorLookup;
        private readonly ITopicMentorService _topicMentorService;

        public TopicsService(
            ITopicReadOnlyQueries read,
            ITopicWriteRepository write,
            ITopicImportService excel,
            IMentorLookupService mentorLookup,
            ITopicMentorService topicMentorService)
        {
            _read = read;
            _write = write;
            _excel = excel;
            _mentorLookup = mentorLookup;
            _topicMentorService = topicMentorService;
        }

        public Task<IReadOnlyList<TopicListItemDto>> GetAllAsync(
            string? q,
            Guid? semesterId,
            string? status,
            Guid? majorId,
            Guid? ownerUserId,
            CancellationToken ct)
            => _read.GetAllAsync(q, semesterId, status, majorId, ownerUserId, ct);

        public Task<TopicDetailDto?> GetByIdAsync(Guid id, CancellationToken ct)
            => _read.GetByIdAsync(id, ct);

        public async Task<Guid> CreateAsync(Guid currentUserId, CreateTopicRequest req, CancellationToken ct)
        {
            if (req.MentorEmails == null || req.MentorEmails.Count == 0)
                throw new InvalidOperationException("At least one mentor email is required.");

            var topicId = await _write.CreateAsync(req, currentUserId, ct);

            var mentorIds = new List<Guid>();
            foreach (var email in req.MentorEmails.Where(e => !string.IsNullOrWhiteSpace(e)))
            {
                var mentorId = await _mentorLookup.GetMentorIdByEmailAsync(email, ct);
                mentorIds.Add(mentorId);
            }

            await _topicMentorService.ReplaceMentorsAsync(topicId, mentorIds, ct);

            return topicId;
        }

        public async Task UpdateAsync(Guid id, UpdateTopicRequest req, CancellationToken ct)
        {
            await _write.UpdateAsync(id, req, ct);

            if (req.MentorEmails != null)
            {
                var mentorIds = new List<Guid>();
                foreach (var email in req.MentorEmails.Where(e => !string.IsNullOrWhiteSpace(e)))
                {
                    var mentorId = await _mentorLookup.GetMentorIdByEmailAsync(email, ct);
                    mentorIds.Add(mentorId);
                }

                await _topicMentorService.ReplaceMentorsAsync(id, mentorIds, ct);
            }
        }

        public Task DeleteAsync(Guid id, CancellationToken ct)
            => _write.DeleteAsync(id, ct);

        public Task<byte[]> BuildTemplateAsync(CancellationToken ct)
            => _excel.BuildTemplateAsync(ct);

        public Task<TopicImportResult> ImportAsync(Guid currentUserId, Stream s, CancellationToken ct)
            => _excel.ImportAsync(s, currentUserId, ct);

        public Task<TopicImportValidationResult> ValidateImportAsync(
            TopicImportValidationRequest request,
            CancellationToken ct)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            var rows = request.Rows ?? Array.Empty<TopicImportPayloadRow>();
            return _excel.ValidateRowsAsync(rows, ct);
        }
    }
}
