using System;
using System.Collections.Generic;

namespace Teammy.Application.Topics.Dtos
{
    public sealed record TopicMentorDto(
        Guid   MentorId,
        string MentorName,
        string MentorEmail
    );

    public sealed record TopicGroupUsageDto(
        Guid GroupId,
        string GroupName,
        string Status
    );

    public sealed record TopicRegistrationFileDto(
        string? FileUrl,
        string? FileName,
        string? ContentType,
        long?   FileSize
    );

    // Dạng cho GetAll
    public sealed record TopicListItemDto(
        Guid   TopicId,

        // Semester
        Guid   SemesterId,
        string SemesterSeason,
        int?   SemesterYear,

        // Major
        Guid?  MajorId,
        string? MajorName,

        // Topic
        string Title,
        string? Description,
        string? Source,
        TopicRegistrationFileDto? RegistrationFile,
        string Status,       // 'open' | 'closed' | 'archived'

        // Created by
        Guid   CreatedById,
        string CreatedByName,
        string CreatedByEmail,

        // Mentors
        IReadOnlyList<TopicMentorDto> Mentors,

        // Groups currently linked to this topic
        IReadOnlyList<TopicGroupUsageDto> Groups,

        IReadOnlyList<string> Skills,

        DateTime CreatedAt
    );

    // Dạng cho GetById
    public sealed record TopicDetailDto(
        Guid   TopicId,

        // Semester
        Guid   SemesterId,
        string SemesterSeason,
        int?   SemesterYear,

        // Major
        Guid?  MajorId,
        string? MajorName,

        // Topic
        string Title,
        string? Description,
        string? Source,
        TopicRegistrationFileDto? RegistrationFile,
        string Status,

        // Created by
        Guid   CreatedById,
        string CreatedByName,
        string CreatedByEmail,

        // Mentors
        IReadOnlyList<TopicMentorDto> Mentors,

        // Groups currently linked to this topic
        IReadOnlyList<TopicGroupUsageDto> Groups,

        IReadOnlyList<string> Skills,

        DateTime CreatedAt
    );

    public sealed record CreateTopicRequest(
        Guid   SemesterId,
        Guid?  MajorId,
        string Title,
        string? Description,
        string Status,               // open/closed/archived
        List<string> MentorEmails
    );

    // Request update topic
    public sealed record UpdateTopicRequest(
        Guid?  SemesterId,
        Guid?  MajorId,
        string Title,
        string? Description,
        string Status,               // open/closed/archived
        List<string> MentorEmails
    );

    public sealed record TopicImportResult(
        int TotalRows,
        int Created,
        int Updated,
        int Skipped,
        IReadOnlyList<string> Errors
    );
}
