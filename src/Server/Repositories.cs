using Npgsql;
using Shared;

namespace Server;

public sealed class UserRepository
{
    private readonly NpgsqlDataSource _ds;
    public UserRepository(NpgsqlDataSource ds) => _ds = ds;

    public async Task<UserDto?> FindByLoginAsync(string login, CancellationToken ct)
    {
        await using var cmd = _ds.CreateCommand(
            "select id, login, role, max_attempts from users where login=@login");
        cmd.Parameters.AddWithValue("login", login);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new UserDto(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3)
        );
    }

    public async Task<string?> GetPasswordHashAsync(string login, CancellationToken ct)
    {
        await using var cmd = _ds.CreateCommand(
            "select password_hash from users where login=@login");
        cmd.Parameters.AddWithValue("login", login);
        var obj = await cmd.ExecuteScalarAsync(ct);
        return obj as string;
    }
}

public sealed class TestRepository
{
    private readonly NpgsqlDataSource _ds;
    public TestRepository(NpgsqlDataSource ds) => _ds = ds;

    public async Task<IReadOnlyList<TestDto>> GetTestsAsync(int limit, CancellationToken ct)
    {
        var list = new List<TestDto>();
        await using var cmd = _ds.CreateCommand(
            "select id, title, description from tests order by id limit @limit");
        cmd.Parameters.AddWithValue("limit", limit);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new TestDto(r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));
        }
        return list;
    }

    public async Task<int> CreateTestAsync(int? createdBy, string title, string? description, CancellationToken ct)
    {
        await using var cmd = _ds.CreateCommand(
            "insert into tests(title, description, created_by) values(@t,@d,@cb) returning id");
        cmd.Parameters.AddWithValue("t", title);
        cmd.Parameters.AddWithValue("d", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("cb", (object?)createdBy ?? DBNull.Value);
        return (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);
    }
}

public sealed class QuestionRepository
{
    private readonly NpgsqlDataSource _ds;
    public QuestionRepository(NpgsqlDataSource ds) => _ds = ds;

    public async Task<IReadOnlyList<QuestionDto>> GetQuestionsWithOptionsAsync(int testId, CancellationToken ct)
    {
        // Fetch questions
        var questions = new List<(int Id, int TestId, string Text, string Kind, int Weight, string? Img)>();
        await using (var qcmd = _ds.CreateCommand(
            "select id, test_id, text, kind, weight, image_base64 from questions where test_id=@tid order by id"))
        {
            qcmd.Parameters.AddWithValue("tid", testId);
            await using var r = await qcmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                questions.Add((
                    r.GetInt32(0),
                    r.GetInt32(1),
                    r.GetString(2),
                    r.GetString(3),
                    r.GetInt32(4),
                    r.IsDBNull(5) ? null : r.GetString(5)
                ));
            }
        }

        // Fetch options for those questions
        var optionMap = new Dictionary<int, List<AnswerOptionDto>>();
        if (questions.Count > 0)
        {
            var ids = questions.Select(q => q.Id).ToArray();
            await using var ocmd = _ds.CreateCommand(
                "select id, question_id, text, is_correct from options where question_id = any(@ids) order by id");
            ocmd.Parameters.AddWithValue("ids", ids);
            await using var r = await ocmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var opt = new AnswerOptionDto(
                    r.GetInt32(0),
                    r.GetInt32(1),
                    r.GetString(2),
                    r.GetBoolean(3)
                );
                if (!optionMap.TryGetValue(opt.QuestionId, out var list))
                {
                    list = new List<AnswerOptionDto>();
                    optionMap[opt.QuestionId] = list;
                }
                list.Add(opt);
            }
        }

        var result = new List<QuestionDto>();
        foreach (var q in questions)
        {
            var kind = q.Kind.Equals("multiple", StringComparison.OrdinalIgnoreCase)
                ? QuestionKind.Multiple
                : QuestionKind.Single;

            optionMap.TryGetValue(q.Id, out var opts);
            result.Add(new QuestionDto(
                q.Id, q.TestId, q.Text, kind, q.Weight, q.Img,
                (IReadOnlyList<AnswerOptionDto>)(opts ?? new List<AnswerOptionDto>())
            ));
        }
        return result;
    }

    public async Task<int> CreateQuestionAsync(int testId, string text, string kind, int weight, string? img, CancellationToken ct)
    {
        await using var cmd = _ds.CreateCommand(
            "insert into questions(test_id, text, kind, weight, image_base64) values(@tid,@txt,@k,@w,@img) returning id");
        cmd.Parameters.AddWithValue("tid", testId);
        cmd.Parameters.AddWithValue("txt", text);
        cmd.Parameters.AddWithValue("k", kind);
        cmd.Parameters.AddWithValue("w", weight);
        cmd.Parameters.AddWithValue("img", (object?)img ?? DBNull.Value);
        return (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);
    }

    public async Task<int> CreateOptionAsync(int questionId, string text, bool isCorrect, CancellationToken ct)
    {
        await using var cmd = _ds.CreateCommand(
            "insert into options(question_id, text, is_correct) values(@qid,@txt,@c) returning id");
        cmd.Parameters.AddWithValue("qid", questionId);
        cmd.Parameters.AddWithValue("txt", text);
        cmd.Parameters.AddWithValue("c", isCorrect);
        return (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);
    }
}

public sealed class AttemptRepository
{
    private readonly NpgsqlDataSource _ds;
    public AttemptRepository(NpgsqlDataSource ds) => _ds = ds;

    public async Task<int> CountFinishedAttemptsAsync(int userId, int testId, CancellationToken ct)
    {
        await using var cmd = _ds.CreateCommand(
            "select count(*) from attempts where user_id=@u and test_id=@t and status='finished'");
        cmd.Parameters.AddWithValue("u", userId);
        cmd.Parameters.AddWithValue("t", testId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<int> StartAttemptAsync(int userId, int testId, CancellationToken ct)
    {
        // Заполняем служебные поля сразу: статус и время старта.
        // В нашей схеме есть и started_at, и started_at_utc — заполним оба,
        // чтобы pgAdmin/отчёты показывали старт понятным образом.
        await using var cmd = _ds.CreateCommand(
            "insert into attempts(user_id, test_id, status, started_at, started_at_utc) values(@u,@t,'started', now(), now()) returning id");
        cmd.Parameters.AddWithValue("u", userId);
        cmd.Parameters.AddWithValue("t", testId);
        return (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);
    }

    public async Task ClearAnswersAsync(int attemptId, CancellationToken ct)
    {
        await using var cmd = _ds.CreateCommand("delete from answers where attempt_id=@a");
        cmd.Parameters.AddWithValue("a", attemptId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task InsertAnswersAsync(int attemptId, IReadOnlyList<SubmitAnswerItem> answers, CancellationToken ct)
    {
        // Транзакция создаётся на соединении, а не на NpgsqlDataSource.
        await using var conn = await _ds.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        foreach (var item in answers)
        {
            foreach (var optId in item.OptionIds)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "insert into answers(attempt_id, question_id, option_id) values(@a,@q,@o)";
                cmd.Parameters.AddWithValue("a", attemptId);
                cmd.Parameters.AddWithValue("q", item.QuestionId);
                cmd.Parameters.AddWithValue("o", optId);
                cmd.Transaction = tx;
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }

        await tx.CommitAsync(ct);
    }

    public async Task<int> FinishAttemptAndScoreAsync(int attemptId, CancellationToken ct)
    {
        // Score: sum weights for questions answered exactly with correct set
        // get testId + questions for that attempt
        int testId;
        await using (var cmd = _ds.CreateCommand("select test_id from attempts where id=@a"))
        {
            cmd.Parameters.AddWithValue("a", attemptId);
            var obj = await cmd.ExecuteScalarAsync(ct);
            if (obj is null) return 0;
            testId = Convert.ToInt32(obj);
        }

        // load questions and correct sets
        var qWeights = new Dictionary<int, int>();
        var correct = new Dictionary<int, HashSet<int>>();
        await using (var cmd = _ds.CreateCommand("select id, weight from questions where test_id=@t"))
        {
            cmd.Parameters.AddWithValue("t", testId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                qWeights[r.GetInt32(0)] = r.GetInt32(1);
            }
        }
        if (qWeights.Count == 0) return 0;

        var qIds = qWeights.Keys.ToArray();
        await using (var cmd = _ds.CreateCommand(
            "select question_id, id from options where is_correct=true and question_id = any(@q)"))
        {
            cmd.Parameters.AddWithValue("q", qIds);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var qid = r.GetInt32(0);
                var oid = r.GetInt32(1);
                if (!correct.TryGetValue(qid, out var set))
                {
                    set = new HashSet<int>();
                    correct[qid] = set;
                }
                set.Add(oid);
            }
        }

        // selected sets
        var selected = new Dictionary<int, HashSet<int>>();
        await using (var cmd = _ds.CreateCommand(
            "select question_id, option_id from answers where attempt_id=@a"))
        {
            cmd.Parameters.AddWithValue("a", attemptId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var qid = r.GetInt32(0);
                var oid = r.GetInt32(1);
                if (!selected.TryGetValue(qid, out var set))
                {
                    set = new HashSet<int>();
                    selected[qid] = set;
                }
                set.Add(oid);
            }
        }

        int score = 0;
        foreach (var (qid, weight) in qWeights)
        {
            correct.TryGetValue(qid, out var corrSet);
            corrSet ??= new HashSet<int>();
            selected.TryGetValue(qid, out var selSet);
            selSet ??= new HashSet<int>();

            if (corrSet.SetEquals(selSet))
                score += weight;
        }

        // Обновляем и finished_at (локальное) и finished_at_utc, чтобы в pgAdmin
        // пользователь видел заполнение независимо от того, какие колонки он смотрит.
        await using (var cmd = _ds.CreateCommand(
            "update attempts set finished_at=now(), finished_at_utc=now(), score=@s, status='finished' where id=@a"))
        {
            cmd.Parameters.AddWithValue("s", score);
            cmd.Parameters.AddWithValue("a", attemptId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        return score;
    }

    public async Task<IReadOnlyList<AttemptResultDto>> GetResultsAsync(int limit, int? testId, CancellationToken ct)
    {
        var list = new List<AttemptResultDto>();

        // We only show finished attempts (results)
        var sql = @"
select a.id as attempt_id,
       a.test_id,
       t.title as test_title,
       a.user_id,
       u.login as student_login,
       u.role as student_role,
       a.started_at_utc,
       a.finished_at_utc,
       a.score
from attempts a
join users u on u.id = a.user_id
join tests t on t.id = a.test_id
where a.status='finished'
  and (@tid is null or a.test_id=@tid)
order by a.finished_at_utc desc nulls last, a.id desc
limit @lim;";

        await using var cmd = _ds.CreateCommand(sql);
        cmd.Parameters.AddWithValue("lim", limit);
        // Если testId == null, Npgsql не может вывести тип параметра из DBNull.
        // Поэтому задаём тип явно.
        var pTid = cmd.Parameters.Add("tid", NpgsqlTypes.NpgsqlDbType.Integer);
        pTid.Value = (object?)testId ?? DBNull.Value;

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new AttemptResultDto(
                AttemptId: r.GetInt32(0),
                TestId: r.GetInt32(1),
                TestTitle: r.GetString(2),
                UserId: r.GetInt32(3),
                StudentLogin: r.GetString(4),
                StudentRole: r.GetString(5),
                StartedAtUtc: r.GetDateTime(6),
                FinishedAtUtc: r.IsDBNull(7) ? null : r.GetDateTime(7),
                Score: r.IsDBNull(8) ? null : r.GetInt32(8)
            ));
        }
        return list;
    }
}
