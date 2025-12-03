namespace FileAnalysisService.Models;

/// <summary>
/// запрос на анализ конкретной работы по её идентификатору
/// </summary>
public record AnalyzeRequest(int SubmissionId);