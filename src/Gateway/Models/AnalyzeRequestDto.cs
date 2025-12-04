namespace Gateway.Models;

/// <summary>
/// DTO-запрос для сервиса анализа при старте проверки конкретной работы
/// </summary>
public record AnalyzeRequestDto(int SubmissionId);