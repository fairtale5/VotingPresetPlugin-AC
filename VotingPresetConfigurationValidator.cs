/// <summary>
/// Validates VotingPresetConfiguration at load. AssettoServer runs this before the plugin starts.
/// Rules: VoteChoices ≥ 2 (need at least two options in a vote); IntervalMinutes ≥ 5 (time between votes); VotingDurationSeconds ≥ 10 (vote window length); TransitionDurationSeconds ≥ 2 (time between restart notification and restart); TransitionDelaySeconds ≥ 0 (delay before restart notification); RequesterCooldownMinutes ≥ 0 (cooldown after failed on-demand vote). If any rule fails, the plugin does not start and the server reports the validation error.
/// </summary>
using FluentValidation;

namespace VotingPresetPlugin;

public class VotingPresetConfigurationValidator : AbstractValidator<VotingPresetConfiguration>
{
    public VotingPresetConfigurationValidator()
    {
        RuleFor(cfg => cfg.VoteChoices).GreaterThanOrEqualTo(2);
        RuleFor(cfg => cfg.IntervalMinutes).GreaterThanOrEqualTo(5);
        RuleFor(cfg => cfg.VotingDurationSeconds).GreaterThanOrEqualTo(10);
        RuleFor(cfg => cfg.TransitionDurationSeconds).GreaterThanOrEqualTo(2);
        RuleFor(cfg => cfg.TransitionDelaySeconds).GreaterThanOrEqualTo(0);
        RuleFor(cfg => cfg.RequesterCooldownMinutes).GreaterThanOrEqualTo(0);
    }
}
