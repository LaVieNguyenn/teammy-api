// Program.cs (.NET 8 Minimal API)
// Teammy.AiGateway - Local-first AI Gateway
// Ports:
// - LLM (llama.cpp OpenAI-compatible):     http://127.0.0.1:8080/v1/chat/completions
// - Embeddings (llama.cpp embeddings):     http://127.0.0.1:8081/v1/embeddings
// - Rerank (python FastAPI cross-encoder): http://127.0.0.1:8090/rerank
// - SQLite + sqlite-vec (vec0)

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

// ======================================================================
// CONFIG (hard-coded as you requested)
// ======================================================================

// AI services
var llmBase = "http://127.0.0.1:8080";
var embedBase = "http://127.0.0.1:8081";
var rerankBase = "http://127.0.0.1:8090";

// Security
var apiKey = "THIS_IS_THE_STRONGEST_API_KEY_EVER_ON_THIS_WORLD_123_321_203";

// SQLite + sqlite-vec
var dbPath = @"C:\Users\PhiHung\Teammy.AiGateway\data\teammy_ai.db";
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

// ABSOLUTE PATH (your request)
var vecExtPath = @"C:\Users\PhiHung\Teammy.AiGateway\sqlite_ext\vec0.dll";  // vec0.dll path

// Embedding dimension
var embedDim = 768;

// Limits
const int MAX_POOL = 40;                // accept max candidates per rerank call
const int MAX_SUGGESTIONS = 8;          // return max 8
const int RERANK_TEXT_MAX = 12000;      // per-candidate text sent to python reranker
const int REASON_SNIPPET_MAX = 6000;    // snippet for LLM micro-summary

// Post generation limits
const int POST_MAX_WORDS_DEFAULT = 120;
const int POST_MAX_WORDS_MIN = 60;
const int POST_MAX_WORDS_MAX = 220;

// Score thresholds
const double TOPIC_MIN_SCORE = 50.0;
const double POST_MIN_SCORE = 50.0;

// Topic absolute calibration (stable across calls)
const double TOPIC_PIVOT = -7.0;
const double TOPIC_SCALE = 1.0;

// Baseline mix (keep mostly AI)
const double WEIGHT_TOPIC_CE = 0.85;
const double WEIGHT_TOPIC_BASE = 0.15;
const double WEIGHT_POST_CE = 0.90;
const double WEIGHT_POST_BASE = 0.10;

// LLM
var temperature = 0.25;

// Debug
var enableDebug = true;
var last = new LastDebug();

// ======================================================================
// Concurrency gates (avoid 500 when 3-4 users hit concurrently)
// - llama.cpp chat should be queued
// - embeddings/rerank can be small parallel
// - sqlite writes must be single-writer
// ======================================================================
var llmGate = new SemaphoreSlim(1, 1);
var embedGate = new SemaphoreSlim(2, 2);
var rerankGate = new SemaphoreSlim(2, 2);
var dbWriteGate = new SemaphoreSlim(1, 1);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

builder.Services.AddHttpClient("llm", c =>
{
    c.BaseAddress = new Uri(llmBase);
    c.Timeout = TimeSpan.FromSeconds(45);
});
builder.Services.AddHttpClient("embed", c =>
{
    c.BaseAddress = new Uri(embedBase);
    c.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient("rerank", c =>
{
    c.BaseAddress = new Uri(rerankBase);
    c.Timeout = TimeSpan.FromSeconds(30);
});

var app = builder.Build();

// ======================================================================
// Global exception shielding (avoid raw 500)
// ======================================================================
app.Use(async (ctx, next) =>
{
    try
    {
        await next();
    }
    catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
    {
        if (!ctx.Response.HasStarted) ctx.Response.StatusCode = 499;
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Unhandled exception");
        if (!ctx.Response.HasStarted)
        {
            ctx.Response.StatusCode = 503;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                ok = false,
                error = "gateway_unhandled_exception",
                detail = ex.Message
            }, JsonOpts()));
        }
    }
});

bool Authorized(HttpRequest req)
{
    if (!req.Headers.TryGetValue("Authorization", out var auth)) return false;
    var s = auth.ToString();
    return s.StartsWith("Bearer ", StringComparison.Ordinal) && s["Bearer ".Length..] == apiKey;
}

// ======================================================================
// DB init
// ======================================================================
try
{
    InitDb(dbPath, vecExtPath, embedDim);
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Failed to init DB / load vec0 extension. Check vecExtPath.");
}

// ======================================================================
// Health
// ======================================================================
app.MapGet("/health/live", () => Results.Ok(new { ok = true, service = "aiGateway", timeUtc = DateTime.UtcNow }));

app.MapGet("/health", async (IHttpClientFactory hf, CancellationToken ct) =>
{
    var llm = hf.CreateClient("llm");
    var embed = hf.CreateClient("embed");
    var rr = hf.CreateClient("rerank");

    var checks = new List<object>
    {
        await CheckHttpAsync("llm", llm, "/v1/models", ct),
        await CheckEmbeddingsAsync(embed, ct),
        await CheckRerankAsync(rr, ct),
        CheckDb(dbPath, vecExtPath)
    };

    var allOk = checks.All(x => GetBoolProp(x, "ok"));
    return Results.Json(new { ok = allOk, timeUtc = DateTime.UtcNow, checks }, statusCode: allOk ? 200 : 503);
});

app.MapGet("/debug/last-rerank", (HttpRequest req) =>
{
    if (!Authorized(req)) return Results.Unauthorized();
    if (!enableDebug) return Results.NotFound();

    return Results.Ok(new
    {
        last.AtUtc,
        last.Mode,
        last.CandidatesCount,
        last.SeedsCount,
        last.TopN,
        last.MinScore,
        last.QueryTextPreview,
        last.LogitsPreview,
        last.CeScoresPreview,
        last.FinalScoresPreview,
        last.RequestPreview,
        last.SystemPreview,
        last.UserPreview,
        last.RawResponsePreview
    });
});

// ======================================================================
// Vector index APIs
// ======================================================================

app.MapPost("/index/upsert", async (HttpRequest req, IHttpClientFactory hf) =>
{
    if (!Authorized(req)) return Results.Unauthorized();

    var body = await JsonSerializer.DeserializeAsync<UpsertRequest>(req.Body, JsonOpts(), req.HttpContext.RequestAborted);
    if (body is null || string.IsNullOrWhiteSpace(body.Text) || string.IsNullOrWhiteSpace(body.Type))
        return Results.BadRequest(new { error = "Missing text/type" });

    var embed = hf.CreateClient("embed");
    var emb = await LlamaEmbedSafeAsync(embed, Clip(body.Text!, 9000), embedGate, req.HttpContext.RequestAborted);
    if (!emb.ok)
        return Results.Json(new { ok = false, error = "embed_failed", detail = emb.error, status = emb.status }, statusCode: 503);

    var pointId = body.PointId ?? Guid.NewGuid().ToString("N");

    await dbWriteGate.WaitAsync(req.HttpContext.RequestAborted);
    try
    {
        using var conn = OpenDbSafe(dbPath, vecExtPath);
        using var tx = conn.BeginTransaction();

        // items table => UPSERT OK
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
            insert into items(point_id, type, entity_id, title, semester_id, major_id, text)
            values ($pid, $type, $eid, $title, $sid, $mid, $text)
            on conflict(point_id) do update set
              type=excluded.type,
              entity_id=excluded.entity_id,
              title=excluded.title,
              semester_id=excluded.semester_id,
              major_id=excluded.major_id,
              text=excluded.text;
            """;
            cmd.Parameters.AddWithValue("$pid", pointId);
            cmd.Parameters.AddWithValue("$type", body.Type);
            cmd.Parameters.AddWithValue("$eid", (object?)body.EntityId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$title", (object?)body.Title ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$sid", (object?)body.SemesterId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$mid", (object?)body.MajorId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$text", body.Text);
            await cmd.ExecuteNonQueryAsync(req.HttpContext.RequestAborted);
        }

        // vec0 virtual table => NO UPSERT. Delete + Insert.
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "delete from item_vec where point_id = $pid;";
            cmd.Parameters.AddWithValue("$pid", pointId);
            await cmd.ExecuteNonQueryAsync(req.HttpContext.RequestAborted);
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "insert into item_vec(point_id, embedding) values ($pid, $vec);";
            cmd.Parameters.AddWithValue("$pid", pointId);
            cmd.Parameters.AddWithValue("$vec", JsonSerializer.Serialize(emb.vector!, JsonOpts()));
            await cmd.ExecuteNonQueryAsync(req.HttpContext.RequestAborted);
        }

        tx.Commit();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Upsert failed");
        return Results.Json(new { ok = false, error = "db_upsert_failed", detail = ex.Message }, statusCode: 503);
    }
    finally
    {
        dbWriteGate.Release();
    }

    return Results.Ok(new { ok = true, pointId });
});

app.MapPost("/index/delete", async (HttpRequest req) =>
{
    if (!Authorized(req)) return Results.Unauthorized();

    var raw = await ReadBodyAsync(req);
    using var doc = JsonDocument.Parse(raw);
    var root = doc.RootElement;

    var pointId = GetStringAny(root, "pointId", "PointId");
    if (string.IsNullOrWhiteSpace(pointId))
        return Results.BadRequest(new { error = "Missing pointId" });

    await dbWriteGate.WaitAsync(req.HttpContext.RequestAborted);
    try
    {
        using var conn = OpenDbSafe(dbPath, vecExtPath);
        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "delete from item_vec where point_id = $pid;";
            cmd.Parameters.AddWithValue("$pid", pointId);
            await cmd.ExecuteNonQueryAsync(req.HttpContext.RequestAborted);
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "delete from items where point_id = $pid;";
            cmd.Parameters.AddWithValue("$pid", pointId);
            await cmd.ExecuteNonQueryAsync(req.HttpContext.RequestAborted);
        }

        tx.Commit();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Delete failed");
        return Results.Json(new { ok = false, error = "db_delete_failed", detail = ex.Message }, statusCode: 503);
    }
    finally
    {
        dbWriteGate.Release();
    }

    return Results.Ok(new { ok = true, pointId });
});

app.MapPost("/search", async (HttpRequest req, IHttpClientFactory hf) =>
{
    if (!Authorized(req)) return Results.Unauthorized();

    var body = await JsonSerializer.DeserializeAsync<SearchRequest>(req.Body, JsonOpts(), req.HttpContext.RequestAborted);
    if (body is null || string.IsNullOrWhiteSpace(body.QueryText))
        return Results.BadRequest(new { error = "Missing queryText" });

    var embed = hf.CreateClient("embed");
    var emb = await LlamaEmbedSafeAsync(embed, Clip(body.QueryText!, 9000), embedGate, req.HttpContext.RequestAborted);
    if (!emb.ok)
        return Results.Json(new { ok = false, error = "embed_failed", detail = emb.error, status = emb.status }, statusCode: 503);

    var limit = body.Limit <= 0 ? 20 : Math.Min(body.Limit, 100);

    using var conn = OpenDbSafe(dbPath, vecExtPath);

    var hits = new List<(string pointId, double distance)>();
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = """
        select point_id, distance
        from item_vec
        where embedding match $q
        order by distance
        limit $k;
        """;
        cmd.Parameters.AddWithValue("$q", JsonSerializer.Serialize(emb.vector!, JsonOpts()));
        cmd.Parameters.AddWithValue("$k", limit);

        using var r = await cmd.ExecuteReaderAsync(req.HttpContext.RequestAborted);
        while (await r.ReadAsync(req.HttpContext.RequestAborted))
            hits.Add((r.GetString(0), r.GetDouble(1)));
    }

    var results = new List<object>();
    foreach (var h in hits)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        select type, entity_id, title, semester_id, major_id, text
        from items
        where point_id = $pid;
        """;
        cmd.Parameters.AddWithValue("$pid", h.pointId);

        using var r = await cmd.ExecuteReaderAsync(req.HttpContext.RequestAborted);
        if (!await r.ReadAsync(req.HttpContext.RequestAborted)) continue;

        var type = r.GetString(0);
        var entityId = r.IsDBNull(1) ? null : r.GetString(1);
        var title = r.IsDBNull(2) ? null : r.GetString(2);
        var semesterId = r.IsDBNull(3) ? null : r.GetString(3);
        var majorId = r.IsDBNull(4) ? null : r.GetString(4);
        var text = r.GetString(5);

        if (!string.IsNullOrWhiteSpace(body.Type) && body.Type != type) continue;
        if (!string.IsNullOrWhiteSpace(body.SemesterId) && body.SemesterId != semesterId) continue;
        if (!string.IsNullOrWhiteSpace(body.MajorId) && body.MajorId != majorId) continue;

        results.Add(new
        {
            id = h.pointId,
            distance = h.distance,
            payload = new { type, entityId, title, semesterId, majorId, text }
        });
    }

    return Results.Json(new { hits = results });
});

// ======================================================================
// LLM: Extract Skills
// ======================================================================
app.MapPost("/llm/extract-skills", async (HttpRequest req, IHttpClientFactory hf) =>
{
    if (!Authorized(req)) return Results.Unauthorized();

    var body = await JsonSerializer.DeserializeAsync<ExtractSkillsRequest>(req.Body, JsonOpts(), req.HttpContext.RequestAborted);
    if (body is null || string.IsNullOrWhiteSpace(body.Text))
        return Results.BadRequest(new { error = "Missing text" });

    var sys = """
Return ONLY valid JSON. No markdown. No code fences. No extra keys.
Extract skills from the text. Do NOT fabricate.
Schema:
{"primaryRole":"Frontend|Backend|Mobile|AI|Data|QA|DevOps|null","skills":["string"],"matchedSkills":["string"],"evidence":[{"skill":"string","quote":"string"}]}
""";

    var user = JsonSerializer.Serialize(new
    {
        maxSkills = Math.Clamp(body.MaxSkills <= 0 ? 20 : body.MaxSkills, 1, 60),
        knownSkills = body.KnownSkills ?? new List<string>(),
        text = Clip(body.Text!, 9000)
    }, JsonOpts());

    var llm = hf.CreateClient("llm");

    var call = await LlamaChatSafeAsync(llm, sys, user, temperature: 0.2, maxTokens: 2600, llmGate, req.HttpContext.RequestAborted);
    if (!call.ok)
        return Results.Json(new { ok = false, error = "llm_failed", detail = call.error, status = call.status }, statusCode: 503);

    var extracted = ExtractFirstCompleteJsonValue(call.content);

    if (extracted is null || call.finish == "length")
    {
        var call2 = await LlamaChatSafeAsync(llm, sys, user, temperature: 0.2, maxTokens: 4200, llmGate, req.HttpContext.RequestAborted);
        if (call2.ok)
            extracted = ExtractFirstCompleteJsonValue(call2.content);
    }

    if (extracted is null)
        return Results.Json(new { ok = false, error = "llm_invalid_json_or_truncated" }, statusCode: 200);

    extracted = EscapeNewlinesInsideJsonStrings(extracted);

    if (!TryParseJson(extracted, out var parsed, out var err))
        return Results.Json(new { ok = false, error = "llm_invalid_json", detail = err }, statusCode: 200);

    return Results.Text(parsed!.RootElement.GetRawText(), "application/json");
});

// ======================================================================
// LLM: Generate Group Post Draft
// ======================================================================
app.MapPost("/llm/generate-post/group", async (HttpRequest req, IHttpClientFactory hf) =>
{
    if (!Authorized(req)) return Results.Unauthorized();

    var body = await JsonSerializer.DeserializeAsync<GenerateGroupPostRequest>(req.Body, JsonOpts(), req.HttpContext.RequestAborted);
    if (body?.Group is null || string.IsNullOrWhiteSpace(body.Group.Name))
        return Results.BadRequest(new { error = "Missing group.name" });

    var opts = body.Options ?? new PostOptions(Language: null, MaxWords: null, Tone: null);
    var lang = string.IsNullOrWhiteSpace(opts.Language) ? "en" : opts.Language!.Trim();
    var tone = string.IsNullOrWhiteSpace(opts.Tone) ? "friendly" : opts.Tone!.Trim();
    var maxWords = Math.Clamp(opts.MaxWords ?? POST_MAX_WORDS_DEFAULT, POST_MAX_WORDS_MIN, POST_MAX_WORDS_MAX);

    var spec = ComputeGroupPostSpec(body);

    var allowedPositions = new[]
    {
        "Frontend Developer","Backend Developer","Mobile Developer","AI Engineer","QA Engineer",
        "Project Manager","Data Analyst","UI/UX Designer","DevOps Engineer","Business Analyst"
    };

    var sys = """
Return ONLY valid JSON. No markdown. No code fences. No extra keys.

You are writing a GROUP recruitment post draft.

OUTPUT LANGUAGE:
- English (en) only.

RULES:
- Do NOT invent skills.
- Choose a specific positionNeed from allowedPositions.
- Choose requiredSkills (3-6 items) from skillBank ONLY.
- Prefer requiredSkills that are NOT already in teamTopSkills.
- Mention what the team already has and what is missing.
- Keep description under maxWords words.
- End with a call-to-action (DM / Apply).

Schema:
{"title":"...","description":"...","positionNeed":"...","requiredSkills":["..."]}
""";

    var user = JsonSerializer.Serialize(new
    {
        language = lang,
        tone,
        maxWords,
        allowedPositions,
        skillBank = spec.SkillBank,
        group = new
        {
            name = body.Group.Name,
            primaryNeed = body.Group.PrimaryNeed,
            currentMix = body.Group.CurrentMix,
            openSlots = body.Group.OpenSlots,
            teamTopSkills = body.Group.TeamTopSkills ?? Array.Empty<string>()
        },
        project = body.Project is null ? null : new { title = body.Project.Title, summary = body.Project.Summary }
    }, JsonOpts());

    var llm = hf.CreateClient("llm");
    var (ok, json, detail) = await CallJsonWithRetryAsync(llm, sys, user, llmGate, req.HttpContext.RequestAborted);
    if (!ok)
        return Results.Ok(new { draft = (object?)null, error = "llm_invalid_json_or_incomplete", detail });

    if (!TryParseJson(json!, out var parsed, out var perr))
        return Results.Ok(new { draft = (object?)null, error = "llm_invalid_json", detail = perr });

    var root = parsed!.RootElement;
    if (!HasRequiredKeys(root, "title", "description", "positionNeed", "requiredSkills"))
        return Results.Ok(new { draft = (object?)null, error = "llm_missing_required_keys", detail = Clip(root.GetRawText(), 1200) });

    return Results.Json(new { draft = BuildGroupDraftResponse(root, spec) });
});

// ======================================================================
// LLM: Generate Personal Post Draft
// ======================================================================
app.MapPost("/llm/generate-post/personal", async (HttpRequest req, IHttpClientFactory hf) =>
{
    if (!Authorized(req)) return Results.Unauthorized();

    var body = await JsonSerializer.DeserializeAsync<GeneratePersonalPostRequest>(req.Body, JsonOpts(), req.HttpContext.RequestAborted);
    if (body?.User is null || string.IsNullOrWhiteSpace(body.User.DisplayName))
        return Results.BadRequest(new { error = "Missing user.displayName" });

    var opts = body.Options ?? new PostOptions(Language: null, MaxWords: null, Tone: null);
    var lang = string.IsNullOrWhiteSpace(opts.Language) ? "en" : opts.Language!.Trim();
    var tone = string.IsNullOrWhiteSpace(opts.Tone) ? "professional" : opts.Tone!.Trim();
    var maxWords = Math.Clamp(opts.MaxWords ?? POST_MAX_WORDS_DEFAULT, POST_MAX_WORDS_MIN, POST_MAX_WORDS_MAX);

    var skills = (body.User.Skills ?? new List<string>())
        .Where(s => !string.IsNullOrWhiteSpace(s))
        .Select(s => s.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(12)
        .ToArray();

    var sys = """
Return ONLY valid JSON. No markdown. No code fences. No extra keys.

You are writing a PERSONAL introduction post draft.

OUTPUT LANGUAGE:
- English (en) only.

RULES:
- Do NOT invent skills. Use provided skills only.
- Title 6-12 words, meaningful.
- Keep description under maxWords words.
- End with a call-to-action (DM / connect).

Schema:
{"title":"...","description":"..."}
""";

    var user = JsonSerializer.Serialize(new
    {
        language = lang,
        tone,
        maxWords,
        person = new
        {
            displayName = body.User.DisplayName,
            goal = body.User.Goal,
            availability = body.User.Availability,
            skills
        }
    }, JsonOpts());

    var llm = hf.CreateClient("llm");
    var (ok, json, detail) = await CallJsonWithRetryAsync(llm, sys, user, llmGate, req.HttpContext.RequestAborted);
    if (!ok)
        return Results.Ok(new { draft = (object?)null, error = "llm_invalid_json_or_incomplete", detail });

    if (!TryParseJson(json!, out var parsed, out var perr))
        return Results.Ok(new { draft = (object?)null, error = "llm_invalid_json", detail = perr });

    var root = parsed!.RootElement;
    if (!HasRequiredKeys(root, "title", "description"))
        return Results.Ok(new { draft = (object?)null, error = "llm_missing_required_keys", detail = Clip(root.GetRawText(), 1200) });

    return Results.Json(new { draft = new { title = GetStringAny(root, "title", "Title") ?? "", description = GetStringAny(root, "description", "Description") ?? "" } });
});

// ======================================================================
// LLM: Rerank (topic / group_post / personal_post)
// - mode=topic => ABSOLUTE score mapping
// - mode=group_post/personal_post => RELATIVE score mapping (min-max in batch)
// ======================================================================
app.MapPost("/llm/rerank", async (HttpRequest req, IHttpClientFactory hf) =>
{
    if (!Authorized(req)) return Results.Unauthorized();

    var rawRequest = await ReadBodyAsync(req);
    using var doc = JsonDocument.Parse(rawRequest);
    var root = doc.RootElement;

    var mode = NormalizeMode(GetStringAny(root, "mode", "Mode") ?? "topic");

    var rawQueryText = GetStringAny(root, "queryText", "QueryText") ?? "";
    var policy = GetStringAny(root, "policy", "Policy") ?? "Explain match. Do not fabricate.";

    TeamContext? team = null;
    if (TryGetObjectAny(root, out var teamEl, "teamContext", "TeamContext", "team", "Team"))
        team = ParseTeamContext(teamEl);

    var queryText = BuildQueryText(mode, rawQueryText, team);
    if (string.IsNullOrWhiteSpace(queryText))
        return Results.BadRequest(new { error = "Missing queryText" });

    var topN = Math.Clamp(GetIntAny(root, "topN", "TopN") ?? MAX_SUGGESTIONS, 1, MAX_SUGGESTIONS);

    if (!TryGetArrayAny(root, out var candsEl, "candidates", "Candidates"))
        return Results.BadRequest(new { error = "Missing candidates[]" });

    var candidates = new List<RerankCandidate>(Math.Min(MAX_POOL, candsEl.GetArrayLength()));
    foreach (var c in candsEl.EnumerateArray())
    {
        var key = GetStringAny(c, "key", "Key") ?? $"c{candidates.Count + 1:00}";

        var idStr = GetStringAny(c, "entityId", "EntityId", "id", "Id");
        Guid entityId = Guid.Empty;
        if (!string.IsNullOrWhiteSpace(idStr)) Guid.TryParse(idStr, out entityId);

        var title = GetStringAny(c, "title", "Title") ?? "(no title)";
        var text = GetStringAny(c, "text", "Text") ?? title;

        var baseline = GetIntAny(c, "baselineScore", "BaselineScore")
                       ?? GetIntAny(c, "score", "Score")
                       ?? GetIntAny(c, "finalScore", "FinalScore")
                       ?? 0;

        var neededRole = GetStringAny(c, "neededRole", "NeededRole");

        var gf = GetIntAny(c, "groupFrontend", "GroupFrontend") ?? team?.CurrentMixFe ?? 0;
        var gb = GetIntAny(c, "groupBackend", "GroupBackend") ?? team?.CurrentMixBe ?? 0;
        var go = GetIntAny(c, "groupOther", "GroupOther") ?? team?.CurrentMixOther ?? 0;

        candidates.Add(new RerankCandidate(key, entityId, title, text, baseline, neededRole, gf, gb, go));
        if (candidates.Count >= MAX_POOL) break;
    }

    if (candidates.Count == 0)
        return Results.Json(new { ranked = Array.Empty<object>() });

    // Phase A: Cross-encoder rerank
    var rrClient = hf.CreateClient("rerank");
    var rrTexts = candidates.Select(c => BuildRerankText(c, RERANK_TEXT_MAX)).ToArray();

    var rrRes = await RerankSafeAsync(rrClient, queryText, rrTexts, rerankGate, req.HttpContext.RequestAborted);
    if (!rrRes.ok)
        return Results.Json(new { ok = false, error = "rerank_failed", detail = rrRes.error, status = rrRes.status }, statusCode: 503);

    var logits = rrRes.logits!;
    if (logits.Length != candidates.Count)
        return Results.Json(new { ok = false, error = "rerank_scores_mismatch", scores = logits.Length, candidates = candidates.Count }, statusCode: 200);

    // Phase B: score mapping (separated by mode)
    double[] ceScores = IsTopicMode(mode)
        ? ScoreTopicAbsolute(logits, TOPIC_PIVOT, TOPIC_SCALE)
        : ScorePostRelative(logits);

    double FinalScore(int i)
    {
        var ce = ceScores[i];
        var b = Math.Clamp(candidates[i].BaselineScore, 0, 100);

        if (IsTopicMode(mode))
            return (WEIGHT_TOPIC_CE * ce) + (WEIGHT_TOPIC_BASE * b);

        return (WEIGHT_POST_CE * ce) + (WEIGHT_POST_BASE * b);
    }

    var minScore = IsTopicMode(mode) ? TOPIC_MIN_SCORE : POST_MIN_SCORE;

    var seeds = candidates
        .Select((c, i) => new RankedSeed(
            Key: c.Key,
            EntityId: c.EntityId,
            Title: c.Title,
            Text: Clip(BuildReasonSnippet(c), REASON_SNIPPET_MAX),
            FinalScore: Math.Round(FinalScore(i), 2)
        ))
        .OrderByDescending(x => x.FinalScore)
        // Topic suggestions should respect a minimum quality bar.
        // Post suggestions should return multiple candidates; the UI can display lower scores.
        .Where(x => !IsTopicMode(mode) || x.FinalScore >= minScore)
        .Take(topN)
        .ToList();

    if (enableDebug)
    {
        last.AtUtc = DateTime.UtcNow;
        last.Mode = mode;
        last.CandidatesCount = candidates.Count;
        last.SeedsCount = seeds.Count;
        last.TopN = topN;
        last.MinScore = minScore;
        last.QueryTextPreview = Clip(queryText, 1400);
        last.LogitsPreview = string.Join(", ", logits.Select(x => x.ToString("0.########", CultureInfo.InvariantCulture)));
        last.CeScoresPreview = string.Join(", ", ceScores.Select(x => x.ToString("0.##", CultureInfo.InvariantCulture)));
        last.FinalScoresPreview = string.Join(", ", candidates.Select((_, i) => FinalScore(i).ToString("0.##", CultureInfo.InvariantCulture)));
        last.RequestPreview = Clip(rawRequest, 1800);
        last.SystemPreview = null;
        last.UserPreview = null;
        last.RawResponsePreview = null;
    }

    if (seeds.Count == 0)
        return Results.Json(new { ranked = Array.Empty<object>() });

    // Phase D: LLM reasons (batch)
    var llm = hf.CreateClient("llm");
    var sysBatch = BuildReasonBatchSystemPrompt(mode);

    var items = seeds.Select(s => new
    {
        key = s.Key,
        finalScore = s.FinalScore,
        snippet = BuildReasonSnippetForLlm(s.Text)
    }).ToArray();

    var userBatch = JsonSerializer.Serialize(new { mode, queryText, policy, items }, JsonOpts());

    var reasonsByKey = new Dictionary<string, SummaryInfo>(StringComparer.OrdinalIgnoreCase);

    var batchCall = await LlamaChatSafeAsync(llm, sysBatch, userBatch, temperature: 0.2, maxTokens: 1600, llmGate, req.HttpContext.RequestAborted);
    if (batchCall.ok)
    {
        var extracted = ExtractFirstCompleteJsonValue(batchCall.content);
        if (extracted is not null)
        {
            extracted = EscapeNewlinesInsideJsonStrings(extracted);
            if (TryParseBatchSummaries(extracted, out var dict) && dict.Count > 0)
            {
                foreach (var kv in dict)
                {
                    var cleaned = NormalizeReasonFinal(kv.Value.Summary);
                    var info = new SummaryInfo(cleaned, kv.Value.MatchedSkills ?? Array.Empty<string>());
                    if (!IsBadReasonSummary(info.Summary))
                        reasonsByKey[kv.Key] = info;
                }
            }
        }
    }

    // Fallback per-item (only if missing/bad)
    var sysOne = BuildReasonSystemPrompt(mode);
    foreach (var s in seeds)
    {
        if (reasonsByKey.ContainsKey(s.Key)) continue;

        var userOne = JsonSerializer.Serialize(new
        {
            mode,
            queryText,
            policy,
            item = new { key = s.Key, finalScore = s.FinalScore, snippet = BuildReasonSnippetForLlm(s.Text) }
        }, JsonOpts());

        var oneCall = await LlamaChatSafeAsync(llm, sysOne, userOne, temperature: 0.15, maxTokens: 420, llmGate, req.HttpContext.RequestAborted);

        SummaryInfo info;
        if (oneCall.ok)
        {
            var j = ExtractFirstCompleteJsonValue(oneCall.content);
            if (j is not null && TryParseSingleSummary(EscapeNewlinesInsideJsonStrings(j), out var parsedInfo))
            {
                info = new SummaryInfo(NormalizeReasonFinal(parsedInfo.Summary), parsedInfo.MatchedSkills ?? Array.Empty<string>());
            }
            else
            {
                // plain text fallback
                info = new SummaryInfo(NormalizeReasonFinal(CleanReasonText(oneCall.content) ?? "Strong match based on overlapping skills."), Array.Empty<string>());
            }
        }
        else
        {
            info = new SummaryInfo("Strong match based on overlapping skills.", Array.Empty<string>());
        }

        if (IsBadReasonSummary(info.Summary))
            info = new SummaryInfo("Strong match based on overlapping skills.", Array.Empty<string>());

        reasonsByKey[s.Key] = info;
    }

    var ranked = seeds.Select(s =>
    {
        reasonsByKey.TryGetValue(s.Key, out var info);
        info ??= new SummaryInfo("Strong match based on overlapping skills.", Array.Empty<string>());

        return new RerankResultItem(
            Key: s.Key,
            FinalScore: s.FinalScore,
            Reason: NormalizeReasonFinal(info.Summary),
            MatchedSkills: (info.MatchedSkills ?? Array.Empty<string>()).Take(3).ToArray(),
            BalanceNote: BuildBalanceNote(mode, team)
        );
    }).ToArray();

    return Results.Json(new { ranked });
});

app.Run();

// ======================================================================
// Helpers
// ======================================================================

static bool HasRequiredKeys(JsonElement root, params string[] keys)
{
    if (root.ValueKind != JsonValueKind.Object) return false;
    foreach (var k in keys)
        if (!root.TryGetProperty(k, out _))
            return false;
    return true;
}

static object BuildGroupDraftResponse(JsonElement llmJson, GroupPostSpec spec)
{
    var title = (GetStringAny(llmJson, "title", "Title") ?? "").Trim();
    var description = (GetStringAny(llmJson, "description", "Description") ?? "").Trim();
    var positionNeed = (GetStringAny(llmJson, "positionNeed", "PositionNeed") ?? "").Trim();

    if (string.IsNullOrWhiteSpace(positionNeed) || positionNeed.Equals("Other", StringComparison.OrdinalIgnoreCase))
        positionNeed = spec.SuggestedBucket switch
        {
            "Frontend" => "Frontend Developer",
            "Backend" => "Backend Developer",
            "AI" => "AI Engineer",
            "Mobile" => "Mobile Developer",
            _ => "Project Manager"
        };

    var requiredSkills = new List<string>();
    if (TryGetArrayAny(llmJson, out var arr, "requiredSkills", "RequiredSkills"))
    {
        foreach (var x in arr.EnumerateArray())
        {
            if (x.ValueKind != JsonValueKind.String) continue;
            var v = (x.GetString() ?? "").Trim();
            if (v.Length == 0) continue;
            requiredSkills.Add(v);
            if (requiredSkills.Count >= 6) break;
        }
    }
    if (requiredSkills.Count == 0)
        requiredSkills.AddRange(spec.SkillBank.Take(4));

    return new
    {
        title,
        description,
        positionNeed,
        requiredSkills = requiredSkills.ToArray(),
        expiresAt = DateTime.UtcNow.AddDays(1).ToString("O", CultureInfo.InvariantCulture)
    };
}

static GroupPostSpec ComputeGroupPostSpec(GenerateGroupPostRequest req)
{
    var g = req.Group;
    var open = g.OpenSlots ?? new Mix(0, 0, 0);
    var mix = g.CurrentMix ?? new Mix(0, 0, 0);

    string roleBucket = PickNeededRole(open, g.PrimaryNeed, mix);

    var fe = new[] { "React", "TypeScript", "Tailwind", "Vite", "Next.js", "Redux", "UI", "UX" };
    var be = new[] { "C#", "ASP.NET Core", "SQL", "Docker", "REST", "Azure", "CI/CD" };
    var ai = new[] { "Python", "PyTorch", "LLM", "RAG", "Embeddings", "Vector DB" };
    var mobile = new[] { "Flutter", "React Native", "Android", "iOS" };
    var qa = new[] { "QA", "Testing", "Test Cases", "Bug Reporting", "Cypress" };
    var pm = new[] { "Project Management", "Agile", "Communication", "Planning", "Documentation" };

    var skillBank = fe.Concat(be).Concat(ai).Concat(mobile).Concat(qa).Concat(pm)
        .Select(s => s.Trim())
        .Where(s => s.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(80)
        .ToArray();

    return new GroupPostSpec(roleBucket, skillBank);

    static string PickNeededRole(Mix open, string? primaryNeed, Mix mix)
    {
        if (open.Fe > 0 || open.Be > 0 || open.Other > 0)
        {
            if (open.Fe >= open.Be && open.Fe >= open.Other) return "Frontend";
            if (open.Be >= open.Fe && open.Be >= open.Other) return "Backend";
            return "Other";
        }

        var pn = (primaryNeed ?? "").Trim().ToLowerInvariant();
        if (pn.Contains("front")) return "Frontend";
        if (pn.Contains("back")) return "Backend";
        if (pn.Contains("ai") || pn.Contains("ml")) return "AI";
        if (pn.Contains("mobile")) return "Mobile";

        if (mix.Be >= 2 && mix.Fe == 0) return "Frontend";
        if (mix.Fe >= 2 && mix.Be == 0) return "Backend";
        return "Other";
    }
}

// ======================================================================
// Scoring
// ======================================================================

static bool IsTopicMode(string mode) => NormalizeMode(mode) == "topic";

static double Sigmoid(double x) => 1.0 / (1.0 + Math.Exp(-x));

static double[] ScoreTopicAbsolute(double[] logits, double pivot, double scale)
{
    var inv = 1.0 / Math.Max(1e-9, scale);
    var scores = new double[logits.Length];
    for (int i = 0; i < logits.Length; i++)
    {
        var z = (logits[i] - pivot) * inv;
        scores[i] = 100.0 * Sigmoid(z);
    }
    return scores;
}

static double[] ScorePostRelative(double[] logits)
{
    var raws = logits.Select(Sigmoid).ToArray();
    var max = raws.Max();
    var min = raws.Min();
    var denom = max - min;

    var scores = new double[logits.Length];
    if (denom < 1e-12)
    {
        for (int i = 0; i < scores.Length; i++) scores[i] = 100.0;
        return scores;
    }

    for (int i = 0; i < raws.Length; i++)
        scores[i] = 100.0 * ((raws[i] - min) / denom);

    return scores;
}

// ======================================================================
// Prompt builders + reason cleanup (NO "(Join)")
// ======================================================================

static string BuildReasonSystemPrompt(string mode)
{
    mode = NormalizeMode(mode);

    var common = """
Return ONLY valid JSON. No markdown. No code fences. No extra keys.

Write ONE neutral justification sentence:
- 1 sentence, <= 180 characters.
- NEVER start with "Join".
- No call-to-action (no DM, apply, connect).
- Mention 1-2 technologies that appear in the snippet.
- End with a period.

Schema:
{"summary":"...","matchedSkills":["..."]}
""";

    if (mode is "group_post" or "personal_post")
        return common + "\nFocus: Team gap matching (needed role + mix + matching skills).\n";

    return common + "\nFocus: Topic match to query goals and skills overlap.\n";
}

static string BuildReasonBatchSystemPrompt(string mode)
{
    mode = NormalizeMode(mode);

    var common = """
Return ONLY valid JSON. No markdown. No code fences. No extra keys.

For each item:
- 1 neutral sentence, <= 180 characters, ends with a period.
- NEVER start with "Join".
- No call-to-action.
- Mention 1-2 technologies present in snippet.
- matchedSkills: 1-3 skills found in snippet.

Return schema:
{"items":[{"key":"...","summary":"...","matchedSkills":["..."]}]}
""";

    if (mode is "group_post" or "personal_post")
        return common + "\nFocus: Team gap matching (needed role + mix + matching skills).\n";

    return common + "\nFocus: Topic match to query goals and skills overlap.\n";
}

static string NormalizeReasonFinal(string summary)
{
    var s = NormalizeWhitespace(summary);

    // strip trailing "(Join)" / "(Something)" patterns
    s = StripTrailingParen(s);

    if (s.StartsWith("Join ", StringComparison.OrdinalIgnoreCase))
        s = "Strong match based on overlapping skills and role needs.";

    if (!s.EndsWith('.')) s += ".";
    while (s.EndsWith("..", StringComparison.Ordinal)) s = s[..^1];

    return s;

    static string StripTrailingParen(string input)
    {
        var t = (input ?? "").Trim();
        if (t.Length == 0) return t;

        // remove final period to check trailing paren
        var hadDot = t.EndsWith('.');
        if (hadDot) t = t[..^1].TrimEnd();

        if (!t.EndsWith(")", StringComparison.Ordinal))
            return (hadDot ? (t + ".") : t);

        var open = t.LastIndexOf('(');
        if (open < 0) return (hadDot ? (t + ".") : t);

        var inside = t[(open + 1)..^1].Trim();
        if (inside.Length is > 0 and <= 20)
        {
            t = t[..open].TrimEnd();
        }

        return (t + ".");
    }
}

static bool IsBadReasonSummary(string? summary)
{
    if (string.IsNullOrWhiteSpace(summary)) return true;
    var s = NormalizeWhitespace(summary);
    if (s.Length < 10) return true;
    if (s.StartsWith("Join ", StringComparison.OrdinalIgnoreCase)) return true;
    if (s.Contains("DM", StringComparison.OrdinalIgnoreCase)) return true;
    if (s.Contains("apply", StringComparison.OrdinalIgnoreCase)) return true;
    if (s.Contains("connect", StringComparison.OrdinalIgnoreCase)) return true;
    return false;
}

static string NormalizeWhitespace(string s)
{
    if (string.IsNullOrWhiteSpace(s)) return "";
    var sb = new StringBuilder(s.Length);
    bool prev = false;
    foreach (var ch in s)
    {
        if (char.IsWhiteSpace(ch))
        {
            if (!prev) sb.Append(' ');
            prev = true;
        }
        else
        {
            sb.Append(ch);
            prev = false;
        }
    }
    return sb.ToString().Trim();
}

static string? CleanReasonText(string raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return null;
    raw = raw.Trim();

    if (raw.StartsWith("```", StringComparison.Ordinal))
    {
        var firstNl = raw.IndexOf('\n');
        if (firstNl >= 0) raw = raw[(firstNl + 1)..];
        var endFence = raw.LastIndexOf("```", StringComparison.Ordinal);
        if (endFence >= 0) raw = raw[..endFence];
        raw = raw.Trim();
    }

    // if it contains JSON braces, refuse
    if (raw.Contains('{') || raw.Contains('[')) return null;

    raw = raw.Trim().Trim('"').Trim('\'');
    raw = NormalizeWhitespace(raw);
    if (raw.Length == 0) return null;
    return NormalizeReasonFinal(raw);
}

static string BuildReasonSnippetForLlm(string seedSnippet)
{
    if (string.IsNullOrWhiteSpace(seedSnippet)) return "";
    var kept = new List<string>();

    foreach (var raw in seedSnippet.Split('\n'))
    {
        var line = raw.TrimEnd();
        if (line.Length == 0) continue;

        if (line.StartsWith("TITLE:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("ROLE:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("MAJOR:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("NEEDED_ROLE:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("DESIRED_POSITION:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("GPA:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("TEAM_MIX:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("BASELINE_SCORE:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("SKILLS:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("MATCHING_SKILLS:", StringComparison.OrdinalIgnoreCase))
        {
            kept.Add(line);
        }
    }

    return Clip(string.Join("\n", kept), 2500);
}

static string BuildBalanceNote(string mode, TeamContext? team)
{
    mode = NormalizeMode(mode);
    if (mode is not ("group_post" or "personal_post")) return "";

    if (team is null) return "";

    if (team.CurrentMixBe >= 2) return "Backend already strong; prefer complementary roles.";
    if (team.CurrentMixFe >= 2) return "Frontend already strong; prefer complementary roles.";
    return "";
}

// ======================================================================
// Query + Team context
// ======================================================================

static string BuildQueryText(string mode, string rawQueryText, TeamContext? team)
{
    mode = NormalizeMode(mode);
    rawQueryText = (rawQueryText ?? "").Trim();

    if (team is null) return rawQueryText;

    var sb = new StringBuilder();
    sb.AppendLine($"TEAM: {team.TeamName}");
    sb.AppendLine($"MODE: {mode}");

    if (!string.IsNullOrWhiteSpace(team.PrimaryNeed))
        sb.AppendLine($"PRIMARY_NEED: {team.PrimaryNeed}");

    sb.AppendLine($"CURRENT_MIX: FE {team.CurrentMixFe}, BE {team.CurrentMixBe}, Other {team.CurrentMixOther}");

    if (team.Skills.Count > 0)
        sb.AppendLine($"TEAM_SKILLS: {string.Join(", ", team.Skills.Take(28))}");

    if (!string.IsNullOrWhiteSpace(rawQueryText))
        sb.AppendLine($"QUERY: {rawQueryText}");

    return sb.ToString().Trim();
}

static TeamContext ParseTeamContext(JsonElement teamEl)
{
    var teamName = GetStringAny(teamEl, "teamName", "TeamName", "name", "Name") ?? "Team";
    var primaryNeed = (GetStringAny(teamEl, "primaryNeed", "PrimaryNeed") ?? "").Trim();

    var skills = new List<string>();
    if (TryGetArrayAny(teamEl, out var skillsArr, "skills", "Skills", "teamTopSkills", "TeamTopSkills"))
    {
        skills = skillsArr.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()!.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(80)
            .ToList();
    }

    int mixFe = 0, mixBe = 0, mixOther = 0;
    if (TryGetObjectAny(teamEl, out var mixEl, "currentMix", "CurrentMix", "mix", "Mix"))
    {
        mixFe = GetIntAny(mixEl, "fe", "FE", "frontend", "Frontend") ?? 0;
        mixBe = GetIntAny(mixEl, "be", "BE", "backend", "Backend") ?? 0;
        mixOther = GetIntAny(mixEl, "other", "Other") ?? 0;
    }

    return new TeamContext(teamName, primaryNeed, skills, mixFe, mixBe, mixOther);
}

// ======================================================================
// Candidate text builders
// ======================================================================

static string BuildRerankText(RerankCandidate c, int maxChars)
{
    var sb = new StringBuilder();

    sb.Append("TITLE: ").AppendLine(TrimOneLine(c.Title, 180));

    if (!string.IsNullOrWhiteSpace(c.NeededRole))
        sb.Append("NEEDED_ROLE: ").AppendLine(TrimOneLine(c.NeededRole!, 100));

    if (c.GroupFrontend + c.GroupBackend + c.GroupOther > 0)
        sb.Append("TEAM_MIX: FE ").Append(c.GroupFrontend).Append(" BE ").Append(c.GroupBackend).Append(" Other ").Append(c.GroupOther).AppendLine();

    if (c.BaselineScore > 0)
        sb.Append("BASELINE_SCORE: ").Append(c.BaselineScore).AppendLine();

    // Keep original block lines if present
    foreach (var line in c.Text.Split('\n'))
    {
        var l = line.TrimEnd();
        if (l.StartsWith("SKILLS:", StringComparison.OrdinalIgnoreCase)
            || l.StartsWith("MATCHING_SKILLS:", StringComparison.OrdinalIgnoreCase)
            || l.StartsWith("SUMMARY:", StringComparison.OrdinalIgnoreCase)
            || l.StartsWith("ROLE:", StringComparison.OrdinalIgnoreCase)
            || l.StartsWith("MAJOR:", StringComparison.OrdinalIgnoreCase)
            || l.StartsWith("DESIRED_POSITION:", StringComparison.OrdinalIgnoreCase)
            || l.StartsWith("Desired position:", StringComparison.OrdinalIgnoreCase)
            || l.StartsWith("GPA:", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine(l);
        }
    }

    sb.Append("DETAILS: ").AppendLine(TrimOneLine(c.Text, 4500));

    return Clip(sb.ToString(), maxChars);
}

static string BuildReasonSnippet(RerankCandidate c) => BuildRerankText(c, REASON_SNIPPET_MAX);

// ======================================================================
// SAFE upstream calls
// ======================================================================

static async Task<(bool ok, double[]? vector, int status, string? error)> LlamaEmbedSafeAsync(
    HttpClient embed, string input, SemaphoreSlim gate, CancellationToken ct)
{
    await gate.WaitAsync(ct);
    try
    {
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var res = await embed.PostAsJsonAsync("/v1/embeddings", new { input = Clip(input, 9000) }, ct);
                var json = await res.Content.ReadAsStringAsync(ct);

                if (!res.IsSuccessStatusCode)
                {
                    if (attempt < 2 && IsRetryableStatus(res.StatusCode))
                    {
                        await Task.Delay(150, ct);
                        continue;
                    }
                    return (false, null, (int)res.StatusCode, Clip(json, 800));
                }

                using var doc = JsonDocument.Parse(json);
                var vec = doc.RootElement.GetProperty("data")[0].GetProperty("embedding")
                    .EnumerateArray().Select(x => x.GetDouble()).ToArray();

                return (true, vec, (int)res.StatusCode, null);
            }
            catch (Exception ex) when (attempt < 2)
            {
                _ = ex;
                await Task.Delay(150, ct);
            }
            catch (Exception ex)
            {
                return (false, null, 0, ex.Message);
            }
        }

        return (false, null, 0, "embed_unknown_failure");
    }
    finally
    {
        gate.Release();
    }
}

static async Task<(bool ok, double[]? logits, int status, string? error)> RerankSafeAsync(
    HttpClient rr, string query, string[] candidates, SemaphoreSlim gate, CancellationToken ct)
{
    await gate.WaitAsync(ct);
    try
    {
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var res = await rr.PostAsJsonAsync("/rerank", new { query, candidates }, ct);
                var json = await res.Content.ReadAsStringAsync(ct);

                if (!res.IsSuccessStatusCode)
                {
                    if (attempt < 2 && IsRetryableStatus(res.StatusCode))
                    {
                        await Task.Delay(150, ct);
                        continue;
                    }
                    return (false, null, (int)res.StatusCode, Clip(json, 900));
                }

                using var doc = JsonDocument.Parse(json);
                var logits = doc.RootElement.GetProperty("scores").EnumerateArray().Select(x => x.GetDouble()).ToArray();
                return (true, logits, (int)res.StatusCode, null);
            }
            catch (Exception ex) when (attempt < 2)
            {
                _ = ex;
                await Task.Delay(150, ct);
            }
            catch (Exception ex)
            {
                return (false, null, 0, ex.Message);
            }
        }

        return (false, null, 0, "rerank_unknown_failure");
    }
    finally
    {
        gate.Release();
    }
}

static async Task<(bool ok, string content, string finish, int status, string? error)> LlamaChatSafeAsync(
    HttpClient llm,
    string system,
    string user,
    double temperature,
    int maxTokens,
    SemaphoreSlim gate,
    CancellationToken ct)
{
    await gate.WaitAsync(ct);
    try
    {
        var payload = new
        {
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            },
            temperature,
            max_tokens = maxTokens
        };

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var res = await llm.PostAsJsonAsync("/v1/chat/completions", payload, ct);
                var json = await res.Content.ReadAsStringAsync(ct);

                if (!res.IsSuccessStatusCode)
                {
                    if (attempt < 2 && IsRetryableStatus(res.StatusCode))
                    {
                        await Task.Delay(180, ct);
                        continue;
                    }
                    return (false, "", "error", (int)res.StatusCode, Clip(json, 900));
                }

                using var doc = JsonDocument.Parse(json);
                var choice = doc.RootElement.GetProperty("choices")[0];
                var finish = choice.TryGetProperty("finish_reason", out var fr) ? (fr.GetString() ?? "unknown") : "unknown";
                var content = choice.GetProperty("message").GetProperty("content").GetString() ?? "";
                return (true, content, finish, (int)res.StatusCode, null);
            }
            catch (Exception ex) when (attempt < 2)
            {
                _ = ex;
                await Task.Delay(180, ct);
            }
            catch (Exception ex)
            {
                return (false, "", "error", 0, ex.Message);
            }
        }

        return (false, "", "error", 0, "llm_unknown_failure");
    }
    finally
    {
        gate.Release();
    }
}

static bool IsRetryableStatus(HttpStatusCode code)
{
    var n = (int)code;
    return n == 408 || n == 429 || n == 500 || n == 502 || n == 503 || n == 504;
}

// ======================================================================
// JSON extraction / parsing
// ======================================================================

static string? ExtractFirstCompleteJsonValue(string raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return null;
    raw = raw.Trim();

    if (raw.StartsWith("```", StringComparison.Ordinal))
    {
        var firstNl = raw.IndexOf('\n');
        if (firstNl >= 0) raw = raw[(firstNl + 1)..];
        var endFence = raw.LastIndexOf("```", StringComparison.Ordinal);
        if (endFence >= 0) raw = raw[..endFence];
        raw = raw.Trim();
    }

    var startObj = raw.IndexOf('{');
    var startArr = raw.IndexOf('[');

    int start;
    char open, close;

    if (startObj < 0 && startArr < 0) return null;
    if (startObj >= 0 && (startArr < 0 || startObj < startArr))
    {
        start = startObj; open = '{'; close = '}';
    }
    else
    {
        start = startArr; open = '['; close = ']';
    }

    int depth = 0;
    bool inStr = false, esc = false;

    for (int i = start; i < raw.Length; i++)
    {
        var ch = raw[i];

        if (inStr)
        {
            if (esc) { esc = false; continue; }
            if (ch == '\\') { esc = true; continue; }
            if (ch == '"') inStr = false;
            continue;
        }

        if (ch == '"') { inStr = true; continue; }

        if (ch == open) depth++;
        else if (ch == close)
        {
            depth--;
            if (depth == 0)
                return raw.Substring(start, i - start + 1);
        }
    }

    return null;
}

static string EscapeNewlinesInsideJsonStrings(string json)
{
    var sb = new StringBuilder(json.Length + 32);
    bool inStr = false, esc = false;

    foreach (var ch in json)
    {
        if (!inStr)
        {
            sb.Append(ch);
            if (ch == '"') inStr = true;
            continue;
        }

        if (esc)
        {
            esc = false;
            sb.Append(ch);
            continue;
        }

        if (ch == '\\')
        {
            esc = true;
            sb.Append(ch);
            continue;
        }

        if (ch == '"')
        {
            inStr = false;
            sb.Append(ch);
            continue;
        }

        if (ch == '\n') { sb.Append("\\n"); continue; }
        if (ch == '\r') { sb.Append("\\r"); continue; }
        if (ch == '\t') { sb.Append("\\t"); continue; }

        if (ch < ' ')
        {
            sb.Append("\\u");
            sb.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
            continue;
        }

        sb.Append(ch);
    }

    return sb.ToString();
}

static bool TryParseJson(string json, out JsonDocument? doc, out string? error)
{
    try
    {
        doc = JsonDocument.Parse(json);
        error = null;
        return true;
    }
    catch (Exception ex)
    {
        doc = null;
        error = ex.Message;
        return false;
    }
}

static bool TryParseSingleSummary(string? json, out SummaryInfo info)
{
    info = new SummaryInfo("", Array.Empty<string>());
    if (string.IsNullOrWhiteSpace(json)) return false;

    try
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var summary = (GetStringAny(root, "summary", "Summary", "reason", "Reason") ?? "").Trim();
        var ms = new List<string>();

        if (TryGetArrayAny(root, out var arr, "matchedSkills", "MatchedSkills"))
        {
            foreach (var x in arr.EnumerateArray())
            {
                if (x.ValueKind != JsonValueKind.String) continue;
                var v = (x.GetString() ?? "").Trim();
                if (v.Length == 0) continue;
                ms.Add(v);
                if (ms.Count >= 3) break;
            }
        }

        info = new SummaryInfo(summary, ms.ToArray());
        return !string.IsNullOrWhiteSpace(info.Summary);
    }
    catch
    {
        return false;
    }
}

static bool TryParseBatchSummaries(string json, out Dictionary<string, SummaryInfo> dict)
{
    dict = new Dictionary<string, SummaryInfo>(StringComparer.OrdinalIgnoreCase);

    try
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var it in items.EnumerateArray())
        {
            if (it.ValueKind != JsonValueKind.Object) continue;
            var key = GetStringAny(it, "key", "Key");
            var summary = GetStringAny(it, "summary", "Summary");
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(summary)) continue;

            var ms = new List<string>();
            if (TryGetArrayAny(it, out var arr, "matchedSkills", "MatchedSkills"))
            {
                foreach (var x in arr.EnumerateArray())
                {
                    if (x.ValueKind != JsonValueKind.String) continue;
                    var v = (x.GetString() ?? "").Trim();
                    if (v.Length == 0) continue;
                    if (!ms.Contains(v, StringComparer.OrdinalIgnoreCase))
                        ms.Add(v);
                    if (ms.Count >= 3) break;
                }
            }

            dict[key!] = new SummaryInfo(summary!, ms.ToArray());
        }

        return dict.Count > 0;
    }
    catch
    {
        return false;
    }
}

static async Task<(bool ok, string? json, object detail)> CallJsonWithRetryAsync(
    HttpClient llm, string system, string user, SemaphoreSlim gate, CancellationToken ct)
{
    var c1 = await LlamaChatSafeAsync(llm, system, user, temperature: 0.35, maxTokens: 800, gate, ct);
    if (!c1.ok)
        return (false, null, new { attempt = 1, c1.status, c1.error });

    var j1 = ExtractFirstCompleteJsonValue(c1.content);
    if (j1 is not null && TryParseJson(EscapeNewlinesInsideJsonStrings(j1), out _, out _))
        return (true, EscapeNewlinesInsideJsonStrings(j1), new { attempt = 1, finish = c1.finish });

    var sys2 = system + "\nReturn ONE complete JSON object matching the schema.";
    var c2 = await LlamaChatSafeAsync(llm, sys2, user, temperature: 0.25, maxTokens: 1200, gate, ct);
    if (!c2.ok)
        return (false, null, new { attempt = 2, c2.status, c2.error });

    var j2 = ExtractFirstCompleteJsonValue(c2.content);
    if (j2 is not null && TryParseJson(EscapeNewlinesInsideJsonStrings(j2), out _, out _))
        return (true, EscapeNewlinesInsideJsonStrings(j2), new { attempt = 2, finish = c2.finish });

    return (false, null, new
    {
        attempt1 = new { finish = c1.finish, preview = Clip(c1.content, 600) },
        attempt2 = new { finish = c2.finish, preview = Clip(c2.content, 600) }
    });
}

// ======================================================================
// DB helpers
// ======================================================================

static void InitDb(string dbPath, string vecExtPath, int dim)
{
    using var conn = OpenDbSafe(dbPath, vecExtPath);

    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = """
        create table if not exists items(
          point_id text primary key,
          type text not null,
          entity_id text,
          title text,
          semester_id text,
          major_id text,
          text text not null
        );
        """;
        cmd.ExecuteNonQuery();
    }

    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = $"""
        create virtual table if not exists item_vec using vec0(
          point_id text primary key,
          embedding float[{dim}]
        );
        """;
        cmd.ExecuteNonQuery();
    }
}

static SqliteConnection OpenDbSafe(string dbPath, string vecExtPath)
{
    var conn = new SqliteConnection($"Data Source={dbPath};Cache=Shared;Pooling=True;Default Timeout=5;");
    conn.Open();

    conn.EnableExtensions(true);
    conn.LoadExtension(vecExtPath);

    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = """
        pragma journal_mode = WAL;
        pragma synchronous = NORMAL;
        pragma temp_store = MEMORY;
        pragma busy_timeout = 5000;
        """;
        cmd.ExecuteNonQuery();
    }

    return conn;
}

// ======================================================================
// Health helpers
// ======================================================================

static async Task<object> CheckHttpAsync(string name, HttpClient c, string path, CancellationToken ct)
{
    var sw = Stopwatch.StartNew();
    try
    {
        using var res = await c.GetAsync(path, ct);
        var txt = await res.Content.ReadAsStringAsync(ct);
        sw.Stop();
        return new { name, ok = res.IsSuccessStatusCode, status = (int)res.StatusCode, ms = sw.ElapsedMilliseconds, sample = TrimOneLine(txt, 160) };
    }
    catch (Exception ex)
    {
        sw.Stop();
        return new { name, ok = false, status = 0, ms = sw.ElapsedMilliseconds, error = ex.Message };
    }
}

static async Task<object> CheckEmbeddingsAsync(HttpClient embed, CancellationToken ct)
{
    var sw = Stopwatch.StartNew();
    try
    {
        using var res = await embed.PostAsJsonAsync("/v1/embeddings", new { input = "ping" }, ct);
        var txt = await res.Content.ReadAsStringAsync(ct);
        sw.Stop();
        return new { name = "embeddings", ok = res.IsSuccessStatusCode, status = (int)res.StatusCode, ms = sw.ElapsedMilliseconds, sample = TrimOneLine(txt, 160) };
    }
    catch (Exception ex)
    {
        sw.Stop();
        return new { name = "embeddings", ok = false, status = 0, ms = sw.ElapsedMilliseconds, error = ex.Message };
    }
}

static async Task<object> CheckRerankAsync(HttpClient rr, CancellationToken ct)
{
    var sw = Stopwatch.StartNew();
    try
    {
        var payload = new { query = "backend developer", candidates = new[] { "C# ASP.NET Core Docker", "cooking hobbies" } };
        using var res = await rr.PostAsJsonAsync("/rerank", payload, ct);
        var txt = await res.Content.ReadAsStringAsync(ct);
        sw.Stop();
        return new { name = "rerank", ok = res.IsSuccessStatusCode, status = (int)res.StatusCode, ms = sw.ElapsedMilliseconds, sample = TrimOneLine(txt, 160) };
    }
    catch (Exception ex)
    {
        sw.Stop();
        return new { name = "rerank", ok = false, status = 0, ms = sw.ElapsedMilliseconds, error = ex.Message };
    }
}

static object CheckDb(string dbPath, string vecExtPath)
{
    var sw = Stopwatch.StartNew();
    try
    {
        using var conn = OpenDbSafe(dbPath, vecExtPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "select 1;";
        cmd.ExecuteScalar();
        sw.Stop();
        return new { name = "sqlite", ok = true, ms = sw.ElapsedMilliseconds, dbPath, vecExtPath };
    }
    catch (Exception ex)
    {
        sw.Stop();
        return new { name = "sqlite", ok = false, ms = sw.ElapsedMilliseconds, dbPath, vecExtPath, error = ex.Message };
    }
}

static bool GetBoolProp(object obj, string prop)
{
    var pi = obj.GetType().GetProperty(prop);
    return pi is not null && pi.PropertyType == typeof(bool) && (bool)(pi.GetValue(obj) ?? false);
}

// ======================================================================
// Generic helpers (NO char->string issues)
// ======================================================================

static JsonSerializerOptions JsonOpts() => new()
{
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.AllowReadingFromString
};

static async Task<string> ReadBodyAsync(HttpRequest req)
{
    using var sr = new StreamReader(req.Body, Encoding.UTF8);
    return await sr.ReadToEndAsync();
}

static string Clip(string s, int maxLen)
{
    if (string.IsNullOrEmpty(s)) return s;
    if (s.Length <= maxLen) return s;
    return s[..maxLen] + "...";
}

static string TrimOneLine(string s, int maxLen)
{
    s = (s ?? "").Replace("\r", "").Replace("\n", " ").Trim();
    return Clip(s, maxLen);
}

static string? GetStringAny(JsonElement el, params string[] names)
{
    foreach (var n in names)
    {
        if (el.TryGetProperty(n, out var p))
        {
            if (p.ValueKind == JsonValueKind.String) return p.GetString();
            if (p.ValueKind != JsonValueKind.Null && p.ValueKind != JsonValueKind.Undefined) return p.ToString();
        }
    }
    return null;
}

static int? GetIntAny(JsonElement el, params string[] names)
{
    foreach (var n in names)
    {
        if (el.TryGetProperty(n, out var p))
        {
            if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v)) return v;
            if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v2)) return v2;
        }
    }
    return null;
}

static bool TryGetArrayAny(JsonElement el, out JsonElement arr, params string[] names)
{
    foreach (var n in names)
    {
        if (el.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Array)
        {
            arr = p;
            return true;
        }
    }
    arr = default;
    return false;
}

static bool TryGetObjectAny(JsonElement el, out JsonElement obj, params string[] names)
{
    foreach (var n in names)
    {
        if (el.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Object)
        {
            obj = p;
            return true;
        }
    }
    obj = default;
    return false;
}

static string NormalizeMode(string mode)
{
    mode = (mode ?? "topic").Trim().ToLowerInvariant();
    mode = mode.Replace("-", "_").Replace(" ", "_");

    return mode switch
    {
        "grouppost" => "group_post",
        "group_post" => "group_post",
        "personalpost" => "personal_post",
        "personal_post" => "personal_post",
        _ => "topic"
    };
}

// ======================================================================
// DTOs
// ======================================================================

record UpsertRequest(string Type, string? EntityId, string? Title, string? Text, string? SemesterId, string? MajorId, string? PointId);
record SearchRequest(string QueryText, string? Type, string? SemesterId, string? MajorId, int Limit, double? ScoreThreshold);
record ExtractSkillsRequest(string Text, List<string>? KnownSkills, int MaxSkills);

record SummaryInfo(string Summary, string[] MatchedSkills);

record TeamContext(
    string TeamName,
    string PrimaryNeed,
    List<string> Skills,
    int CurrentMixFe,
    int CurrentMixBe,
    int CurrentMixOther
);

record RerankCandidate(
    string Key,
    Guid EntityId,
    string Title,
    string Text,
    int BaselineScore,
    string? NeededRole,
    int GroupFrontend,
    int GroupBackend,
    int GroupOther
);

record RankedSeed(string Key, Guid EntityId, string Title, string Text, double FinalScore);

record RerankResultItem(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("finalScore")] double FinalScore,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("matchedSkills")] string[] MatchedSkills,
    [property: JsonPropertyName("balanceNote")] string BalanceNote
);

record GenerateGroupPostRequest(GroupInfo Group, ProjectInfo? Project, PostOptions? Options);
record GeneratePersonalPostRequest(PersonalUser User, PostOptions? Options);

record GroupInfo(
    string Name,
    string? PrimaryNeed,
    Mix? CurrentMix,
    Mix? OpenSlots,
    string[]? TeamTopSkills
);

record Mix(int Fe, int Be, int Other);
record ProjectInfo(string? Title, string? Summary);
record PersonalUser(string DisplayName, List<string>? Skills, string? Goal, string? Availability);
record PostOptions(string? Language, int? MaxWords, string? Tone);

record GroupPostSpec(string SuggestedBucket, string[] SkillBank);

sealed class LastDebug
{
    public DateTime? AtUtc { get; set; }
    public string? Mode { get; set; }
    public int CandidatesCount { get; set; }
    public int SeedsCount { get; set; }
    public int TopN { get; set; }
    public double MinScore { get; set; }
    public string? QueryTextPreview { get; set; }
    public string? LogitsPreview { get; set; }
    public string? CeScoresPreview { get; set; }
    public string? FinalScoresPreview { get; set; }
    public string? RequestPreview { get; set; }
    public string? SystemPreview { get; set; }
    public string? UserPreview { get; set; }
    public string? RawResponsePreview { get; set; }
}
