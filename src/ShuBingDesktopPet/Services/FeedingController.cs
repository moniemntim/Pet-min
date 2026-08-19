using ShuBingDesktopPet.Models;

namespace ShuBingDesktopPet.Services;

public sealed class FeedingController
{
    public const double CompletedMessageSeconds = 1.3;
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(3);

    private readonly TimeSpan _completedMessageDuration = TimeSpan.FromSeconds(CompletedMessageSeconds);

    private PetFoodType _activeFood = PetFoodType.None;
    private DateTime _nextAllowedUtc = DateTime.MinValue;
    private DateTime? _completedMessageUntilUtc;

    public bool IsActive => _activeFood != PetFoodType.None;
    public PetFoodType ActiveFood => _activeFood;
    public DateTime NextAllowedUtc => _nextAllowedUtc;

    public bool IsCompletedMessageVisible(DateTime nowUtc)
    {
        if (_completedMessageUntilUtc is null)
        {
            return false;
        }

        if (nowUtc <= _completedMessageUntilUtc.Value)
        {
            return true;
        }

        _completedMessageUntilUtc = null;
        return false;
    }

    public bool TryStart(PetFoodType food, DateTime nowUtc, bool isBusy)
    {
        if (food is not (PetFoodType.Apple or PetFoodType.Carrot or PetFoodType.Pellet))
        {
            return false;
        }

        if (isBusy || IsActive || IsOnCooldown(nowUtc))
        {
            return false;
        }

        _activeFood = food;
        return true;
    }

    public bool TryComplete(DateTime nowUtc, out PetFoodType completedFood)
    {
        completedFood = _activeFood;
        if (_activeFood != PetFoodType.None)
        {
            _activeFood = PetFoodType.None;
            _nextAllowedUtc = nowUtc.Add(Cooldown);
            _completedMessageUntilUtc = nowUtc.Add(_completedMessageDuration);
            return true;
        }

        completedFood = PetFoodType.None;
        return false;
    }

    public void Cancel()
    {
        _activeFood = PetFoodType.None;
        _completedMessageUntilUtc = null;
    }

    public bool IsOnCooldown(DateTime nowUtc) => nowUtc < _nextAllowedUtc;
}
