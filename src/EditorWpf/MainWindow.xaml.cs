using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Shared;

namespace EditorWpf;

public partial class MainWindow : Window
{
    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    private UserDto? _user;

    private string? _pickedImageBase64;

    private List<TestDto> _tests = new();
    private List<QuestionDto> _questions = new();

    private List<AttemptResultDto> _results = new();

    public MainWindow()
    {
        InitializeComponent();
        Log("Редактор готов. Войдите как admin/admin (роль teacher), чтобы создавать тесты и смотреть результаты.");
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
        catch { }

        _writer = null;
        _reader = null;
        _client = null;
        _user = null;

        TestsList.ItemsSource = null;
        QuestionsList.ItemsSource = null;
        OptionsList.ItemsSource = null;

        Log("Disconnected");
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnected()) return;

        var env = Protocol.Pack(MessageTypes.Login, new LoginRequest(LoginBox.Text.Trim(), PasswordBox.Password));
        var line = await SendRawAsync(env);
        if (line is null) return;

        var resp = Protocol.ReadPayload<LoginResponse>(Protocol.Unpack(line));
        if (!resp.Ok || resp.User is null)
        {
            Log("Login failed: " + resp.Error);
            return;
        }

        _user = resp.User;
        Log($"Login OK. User={_user.Login}, role={_user.Role}");
    }

    private async void GetTests_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnected() || !EnsureLoggedIn()) return;

        var env = Protocol.Pack(MessageTypes.TestsGet, new TestsGetRequest(200));
        var line = await SendRawAsync(env);
        if (line is null) return;

        var resp = Protocol.ReadPayload<TestsGetResponse>(Protocol.Unpack(line));
        if (!resp.Ok || resp.Tests is null)
        {
            Log("Get tests failed: " + resp.Error);
            return;
        }

        _tests = resp.Tests.ToList();
        TestsList.ItemsSource = _tests;
        Log($"Tests loaded: {_tests.Count}");
    }

    
private void OpenResults_Click(object sender, RoutedEventArgs e)
{
    // Переключаемся на вкладку результатов
    MainTabs.SelectedItem = ResultsTab;

    // И сразу обновляем список результатов
    GetResults_Click(sender, e);
}

private async void GetResults_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnected() || !EnsureLoggedIn()) return;

        // Optional filter by selected test
        int? testId = (TestsList.SelectedItem is TestDto t) ? t.Id : null;

        var env = Protocol.Pack(MessageTypes.ResultsGet, new ResultsGetRequest(200, testId));
        var line = await SendRawAsync(env);
        if (line is null) return;

        var resp = Protocol.ReadPayload<ResultsGetResponse>(Protocol.Unpack(line));
        if (!resp.Ok || resp.Results is null)
        {
            Log("Получение результатов не удалось: " + resp.Error);
            return;
        }

        _results = resp.Results.ToList();
        ResultsList.ItemsSource = _results.Select(r =>
            $"Попытка #{r.AttemptId} | {r.StudentLogin} | тест #{r.TestId}: {r.TestTitle} | балл: {(r.Score?.ToString() ?? "-")} | завершено: {(r.FinishedAtUtc?.ToString("u") ?? "-")}");

        Log($"Результаты загружены: {_results.Count}");
    }

    private async void CreateTest_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnected() || !EnsureLoggedIn()) return;

        var title = NewTestTitle.Text.Trim();
        var desc = string.IsNullOrWhiteSpace(NewTestDesc.Text) ? null : NewTestDesc.Text.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            Log("Title required");
            return;
        }

        var env = Protocol.Pack(MessageTypes.TestsCreate, new CreateTestRequest(title, desc));
        var line = await SendRawAsync(env);
        if (line is null) return;

        var resp = Protocol.ReadPayload<CreateTestResponse>(Protocol.Unpack(line));
        if (!resp.Ok)
        {
            Log("Create test failed: " + resp.Error);
            return;
        }

        Log($"Created test id={resp.TestId}");
        NewTestTitle.Text = "";
        NewTestDesc.Text = "";
        GetTests_Click(sender, e);
    }

    private async void TestsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TestsList.SelectedItem is not TestDto test) return;
        if (!EnsureConnected() || !EnsureLoggedIn()) return;

        var env = Protocol.Pack(MessageTypes.QuestionsGet, new QuestionsGetRequest(test.Id));
        var line = await SendRawAsync(env);
        if (line is null) return;

        var resp = Protocol.ReadPayload<QuestionsGetResponse>(Protocol.Unpack(line));
        if (!resp.Ok || resp.Questions is null)
        {
            Log("Load questions failed: " + resp.Error);
            return;
        }

        _questions = resp.Questions.ToList();
        QuestionsList.ItemsSource = _questions;
        OptionsList.ItemsSource = null;

        Log($"Questions loaded: {_questions.Count}");
    }

    private void QuestionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (QuestionsList.SelectedItem is not QuestionDto q)
        {
            OptionsList.ItemsSource = null;
            return;
        }

        OptionsList.ItemsSource = q.Options.ToList();
    }

    private void PickImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp"
        };

        if (dlg.ShowDialog() != true) return;

        var bytes = File.ReadAllBytes(dlg.FileName);
        _pickedImageBase64 = Convert.ToBase64String(bytes);
        PickedImageInfo.Text = $"Picked image: {Path.GetFileName(dlg.FileName)} ({bytes.Length} bytes)";
    }

    private async void CreateQuestion_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnected() || !EnsureLoggedIn()) return;
        if (TestsList.SelectedItem is not TestDto test)
        {
            Log("Select a test first");
            return;
        }

        var text = NewQuestionText.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            Log("Question text required");
            return;
        }

        var kind = ((ComboBoxItem)QuestionKindBox.SelectedItem).Content?.ToString() ?? "single";
        var weight = int.TryParse(QuestionWeight.Text.Trim(), out var w) ? w : 1;

        var env = Protocol.Pack(MessageTypes.QuestionsCreate, new CreateQuestionRequest(test.Id, text, kind, weight, _pickedImageBase64));
        var line = await SendRawAsync(env);
        if (line is null) return;

        var resp = Protocol.ReadPayload<CreateQuestionResponse>(Protocol.Unpack(line));
        if (!resp.Ok)
        {
            Log("Create question failed: " + resp.Error);
            return;
        }

        Log($"Created question id={resp.QuestionId}");
        NewQuestionText.Text = "";
        QuestionWeight.Text = "1";
        _pickedImageBase64 = null;
        PickedImageInfo.Text = "";

        // reload questions
        // SelectionChangedEventArgs ждёт System.Collections.IList; new() тут пытался создать интерфейс IList
        TestsList_SelectionChanged(TestsList,
            new SelectionChangedEventArgs(ListBox.SelectionChangedEvent,
                new System.Collections.ArrayList(),
                new System.Collections.ArrayList()));
    }

    private async void CreateOption_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureConnected() || !EnsureLoggedIn()) return;
        if (QuestionsList.SelectedItem is not QuestionDto q)
        {
            Log("Select a question first");
            return;
        }

        var text = NewOptionText.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            Log("Option text required");
            return;
        }

        var isCorrect = OptionIsCorrect.IsChecked == true;

        var env = Protocol.Pack(MessageTypes.OptionsCreate, new CreateOptionRequest(q.Id, text, isCorrect));
        var line = await SendRawAsync(env);
        if (line is null) return;

        var resp = Protocol.ReadPayload<CreateOptionResponse>(Protocol.Unpack(line));
        if (!resp.Ok)
        {
            Log("Create option failed: " + resp.Error);
            return;
        }

        Log($"Created option id={resp.OptionId}");
        NewOptionText.Text = "";
        OptionIsCorrect.IsChecked = false;

        // reload questions to refresh options
        TestsList_SelectionChanged(TestsList,
            new SelectionChangedEventArgs(ListBox.SelectionChangedEvent,
                new System.Collections.ArrayList(),
                new System.Collections.ArrayList()));
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
