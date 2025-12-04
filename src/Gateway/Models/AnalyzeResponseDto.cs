namespace Gateway.Models;

/// <summary>
/// DTO-ответ от сервиса анализа с информацией о схожести и признаках плагиата
/// </summary>
public record AnalyzeResponseDto(int SubmissionId, int? ClosestSubmissionId, double MaxSimilarity, bool IsPlagiarism);