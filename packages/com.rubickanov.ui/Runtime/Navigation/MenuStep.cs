namespace Rubickanov.UI
{
    /// <summary>A step of a menu's focus: one of the four ways, a tab over, a page along.</summary>
    public enum MenuStep
    {
        Up,
        Down,
        Left,
        Right,
        PreviousTab,
        NextTab,
        PreviousPage,
        NextPage,
    }

    /// <summary>A menu that takes steps and presses while it is on top (<see cref="MenuNavigation.Claim"/>).</summary>
    public interface IMenuTarget
    {
        void Step(MenuStep step);

        void Submit();
    }
}
