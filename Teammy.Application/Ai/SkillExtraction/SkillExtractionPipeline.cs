using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Teammy.Application.Ai.Models;
using Teammy.Application.Common.Interfaces;

namespace Teammy.Application.Ai.SkillExtraction;

public static class SkillExtractionPipeline
{
    private const int DefaultChunkChars = 3500;
    private const int MaxChunks = 12;
    private const int MaxSkills = 50;

    public static async Task<IReadOnlyList<string>> ExtractSkillsAsync(
        IAiLlmClient llmClient,
        string sourceType,
        Guid sourceId,
        string fullText,
        CancellationToken ct,
        int chunkSize = DefaultChunkChars)
    {
        if (llmClient is null)
            throw new ArgumentNullException(nameof(llmClient));
        if (string.IsNullOrWhiteSpace(fullText))
            return Array.Empty<string>();

        var segments = ChunkText(fullText, chunkSize).ToList();
        if (segments.Count == 0)
            return Array.Empty<string>();

        var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < segments.Count; i++)
        {
            var content = segments[i];
            var request = new AiSkillExtractionRequest(
                sourceType,
                $"{sourceId:N}:{i + 1}",
                content);

            AiSkillExtractionResponse? response = null;
            try
            {
                response = await llmClient.ExtractSkillsAsync(request, ct);
            }
            catch
            {
                // ignore extraction errors for this chunk
            }

            if (response?.Skills is null)
                continue;

            foreach (var skill in response.Skills)
            {
                if (string.IsNullOrWhiteSpace(skill.Name))
                    continue;

                var name = skill.Name.Trim();
                var confidence = double.IsFinite(skill.Confidence) ? skill.Confidence : 0;
                if (!scores.TryGetValue(name, out var existing) || confidence > existing)
                    scores[name] = confidence;
            }
        }

        if (scores.Count == 0)
            return Array.Empty<string>();

        return scores
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair.Key)
            .Take(MaxSkills)
            .ToList();
    }

    private static IEnumerable<string> ChunkText(string text, int chunkSize)
    {
        if (chunkSize <= 0)
            chunkSize = DefaultChunkChars;

        var normalized = text.Replace("\r\n", "\n");
        var total = normalized.Length;
        if (total == 0)
            yield break;

        var chunkIndex = 0;
        var cursor = 0;
        while (cursor < total && chunkIndex < MaxChunks)
        {
            var remaining = total - cursor;
            var length = Math.Min(chunkSize, remaining);
            var slice = normalized.AsSpan(cursor, length);

            // try to break on newline to keep sentences together
            var lastBreak = slice.LastIndexOf('\n');
            if (lastBreak > chunkSize / 2)
            {
                length = lastBreak + 1;
                slice = normalized.AsSpan(cursor, length);
            }

            yield return slice.ToString();
            cursor += length;
            chunkIndex++;
        }
    }
}
