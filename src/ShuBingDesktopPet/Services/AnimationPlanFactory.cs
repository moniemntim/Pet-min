using ShuBingDesktopPet.Models;

namespace ShuBingDesktopPet.Services;

public static class AnimationPlanFactory
{
    public static AnimationRowSpec RowSpec(int row) => row switch
    {
        0 => new AnimationRowSpec(6, new[] { 280, 110, 110, 140, 140, 320 }, true),
        1 => new AnimationRowSpec(8, new[] { 120, 120, 120, 120, 120, 120, 120, 120 }, true),
        2 => new AnimationRowSpec(8, new[] { 120, 120, 120, 120, 120, 120, 120, 120 }, true),
        3 => new AnimationRowSpec(4, new[] { 140, 140, 140, 280 }, false),
        4 => new AnimationRowSpec(5, new[] { 140, 140, 140, 140, 280 }, false),
        5 => new AnimationRowSpec(8, new[] { 120, 120, 120, 120, 120, 120, 120, 180 }, true),
        6 => new AnimationRowSpec(6, new[] { 150, 150, 150, 150, 150, 260 }, false),
        7 => new AnimationRowSpec(6, new[] { 220, 180, 180, 180, 180, 320 }, false),
        8 => new AnimationRowSpec(6, new[] { 150, 150, 150, 150, 150, 280 }, true),
        9 => new AnimationRowSpec(8, new[] { 120, 120, 120, 120, 120, 120, 120, 220 }, true),
        10 => new AnimationRowSpec(8, new[] { 120, 120, 120, 120, 120, 120, 120, 220 }, true),
        _ => new AnimationRowSpec(6, new[] { 280, 110, 110, 140, 140, 320 }, true)
    };

    public static AnimationPlan CreateRowPlan(int row, int fixedColumn = -1) => row switch
    {
        0 => new AnimationPlan(0, Loop: true, IsOneShot: false, FixedColumn: fixedColumn),
        1 => new AnimationPlan(1, Loop: true, IsOneShot: false, FixedColumn: fixedColumn),
        2 => new AnimationPlan(2, Loop: true, IsOneShot: false, FixedColumn: fixedColumn),
        3 => new AnimationPlan(3, Loop: false, IsOneShot: true, FixedColumn: fixedColumn),
        4 => new AnimationPlan(4, Loop: false, IsOneShot: true, FixedColumn: fixedColumn),
        5 => new AnimationPlan(5, Loop: true, IsOneShot: false, FixedColumn: fixedColumn),
        6 => new AnimationPlan(6, Loop: false, IsOneShot: true, FixedColumn: fixedColumn),
        7 => new AnimationPlan(7, Loop: false, IsOneShot: true, FixedColumn: fixedColumn),
        8 => new AnimationPlan(8, Loop: true, IsOneShot: true, FixedColumn: fixedColumn),
        9 => new AnimationPlan(9, Loop: true, IsOneShot: false, FixedColumn: fixedColumn),
        10 => new AnimationPlan(10, Loop: true, IsOneShot: false, FixedColumn: fixedColumn),
        _ => new AnimationPlan(0, Loop: true, IsOneShot: false, FixedColumn: fixedColumn),
    };

    public static int ReactionRow(PetAction action) => action switch
    {
        PetAction.Wave => 3,
        PetAction.HappyJump => 4,
        PetAction.Groom => 7,
        PetAction.Petted => 6,
        PetAction.Feed => 7,
        PetAction.Breath => 8,
        PetAction.Blink => 8,
        PetAction.Scratch => 8,
        _ => 7
    };

    public static AnimationPlan CreateRowPlanForAction(PetAction action, int fixedColumn = -1) =>
        CreateRowPlan(ReactionRow(action), fixedColumn);
}
