using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using EmojiTextBlock = Emoji.Wpf.TextBlock;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using FormsScreen = System.Windows.Forms.Screen;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace TypeSenseOverlay;

internal sealed class SuggestionOverlay : Window
{
    private readonly UserSettings _settings;
    private readonly TextBlock[] _words = new TextBlock[3];
    private readonly StackPanel _emojiPredictionPanel;
    private readonly Button _expandButton;
    private readonly Border _expandedPanel;
    private readonly Border _emojiPickerPanel;
    private readonly TextBlock _hint = new TextBlock();
    private readonly Border _frame;
    private readonly Border _moveGrip;
    private readonly StackPanel _layout;
    private readonly Grid _row;
    private readonly Grid _emojiTile;
    private readonly List<EmojiTextBlock> _emojiPickerItems = new List<EmojiTextBlock>();

    private Action<int>? _predictionClicked;
    private Action<string>? _emojiInsert;
    private IReadOnlyList<string> _pendingCandidates = Array.Empty<string>();
    private bool _pendingPaused;
    private int _pendingSelectedIndex = -1;
    private int _selectedIndex = -1;
    private bool _renderPending;
    private bool _followCaretPending;
    private bool _expanded;
    private bool _emojiPickerExpanded;
    private double _collapsedHeight = 58.0;
    private readonly DispatcherTimer _emojiOutsideClickTimer;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private const int VK_LBUTTON = 0x01;

    public SuggestionOverlay(UserSettings settings)
    {
        _settings = settings;
        Width = 410.0;
        Height = _collapsedHeight;
        Topmost = true;
        ShowInTaskbar = false;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = MediaBrushes.Transparent;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;

        _frame = new Border
        {
            Background = new SolidColorBrush(MediaColor.FromRgb(27, 42, 62)),
            CornerRadius = new CornerRadius(18.0),
            Padding = new Thickness(10.0, 8.0, 10.0, 7.0),
            BorderBrush = new SolidColorBrush(MediaColor.FromRgb(76, 101, 133)),
            BorderThickness = new Thickness(1.0),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.28,
                BlurRadius = 15.0,
                ShadowDepth = 4.0
            }
        };

        _layout = new StackPanel();
        _row = new Grid();
        Grid row = _row;
        // Collapsed-row geometry: 42px grip + 3 equal prediction slots + 42px More.
        // The three prediction columns share exactly the same GridLength, so no
        // prediction can acquire a special width or baseline.
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42.0) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42.0) });

        _moveGrip = new Border
        {
            Width = 42.0,
            Height = 42.0,
            CornerRadius = new CornerRadius(9.0),
            Margin = new Thickness(0.0),
            Cursor = System.Windows.Input.Cursors.SizeAll,
            Child = new TextBlock
            {
                Text = "✥",
                FontSize = 17.0,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        _moveGrip.MouseLeftButtonDown += delegate { MoveOverlay(); };
        Grid.SetColumn(_moveGrip, 0);
        row.Children.Add(_moveGrip);

        // All three prediction tiles are the same control type and the same
        // Grid slot geometry. This is important: prediction #3 must not use
        // Emoji.Wpf.TextBlock when it is displaying ordinary text.
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            _words[i] = CreatePredictionText();
            _words[i].MouseLeftButtonUp += delegate { _predictionClicked?.Invoke(index); };
            Grid.SetColumn(_words[i], i + 1);
            row.Children.Add(_words[i]);
        }

        _emojiPredictionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // The third visual prediction slot is a single slot containing up to
        // three individual emoji candidates.
        for (int i = 0; i < 3; i++)
        {
            int index = i + 2;
            Button button = new Button
            {
                Width = 34.0,
                Height = 34.0,
                MinWidth = 34.0,
                MaxWidth = 34.0,
                MinHeight = 34.0,
                MaxHeight = 34.0,
                Padding = new Thickness(0),
                Margin = new Thickness(1.0, 0.0, 1.0, 0.0),
                BorderThickness = new Thickness(0),
                Background = MediaBrushes.Transparent,
                Foreground = MediaBrushes.White,
                FontFamily = new FontFamily("Segoe UI Emoji"),
                FontSize = 18.0,
                Cursor = System.Windows.Input.Cursors.Hand,
                Visibility = Visibility.Collapsed
            };
            button.Click += delegate { _predictionClicked?.Invoke(index); };
            _emojiPredictionPanel.Children.Add(button);
        }
        _emojiTile = new Grid
        {
            Width = double.NaN,
            Height = 42.0,
            MinHeight = 42.0,
            MaxHeight = 42.0
        };
        Grid.SetColumn(_emojiPredictionPanel, 0);
        _emojiTile.Children.Add(_emojiPredictionPanel);

        // _words[2] is already a child of the main prediction row (column 3).
        // Do NOT re-parent it into _emojiTile: WPF elements can have only one
        // logical parent. _emojiTile is an overlay for emoji candidates only.
        Grid.SetColumn(_emojiTile, 3);
        Panel.SetZIndex(_emojiTile, 10);
        _emojiTile.Visibility = Visibility.Collapsed;
        row.Children.Add(_emojiTile);

        _expandButton = new Button
        {
            Content = "⋯",
            FontSize = 21.0,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            Width = 42.0,
            Height = 42.0,
            MinWidth = 42.0,
            MinHeight = 42.0,
            ToolTip = "More GreyBoard options",
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(1.0),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(80, 140, 165, 205)),
            Background = new SolidColorBrush(MediaColor.FromArgb(75, 170, 190, 220))
        };
        _expandButton.Click += delegate { SetExpanded(!_expanded); };
        Grid.SetColumn(_expandButton, 4);
        row.Children.Add(_expandButton);

        _hint.Foreground = new SolidColorBrush(MediaColor.FromRgb(143, 188, 255));
        _hint.FontSize = 10.0;
        _hint.TextAlignment = TextAlignment.Center;
        _hint.Margin = new Thickness(0.0, 3.0, 0.0, 0.0);

        _expandedPanel = BuildCapabilitiesPanel();
        _emojiPickerPanel = BuildEmojiPickerPanel();
        _expandedPanel.Visibility = Visibility.Collapsed;
        _emojiPickerPanel.Visibility = Visibility.Collapsed;

        _emojiOutsideClickTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(35.0)
        };
        _emojiOutsideClickTimer.Tick += delegate { CheckEmojiOutsideClick(); };

        _frame.Child = _layout;
        RefreshExpandedLayout();
        Content = _frame;

        // When the emoji picker is expanded, clicking elsewhere on the
        // overlay closes it. Clicks inside the picker are intentionally ignored.
        _layout.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
        {
            if (!_emojiPickerExpanded)
                return;

            DependencyObject? source = e.OriginalSource as DependencyObject;
            if (source == null ||
                (!IsInsideElement(source, _emojiPickerPanel) &&
                 !IsInsideElement(source, _emojiTile)))
            {
                SetExpanded(false);
                e.Handled = true;
            }
        };

        // This overlay is deliberately non-activating, so WPF Deactivated is
        // not reliable for detecting a click in the host application. The
        // lightweight timer below watches only while the emoji picker is open
        // and never captures/intercepts the mouse. Emoji/tile clicks therefore
        // remain usable while every other click closes the picker.
        Deactivated += delegate
        {
            if (_emojiPickerExpanded)
                SetExpanded(false);
        };

        SourceInitialized += delegate
        {
            nint handle = new WindowInteropHelper(this).Handle;
            nint windowLong = Native.GetWindowLong(handle, -20);
            Native.SetWindowLong(handle, -20, windowLong | 0x8000000 | 0x80);
            ApplyAppearance();
        };

        Loaded += delegate
        {
            Left = _settings.Placement == "Fixed" ? _settings.FixedLeft : 50.0;
            Top = _settings.Placement == "Fixed" ? _settings.FixedTop : 50.0;
            ClampToWorkArea();
        };
    }

    private static TextBlock CreatePredictionText()
    {
        return new TextBlock
        {
            // Let the Grid own the 42px tile height. The text itself must remain
            // at its natural line height so WPF can center the whole text element.
            // The 1px top margin compensates for Segoe UI's visual ascent/baseline
            // so the glyphs, rather than the line box, sit on the tile center.
            Foreground = MediaBrushes.White,
            FontSize = 15.0,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"),
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Padding = new Thickness(5.0, 0.0, 5.0, 0.0),
            Margin = new Thickness(0.0, 1.0, 0.0, 0.0),
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private Border BuildCapabilitiesPanel()
    {
        Grid grid = new Grid { Margin = new Thickness(0.0, 5.0, 0.0, 5.0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42.0) });
        for (int i = 0; i < 3; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

        Button closeButton = new Button
        {
            Content = "×",
            FontSize = 22.0,
            FontWeight = FontWeights.SemiBold,
            Width = 42.0,
            Height = 42.0,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(1.0),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(80, 140, 165, 205)),
            Background = new SolidColorBrush(MediaColor.FromArgb(75, 170, 190, 220)),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Close GreyBoard options"
        };
        closeButton.Click += delegate { SetExpanded(false); };
        Grid.SetColumn(closeButton, 0);
        grid.Children.Add(closeButton);

        AddCapabilityButton(grid, 1, "😊  Emojis", delegate
        {
            _expandedPanel.Visibility = Visibility.Collapsed;
            _emojiPickerExpanded = true;
            _emojiPickerPanel.Visibility = Visibility.Visible;
            RefreshExpandedLayout();
            UpdateExpandedHeight();
            _emojiOutsideClickTimer.Start();
        });

        AddCapabilityButton(grid, 2, "✨  Enhance", delegate { });
        AddCapabilityButton(grid, 3, "⋯  More", delegate { });

        return new Border
        {
            CornerRadius = new CornerRadius(12.0),
            BorderThickness = new Thickness(0.0),
            Child = grid
        };
    }

    private static void AddCapabilityButton(Grid grid, int column, string text, RoutedEventHandler handler)
    {
        Button button = new Button
        {
            Content = text,
            Height = 42.0,
            Margin = new Thickness(4.0, 0.0, 4.0, 0.0),
            Padding = new Thickness(6.0, 0.0, 6.0, 0.0),
            BorderThickness = new Thickness(1.0),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(80, 120, 151, 194)),
            Background = new SolidColorBrush(MediaColor.FromArgb(45, 120, 151, 194)),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontSize = 13.0,
            FontWeight = FontWeights.SemiBold
        };
        button.Click += handler;
        Grid.SetColumn(button, column);
        grid.Children.Add(button);
    }

    private Border BuildEmojiPickerPanel()
    {
        StackPanel root = new StackPanel();

        TextBlock toneLabel = new TextBlock
        {
            Text = "Skin tone",
            FontSize = 10.0,
            Margin = new Thickness(6.0, 2.0, 6.0, 2.0),
            Foreground = MediaBrushes.White
        };
        root.Children.Add(toneLabel);

        WrapPanel tones = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(4.0, 0.0, 4.0, 4.0)
        };

        string[] toneLabels = { "👋", "👋🏻", "👋🏼", "👋🏽", "👋🏾", "👋🏿" };
        string[] toneValues = { "", "🏻", "🏼", "🏽", "🏾", "🏿" };

        for (int i = 0; i < toneValues.Length; i++)
        {
            string tone = toneValues[i];
            Button toneButton = new Button
            {
                Content = new EmojiTextBlock
                {
                    Text = toneLabels[i],
                    FontSize = 18.0,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                Width = 42.0,
                Height = 32.0,
                Margin = new Thickness(1.0),
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = MediaBrushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = i == 0 ? "Default" : "Skin tone " + i
            };

            toneButton.Click += delegate
            {
                EmojiMap.SetSkinTone(tone);
                RefreshEmojiPickerSkinTone();
            };

            tones.Children.Add(toneButton);
        }

        root.Children.Add(tones);

        Border emojiScrollBorder = new Border
        {
            CornerRadius = new CornerRadius(12.0),
            BorderThickness = new Thickness(1.0),
            Child = CreateEmojiScroll()
        };
        root.Children.Add(emojiScrollBorder);

        return new Border
        {
            CornerRadius = new CornerRadius(12.0),
            BorderThickness = new Thickness(1.0),
            Padding = new Thickness(4.0),
            Height = 266.0,
            MinHeight = 266.0,
            MaxHeight = 266.0,
            ClipToBounds = true,
            Child = root
        };
    }

    private ScrollViewer CreateEmojiScroll()
    {
        WrapPanel wrap = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(4.0)
        };

        foreach (string emoji in EmojiMap.AllEmojis)
        {
            string displayedEmoji = EmojiMap.ApplySelectedSkinTone(emoji);

            EmojiTextBlock emojiText = new EmojiTextBlock
            {
                Text = displayedEmoji,
                FontSize = 21.0,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            Button button = new Button
            {
                Content = emojiText,
                FontSize = 21.0,
                Width = 38.0,
                Height = 36.0,
                Padding = new Thickness(0),
                Margin = new Thickness(1.5),
                BorderThickness = new Thickness(0),
                Background = MediaBrushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = displayedEmoji
            };

            _emojiPickerItems.Add(emojiText);

            string selectedEmoji = displayedEmoji;
            button.Click += delegate
            {
                // Keep the emoji picker open for repeated selection.
                _emojiInsert?.Invoke(selectedEmoji);
            };
            wrap.Children.Add(button);
        }

        return new ScrollViewer
        {
            Content = wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Height = 220.0
        };
    }

    private void RefreshEmojiPickerSkinTone()
    {
        // Update only the already-created emoji text controls. This avoids
        // rebuilding hundreds of WPF elements when a tone is selected.
        int count = Math.Min(_emojiPickerItems.Count, EmojiMap.AllEmojis.Count);
        for (int i = 0; i < count; i++)
            _emojiPickerItems[i].Text = EmojiMap.ApplySelectedSkinTone(EmojiMap.AllEmojis[i]);
    }

    public void SetPredictionClickHandler(Action<int> handler) => _predictionClicked = handler;

    public void SetEmojiInsertHandler(Action<string> handler) => _emojiInsert = handler;

    public void Render(IReadOnlyList<string> candidates, bool paused, int selectedIndex = -1)
    {
        _pendingCandidates = candidates.Count == 0 ? Array.Empty<string>() : candidates.ToArray();
        _pendingPaused = paused;
        _pendingSelectedIndex = selectedIndex;

        if (_renderPending)
            return;

        _renderPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, (Action)(() =>
        {
            _renderPending = false;
            _selectedIndex = _pendingSelectedIndex;

            for (int i = 0; i < 2; i++)
            {
                string value = i < _pendingCandidates.Count ? _pendingCandidates[i] : "";
                _words[i].Text = value;
                bool selected = i == _selectedIndex;
                _words[i].FontWeight = selected ? FontWeights.Bold : FontWeights.SemiBold;
                _words[i].Opacity = (_selectedIndex < 0 || selected) ? 1.0 : 0.72;
                _words[i].TextDecorations = selected ? TextDecorations.Underline : null;
            }

            bool hasEmoji = _pendingCandidates.Skip(2).Any(EmojiMap.IsEmoji);
            _emojiPredictionPanel.Visibility = hasEmoji ? Visibility.Visible : Visibility.Collapsed;
            _emojiTile.Visibility = hasEmoji ? Visibility.Visible : Visibility.Collapsed;
            _words[2].Visibility = hasEmoji ? Visibility.Collapsed : Visibility.Visible;

            for (int i = 0; i < 3; i++)
            {
                if (_emojiPredictionPanel.Children[i] is not Button button)
                    continue;

                int candidateIndex = i + 2;
                string value = candidateIndex < _pendingCandidates.Count && EmojiMap.IsEmoji(_pendingCandidates[candidateIndex])
                    ? _pendingCandidates[candidateIndex]
                    : "";

                bool selected = candidateIndex == _selectedIndex;
                if (button.Content is EmojiTextBlock emojiText)
                {
                    emojiText.Text = value;
                    emojiText.FontSize = selected ? 21.0 : 18.0;
                    emojiText.FontWeight = selected ? FontWeights.Bold : FontWeights.Normal;
                    emojiText.Opacity = (_selectedIndex < 0 || selected) ? 1.0 : 0.68;
                    emojiText.RenderTransform = selected
                        ? new ScaleTransform(1.08, 1.08)
                        : Transform.Identity;
                    emojiText.RenderTransformOrigin = new Point(0.5, 0.5);
                }
                else
                {
                    button.Content = new EmojiTextBlock
                    {
                        Text = value,
                        FontSize = selected ? 21.0 : 18.0,
                        FontWeight = selected ? FontWeights.Bold : FontWeights.Normal,
                        RenderTransformOrigin = new Point(0.5, 0.5),
                        RenderTransform = selected
                            ? new ScaleTransform(1.08, 1.08)
                            : Transform.Identity
                    };
                }

                button.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
                button.Opacity = (_selectedIndex < 0 || selected) ? 1.0 : 0.86;
            }

            if (!hasEmoji && _pendingCandidates.Count > 2)
            {
                _words[2].Text = _pendingCandidates[2];
                bool selected = _selectedIndex == 2;
                _words[2].FontWeight = selected ? FontWeights.Bold : FontWeights.SemiBold;
                _words[2].Opacity = (_selectedIndex < 0 || selected) ? 1.0 : 0.72;
                _words[2].TextDecorations = selected ? TextDecorations.Underline : null;
            }
            else
            {
                _words[2].Text = "";
                _words[2].TextDecorations = null;
            }

            _hint.Text = _pendingPaused
                ? "PAUSED - press " + _settings.PauseShortcut + " to resume"
                : "✥ " + _settings.AcceptFirst + "/" + _settings.AcceptSecond + "/" + _settings.AcceptThird + " select  ·  ⋯ more";

            Opacity = ((_pendingPaused || _pendingCandidates.Count > 0) ? 1.0 : 0.78) *
                      (Math.Clamp(_settings.TransparencyPercent, 35, 100) / 100.0);
        }));
    }

    private void CheckEmojiOutsideClick()
    {
        if (!_emojiPickerExpanded || !IsVisible)
            return;

        // Only react to a fresh left-button press. GetAsyncKeyState is used
        // instead of WPF mouse capture because this window intentionally has
        // WS_EX_NOACTIVATE and must not steal focus from the typing target.
        if ((GetAsyncKeyState(VK_LBUTTON) & 0x8000) == 0)
            return;

        if (!GetCursorPos(out NativePoint nativePoint))
            return;

        Point screenPoint = new Point(nativePoint.X, nativePoint.Y);
        if (IsScreenPointInside(_emojiPickerPanel, screenPoint) ||
            IsScreenPointInside(_emojiTile, screenPoint))
            return;

        SetExpanded(false);
    }

    private static bool IsScreenPointInside(FrameworkElement element, Point screenPoint)
    {
        if (!element.IsVisible || element.ActualWidth <= 0.0 || element.ActualHeight <= 0.0)
            return false;

        try
        {
            Point topLeft = element.PointToScreen(new Point(0.0, 0.0));
            Rect bounds = new Rect(topLeft, new Size(element.ActualWidth, element.ActualHeight));
            return bounds.Contains(screenPoint);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsInsideElement(DependencyObject source, DependencyObject target)
    {
        DependencyObject? current = source;
        while (current != null)
        {
            if (ReferenceEquals(current, target))
                return true;

            if (current is Visual)
            {
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            else
            {
                current = LogicalTreeHelper.GetParent(current);
            }
        }

        return false;
    }

    private void RefreshExpandedLayout()
    {
        _layout.Children.Clear();

        // The prediction row is deliberately last while expanded. Because the
        // window bottom is anchored to the normal bar position, all extra UI
        // grows upward without moving the prediction bar.
        if (_expanded)
        {
            if (_emojiPickerExpanded)
                _layout.Children.Add(_emojiPickerPanel);
            else
                _layout.Children.Add(_expandedPanel);

            _layout.Children.Add(_row);
            _layout.Children.Add(_hint);
        }
        else
        {
            _layout.Children.Add(_row);
            _layout.Children.Add(_hint);
        }
    }

    public void SetExpanded(bool expanded)
    {
        _expanded = expanded;
        if (!expanded)
        {
            _emojiPickerExpanded = false;
            _emojiOutsideClickTimer.Stop();
            _expandedPanel.Visibility = Visibility.Collapsed;
            _emojiPickerPanel.Visibility = Visibility.Collapsed;
            _expandButton.Content = "⋯";
        }
        else
        {
            _emojiPickerExpanded = false;
            _emojiOutsideClickTimer.Stop();
            _expandedPanel.Visibility = Visibility.Visible;
            _emojiPickerPanel.Visibility = Visibility.Collapsed;
            _expandButton.Content = "▲";
            ShowOverlay();
        }

        RefreshExpandedLayout();
        UpdateExpandedHeight();
        ApplyAppearance();
        ClampToWorkArea();
    }

    private void UpdateExpandedHeight()
    {
        double oldHeight = Height;
        double newHeight = _collapsedHeight;
        if (_expanded)
            newHeight += _emojiPickerExpanded ? 274.0 : 48.0;

        Height = newHeight;

        // Keep the bottom edge anchored so expansion is upward rather than
        // pushing the typing area downward.
        if (IsVisible)
        {
            Top -= newHeight - oldHeight;
            ClampToWorkArea();
        }
    }

    public void ApplyAppearance()
    {
        Dispatcher.BeginInvoke((Action)delegate
        {
            bool dark = _settings.Theme != "Light";
            MediaColor background = !dark
                ? (_settings.Glass ? MediaColor.FromArgb(218, 246, 249, 254) : MediaColor.FromRgb(252, 253, 255))
                : (_settings.Glass ? MediaColor.FromArgb(205, 23, 31, 43) : MediaColor.FromRgb(27, 42, 62));

            _frame.Background = new SolidColorBrush(background);
            _frame.BorderBrush = new SolidColorBrush(
                dark ? MediaColor.FromArgb(170, 120, 151, 194) : MediaColor.FromRgb(205, 217, 232));
            _moveGrip.Background = new SolidColorBrush(
                dark ? MediaColor.FromArgb(120, 255, 255, 255) : MediaColor.FromRgb(235, 240, 247));

            if (_moveGrip.Child is TextBlock grip)
                grip.Foreground = dark ? MediaBrushes.White : new SolidColorBrush(MediaColor.FromRgb(43, 73, 110));

            for (int i = 0; i < 3; i++)
                _words[i].Foreground = dark
                    ? (i == 0 ? MediaBrushes.White : new SolidColorBrush(MediaColor.FromRgb(204, 218, 237)))
                    : new SolidColorBrush(i == 0 ? MediaColor.FromRgb(21, 67, 131) : MediaColor.FromRgb(53, 72, 95));

            _expandButton.Foreground = dark ? MediaBrushes.White : new SolidColorBrush(MediaColor.FromRgb(43, 73, 110));
            _hint.Foreground = new SolidColorBrush(
                dark ? MediaColor.FromRgb(143, 188, 255) : MediaColor.FromRgb(56, 111, 185));
            _moveGrip.Opacity = _settings.PositionLocked ? 0.45 : 1.0;
            Native.SetGlass(new WindowInteropHelper(this).Handle, _settings.Glass);
        });
    }

    private void MoveOverlay()
    {
        if (_settings.Placement != "Fixed" || _settings.PositionLocked)
            return;

        try
        {
            DragMove();
            ClampToWorkArea();
            _settings.FixedLeft = Left;
            _settings.FixedTop = Top;
            _settings.Save();
        }
        catch { }
    }

    private void ClampToWorkArea()
    {
        DrawingRectangle screen = FormsScreen.FromPoint(new DrawingPoint((int)Left, (int)Top)).WorkingArea;
        Left = Math.Clamp(Left, screen.Left + 8, screen.Right - Width - 8.0);
        Top = Math.Clamp(Top, screen.Top + 8, screen.Bottom - Height - 8.0);
    }

    public void ShowOverlay()
    {
        Dispatcher.BeginInvoke((Action)delegate
        {
            if (!IsVisible)
                Show();
        });
    }

    public void HideOverlay()
    {
        Dispatcher.BeginInvoke((Action)delegate
        {
            if (IsVisible)
                Hide();
        });
    }

    public void FollowCaret()
    {
        if (_settings.Placement == "Fixed" || _followCaretPending)
            return;

        if (!Native.TryGetCaretScreenPosition(out DrawingPoint point))
        {
            HideOverlay();
            return;
        }

        _followCaretPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, (Action)(() =>
        {
            _followCaretPending = false;
            if (!IsVisible)
                Show();

            DrawingRectangle workingArea = FormsScreen.FromPoint(point).WorkingArea;
            Left = Math.Clamp(point.X, workingArea.Left + 8, workingArea.Right - Width - 8.0);

            double collapsedTop = point.Y + 25.0;
            if (_expanded || _emojiPickerExpanded)
            {
                // Anchor the normal prediction row to exactly the same screen
                // position as the collapsed bar; the additional content is
                // entirely above it.
                double expandedTop = collapsedTop - (Height - _collapsedHeight);
                Top = Math.Clamp(
                    expandedTop,
                    workingArea.Top + 8,
                    workingArea.Bottom - Height - 8.0);
            }
            else
            {
                Top = collapsedTop;
                if (Top + Height > workingArea.Bottom - 8)
                    Top = Math.Max(workingArea.Top + 8, point.Y - Height - 9.0);
            }
        }));
    }
}
