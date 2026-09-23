using LocalCopilot_App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCopilot.Core.Tests;

[TestClass]
public sealed class OcrBenchmarkScoringTests
{
    [TestMethod]
    public void Score_ExactPersianEnglishText_IsZeroError()
    {
        const string text =
            "خطای Servo ER01 در Axis A1";

        OcrBenchmarkTextMetrics result =
            OcrBenchmarkScoring.Score(
                text,
                text);

        Assert.AreEqual(0, result.CharacterEdits);
        Assert.AreEqual(0.0, result.CharacterErrorRate, 0.000001);
        Assert.AreEqual(0, result.WordEdits);
        Assert.AreEqual(0.0, result.WordErrorRate, 0.000001);
        Assert.IsTrue(result.ExactNormalizedMatch);
    }

    [TestMethod]
    public void Score_NormalizesLineEndingsAndUnicodeNfc()
    {
        string reference =
            "Café\r\nدستگاه";

        string hypothesis =
            "Café\nدستگاه";

        OcrBenchmarkTextMetrics result =
            OcrBenchmarkScoring.Score(
                reference,
                hypothesis);

        Assert.AreEqual(0, result.CharacterEdits);
        Assert.AreEqual(0, result.WordEdits);
        Assert.IsTrue(result.ExactNormalizedMatch);
    }

    [TestMethod]
    public void Score_CountsPersianSubstitutionAsOneCharacterEdit()
    {
        OcrBenchmarkTextMetrics result =
            OcrBenchmarkScoring.Score(
                "فشار جک",
                "فشار حک");

        Assert.AreEqual(1, result.CharacterEdits);
        Assert.IsGreaterThan(0.0, result.CharacterErrorRate);
        Assert.AreEqual(1, result.WordEdits);
        Assert.IsFalse(result.ExactNormalizedMatch);
    }

    [TestMethod]
    public void Score_WordInsertion_IsOneWordEdit()
    {
        OcrBenchmarkTextMetrics result =
            OcrBenchmarkScoring.Score(
                "motor ready",
                "motor axis ready");

        Assert.AreEqual(1, result.WordEdits);
        Assert.AreEqual(0.5, result.WordErrorRate, 0.000001);
    }

    [TestMethod]
    public void Score_WhitespaceRuns_DoNotInflateWordCount()
    {
        OcrBenchmarkTextMetrics result =
            OcrBenchmarkScoring.Score(
                "alpha   beta\tgamma",
                "alpha beta gamma");

        Assert.AreEqual(3, result.ReferenceWordCount);
        Assert.AreEqual(3, result.HypothesisWordCount);
        Assert.AreEqual(0, result.WordEdits);
        Assert.IsFalse(result.ExactNormalizedMatch);
    }

    [TestMethod]
    public void Score_EmptyReferenceAndHypothesis_IsZeroRate()
    {
        OcrBenchmarkTextMetrics result =
            OcrBenchmarkScoring.Score(
                "",
                "");

        Assert.AreEqual(0, result.ReferenceCodePointCount);
        Assert.AreEqual(0, result.CharacterEdits);
        Assert.AreEqual(0.0, result.CharacterErrorRate, 0.000001);
        Assert.AreEqual(0.0, result.WordErrorRate, 0.000001);
        Assert.IsTrue(result.ExactNormalizedMatch);
    }

    [TestMethod]
    public void Score_NonEmptyHypothesisAgainstEmptyReference_IsUnitRate()
    {
        OcrBenchmarkTextMetrics result =
            OcrBenchmarkScoring.Score(
                "",
                "unexpected");

        Assert.AreEqual(10, result.CharacterEdits);
        Assert.AreEqual(1.0, result.CharacterErrorRate, 0.000001);
        Assert.AreEqual(1, result.WordEdits);
        Assert.AreEqual(1.0, result.WordErrorRate, 0.000001);
    }

    [TestMethod]
    public void Score_UsesUnicodeCodePointsInsteadOfUtf16CodeUnits()
    {
        OcrBenchmarkTextMetrics result =
            OcrBenchmarkScoring.Score(
                "A😀B",
                "A😃B");

        Assert.AreEqual(3, result.ReferenceCodePointCount);
        Assert.AreEqual(3, result.HypothesisCodePointCount);
        Assert.AreEqual(1, result.CharacterEdits);
    }

    [TestMethod]
    public void Normalize_TrimsOnlyOuterWhitespace()
    {
        string normalized =
            OcrBenchmarkScoring.Normalize(
                "  a  b  ");

        Assert.AreEqual(
            "a  b",
            normalized);
    }
}
