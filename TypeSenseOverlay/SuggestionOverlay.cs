using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace TypeSenseOverlay;

internal sealed class SuggestionOverlay : Window
{
	private readonly UserSettings _settings;

	private readonly TextBlock[] _words = new TextBlock[3];

	private readonly TextBlock _hint = new TextBlock();

	private readonly Border _frame;

	private readonly Border _moveGrip;

    private Action<int>? _predictionClicked;
    private int _selectedIndex = -1;
    private IReadOnlyList<string> _pendingCandidates = Array.Empty<string>();
    private bool _pendingPaused;
    private int _pendingSelectedIndex = -1;
    private bool _renderPending;
    private bool _followCaretPending;

	public SuggestionOverlay(UserSettings settings)
	{
		_settings = settings;
		base.Width = 348.0;
		base.Height = 58.0;
		base.Topmost = true;
		base.ShowInTaskbar = false;
		base.WindowStyle = WindowStyle.None;
		base.AllowsTransparency = true;
		base.Background = System.Windows.Media.Brushes.Transparent;
		base.ShowActivated = false;
		base.ResizeMode = ResizeMode.NoResize;
		_frame = new Border
		{
			Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(27, 42, 62)),
			CornerRadius = new CornerRadius(18.0),
			Padding = new Thickness(12.0, 9.0, 12.0, 7.0),
			BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 101, 133)),
			BorderThickness = new Thickness(1.0),
			Effect = new DropShadowEffect
			{
				Color = Colors.Black,
				Opacity = 0.28,
				BlurRadius = 15.0,
				ShadowDepth = 4.0
			}
		};
		StackPanel layout = new StackPanel();
		Grid row = new Grid
		{
			ColumnDefinitions = 
			{
				new ColumnDefinition
				{
					Width = GridLength.Auto
				}
			}
		};
		for (int i = 0; i < 3; i++)
		{
			row.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(1.0, GridUnitType.Star)
			});
		}
		_moveGrip = new Border
		{
			Width = 28.0,
			Height = 28.0,
			CornerRadius = new CornerRadius(9.0),
			Margin = new Thickness(0.0, 0.0, 5.0, 0.0),
			Cursor = System.Windows.Input.Cursors.SizeAll,
			Child = new TextBlock
			{
				Text = "✥",
				FontSize = 16.0,
				FontWeight = FontWeights.SemiBold,
				TextAlignment = TextAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			}
		};
		_moveGrip.MouseLeftButtonDown += delegate
		{
			MoveOverlay();
		};
		Grid.SetColumn(_moveGrip, 0);
		row.Children.Add(_moveGrip);
		for (int i2 = 0; i2 < 3; i2++)
		{
			int index = i2;
            _words[i2] = new TextBlock
			{
				Foreground = ((i2 == 0) ? System.Windows.Media.Brushes.White : new SolidColorBrush(System.Windows.Media.Color.FromRgb(204, 218, 237))),
				FontSize = 15.0,
				FontWeight = FontWeights.SemiBold,
				TextAlignment = TextAlignment.Center,
				TextTrimming = TextTrimming.CharacterEllipsis,
				Padding = new Thickness(5.0, 1.0, 5.0, 1.0),
                Cursor = System.Windows.Input.Cursors.Hand
			};
            _words[i2].MouseLeftButtonUp += delegate
            {
                _predictionClicked?.Invoke(index);
            };
			Grid.SetColumn(_words[i2], i2 + 1);
			row.Children.Add(_words[i2]);
		}
		_hint.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(143, 188, byte.MaxValue));
		_hint.FontSize = 10.0;
		_hint.TextAlignment = TextAlignment.Center;
		_hint.Margin = new Thickness(0.0, 3.0, 0.0, 0.0);
		layout.Children.Add(row);
		layout.Children.Add(_hint);
		_frame.Child = layout;
		base.Content = _frame;
		base.SourceInitialized += delegate
		{
			nint handle = new WindowInteropHelper(this).Handle;
			nint windowLong = Native.GetWindowLong(handle, -20);
			Native.SetWindowLong(handle, -20, windowLong | 0x8000000 | 0x80);
			ApplyAppearance();
		};
		base.Loaded += delegate
		{
			base.Left = ((_settings.Placement == "Fixed") ? _settings.FixedLeft : 50.0);
			base.Top = ((_settings.Placement == "Fixed") ? _settings.FixedTop : 50.0);
			ClampToWorkArea();
		};
	}

	public void SetPredictionClickHandler(Action<int> handler)
    {
        _predictionClicked = handler;
    }

    public void Render(IReadOnlyList<string> candidates, bool paused, int selectedIndex = -1)
    {
        _pendingCandidates = candidates.Count == 0 ? Array.Empty<string>() : candidates.ToArray();
        _pendingPaused = paused;
        _pendingSelectedIndex = selectedIndex;

        if (_renderPending)
            return;

        _renderPending = true;
        base.Dispatcher.BeginInvoke(DispatcherPriority.Render, (Action)(() =>
        {
            _renderPending = false;
            IReadOnlyList<string> latestCandidates = _pendingCandidates;
            _selectedIndex = _pendingSelectedIndex;

            for (int i = 0; i < _words.Length; i++)
            {
                _words[i].Text = i < latestCandidates.Count ? latestCandidates[i] : "";
                bool selected = i == _selectedIndex;
                _words[i].FontWeight = selected ? FontWeights.Bold : FontWeights.SemiBold;
                _words[i].Opacity = (_selectedIndex < 0 || selected) ? 1.0 : 0.72;
                _words[i].TextDecorations = selected ? TextDecorations.Underline : null;
            }

            _hint.Text = _pendingPaused
                ? "PAUSED - press " + _settings.PauseShortcut + " to resume"
                : $"✥ {_settings.AcceptFirst}/{_settings.AcceptSecond}/{_settings.AcceptThird} select";

            base.Opacity = ((_pendingPaused || latestCandidates.Count > 0) ? 1.0 : 0.78) *
                           ((double)Math.Clamp(_settings.TransparencyPercent, 35, 100) / 100.0);
        }));
    }

	public void ApplyAppearance()
	{
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			bool flag = _settings.Theme != "Light";
			System.Windows.Media.Color color = ((!flag) ? (_settings.Glass ? System.Windows.Media.Color.FromArgb(218, 246, 249, 254) : System.Windows.Media.Color.FromRgb(252, 253, byte.MaxValue)) : (_settings.Glass ? System.Windows.Media.Color.FromArgb(205, 23, 31, 43) : System.Windows.Media.Color.FromRgb(27, 42, 62)));
			_frame.Background = new SolidColorBrush(color);
			_frame.BorderBrush = new SolidColorBrush(flag ? System.Windows.Media.Color.FromArgb(170, 120, 151, 194) : System.Windows.Media.Color.FromRgb(205, 217, 232));
			_frame.BorderThickness = new Thickness(1.0);
			_moveGrip.Background = new SolidColorBrush(flag ? System.Windows.Media.Color.FromArgb(120, byte.MaxValue, byte.MaxValue, byte.MaxValue) : System.Windows.Media.Color.FromRgb(235, 240, 247));
			if (_moveGrip.Child is TextBlock textBlock)
			{
				textBlock.Foreground = (flag ? System.Windows.Media.Brushes.White : new SolidColorBrush(System.Windows.Media.Color.FromRgb(43, 73, 110)));
			}
			for (int i = 0; i < _words.Length; i++)
			{
				_words[i].Foreground = (flag ? ((i == 0) ? System.Windows.Media.Brushes.White : new SolidColorBrush(System.Windows.Media.Color.FromRgb(204, 218, 237))) : new SolidColorBrush((i == 0) ? System.Windows.Media.Color.FromRgb(21, 67, 131) : System.Windows.Media.Color.FromRgb(53, 72, 95)));
			}
			_hint.Foreground = new SolidColorBrush(flag ? System.Windows.Media.Color.FromRgb(143, 188, byte.MaxValue) : System.Windows.Media.Color.FromRgb(56, 111, 185));
			_moveGrip.Opacity = (_settings.PositionLocked ? 0.45 : 1.0);
			_moveGrip.ToolTip = ((_settings.Placement != "Fixed") ? "Set popup position to Fixed in Settings to drag" : (_settings.PositionLocked ? "Position is locked" : "Drag to move this bar"));
			Native.SetGlass(new WindowInteropHelper(this).Handle, _settings.Glass);
		});
	}

	private void MoveOverlay()
	{
		if (_settings.Placement != "Fixed" || _settings.PositionLocked)
		{
			return;
		}
		try
		{
			DragMove();
			ClampToWorkArea();
			_settings.FixedLeft = base.Left;
			_settings.FixedTop = base.Top;
			_settings.Save();
		}
		catch
		{
		}
	}

	private void ClampToWorkArea()
	{
		Rectangle screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)base.Left, (int)base.Top)).WorkingArea;
		base.Left = Math.Clamp(base.Left, screen.Left + 8, (double)screen.Right - base.Width - 8.0);
		base.Top = Math.Clamp(base.Top, screen.Top + 8, (double)screen.Bottom - base.Height - 8.0);
	}

	public void ShowOverlay()
	{
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			if (!base.IsVisible)
			{
				Show();
			}
		});
	}

	public void HideOverlay()
	{
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			if (base.IsVisible)
			{
				Hide();
			}
		});
	}

    public void FollowCaret()
    {
        if (_settings.Placement == "Fixed" || _followCaretPending)
            return;

        if (!Native.TryGetCaretScreenPosition(out System.Drawing.Point point))
        {
            HideOverlay();
            return;
        }

        _followCaretPending = true;
        base.Dispatcher.BeginInvoke(DispatcherPriority.Render, (Action)(() =>
        {
            _followCaretPending = false;
            if (!base.IsVisible)
                Show();

            Rectangle workingArea = System.Windows.Forms.Screen.FromPoint(point).WorkingArea;
            base.Left = Math.Clamp(point.X, workingArea.Left + 8, (double)workingArea.Right - base.Width - 8.0);
            base.Top = point.Y + 25;
            if (base.Top + base.Height > workingArea.Bottom - 8)
                base.Top = Math.Max(workingArea.Top + 8, point.Y - base.Height - 9.0);
        }));
    }

}
