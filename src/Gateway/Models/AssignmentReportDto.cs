namespace Gateway.Models;

/// <summary>
/// DTO-строка отчёта по всем работам для одного задания
/// </summary>
public record AssignmentReportDto(int SubmissionId, string StudentName, string AssignmentName, bool IsPlagiarism, double Similarity, DateTime CreatedAt);