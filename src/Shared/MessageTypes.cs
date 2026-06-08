namespace Shared;

public static class MessageTypes
{
    public const string Login = "login";
    public const string TestsGet = "tests.get";

    public const string QuestionsGet = "questions.get";

    public const string AttemptStart = "attempt.start";
    public const string AttemptSubmit = "attempt.submit";
    public const string AttemptFinish = "attempt.finish";

    // Teacher/admin
    public const string ResultsGet = "results.get";

    public const string TestsCreate = "tests.create";
    public const string QuestionsCreate = "questions.create";
    public const string OptionsCreate = "options.create";
    public const string OptionsGet = "options.get";
}
