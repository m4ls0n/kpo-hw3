using Npgsql;
using FileAnalysisService.Models;
using FileAnalysisService.Utils;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// cтрока подключения к postgresql берётся из appsettings или переменной окружения
var connectionString = configuration.GetConnectionString("DefaultConnection") ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING") ?? "Host=db;Port=5432;Database=antiplag;Username=antiplag;Password=antiplag";
// пул подключений к postgresql
builder.Services.AddSingleton(new NpgsqlDataSourceBuilder(connectionString).Build());
var app = builder.Build();

/// <summary>
/// точка входа сервиса анализа
/// принимает идентификатор работы и возвращает результат анализа плагиата
/// </summary>
app.MapPost("/analyze", async (AnalyzeRequest request, NpgsqlDataSource dataSource) =>
{
    await using var conn = await dataSource.OpenConnectionAsync();
    // 1 - получаем текущую работу по её идентификатору
    await using (var cmd = new NpgsqlCommand("SELECT id, assignment_name, content FROM submissions WHERE id = @id", conn))
    {
        cmd.Parameters.AddWithValue("id", request.SubmissionId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return Results.NotFound($"Работа с id={request.SubmissionId} не найдена");
        }

        var id = reader.GetInt32(0);
        var assignment = reader.GetString(1);
        var content = reader.GetString(2);
        await reader.CloseAsync();
        // 2 - получаем другие работы по тому же заданию
        await using var othersCmd = new NpgsqlCommand("SELECT id, content FROM submissions WHERE assignment_name = @assignment AND id <> @id", conn);
        othersCmd.Parameters.AddWithValue("assignment", assignment);
        othersCmd.Parameters.AddWithValue("id", id);
        await using var othersReader = await othersCmd.ExecuteReaderAsync();
        var tokensA = TextUtils.Tokenize(content);
        var setA = new HashSet<string>(tokensA, StringComparer.OrdinalIgnoreCase);
        double maxSim = 0.0;
        int? closestId = null;
        // 3 - сравниваем текущую работу с каждой другой и ищем максимальную схожесть
        while (await othersReader.ReadAsync())
        {
            var otherId = othersReader.GetInt32(0);
            var otherContent = othersReader.GetString(1);
            var tokensB = TextUtils.Tokenize(otherContent);
            var setB = new HashSet<string>(tokensB, StringComparer.OrdinalIgnoreCase);
            var sim = TextUtils.Jaccard(setA, setB);
            if (sim > maxSim)
            {
                maxSim = sim;
                closestId = otherId;
            }
        }

        // 4 - признак плагиата определяется по порогу 0.8 (80%).
        var isPlagiarism = maxSim >= 0.8;
        var result = new AnalyzeResponse(id, closestId, maxSim, isPlagiarism);
        return Results.Ok(result);
    }
});

app.Run();