using System.Text.Json;
using Npgsql;
using Shared;

namespace Server;

public sealed class RequestRouter
{
    private readonly UserRepository _users;
    private readonly TestRepository _tests;
    private readonly QuestionRepository _questions;
    private readonly AttemptRepository _attempts;

    public RequestRouter(NpgsqlDataSource ds)
    {
        _users = new UserRepository(ds);
        _tests = new TestRepository(ds);
        _questions = new QuestionRepository(ds);
        _attempts = new AttemptRepository(ds);
    }

    public async Task<string> HandleAsync(SessionState session, string type, JsonElement payload, CancellationToken ct)
    {
        try
        {
            return type switch
            {
                MessageTypes.Login => await LoginAsync(session, payload, ct),

                MessageTypes.TestsGet => await TestsGetAsync(session, payload, ct),

                MessageTypes.QuestionsGet => await QuestionsGetAsync(session, payload, ct),

                MessageTypes.AttemptStart => await AttemptStartAsync(session, payload, ct),
                MessageTypes.AttemptSubmit => await AttemptSubmitAsync(session, payload, ct),
                MessageTypes.AttemptFinish => await AttemptFinishAsync(session, payload, ct),

                MessageTypes.ResultsGet => await ResultsGetAsync(session, payload, ct),

                MessageTypes.TestsCreate => await CreateTestAsync(session, payload, ct),
                MessageTypes.QuestionsCreate => await CreateQuestionAsync(session, payload, ct),
                MessageTypes.OptionsCreate => await CreateOptionAsync(session, payload, ct),

                _ => Protocol.Pack("error", new ErrorResponse(false, $"Unknown message type: {type}"))
            };
        }
        catch (Exception ex)
        {
            return Protocol.Pack("error", new ErrorResponse(false, ex.Message));
        }
    }

    private static void EnsureLoggedIn(SessionState session)
    {
        if (session.User is null) throw new InvalidOperationException("Login required");
    }

    private static void EnsureTeacher(SessionState session)
    {
        EnsureLoggedIn(session);
        if (!string.Equals(session.User!.Role, "teacher", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Teacher role required");
    }

    private async Task<string> LoginAsync(SessionState session, JsonElement payload, CancellationToken ct)
    {
        var req = payload.Deserialize<LoginRequest>(Protocol.Options)!;

        var hash = await _users.GetPasswordHashAsync(req.Login, ct);
        if (hash is null)
            return Protocol.Pack(MessageTypes.Login, new LoginResponse(false, "User not found", null));

        // demo: password_hash stored as plain text
        if (!string.Equals(hash, req.Password, StringComparison.Ordinal))
            return Protocol.Pack(MessageTypes.Login, new LoginResponse(false, "Wrong password", null));

        var user = await _users.FindByLoginAsync(req.Login, ct);
        session.User = user;
        return Protocol.Pack(MessageTypes.Login, new LoginResponse(true, null, user));
    }

    private async Task<string> TestsGetAsync(SessionState session, JsonElement payload, CancellationToken ct)
    {
        EnsureLoggedIn(session);
        var req = payload.Deserialize<TestsGetRequest>(Protocol.Options)!;
        var tests = await _tests.GetTestsAsync(req.Limit, ct);
        return Protocol.Pack(MessageTypes.TestsGet, new TestsGetResponse(true, null, tests));
    }

    private async Task<string> QuestionsGetAsync(SessionState session, JsonElement payload, CancellationToken ct)
    {
        EnsureLoggedIn(session);
        var req = payload.Deserialize<QuestionsGetRequest>(Protocol.Options)!;
        var questions = await _questions.GetQuestionsWithOptionsAsync(req.TestId, ct);
        return Protocol.Pack(MessageTypes.QuestionsGet, new QuestionsGetResponse(true, null, questions));
    }

    private async Task<string> AttemptStartAsync(SessionState session, JsonElement payload, CancellationToken ct)
    {
        EnsureLoggedIn(session);
        var user = session.User!;

        var req = payload.Deserialize<AttemptStartRequest>(Protocol.Options)!;

        var used = await _attempts.CountFinishedAttemptsAsync(user.Id, req.TestId, ct);
        if (used >= user.MaxAttempts)
            return Protocol.Pack(MessageTypes.AttemptStart, new AttemptStartResponse(false, "No attempts left", null));

        var attemptId = await _attempts.StartAttemptAsync(user.Id, req.TestId, ct);
        var attempt = new AttemptDto(attemptId, user.Id, req.TestId, DateTime.UtcNow, null, null);
        return Protocol.Pack(MessageTypes.AttemptStart, new AttemptStartResponse(true, null, attempt));
    }

    private async Task<string> AttemptSubmitAsync(SessionState session, JsonElement payload, CancellationToken ct)
    {
        EnsureLoggedIn(session);
        var req = payload.Deserialize<AttemptSubmitRequest>(Protocol.Options)!;

        await _attempts.ClearAnswersAsync(req.AttemptId, ct);
        await _attempts.InsertAnswersAsync(req.AttemptId, req.Answers, ct);

        return Protocol.Pack(MessageTypes.AttemptSubmit, new AttemptSubmitResponse(true, null));
    }

    private async Task<string> AttemptFinishAsync(SessionState session, JsonElement payload, CancellationToken ct)
    {
        EnsureLoggedIn(session);
        var req = payload.Deserialize<AttemptFinishRequest>(Protocol.Options)!;

        var score = await _attempts.FinishAttemptAndScoreAsync(req.AttemptId, ct);
        return Protocol.Pack(MessageTypes.AttemptFinish, new AttemptFinishResponse(true, null, score));
    }

    private async Task<string> ResultsGetAsync(SessionState session, JsonElement payload, CancellationToken ct)
    {
        EnsureTeacher(session);
        var req = payload.Deserialize<ResultsGetRequest>(Protocol.Options)!;
        var rows = await _attempts.GetResultsAsync(req.Limit, req.TestId, ct);
        return Protocol.Pack(MessageTypes.ResultsGet, new ResultsGetResponse(true, null, rows));
    }

    private async Task<string> CreateTestAsync(SessionState session, JsonElement payload, CancellationToken ct)
    {
        EnsureTeacher(session);
        var req = payload.Deserialize<CreateTestRequest>(Protocol.Options)!;
        var id = await _tests.CreateTestAsync(session.User!.Id, req.Title, req.Description, ct);
        return Protocol.Pack(MessageTypes.TestsCreate, new CreateTestResponse(true, null, id));
    }

    private async Task<string> CreateQuestionAsync(SessionState session, JsonElement payload, CancellationToken ct)
    {
        EnsureTeacher(session);
        var req = payload.Deserialize<CreateQuestionRequest>(Protocol.Options)!;
        var kind = req.Kind.ToLowerInvariant();
        if (kind != "single" && kind != "multiple")
            return Protocol.Pack(MessageTypes.QuestionsCreate, new CreateQuestionResponse(false, "kind must be 'single' or 'multiple'", null));

        var id = await _questions.CreateQuestionAsync(req.TestId, req.Text, kind, req.Weight, req.ImageBase64, ct);
        return Protocol.Pack(MessageTypes.QuestionsCreate, new CreateQuestionResponse(true, null, id));
    }

    private async Task<string> CreateOptionAsync(SessionState session, JsonElement payload, CancellationToken ct)
    {
        EnsureTeacher(session);
        var req = payload.Deserialize<CreateOptionRequest>(Protocol.Options)!;
        var id = await _questions.CreateOptionAsync(req.QuestionId, req.Text, req.IsCorrect, ct);
        return Protocol.Pack(MessageTypes.OptionsCreate, new CreateOptionResponse(true, null, id));
    }

}
