using System.Windows;
using System.Windows.Controls;
using Winly.Core.Companion;

namespace Winly.App.Overlay;

public partial class SquareBuddyControl : UserControl
{
    public SquareBuddyControl()
    {
        InitializeComponent();
        Loaded += (_, _) => SetState(CompanionState.Idle);
    }

    /// <summary>Idle, Listening, Working and Speaking each have a distinct look (FR-022).</summary>
    public void SetState(CompanionState state) => VisualStateManager.GoToElementState(Root, state.ToString(), useTransitions: true);

    public void SetPointing(bool pointing) => Pointer.Opacity = pointing ? 1 : 0;
}
