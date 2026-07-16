using Godot;
using Competition.CompetitionCode.UI;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace Competition.CompetitionCode;

/// <summary>
/// Owns the small, local-only Competition menu shown from the game's main menu.
/// No game, save, or networking state is created here.
/// </summary>
public static class CompetitionRuntime
{
    private const string ButtonContainerPath = "MainMenuTextButtons";
    private const string MultiplayerButtonPath = "MainMenuTextButtons/MultiplayerButton";
    private const string CompetitionButtonName = "CompetitionButton";

    public static void OnMainMenuReady(NMainMenu mainMenu)
    {
        if (mainMenu.GetNodeOrNull<Control>($"{ButtonContainerPath}/{CompetitionButtonName}") != null)
        {
            return;
        }

        Control? buttonContainer = mainMenu.GetNodeOrNull<Control>(ButtonContainerPath);
        NMainMenuTextButton? multiplayerButton = mainMenu.GetNodeOrNull<NMainMenuTextButton>(MultiplayerButtonPath);
        if (buttonContainer == null || multiplayerButton == null)
        {
            MainFile.Logger.Error("Could not find the main menu button template.");
            return;
        }

        // Keep the template's script and children, but do not copy its scene signal
        // connections (which could otherwise still open the original multiplayer menu).
        if (multiplayerButton.Duplicate((int)Node.DuplicateFlags.Scripts) is not NMainMenuTextButton competitionButton)
        {
            MainFile.Logger.Error("Could not duplicate MultiplayerButton.");
            return;
        }

        competitionButton.Name = CompetitionButtonName;
        buttonContainer.AddChild(competitionButton);
        buttonContainer.MoveChild(competitionButton, multiplayerButton.GetIndex() + 1);
        if (competitionButton.label == null)
        {
            competitionButton.QueueFree();
            MainFile.Logger.Error("CompetitionButton label was not initialized.");
            return;
        }

        competitionButton.label.Text = "1v1爬塔";
        competitionButton.GuiInput += inputEvent =>
        {
            if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
            {
                OpenMenu(mainMenu);
            }
        };
    }

    private static void OpenMenu(NMainMenu mainMenu)
    {
        CompetitionModeSubmenu.Show(mainMenu);
    }
}
