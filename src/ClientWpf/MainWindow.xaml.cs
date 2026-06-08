using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Shared;

namespace ClientWpf;

public partial class MainWindow : Window
{
    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    private UserDto? _user;
    private int? _attemptId;

    private List<QuestionDto> _questions = new();
    private int _qIndex = 0;

    // Accumulated answers: questionId -> set(optionId)
    private readonly Dictionary<int, HashSet<int>> _answers = new();

    public MainWindow()
    {
        InitializeComponent();
        Log("Not connected");
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var host = HostBox.Text.Trim();
            var port = int.Parse(PortBox.Text.Trim());

            _client = new TcpClient();
            await _client.ConnectAsync(host, port);

            var stream = _client.GetStream();
            _reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            _writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            Log($"Connected to {host}:{port}");
        }
        catch (Exception ex)
        {
            Log("Connect error: " + ex.Message);
        }
    }

    private void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _writer?.Dispose();
            _reader?.Dispose();
            _client?.Close();
        }
        catch { /* ignore */ }

        _writer = null;
        _reader = null;
        _client = null;
        _user = null;
        _attemptId = null;
        _questions.Clear();
        _answers.Clear();
        OptionsPanel.Children.Clear();
        QuestionText.Text = "";
        QuestionImage.Source = null;

        Log("Disconnected");
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnected()) return;

        var login = LoginBox.Text.Trim();
        var pass = PasswordBox.Password;

        var env = Protocol.Pack(MessageTypes.Login, new LoginRequest(login, pass));
        var respLine = await SendRawAsync(env);
        if (respLine is null) return;

        var envResp = Protocol.Unpack(respLine);
        var resp = Protocol.ReadPayload<LoginResponse>(envResp);

        if (!resp.Ok || resp.User is null)
        {
            Log("Login failed: " + resp.Error);
            return;
        }

        _user = resp.User;
        Log($"Login OK. User={_user.Login}, role={_user.Role}, maxAttempts={_user.MaxAttempts}");
    }

    private async void GetTests_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnected() || !EnsureLoggedIn()) return;

        var env = Protocol.Pack(MessageTypes.TestsGet, new TestsGetRequest(50));
        var respLine = await SendRawAsync(env);
        if (respLine is null) return;

        var envResp = Protocol.Unpack(respLine);
        var resp = Protocol.ReadPayload<TestsGetResponse>(envResp);

        if (!resp.Ok || resp.Tests is null)
        {
            Log("Get tests failed: " + resp.Error);
            return;
        }

        TestsList.ItemsSource = resp.Tests;
        Log($"Tests count: {resp.Tests.Count}");
    }

    private async void StartAttempt_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnected() || !EnsureLoggedIn()) return;
        if (TestsList.SelectedItem is not TestDto test)
        {
            Log("Select a test first");
            return;
        }

        // Load questions first
        var qEnv = Protocol.Pack(MessageTypes.QuestionsGet, new QuestionsGetRequest(test.Id));
        var qLine = await SendRawAsync(qEnv);
        if (qLine is null) return;
        var qResp = Protocol.ReadPayload<QuestionsGetResponse>(Protocol.Unpack(qLine));
        if (!qResp.Ok || qResp.Questions is null || qResp.Questions.Count == 0)
        {
            Log("No questions or error: " + qResp.Error);
            return;
        }

        _questions = qResp.Questions.ToList();
        _qIndex = 0;
        _answers.Clear();

        // Start attempt on server (saves to DB)
        var env = Protocol.Pack(MessageTypes.AttemptStart, new AttemptStartRequest(test.Id));
        var respLine = await SendRawAsync(env);
        if (respLine is null) return;

        var envResp = Protocol.Unpack(respLine);
        var resp = Protocol.ReadPayload<AttemptStartResponse>(envResp);

        if (!resp.Ok || resp.Attempt is null)
        {
            Log("Start attempt failed: " + resp.Error);
            return;
        }

        _attemptId = resp.Attempt.Id;
        Log($"Attempt started: id={_attemptId}");

        ShowQuestion();
    }

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        if (_questions.Count == 0) return;
        SaveUiSelections();
        _qIndex = Math.Max(0, _qIndex - 1);
        ShowQuestion();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_questions.Count == 0) return;
        SaveUiSelections();
        _qIndex = Math.Min(_questions.Count - 1, _qIndex + 1);
        ShowQuestion();
    }

    private async void SubmitAnswers_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnected() || !EnsureLoggedIn()) return;
        if (_attemptId is null)
        {
            Log("Start attempt first");
            return;
        }

        SaveUiSelections();

        var items = _answers.Select(kvp =>
            new SubmitAnswerItem(kvp.Key, kvp.Value.ToList())
        ).ToList();

        var env = Protocol.Pack(MessageTypes.AttemptSubmit, new AttemptSubmitRequest(_attemptId.Value, items));
        var respLine = await SendRawAsync(env);
        if (respLine is null) return;

        var resp = Protocol.ReadPayload<AttemptSubmitResponse>(Protocol.Unpack(respLine));
        if (!resp.Ok)
        {
            Log("Submit failed: " + resp.Error);
            return;
        }

        Log($"Submit OK. Answers saved in DB for attempt={_attemptId}");
    }

    private async void FinishAttempt_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnected() || !EnsureLoggedIn()) return;
        if (_attemptId is null)
        {
            Log("Start attempt first");
            return;
        }

        // ensure submitted
        await Task.Run(() => { });

        var env = Protocol.Pack(MessageTypes.AttemptFinish, new AttemptFinishRequest(_attemptId.Value));
        var respLine = await SendRawAsync(env);
        if (respLine is null) return;

        var resp = Protocol.ReadPayload<AttemptFinishResponse>(Protocol.Unpack(respLine));
        if (!resp.Ok)
        {
            Log("Finish failed: " + resp.Error);
            return;
        }

        Log($"Attempt finished. Score={resp.Score}");
        _attemptId = null;
    }

    private void ShowQuestion()
    {
        if (_questions.Count == 0) return;

        var q = _questions[_qIndex];
        Title = $"Test Client - Q {_qIndex + 1}/{_questions.Count}";

        QuestionText.Text = q.Text;

        // image
        QuestionImage.Source = null;
        if (!string.IsNullOrWhiteSpace(q.ImageBase64))
        {
            try
            {
                var bytes = Convert.FromBase64String(q.ImageBase64);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                QuestionImage.Source = bmp;
            }
            catch
            {
                // ignore invalid image
            }
        }

        OptionsPanel.Children.Clear();

        // Build UI controls for options
        var selected = _answers.TryGetValue(q.Id, out var set) ? set : new HashSet<int>();

        if (q.Kind == QuestionKind.Single)
        {
            var group = $"q_{q.Id}";
            foreach (var opt in q.Options)
            {
                var rb = new RadioButton
                {
                    Content = opt.Text,
                    Tag = opt.Id,
                    GroupName = group,
                    Margin = new Thickness(0, 0, 0, 6),
                    IsChecked = selected.Contains(opt.Id)
                };
                OptionsPanel.Children.Add(rb);
            }
        }
        else
        {
            foreach (var opt in q.Options)
            {
                var cb = new CheckBox
                {
                    Content = opt.Text,
                    Tag = opt.Id,
                    Margin = new Thickness(0, 0, 0, 6),
                    IsChecked = selected.Contains(opt.Id)
                };
                OptionsPanel.Children.Add(cb);
            }
        }

        Log($"Showing question id={q.Id}");
    }

    private void SaveUiSelections()
    {
        if (_questions.Count == 0) return;
        var q = _questions[_qIndex];

        var set = new HashSet<int>();
        foreach (var child in OptionsPanel.Children)
        {
            if (q.Kind == QuestionKind.Single && child is RadioButton rb)
            {
                if (rb.IsChecked == true) set.Add((int)rb.Tag);
            }
            if (q.Kind == QuestionKind.Multiple && child is CheckBox cb)
            {
                if (cb.IsChecked == true) set.Add((int)cb.Tag);
            }
        }

        _answers[q.Id] = set;
    }

    private bool EnsureConnected()
    {
        if (_client is null || _reader is null || _writer is null)
        {
            Log("Not connected");
            return false;
        }
        return true;
    }

    private bool EnsureLoggedIn()
    {
        if (_user is null)
        {
            Log("Login first");
            return false;
        }
        return true;
    }

    private async Task<string?> SendRawAsync(string line)
    {
        try
        {
            if (_writer is null || _reader is null) return null;

            Log("SEND: " + line);
            await _writer.WriteLineAsync(line);

            var resp = await _reader.ReadLineAsync();
            if (resp is null)
            {
                Log("Connection closed by server");
                return null;
            }

            Log("RECV: " + resp);
            return resp;
        }
        catch (Exception ex)
        {
            Log("Network error: " + ex.Message);
            return null;
        }
    }

    private void Log(string msg)
    {
        LogBox.AppendText(msg + Environment.NewLine);
        LogBox.ScrollToEnd();
    }
}
