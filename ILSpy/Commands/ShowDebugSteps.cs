#if DEBUG

namespace ICSharpCode.ILSpy.Commands
{
	[ExportMainMenuCommand(Menu = "_View", Header = "_Show debug steps", MenuOrder = 5000)]
	internal class ShowDebugSteps : SimpleCommand
	{
		public override void Execute(object parameter)
		{
			DebugSteps.Show();
		}
	}
}

#endif