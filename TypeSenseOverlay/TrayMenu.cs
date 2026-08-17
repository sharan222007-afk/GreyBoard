using System;
using System.Drawing;
using System.Windows.Forms;

namespace TypeSenseOverlay;

internal sealed class TrayMenu : IDisposable
{
	private readonly NotifyIcon _icon;

	public TrayMenu(TypingEngine engine, SuggestionOverlay overlay, UserSettings settings, ControlCenter controlCenter)
	{
		ContextMenuStrip menu = new ContextMenuStrip
		{
			Items = 
			{
				{
					"Open Grey Board",
					(Image?)null,
					(EventHandler?)delegate
					{
						overlay.Dispatcher.Invoke(controlCenter.ShowAndFocus);
					}
				},
				{
					"Start prediction popup",
					(Image?)null,
					(EventHandler?)delegate
					{
						engine.Start();
					}
				},
				{
					"Stop prediction popup",
					(Image?)null,
					(EventHandler?)delegate
					{
						engine.Stop();
					}
				},
				(ToolStripItem)new ToolStripSeparator(),
				{
					"Settings...",
					(Image?)null,
					(EventHandler?)delegate
					{
						overlay.Dispatcher.Invoke(delegate
						{
							new SettingsWindow(settings, engine).Show();
						});
					}
				},
				{
					"Pause / Resume",
					(Image?)null,
					(EventHandler?)delegate
					{
						engine.TogglePause();
					}
				},
				{
					"Exit Grey Board",
					(Image?)null,
					(EventHandler?)delegate
					{
						overlay.Dispatcher.Invoke(controlCenter.ExitApplication);
					}
				}
			}
		};
		_icon = new NotifyIcon
		{
			Text = "Grey Board",
			Icon = SystemIcons.Information,
			ContextMenuStrip = menu,
			Visible = true
		};
	}

	public void Dispose()
	{
		_icon.Dispose();
	}
}
