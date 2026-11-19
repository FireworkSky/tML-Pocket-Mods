using System.Collections.Generic;

namespace MobileJoystick;

internal enum MobilePauseAction
{
	Resume, Invite, Controls, Settings, Journey, Achievements, Bestiary, SaveExit
}

internal static class MobilePauseMenu
{
	internal static MobilePauseAction[] Create(bool canInvite, bool journey)
	{
		var actions = new List<MobilePauseAction> { MobilePauseAction.Resume };
		if (canInvite) actions.Add(MobilePauseAction.Invite);
		actions.Add(MobilePauseAction.Controls);
		actions.Add(MobilePauseAction.Settings);
		if (journey) actions.Add(MobilePauseAction.Journey);
		actions.Add(MobilePauseAction.Achievements);
		actions.Add(MobilePauseAction.Bestiary);
		actions.Add(MobilePauseAction.SaveExit);
		return actions.ToArray();
	}
}
