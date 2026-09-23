using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LocalCopilot_App.Services;

public sealed record OcrBenchmarkTextMetrics(
    int ReferenceCodePointCount,
    int HypothesisCodePointCount,
    int CharacterEdits,
    double CharacterErrorRate,
    int ReferenceWordCount,
    int HypothesisWordCount,
    int WordEdits,
    double WordErrorRate,
    bool ExactNormalizedMatch);

public static class OcrBenchmarkScoring
{
    public static OcrBenchmarkTextMetrics Score(
        string reference,
        string hypothesis)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(hypothesis);

        string normalizedReference =
            Normalize(reference);

        string normalizedHypothesis =
            Normalize(hypothesis);

        Rune[] referenceRunes =
            normalizedReference
                .EnumerateRunes()
                .ToArray();

        Rune[] hypothesisRunes =
            normalizedHypothesis
                .EnumerateRunes()
                .ToArray();

        string[] referenceWords =
            SplitWords(normalizedReference);

        string[] hypothesisWords =
            SplitWords(normalizedHypothesis);

        int characterEdits =
            LevenshteinDistance(
                referenceRunes,
                hypothesisRunes);

        int wordEdits =
            LevenshteinDistance(
                referenceWords,
                hypothesisWords,
                StringComparer.Ordinal);

        double characterErrorRate =
            Rate(
                characterEdits,
                referenceRunes.Length);

        double wordErrorRate =
            Rate(
                wordEdits,
                referenceWords.Length);

        return new OcrBenchmarkTextMetrics(
            referenceRunes.Length,
            hypothesisRunes.Length,
            characterEdits,
            characterErrorRate,
            referenceWords.Length,
            hypothesisWords.Length,
            wordEdits,
            wordErrorRate,
            string.Equals(
                normalizedReference,
                normalizedHypothesis,
                StringComparison.Ordinal));
    }

    public static string Normalize(
        string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Normalize(NormalizationForm.FormC)
            .Trim();
    }

    private static string[] SplitWords(
        string value) =>
        value.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries);

    private static double Rate(
        int edits,
        int referenceCount)
    {
        if (referenceCount == 0)
        {
            return edits == 0
                ? 0.0
                : 1.0;
        }

        return edits /
            (double)referenceCount;
    }

    private static int LevenshteinDistance<T>(
        IReadOnlyList<T> left,
        IReadOnlyList<T> right,
        IEqualityComparer<T>? comparer = null)
    {
        comparer ??=
            EqualityComparer<T>.Default;

        if (left.Count == 0)
        {
            return right.Count;
        }

        if (right.Count == 0)
        {
            return left.Count;
        }

        int[] previous =
            new int[right.Count + 1];

        int[] current =
            new int[right.Count + 1];

        for (int column = 0;
            column <= right.Count;
            column++)
        {
            previous[column] =
                column;
        }

        for (int row = 1;
            row <= left.Count;
            row++)
        {
            current[0] =
                row;

            for (int column = 1;
                column <= right.Count;
                column++)
            {
                int substitutionCost =
                    comparer.Equals(
                        left[row - 1],
                        right[column - 1])
                        ? 0
                        : 1;

                current[column] =
                    Math.Min(
                        Math.Min(
                            previous[column] + 1,
                            current[column - 1] + 1),
                        previous[column - 1] +
                            substitutionCost);
            }

            (previous, current) =
                (current, previous);
        }

        return previous[right.Count];
    }
}
