namespace Gateway.Models;

/// <summary>
/// DTO-ответ при загрузке работы студентом через API Gateway
/// </summary>
public record SubmissionResponseDto(int SubmissionId, int ReportId, string StudentName, string AssignmentName, string FileName, bool IsPlagiarism, double Similarity);