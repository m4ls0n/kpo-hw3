using System.Text;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Npgsql;
using Gateway.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var configuration = builder.Configuration;
// строка подключения к postgresql берётся из appsettings или переменной окружения
var connectionString = configuration.GetConnectionString("DefaultConnection") ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING") ?? "Host=db;Port=5432;Database=antiplag;Username=antiplag;Password=antiplag";
// регистрируем пул подключений к postgresql
builder.Services.AddSingleton(new NpgsqlDataSourceBuilder(connectionString).Build());
// httpclient для сервиса хранения файлов
builder.Services.AddHttpClient("fileStorage", client =>
{
    var baseUrl = configuration["FileStorage:BaseUrl"] ?? "http://filestorage";
    client.BaseAddress = new Uri(baseUrl);
});

// httpclient для сервиса анализа
builder.Services.AddHttpClient("analysis", client =>
{
    var baseUrl = configuration["Analysis:BaseUrl"] ?? "http://analysis";
    client.BaseAddress = new Uri(baseUrl);
});

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

/// <summary>
/// загрузка работы студента
/// принимает multipart/form-data с полями studentName, assignmentName и файлом
/// 1 - отдаём файл в FileStorageService
/// 2 - читаем текст файла
/// 3 - сохраняем запись о работе в бд
/// 4 - вызываем FileAnalysisService
/// 5 - сохраняем отчёт о плагиате в бд
/// 6 - возвращаем итоговый DTO клиенту
/// </summary>
app.MapPost("/api/submissions", async (HttpRequest httpRequest, NpgsqlDataSource dataSource, IHttpClientFactory httpClientFactory) =>
{
    var form = await httpRequest.ReadFormAsync();
    var studentName = form["studentName"].ToString();
    var assignmentName = form["assignmentName"].ToString();
    var file = form.Files.GetFile("file");
    if (file == null || string.IsNullOrWhiteSpace(studentName) || string.IsNullOrWhiteSpace(assignmentName))
    {
        return Results.BadRequest("studentName, assignmentName и file являются обязательными полями");
    }

    // 1 - сохраняем файл в FileStorageService
    var storageClient = httpClientFactory.CreateClient("fileStorage");
    await using var tempStream = new MemoryStream();
    await file.CopyToAsync(tempStream);
    tempStream.Position = 0;
    using var content = new MultipartFormDataContent();
    var fileContent = new StreamContent(tempStream);
    fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType ?? "application/octet-stream");
    content.Add(fileContent, "file", file.FileName);
    var storageResponse = await storageClient.PostAsync("/files", content);
    if (!storageResponse.IsSuccessStatusCode)
    {
        var errorText = await storageResponse.Content.ReadAsStringAsync();
        return Results.Problem($"Ошибка сервиса хранения файлов: {storageResponse.StatusCode} {errorText}");
    }

    var storageJson = await storageResponse.Content.ReadFromJsonAsync<FileStorageResult>();
    if (storageJson is null)
    {
        return Results.Problem("Не удалось разобрать ответ от сервиса хранения файлов");
    }

    // 2 - считываем текст файла как строку
    tempStream.Position = 0;
    string contentText;
    using (var reader = new StreamReader(tempStream, Encoding.UTF8, leaveOpen: true))
    {
        contentText = await reader.ReadToEndAsync();
    }

    // 3 - сохраняем работу в таблицу submissions
    await using var conn = await dataSource.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(@"INSERT INTO submissions(student_name, assignment_name, file_name, content, created_at)
        VALUES (@student, @assignment, @fileName, @content, NOW())
        RETURNING id;", conn);

    cmd.Parameters.AddWithValue("student", studentName);
    cmd.Parameters.AddWithValue("assignment", assignmentName);
    cmd.Parameters.AddWithValue("fileName", storageJson.FileName);
    cmd.Parameters.AddWithValue("content", contentText);
    var submissionIdObj = await cmd.ExecuteScalarAsync();
    var submissionId = Convert.ToInt32(submissionIdObj);

    // 4 - запускаем анализ плагиата для только что сохранённой работы
    var analysisClient = httpClientFactory.CreateClient("analysis");
    var analyzeReq = new AnalyzeRequestDto(submissionId);
    var analysisResponse = await analysisClient.PostAsJsonAsync("/analyze", analyzeReq);
    if (!analysisResponse.IsSuccessStatusCode)
    {
        var errorText = await analysisResponse.Content.ReadAsStringAsync();
        return Results.Problem($"Ошибка сервиса анализа: {analysisResponse.StatusCode} {errorText}");
    }

    var analysisResult = await analysisResponse.Content.ReadFromJsonAsync<AnalyzeResponseDto>();
    if (analysisResult is null)
    {
        return Results.Problem("Не удалось разобрать ответ от сервиса анализа");
    }

    // 5 - сохраняем отчёт о плагиате в таблицу reports
    await using var cmdReport = new NpgsqlCommand(@"INSERT INTO reports(submission_id, is_plagiarism, similarity, created_at, details)
        VALUES (@subId, @plag, @sim, NOW(), @details)
        RETURNING id;", conn);

    cmdReport.Parameters.AddWithValue("subId", submissionId);
    cmdReport.Parameters.AddWithValue("plag", analysisResult.IsPlagiarism);
    cmdReport.Parameters.AddWithValue("sim", analysisResult.MaxSimilarity);
    cmdReport.Parameters.AddWithValue("details", $"ClosestSubmissionId={analysisResult.ClosestSubmissionId}");

    var reportIdObj = await cmdReport.ExecuteScalarAsync();
    var reportId = Convert.ToInt32(reportIdObj);
    var result = new SubmissionResponseDto(submissionId, reportId, studentName, assignmentName, storageJson.FileName, analysisResult.IsPlagiarism, analysisResult.MaxSimilarity);
    return Results.Ok(result);
});

/// <summary>
/// получение отчётов по конкретному заданию
/// возвращает список всех работ с выводои плагиат или нет и коэффициентом схожести
/// </summary>
app.MapGet("/api/assignments/{assignmentName}/reports", async (string assignmentName, NpgsqlDataSource dataSource) =>
{
    await using var conn = await dataSource.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(@"SELECT s.id, s.student_name, s.assignment_name, r.is_plagiarism, r.similarity, r.created_at
        FROM submissions s
        JOIN reports r ON r.submission_id = s.id
        WHERE s.assignment_name = @assignment
        ORDER BY r.created_at;", conn);

    cmd.Parameters.AddWithValue("assignment", assignmentName);
    await using var reader = await cmd.ExecuteReaderAsync();
    var list = new List<AssignmentReportDto>();
    while (await reader.ReadAsync())
    {
        list.Add(new AssignmentReportDto(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), reader.GetDouble(4), reader.GetDateTime(5)));
    }

    return Results.Ok(list);
});

/// <summary>
/// получение ссылки на облако слов (word cloud) для конкретной работы
/// ссылка строится на основе quickchart.io
/// </summary>
app.MapGet("/api/submissions/{id:int}/wordcloud", async (int id, NpgsqlDataSource dataSource) =>
{
    await using var conn = await dataSource.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand("SELECT content FROM submissions WHERE id = @id", conn);
    cmd.Parameters.AddWithValue("id", id);
    var content = (string?)await cmd.ExecuteScalarAsync();
    if (content is null)
    {
        return Results.NotFound();
    }

    var encoded = Uri.EscapeDataString(content);
    var url = $"https://quickchart.io/wordcloud?text={encoded}";
    return Results.Ok(new { submissionId = id, url });
});

app.MapGet("/", () => Results.Redirect("/swagger"));
app.Run();