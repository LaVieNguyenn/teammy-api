using System;
using System.Collections.Generic;

namespace Teammy.Application.Topics.Dtos
{
    public sealed record TopicMentorDto(
        Guid   MentorId,
        string MentorName,
        string MentorEmail
    );

    public sealed record TopicRegistrationFileDto(
        string? FileUrl,
        string? FileName,
        string? ContentType,
        long?   FileSize
    );

    // Dùng cho GetAll
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

        // Nhiều mentor
        IReadOnlyList<TopicMentorDto> Mentors,

        IReadOnlyList<string> Skills,

        DateTime CreatedAt
    );

    // Dùng cho GetById
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

        // Nhiều mentor
        IReadOnlyList<TopicMentorDto> Mentors,

        IReadOnlyList<string> Skills,

        DateTime CreatedAt
    );

    public sealed record CreateTopicRequest(
        Guid   SemesterId,
        Guid?  MajorId,
        string Title,
        string? Description,
        string? Source,
        string Status,               // open/closed/archived
        List<string> MentorEmails,
        List<string>? Skills    
    );

    // Request update topic
    public sealed record UpdateTopicRequest(
        Guid?  MajorId,
        string Title,
        string? Description,
        string? Source,
        string Status,               // open/closed/archived
        List<string> MentorEmails,
        List<string>? Skills    
    );

    public sealed record TopicImportResult(
        int TotalRows,
        int Created,
        int Updated,
        int Skipped,
        IReadOnlyList<string> Errors
    );
}
