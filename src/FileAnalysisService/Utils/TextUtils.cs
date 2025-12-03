using System.Text;

namespace FileAnalysisService.Utils;

/// <summary>
/// вспомогательные методы для работы с текстом
/// </summary>
public static class TextUtils
{
    /// <summary>
    /// разбивает текст на токены (слова), оставляя только буквы и цифры, все символы приводятся к нижнему регистру
    /// </summary>
    /// <param name="text">исходный текст работы</param>
    /// <returns>последовательность слов токенов</returns>
    public static IEnumerable<string> Tokenize(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                if (sb.Length > 0)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }
            }
        }

        if (sb.Length > 0)
        {
            yield return sb.ToString();
        }
    }

    /// <summary>
    /// вычисляет коэффициент Жаккара между двумя множествами слов
    /// </summary>
    /// <param name="a">1-е множество слов</param>
    /// <param name="b">2-е множество слов</param>
    /// <returns>число от 0 до 1, которое показывает степень пересечения множеств</returns>
    public static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 && b.Count == 0)
        {
            return 1.0;
        }

        var intersection = 0;
        foreach (var item in a)
        {
            if (b.Contains(item))
            {
                intersection++;
            }
        }

        var union = a.Count + b.Count - intersection;
        if (union == 0) 
        {
            return 0.0;
        }
        return (double)intersection / union;
    }
}