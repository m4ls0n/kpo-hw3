namespace Gateway.Models;

/// <summary>
/// результат сохранения файла в сервисе хранения
/// </summary>
public record FileStorageResult(string FileName, string OriginalName);