using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Teammy.Application.Ai.ProjectAssistant.Dtos;

public sealed record ProjectAssistantDraftRequest(
    string Message
);

public sealed record ProjectAssistantCommitRequest(
    JsonElement ApprovedDraft
);

public sealed record ProjectAssistantDraftResponse(
    JsonElement Payload
);
