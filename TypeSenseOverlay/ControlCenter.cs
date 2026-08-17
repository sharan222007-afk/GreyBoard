using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TypeSenseOverlay;

internal sealed class ControlCenter : Window
{
	private readonly UserSettings _settings;

	private readonly TypingEngine _engine;

	private readonly TextBlock _state = new TextBlock();

	private readonly TextBlock _detail = new TextBlock();

	private readonly TextBlock _shortcutSummary = new TextBlock();

    private readonly System.Windows.Controls.Button _start =
    new System.Windows.Controls.Button();

    private readonly System.Windows.Controls.Button _stop =
    new System.Windows.Controls.Button();

    private readonly Border _statusDot = new Border();

	private bool _exiting;

	public ControlCenter(UserSettings settings, TypingEngine engine)
	{
		_settings = settings;
		_engine = engine;
		base.Title = "Grey Board";
		base.Width = 590.0;
		base.Height = 505.0;
		base.MinWidth = 590.0;
		base.MinHeight = 505.0;
		base.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        base.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(246, 249, 253));
        StackPanel root = new StackPanel
		{
			Margin = new Thickness(31.0, 28.0, 31.0, 26.0)
		};
		Grid header = new Grid
		{
			ColumnDefinitions = 
			{
				new ColumnDefinition
				{
					Width = new GridLength(1.0, GridUnitType.Star)
				},
				new ColumnDefinition
				{
					Width = GridLength.Auto
				}
			}
		};
		StackPanel titleBlock = new StackPanel
		{
			Children = 
			{
				(UIElement)new TextBlock
				{
					Text = "Grey Board",
					FontSize = 27.0,
					FontWeight = FontWeights.Bold,
					Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 53, 95))
				},
				(UIElement)new TextBlock
				{
					Text = "Your private predictive typing companion",
					FontSize = 13.0,
					Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(103, 117, 136)),
					Margin = new Thickness(0.0, 3.0, 0.0, 0.0)
				}
			}
		};
		header.Children.Add(titleBlock);
		Button settingsButton = new Button
		{
			Content = "Settings",
			Padding = new Thickness(15.0, 7.0, 15.0, 7.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		settingsButton.Click += delegate
		{
			SettingsWindow settingsWindow = new SettingsWindow(_settings, _engine);
			settingsWindow.Owner = this;
			settingsWindow.Closed += delegate
			{
				UpdateState();
			};
			settingsWindow.ShowDialog();
		};
		Grid.SetColumn(settingsButton, 1);
		header.Children.Add(settingsButton);
		root.Children.Add(header);
		Border statusCard = new Border
		{
			Background = Brushes.White,
			BorderBrush = new SolidColorBrush(Color.FromRgb(220, 228, 239)),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(16.0),
			Padding = new Thickness(20.0, 18.0, 20.0, 18.0),
			Margin = new Thickness(0.0, 25.0, 0.0, 14.0)
		};
		Grid statusGrid = new Grid
		{
			ColumnDefinitions = 
			{
				new ColumnDefinition
				{
					Width = GridLength.Auto
				},
				new ColumnDefinition
				{
					Width = new GridLength(1.0, GridUnitType.Star)
				}
			}
		};
		_statusDot.Width = 12.0;
		_statusDot.Height = 12.0;
		_statusDot.CornerRadius = new CornerRadius(6.0);
		_statusDot.Margin = new Thickness(0.0, 5.0, 13.0, 0.0);
		statusGrid.Children.Add(_statusDot);
		StackPanel statusText = new StackPanel();
		_state.FontSize = 17.0;
		_state.FontWeight = FontWeights.SemiBold;
		_detail.FontSize = 12.0;
		_detail.Foreground = new SolidColorBrush(Color.FromRgb(103, 117, 136));
		_detail.Margin = new Thickness(0.0, 3.0, 0.0, 0.0);
		_detail.TextWrapping = TextWrapping.Wrap;
		statusText.Children.Add(_state);
		statusText.Children.Add(_detail);
		Grid.SetColumn(statusText, 1);
		statusGrid.Children.Add(statusText);
		statusCard.Child = statusGrid;
		root.Children.Add(statusCard);
		Grid actionRow = new Grid
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 18.0)
		};
		actionRow.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		actionRow.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		_start.Content = "Start prediction popup";
		_start.Padding = new Thickness(13.0, 11.0, 13.0, 11.0);
		_start.Margin = new Thickness(0.0, 0.0, 7.0, 0.0);
		_start.FontWeight = FontWeights.SemiBold;
		_start.Background = new SolidColorBrush(Color.FromRgb(28, 103, 242));
		_start.Foreground = Brushes.White;
		_start.BorderBrush = Brushes.Transparent;
		_start.Click += Start;
		_stop.Content = "Stop popup";
		_stop.Padding = new Thickness(13.0, 11.0, 13.0, 11.0);
		_stop.Margin = new Thickness(7.0, 0.0, 0.0, 0.0);
		_stop.FontWeight = FontWeights.SemiBold;
		_stop.Click += delegate
		{
			_engine.Stop();
		};
		actionRow.Children.Add(_start);
		Grid.SetColumn(_stop, 1);
		actionRow.Children.Add(_stop);
		root.Children.Add(actionRow);
		Border overview = new Border
		{
			Background = new SolidColorBrush(Color.FromRgb(235, 243, byte.MaxValue)),
			CornerRadius = new CornerRadius(14.0),
			Padding = new Thickness(18.0, 15.0, 18.0, 15.0)
		};
		overview.Child = new StackPanel
		{
			Children = 
			{
				(UIElement)new TextBlock
				{
					Text = "Popup behavior",
					FontWeight = FontWeights.SemiBold,
					Foreground = new SolidColorBrush(Color.FromRgb(31, 71, 124)),
					Margin = new Thickness(0.0, 0.0, 0.0, 7.0)
				},
				(UIElement)new TextBlock
				{
					Text = "• Appears only beside an available text caret — never at the mouse cursor\n• Accepts predictions and learns word patterns locally\n• Use Settings to control shortcuts, theme, glass style, and placement",
					LineHeight = 20.0,
					Foreground = new SolidColorBrush(Color.FromRgb(61, 86, 120)),
					FontSize = 12.0
				}
			}
		};
		root.Children.Add(overview);
		_shortcutSummary.FontSize = 12.0;
		_shortcutSummary.Foreground = new SolidColorBrush(Color.FromRgb(103, 117, 136));
		_shortcutSummary.TextWrapping = TextWrapping.Wrap;
		_shortcutSummary.Margin = new Thickness(3.0, 16.0, 3.0, 0.0);
		root.Children.Add(_shortcutSummary);
		base.Content = root;
		_engine.StateChanged += OnEngineStateChanged;
		base.Closed += delegate
		{
			_engine.StateChanged -= OnEngineStateChanged;
		};
		UpdateState();
	}

	private void Start(object? sender, RoutedEventArgs e)
	{
		try
		{
			_engine.Start();
		}
		catch (Exception ex)
		{
			MessageBox.Show("Grey Board could not start its keyboard listener.\n\n" + ex.Message, "Could not start", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}

	private void OnEngineStateChanged()
	{
		base.Dispatcher.BeginInvoke(new Action(UpdateState));
	}

	private void UpdateState()
	{
		_start.IsEnabled = !_engine.IsRunning;
		_stop.IsEnabled = _engine.IsRunning;
		_statusDot.Background = (_engine.IsRunning ? new SolidColorBrush(Color.FromRgb(43, 166, 119)) : new SolidColorBrush(Color.FromRgb(151, 164, 180)));
		_state.Text = ((!_engine.IsRunning) ? "Prediction popup is stopped" : (_engine.IsPaused ? "Popup paused" : "Prediction popup is active"));
		_detail.Text = ((!_engine.IsRunning) ? "Click Start when you want TypeSense to monitor your typing." : (_engine.IsPaused ? ("Press " + _settings.PauseShortcut + " to resume.") : "Open a normal text field to see predictions beside its caret."));
		_shortcutSummary.Text = $"Shortcuts: {_settings.AcceptFirst} first prediction · {_settings.AcceptSecond} second · {_settings.AcceptThird} third   |   Theme: {_settings.Theme}{(_settings.Glass ? " glass" : "")}";
	}

	public void ShowAndFocus()
	{
		if (base.WindowState == WindowState.Minimized)
		{
			base.WindowState = WindowState.Normal;
		}
		Show();
		Activate();
		Focus();
	}

	public void ExitApplication()
	{
		_exiting = true;
		_engine.Stop();
		Close();
		Application.Current.Shutdown();
	}

	protected override void OnClosing(CancelEventArgs e)
	{
		if (!_exiting)
		{
			e.Cancel = true;
			Hide();
		}
		else
		{
			base.OnClosing(e);
		}
	}
}
