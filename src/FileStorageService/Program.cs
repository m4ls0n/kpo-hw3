var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
var storageRoot = builder.Configuration["StorageRoot"] ?? Environment.GetEnvironmentVariable("STORAGE_ROOT") ?? "/files";
// гарантируем, что директория для хранения файлов есть
Directory.CreateDirectory(storageRoot);

// загрузка файла в файловое хранилище
app.MapPost("/files", async (HttpRequest request) =>
{
    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file == null || file.Length == 0)
    {
        return Results.BadRequest("Файл не был передан или он пустой");
    }

    var safeName = Path.GetFileName(file.FileName); // защита от передачи путей вместо имени
    var fileName = $"{Guid.NewGuid()}_{safeName}";
    var fullPath = Path.Combine(storageRoot, fileName);
    await using var stream = File.Create(fullPath);
    await file.CopyToAsync(stream);
    return Results.Ok(new
    {
        FileName = fileName,
        OriginalName = file.FileName
    });
});

// скачивание ранее загруженного файла по его имени
app.MapGet("/files/{fileName}", (string fileName) =>
{
    var fullPath = Path.Combine(storageRoot, fileName);
    if (!File.Exists(fullPath))
    {
        return Results.NotFound();
    }

    const string contentType = "application/octet-stream";
    return Results.File(fullPath, contentType, fileDownloadName: fileName);
});

app.Run();