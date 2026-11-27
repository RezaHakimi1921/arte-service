using Arte.Api.Surveys;
using Arte.Core.Surveys;

namespace Arte.Tests;

/// <summary>Pure rules — no Docker / API host.</summary>
public sealed class SurveyRulesTests
{
    [Fact]
    public void Low_satisfaction_is_average_under_threshold_not_a_single_low_star()
    {
        // One weak dimension with a high average must not flag (CASE-4 style: 4,1,5,5 → 3.75).
        Assert.False(SurveyRules.IsLow([4, 1, 5, 5]));
        Assert.False(SurveyRules.IsLowScore(3.75m));
        Assert.False(SurveyRules.IsLow([2, 5, 5])); // avg 4
        Assert.True(SurveyRules.IsLow([3, 3, 2]));  // avg ~2.67
        Assert.False(SurveyRules.IsLow([3, 3, 3]));
        Assert.False(SurveyRules.IsLow([4, 5, 3]));
        Assert.True(SurveyRules.IsLow([4, 5, 2], below: 4)); // avg ~3.67
        Assert.True(SurveyRules.IsLowScore(2.25m));
        Assert.False(SurveyRules.IsLowScore(3m));
    }

    [Fact]
    public void Survey_score_Fa_rounds_half_away_from_zero_like_the_web()
    {
        Assert.Equal("۲٫۳", SurveyService.Fa(2.25m));
        Assert.Equal("۲٫۲", SurveyService.Fa(2.24m));
        Assert.Equal("۴", SurveyService.Fa(4m));
    }
}
