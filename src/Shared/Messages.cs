namespace Shared;

// Envelope: one JSON per line
public sealed record Envelope(string Type, object Payload);

// Common error response
public sealed record ErrorResponse(bool Ok, string Error);

// Login
public sealed record LoginRequest(string Login, string Password);
public sealed record LoginResponse(bool Ok, string? Error, UserDto? User);

// Tests
public sealed record TestsGetRequest(int Limit);
public sealed record TestsGetResponse(bool Ok, string? Error, IReadOnlyList<TestDto>? Tests);

// Questions
public sealed record QuestionsGetRequest(int TestId);
public sealed record QuestionsGetResponse(bool Ok, string? Error, IReadOnlyList<QuestionDto>? Questions);

// Attempt
public sealed record AttemptStartRequest(int TestId);
public sealed record AttemptStartResponse(bool Ok, string? Error, AttemptDto? Attempt);

public sealed record SubmitAnswerItem(int QuestionId, IReadOnlyList<int> OptionIds);
public sealed record AttemptSubmitRequest(int AttemptId, IReadOnlyList<SubmitAnswerItem> Answers);
public sealed record AttemptSubmitResponse(bool Ok, string? Error);

public sealed record AttemptFinishRequest(int AttemptId);
public sealed record AttemptFinishResponse(bool Ok, string? Error, int? Score);

// Results (teacher/admin)
public sealed record ResultsGetRequest(int Limit, int? TestId);
public sealed record ResultsGetResponse(bool Ok, string? Error, IReadOnlyList<AttemptResultDto>? Results);

// Editor - create
public sealed record CreateTestRequest(string Title, string? Description);
public sealed record CreateTestResponse(bool Ok, string? Error, int? TestId);

public sealed record CreateQuestionRequest(int TestId, string Text, string Kind, int Weight, string? ImageBase64);
public sealed record CreateQuestionResponse(bool Ok, string? Error, int? QuestionId);

public sealed record CreateOptionRequest(int QuestionId, string Text, bool IsCorrect);
public sealed record CreateOptionResponse(bool Ok, string? Error, int? OptionId);
