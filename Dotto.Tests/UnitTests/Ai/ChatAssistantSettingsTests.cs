using System.ComponentModel.DataAnnotations;
using Dotto.Ai.Settings;
using Shouldly;

namespace Dotto.Tests.UnitTests.Ai;

/// <summary>
/// Pins the observability defaults and their validation.
/// </summary>
public class ChatAssistantSettingsTests
{
    [Test]
    public void Defaults_MatchTheDocumentedBudget()
    {
        var settings = new ChatAssistantSettings();

        settings.MaxTurns.ShouldBe(20);
        settings.TranscriptTurns.ShouldBeNull();
    }

    [TestCase(nameof(ChatAssistantSettings.MaxTurns), 0)]
    [TestCase(nameof(ChatAssistantSettings.MaxTurns), 101)]
    [TestCase(nameof(ChatAssistantSettings.TranscriptTurns), 0)]
    [TestCase(nameof(ChatAssistantSettings.TranscriptTurns), 101)]
    public void Validation_RejectsOutOfRangeValues(string property, int value)
    {
        var settings = new ChatAssistantSettings();

        switch (property)
        {
            case nameof(ChatAssistantSettings.MaxTurns):
                settings.MaxTurns = value;
                break;
            case nameof(ChatAssistantSettings.TranscriptTurns):
                settings.TranscriptTurns = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(property));
        }

        var results = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(
            settings, new ValidationContext(settings), results, validateAllProperties: true);

        valid.ShouldBeFalse();
        results.ShouldContain(result => result.MemberNames.Contains(property));
    }

    [Test]
    public void Validation_AcceptsTheDefaults()
    {
        var settings = new ChatAssistantSettings();
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(
                settings,
                new ValidationContext(settings),
                results,
                validateAllProperties: true)
            .ShouldBeTrue();
    }
}
