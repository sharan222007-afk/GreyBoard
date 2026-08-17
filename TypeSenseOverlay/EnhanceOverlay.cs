using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TypeSenseOverlay;

internal sealed class EnhanceOverlay : Window
{
    private readonly TextBox _preview;
    private readonly TextBlock _status;
    private readonly Button _apply;
    private readonly AIEnhanceService _service;

    private CancellationTokenSource? _requestCancellation;
    private Action<string>? _applyHandler;
    private string _sourceText = "";
    private string? _context;
    private bool _allowClose;
    private bool _disposed;

    public EnhanceOverlay(UserSettings settings)
    {
        _service = new AIEnhanceService(settings);

        Title = "Grey Board AI Enhance";
        Width = 640;
        Height = 390;
        MinWidth = 520;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.White;

        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "✨ AI Enhance",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(28, 45, 68)),
            Margin = new Thickness(0, 0, 0, 14)
        };
        root.Children.Add(title);

        _preview = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(12),
            FontSize = 15,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 253))
        };
        Grid.SetRow(_preview, 1);
        root.Children.Add(_preview);

        var modes = new WrapPanel { Margin = new Thickness(0, 12, 0, 4) };
        AddModeButton(modes, "✨ Enhance", EnhanceMode.Enhance);
        AddModeButton(modes, "✓ Fix", EnhanceMode.Fix);
        AddModeButton(modes, "↻ Rewrite", EnhanceMode.Rewrite);
        AddModeButton(modes, "Shorten", EnhanceMode.Shorten);
        AddModeButton(modes, "Expand", EnhanceMode.Expand);
        AddModeButton(modes, "Formal", EnhanceMode.Formal);
        AddModeButton(modes, "Casual", EnhanceMode.Casual);
        AddModeButton(modes, "🌐 Translate", EnhanceMode.Translate);
        Grid.SetRow(modes, 2);
        root.Children.Add(modes);

        var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };

        _status = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(100, 112, 128)),
            Margin = new Thickness(0, 0, 0, 10)
        };
        bottom.Children.Add(_status);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };

        _apply = new Button
        {
            Content = "Apply",
            Padding = new Thickness(18, 8, 18, 8),
            IsEnabled = false
        };
        _apply.Click += (_, _) => _applyHandler?.Invoke(_preview.Text);
        buttons.Children.Add(_apply);

        var close = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(18, 8, 18, 8),
            Margin = new Thickness(8, 0, 0, 0)
        };
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);

        bottom.Children.Add(buttons);

        Grid.SetRow(bottom, 3);
        root.Children.Add(bottom);

        Content = root;
    }

    public void SetApplyHandler(Action<string> handler)
    {
        _applyHandler = handler;
    }

    private void AddModeButton(WrapPanel panel, string label, EnhanceMode mode)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 6, 6)
        };

        button.Click += (_, _) => ShowEnhancement(_sourceText, mode, _context);
        panel.Children.Add(button);
    }

    private void CancelCurrentRequest()
    {
        CancellationTokenSource? current =
            Interlocked.Exchange(ref _requestCancellation, null);

        if (current == null)
            return;

        try
        {
            current.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The request owns its CTS and disposes it after its await
            // completes. A late close/cancel must never crash the UI thread.
        }
    }

    public async void ShowEnhancement(
        string selectedText,
        EnhanceMode mode,
        string? context = null)
    {
        if (_disposed)
            return;

        _sourceText = selectedText;
        _context = context;

        // Cancel the previous request, but do NOT dispose its CTS here.
        // The previous async operation owns that CTS and disposes it in its
        // own finally block. This removes the disposal race.
        CancelCurrentRequest();

        var requestCancellation = new CancellationTokenSource();
        Interlocked.Exchange(
            ref _requestCancellation,
            requestCancellation);

        _preview.Text = selectedText;
        _status.Text = $"Working locally with Ollama • {mode}...";
        _apply.IsEnabled = false;

        if (!IsVisible)
            Show();

        Activate();

        try
        {
            AIEnhanceResult result = await _service.EnhanceAsync(
                selectedText,
                mode,
                context,
                requestCancellation.Token);

            if (_disposed ||
                requestCancellation.IsCancellationRequested ||
                !IsVisible)
            {
                return;
            }

            if (result.Success)
            {
                _preview.Text = result.Text;
                _status.Text = "Review the result, then Apply.";
                _apply.IsEnabled = true;
                _preview.Focus();
                _preview.CaretIndex = _preview.Text.Length;
            }
            else
            {
                _status.Text = result.Error;
                _apply.IsEnabled = false;
            }
        }
        catch (OperationCanceledException)
            when (requestCancellation.IsCancellationRequested)
        {
            // Expected when another enhancement replaces this request or
            // the overlay is closed/hidden.
        }
        catch (ObjectDisposedException)
        {
            // Shutdown can race an in-flight HTTP request. Do not surface
            // disposal exceptions to the WPF dispatcher.
        }
        finally
        {
            // Only clear the field if this request is still the current one.
            Interlocked.CompareExchange(
                ref _requestCancellation,
                null,
                requestCancellation);

            requestCancellation.Dispose();
        }
    }

    protected override void OnClosing(
        System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();

            // Closing the overlay normally means "cancel this request",
            // not "destroy the cancellation source".
            CancelCurrentRequest();
            return;
        }

        base.OnClosing(e);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _allowClose = true;

        // Cancel only. The active async operation disposes its own CTS in
        // finally, preventing the exact Cancel-after-Dispose exception seen
        // during shutdown.
        CancelCurrentRequest();

        _service.Dispose();

        if (IsVisible)
            base.Close();
    }
}
