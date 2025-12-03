namespace FileAnalysisService.Models;

/// <summary>
/// результат анализа: максимальная схожесть и признак плагиата
/// </summary>
public record AnalyzeResponse(int SubmissionId, int? ClosestSubmissionId, double MaxSimilarity, bool IsPlagiarism);