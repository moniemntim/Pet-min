namespace ShuBingDesktopPet.Models;

public enum PetAction
{
    Idle,
    Gaze,
    Roaming,
    Dragged,
    Petted,
    Wave,
    HappyJump,
    Groom,
    Breath,
    Blink,
    Scratch,
    Feed
}

public static class PetPriorityResolver
{
    public static PetAction Resolve(
        bool isDragging,
        PetAction? reaction,
        bool roamingEnabled,
        bool hasCursorMovement)
    {
        if (isDragging)
        {
            return PetAction.Dragged;
        }

        if (reaction.HasValue)
        {
            return reaction.Value;
        }

        if (roamingEnabled)
        {
            return PetAction.Roaming;
        }

        if (hasCursorMovement)
        {
            return PetAction.Gaze;
        }

        return PetAction.Idle;
    }
}
