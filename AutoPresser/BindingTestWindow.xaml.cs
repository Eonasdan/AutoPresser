using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using AutoPresser.Services;
using Wpf.Ui.Controls;
using Wpf.Ui.Appearance;

namespace AutoPresser;

public partial class BindingTestWindow : FluentWindow
{
    private readonly BindingDiagnostics? _diagnostics;
    private readonly ObservableCollection<string> _messages = [];

    private int _clickCount;

    public BindingTestWindow(BindingDiagnostics diagnostics)
    {
        InitializeComponent();
        SystemThemeWatcher.Watch(this, WindowBackdropType.None);

        _diagnostics = diagnostics;
        LogList.ItemsSource = _messages;

        _diagnostics.MessageLogged += Diagnostics_MessageLogged;
        DebugEnabledCheckBox.IsChecked = _diagnostics.DebugEnabled;

        Closed += (_, _) =>
        {
            _diagnostics.MessageLogged -= Diagnostics_MessageLogged;
            SystemThemeWatcher.UnWatch(this);
        };
    }

    private void TestClickButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _clickCount++;
        ClickCountText.Text = _clickCount.ToString();

        _diagnostics?.Log(
            $"Test button clicked. Count: {_clickCount}");
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        _diagnostics?.Log($"Key pressed: {e.Key}");
    }

    private void Window_TextInput(object sender, TextCompositionEventArgs e)
    {
        _diagnostics?.Log($"Key pressed: {e.Text}");
    }

    private void Diagnostics_MessageLogged(
        object? sender,
        BindingDiagnosticEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _messages.Add(
                $"{e.Timestamp:HH:mm:ss.fff}  {e.Message}");

            // Keep the newest entry visible.
            if (LogList.Items.Count > 0)
            {
                LogList.ScrollIntoView(LogList.Items[^1]);
            }

            // Bound memory use during long tests.
            while (_messages.Count > 1000)
            {
                _messages.RemoveAt(0);
            }
        });
    }

    private void DebugEnabledCheckBox_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (DebugEnabledCheckBox.IsChecked is { } enabled)
        {
            _diagnostics?.DebugEnabled = enabled;
        }
    }

    private void ClearLogButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _messages.Clear();
    }
}