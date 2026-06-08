using System.Text.Json.Serialization;

namespace Shared;

public enum QuestionKind
{
    Single,
    Multiple
}

public sealed record UserDto(int Id, string Login, string Role, int MaxAttempts);

public sealed record TestDto(int Id, string Title, string? Description);

public sealed record AnswerOptionDto(int Id, int QuestionId, string Text, bool IsCorrect);

public sealed record QuestionDto(
    int Id,
    int TestId,
    string Text,
    QuestionKind Kind,
    int Weight,
    string? ImageBase64,
    IReadOnlyList<AnswerOptionDto> Options
);

public sealed record AttemptDto(int Id, int UserId, int TestId, DateTime StartedAtUtc, DateTime? FinishedAtUtc, int? Score);


public sealed record AttemptResultDto(
    int AttemptId,
    int TestId,
    string TestTitle,
    int UserId,
    string StudentLogin,
    string StudentRole,
    DateTime StartedAtUtc,
    DateTime? FinishedAtUtc,
    int? Score
);
