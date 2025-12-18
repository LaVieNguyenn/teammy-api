using Microsoft.EntityFrameworkCore;
using Npgsql;
using Teammy.Application.Chat.Dtos;
using Teammy.Application.Common.Interfaces;
using Teammy.Infrastructure.Persistence;
using Teammy.Infrastructure.Persistence.Models;

namespace Teammy.Infrastructure.Persistence.Repositories;

public sealed class ChatRepository(AppDbContext db) : IChatRepository
{
    public async Task<Guid> EnsureGroupSessionAsync(Guid groupId, CancellationToken ct)
    {
        var existing = await db.chat_sessions.AsNoTracking()
            .Where(s => s.group_id == groupId)
            .Select(s => (Guid?)s.chat_session_id)
            .FirstOrDefaultAsync(ct);
        if (existing.HasValue) return existing.Value;

        var session = new chat_session
        {
            chat_session_id = Guid.NewGuid(),
            type = "group",
            group_id = groupId,
            members = 0,
            created_at = DateTime.UtcNow,
            updated_at = DateTime.UtcNow,
            last_message = null
        };
        db.chat_sessions.Add(session);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            var fallback = await db.chat_sessions.AsNoTracking()
                .Where(s => s.group_id == groupId)
                .Select(s => (Guid?)s.chat_session_id)
                .FirstOrDefaultAsync(ct);
            if (fallback.HasValue) return fallback.Value;
            throw;
        }
        return session.chat_session_id;
    }

    public async Task<Guid> EnsureDirectSessionAsync(Guid userAId, Guid userBId, CancellationToken ct)
    {
        Guid smaller = userAId.CompareTo(userBId) <= 0 ? userAId : userBId;
        Guid larger = userAId.CompareTo(userBId) <= 0 ? userBId : userAId;

        var existing = await db.chat_sessions.AsNoTracking()
            .Where(s => s.type == "dm" && s.participant_a == smaller && s.participant_b == larger)
            .Select(s => (Guid?)s.chat_session_id)
            .FirstOrDefaultAsync(ct);
        if (existing.HasValue) return existing.Value;

        var session = new chat_session
        {
            chat_session_id = Guid.NewGuid(),
            type = "dm",
            group_id = null,
            participant_a = smaller,
            participant_b = larger,
            members = 2,
            created_at = DateTime.UtcNow,
            updated_at = DateTime.UtcNow,
            last_message = null
        };
        db.chat_sessions.Add(session);
        db.chat_session_participants.Add(new chat_session_participant { chat_session_id = session.chat_session_id, user_id = smaller, joined_at = DateTime.UtcNow });
        db.chat_session_participants.Add(new chat_session_participant { chat_session_id = session.chat_session_id, user_id = larger, joined_at = DateTime.UtcNow });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            var fallback = await db.chat_sessions.AsNoTracking()
                .Where(s => s.type == "dm" && s.participant_a == smaller && s.participant_b == larger)
                .Select(s => (Guid?)s.chat_session_id)
                .FirstOrDefaultAsync(ct);
            if (fallback.HasValue) return fallback.Value;
            throw;
        }
        return session.chat_session_id;
    }

    public async Task<IReadOnlyList<ChatMessageDto>> ListMessagesAsync(Guid chatSessionId, int limit, int offset, CancellationToken ct)
    {
        var q =
            from m in db.messages.AsNoTracking()
            join u in db.users.AsNoTracking() on m.sender_id equals u.user_id
            where m.chat_session_id == chatSessionId
            orderby m.created_at descending
            select new ChatMessageDto(
                m.message_id,
                u.user_id,
                u.display_name ?? string.Empty,
                u.email ?? string.Empty,
                u.avatar_url,
                m.type,
                m.is_deleted ? "[deleted]" : m.content,
                m.created_at,
                m.is_pinned,
                m.pinned_by,
                m.pinned_at,
                m.is_deleted,
                m.deleted_by,
                m.deleted_at);

        var list = await q.Skip(offset).Take(limit).ToListAsync(ct);
        list.Reverse();
        return list;
    }

    public async Task<ChatMessageDto> AddMessageAsync(Guid chatSessionId, Guid senderUserId, string content, string? type, CancellationToken ct)
    {
        var msg = new message
        {
            message_id = Guid.NewGuid(),
            chat_session_id = chatSessionId,
            sender_id = senderUserId,
            type = type,
            content = content,
            created_at = DateTime.UtcNow,
            updated_at = DateTime.UtcNow
        };
        db.messages.Add(msg);

        var session = await db.chat_sessions.FirstOrDefaultAsync(s => s.chat_session_id == chatSessionId, ct);
        if (session is not null)
        {
            session.last_message = content;
            session.updated_at = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        return await ProjectMessageAsync(msg.message_id, ct);
    }

    public async Task UpdateMembersCountAsync(Guid chatSessionId, int members, CancellationToken ct)
    {
        var session = await db.chat_sessions.FirstOrDefaultAsync(s => s.chat_session_id == chatSessionId, ct);
        if (session is null) return;
        session.members = members;
        session.updated_at = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ConversationSummaryDto>> ListConversationsAsync(Guid userId, CancellationToken ct)
    {
        var memberGroups =
            from gm in db.group_members.AsNoTracking()
            where gm.user_id == userId && (gm.status == "member" || gm.status == "leader")
            join g in db.groups.AsNoTracking() on gm.group_id equals g.group_id
            join s in db.chat_sessions.AsNoTracking() on g.group_id equals s.group_id
            select new
            {
                SessionId = s.chat_session_id,
                Type = "group",
                GroupId = (Guid?)g.group_id,
                GroupName = g.name,
                PartnerId = (Guid?)null,
                PartnerName = (string?)null,
                PartnerAvatar = (string?)null,
                s.last_message,
                s.updated_at
            };

        var mentorGroups =
            from g in db.groups.AsNoTracking()
            where g.mentor_id == userId
            join s in db.chat_sessions.AsNoTracking() on g.group_id equals s.group_id
            select new
            {
                SessionId = s.chat_session_id,
                Type = "group",
                GroupId = (Guid?)g.group_id,
                GroupName = g.name,
                PartnerId = (Guid?)null,
                PartnerName = (string?)null,
                PartnerAvatar = (string?)null,
                s.last_message,
                s.updated_at
            };

        var groupRows = await memberGroups
            .Concat(mentorGroups)
            .ToListAsync(ct);

        var distinctGroupRows = groupRows
            .GroupBy(x => x.SessionId)
            .Select(g => g.OrderByDescending(x => x.updated_at).First())
            .ToList();

        var dmRows = await (
            from s in db.chat_sessions.AsNoTracking()
            where s.type == "dm"
            join me in db.chat_session_participants.AsNoTracking() on s.chat_session_id equals me.chat_session_id
            where me.user_id == userId
            join other in db.chat_session_participants.AsNoTracking() on s.chat_session_id equals other.chat_session_id
            where other.user_id != userId
            join u in db.users.AsNoTracking() on other.user_id equals u.user_id
            select new
            {
                SessionId = s.chat_session_id,
                Type = "dm",
                GroupId = (Guid?)null,
                GroupName = (string?)null,
                PartnerId = (Guid?)u.user_id,
                PartnerName = u.display_name,
                PartnerAvatar = u.avatar_url,
                s.last_message,
                s.updated_at
            }
        ).ToListAsync(ct);

        var combined = distinctGroupRows
            .Concat(dmRows)
            .OrderByDescending(x => x.updated_at)
            .Select(x => new ConversationSummaryDto(
                x.SessionId,
                x.Type,
                x.GroupId,
                x.GroupName,
                x.PartnerId,
                x.PartnerName,
                x.PartnerAvatar,
                x.last_message,
                x.updated_at))
            .ToList();

        return combined;
    }

    public Task<(string Type, Guid? GroupId)?> GetSessionInfoAsync(Guid chatSessionId, CancellationToken ct)
        => db.chat_sessions.AsNoTracking()
            .Where(s => s.chat_session_id == chatSessionId)
            .Select(s => new ValueTuple<string, Guid?>(s.type, s.group_id))
            .FirstOrDefaultAsync(ct)
            .ContinueWith(t => t.Result == default ? (ValueTuple<string, Guid?>?)null : t.Result, ct);

    public Task<bool> IsParticipantAsync(Guid chatSessionId, Guid userId, CancellationToken ct)
        => db.chat_session_participants.AsNoTracking()
            .AnyAsync(p => p.chat_session_id == chatSessionId && p.user_id == userId, ct);

    public async Task<(Guid ChatSessionId, Guid SenderId, bool IsDeleted)?> GetMessageMetaAsync(Guid messageId, CancellationToken ct)
    {
        var meta = await db.messages.AsNoTracking()
            .Where(m => m.message_id == messageId)
            .Select(m => new { m.chat_session_id, m.sender_id, m.is_deleted })
            .FirstOrDefaultAsync(ct);

        return meta is null ? null : (meta.chat_session_id, meta.sender_id, meta.is_deleted);
    }

    public async Task<ChatMessageDto> SetMessagePinAsync(Guid chatSessionId, Guid messageId, Guid userId, bool pin, CancellationToken ct)
    {
        var entity = await db.messages.FirstOrDefaultAsync(m => m.message_id == messageId && m.chat_session_id == chatSessionId, ct)
            ?? throw new KeyNotFoundException("Message not found");

        entity.is_pinned = pin;
        entity.pinned_by = pin ? userId : null;
        entity.pinned_at = pin ? DateTime.UtcNow : null;
        entity.updated_at = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await ProjectMessageAsync(messageId, ct);
    }

    public async Task<ChatMessageDto> MarkMessageDeletedAsync(Guid chatSessionId, Guid messageId, Guid userId, CancellationToken ct)
    {
        var entity = await db.messages.FirstOrDefaultAsync(m => m.message_id == messageId && m.chat_session_id == chatSessionId, ct)
            ?? throw new KeyNotFoundException("Message not found");
        if (entity.is_deleted) return await ProjectMessageAsync(messageId, ct);

        entity.is_deleted = true;
        entity.deleted_by = userId;
        entity.deleted_at = DateTime.UtcNow;
        entity.updated_at = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await ProjectMessageAsync(messageId, ct);
    }

    private Task<ChatMessageDto> ProjectMessageAsync(Guid messageId, CancellationToken ct)
    {
        return (
            from m in db.messages.AsNoTracking()
            join u in db.users.AsNoTracking() on m.sender_id equals u.user_id
            where m.message_id == messageId
            select new ChatMessageDto(
                m.message_id,
                u.user_id,
                u.display_name ?? string.Empty,
                u.email ?? string.Empty,
                u.avatar_url,
                m.type,
                m.is_deleted ? "[deleted]" : m.content,
                m.created_at,
                m.is_pinned,
                m.pinned_by,
                m.pinned_at,
                m.is_deleted,
                m.deleted_by,
                m.deleted_at)
        ).FirstAsync(ct);
    }
}
