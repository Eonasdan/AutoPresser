using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.ComponentModel;
using System.Reflection;
using AutoPresser.Models;
using AutoPresser.Services;
using JetBrains.Annotations;
using Wpf.Ui.Controls;

namespace AutoPresser;

/// <summary>Edits one binding without mutating the saved profile until validation succeeds.</summary>
public partial class BindingEditorWindow : FluentWindow
{
    private readonly Guid _bindingId;

    public BindingConfiguration? Result { get; private set; }

    public BindingEditorWindow(BindingConfiguration? existing = null)
    {
        InitializeComponent();

        var binding = existing is null ? new BindingConfiguration() : Clone(existing);
        _bindingId = binding.Id;
        HeadingText.Text = existing is null ? "Add binding" : "Edit binding";

        ActionComboBox.ItemsSource = CreateOptions<BindingAction>();
        DeviceComboBox.ItemsSource = CreateOptions<TargetDevice>();
        MouseButtonComboBox.ItemsSource = CreateOptions<MouseButton>();

        TriggerKeyRecorder.Text = FormatTrigger(binding.Trigger, binding.Modifiers);
        _triggerKey = binding.Trigger;
        _triggerModifiers = binding.Modifiers;
        TargetKeyRecorder.Text = FormatKey(binding.Target.Key);
        _targetKey = binding.Target.Key;
        ControlCheckBox.IsChecked = binding.Modifiers.HasFlag(TriggerModifiers.Control);
        ShiftCheckBox.IsChecked = binding.Modifiers.HasFlag(TriggerModifiers.Shift);
        AltCheckBox.IsChecked = binding.Modifiers.HasFlag(TriggerModifiers.Alt);
        WindowsCheckBox.IsChecked = binding.Modifiers.HasFlag(TriggerModifiers.Windows);
        ActionComboBox.SelectedValue = binding.Action;
        DeviceComboBox.SelectedValue = binding.Target.Device;
        MouseButtonComboBox.SelectedValue = binding.Target.MouseButton;
        IntervalTextBox.Text = binding.IntervalMilliseconds.ToString();
        RepeatCountTextBox.Text = binding.RepeatCount.ToString();
        EnabledCheckBox.IsChecked = binding.IsEnabled;
        UpdateActionControls();
        UpdateTargetControls();
    }

    private static BindingConfiguration Clone(BindingConfiguration source) => new()
    {
        Id = source.Id,
        Trigger = source.Trigger,
        Modifiers = source.Modifiers,
        Action = source.Action,
        Target = new InputTarget
        {
            Device = source.Target.Device,
            MouseButton = source.Target.MouseButton,
            Key = source.Target.Key
        },
        IntervalMilliseconds = source.IntervalMilliseconds,
        RepeatCount = source.RepeatCount,
        IsEnabled = source.IsEnabled
    };

    [UsedImplicitly]
    private sealed record EnumOption<T>(T Value, string Display) where T : struct, Enum;

    private static EnumOption<T>[] CreateOptions<T>() where T : struct, Enum =>
    [
        .. Enum.GetValues<T>()
            .Select(value => new EnumOption<T>(value, GetEnumDisplayName(value)))
    ];

    private static string GetEnumDisplayName<T>(T value) where T : struct, Enum
    {
        if (typeof(T) == typeof(MouseButton))
        {
            return (MouseButton)(object)value switch
            {
                MouseButton.Left => "Left button",
                MouseButton.Middle => "Middle button",
                MouseButton.Right => "Right button",
                MouseButton.XButton1 => "Extra button 1",
                MouseButton.XButton2 => "Extra button 2",
                _ => value.ToString()
            };
        }

        var member = typeof(T).GetMember(value.ToString()).FirstOrDefault();
        var description = member?.GetCustomAttribute<DescriptionAttribute>()?.Description;
        return description ?? SplitPascalCase(value.ToString());
    }

    private static string SplitPascalCase(string value) =>
        System.Text.RegularExpressions.Regex.Replace(value, "(?<=[a-z0-9])(?=[A-Z])", " ");

    private static string FormatKey(Key key) => key switch
    {
        Key.Oem3 => "` / ~ (Oem3)",
        Key.OemMinus => "- / _ (OemMinus)",
        Key.OemPlus => "= / + (OemPlus)",
        Key.OemOpenBrackets => "[ / { (OemOpenBrackets)",
        Key.Oem6 => "] / } (Oem6)",
        Key.Oem5 => "\\ / | (Oem5)",
        Key.Oem1 => "; / : (Oem1)",
        Key.OemQuotes => "' / \" (OemQuotes)",
        Key.OemComma => ", / < (OemComma)",
        Key.OemPeriod => ". / > (OemPeriod)",
        Key.OemQuestion => "/ / ? (OemQuestion)",
        Key.D0 => "0", Key.D1 => "1", Key.D2 => "2", Key.D3 => "3", Key.D4 => "4",
        Key.D5 => "5", Key.D6 => "6", Key.D7 => "7", Key.D8 => "8", Key.D9 => "9",
        _ => SplitPascalCase(key.ToString())
    };

    private Key _triggerKey;
    private TriggerModifiers _triggerModifiers;
    private Key _targetKey;
    private bool _recordingTrigger;
    private bool _recordingTarget;

    private static string FormatTrigger(Key key, TriggerModifiers modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(TriggerModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(TriggerModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(TriggerModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(TriggerModifiers.Windows)) parts.Add("Win");
        parts.Add(FormatKey(key));
        return string.Join(" + ", parts);
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;

    private void TriggerKeyRecorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _recordingTrigger = true;
        _recordingTarget = false;
        TriggerKeyRecorder.Text = "Listening… press the trigger combination";
        TriggerRecorderHintText.Text = "Listening for a trigger combination. Press Esc to cancel.";
        TargetRecorderHintText.Text = "Not listening.";
        TriggerKeyRecorder.Focus();
        e.Handled = true;
    }

    private void TriggerKeyRecorder_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_recordingTrigger) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            CancelRecording();
            return;
        }
        if (IsModifierKey(key)) return;
        if (key is Key.None or Key.DeadCharProcessed or Key.ImeProcessed || KeyInterop.VirtualKeyFromKey(key) == 0)
        {
            TriggerKeyRecorder.Text = "Unsupported key — press another key";
            return;
        }

        _triggerKey = key;
        var modifiers = Keyboard.Modifiers;
        _triggerModifiers = TriggerModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control)) _triggerModifiers |= TriggerModifiers.Control;
        if (modifiers.HasFlag(ModifierKeys.Shift)) _triggerModifiers |= TriggerModifiers.Shift;
        if (modifiers.HasFlag(ModifierKeys.Alt)) _triggerModifiers |= TriggerModifiers.Alt;
        if (modifiers.HasFlag(ModifierKeys.Windows)) _triggerModifiers |= TriggerModifiers.Windows;
        ControlCheckBox.IsChecked = _triggerModifiers.HasFlag(TriggerModifiers.Control);
        ShiftCheckBox.IsChecked = _triggerModifiers.HasFlag(TriggerModifiers.Shift);
        AltCheckBox.IsChecked = _triggerModifiers.HasFlag(TriggerModifiers.Alt);
        WindowsCheckBox.IsChecked = _triggerModifiers.HasFlag(TriggerModifiers.Windows);
        TriggerKeyRecorder.Text = FormatTrigger(_triggerKey, _triggerModifiers);
        _recordingTrigger = false;
        TriggerRecorderHintText.Text = "Trigger recorded. Click the field to change it.";
    }

    private void TargetKeyRecorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _recordingTarget = true;
        _recordingTrigger = false;
        TargetKeyRecorder.Text = "Listening… press the target key";
        TargetRecorderHintText.Text = "Listening for a target key. Press Esc to cancel.";
        TriggerRecorderHintText.Text = "Not listening.";
        TargetKeyRecorder.Focus();
        e.Handled = true;
    }

    private void TargetKeyRecorder_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_recordingTarget) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            CancelRecording();
            return;
        }
        if (IsModifierKey(key)) return;
        if (key is Key.None or Key.DeadCharProcessed or Key.ImeProcessed || KeyInterop.VirtualKeyFromKey(key) == 0)
        {
            TargetKeyRecorder.Text = "Unsupported key — press another key";
            return;
        }
        _targetKey = key;
        TargetKeyRecorder.Text = FormatKey(key);
        _recordingTarget = false;
        TargetRecorderHintText.Text = "Target key recorded. Click the field to change it.";
    }

    private void TriggerKeyRecorder_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_recordingTrigger) CancelRecording();
    }

    private void TargetKeyRecorder_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_recordingTarget) CancelRecording();
    }

    private void CancelRecording()
    {
        _recordingTrigger = false;
        _recordingTarget = false;
        TriggerKeyRecorder.Text = FormatTrigger(_triggerKey, _triggerModifiers);
        TargetKeyRecorder.Text = FormatKey(_targetKey);
        TriggerRecorderHintText.Text = "Not listening. Click the field to record a trigger.";
        TargetRecorderHintText.Text = "Not listening. Click the field to record a target key.";
    }

    private void ActionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateActionControls();

    private void DeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateTargetControls();

    private void UpdateActionControls()
    {
        var isAutoClick = ActionComboBox.SelectedValue is BindingAction.AutoClick;
        AutoClickOptionsPanel.Visibility = isAutoClick ? Visibility.Visible : Visibility.Collapsed;
        ActionHelpText.Text = isAutoClick
            ? "Repeatedly presses and releases the selected target. A repeat count of 0 runs until toggled off or stopped."
            : "Presses the selected target once and keeps it held until toggled off, editing is enabled, or the master switch is turned off.";
    }

    private void UpdateTargetControls()
    {
        var isMouse = DeviceComboBox.SelectedValue is TargetDevice.Mouse;
        MouseTargetPanel.Visibility = isMouse ? Visibility.Visible : Visibility.Collapsed;
        KeyboardTargetPanel.Visibility = isMouse ? Visibility.Collapsed : Visibility.Visible;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = false;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;
        if (ActionComboBox.SelectedValue is not BindingAction action ||
            DeviceComboBox.SelectedValue is not TargetDevice device)
        {
            ValidationText.Text = "Record a trigger key, then choose an action and target device.";
            return;
        }

        var target = new InputTarget { Device = device };
        if (device == TargetDevice.Mouse)
        {
            if (MouseButtonComboBox.SelectedValue is not MouseButton mouseButton)
            {
                ValidationText.Text = "Choose a mouse button.";
                return;
            }
            target.MouseButton = mouseButton;
        }
        else
        {
            target.Key = _targetKey;
        }

        if (!int.TryParse(IntervalTextBox.Text, out var interval) || interval < 1 || interval > 600000)
        {
            ValidationText.Text = "Interval must be a whole number from 1 to 600000 milliseconds.";
            return;
        }

        if (!int.TryParse(RepeatCountTextBox.Text, out var repeatCount) || repeatCount < 0 || repeatCount > 1000000)
        {
            ValidationText.Text = "Repeat count must be a whole number from 0 to 1000000. Use 0 for infinite repetition.";
            return;
        }
        var candidate = new BindingConfiguration
        {
            Id = _bindingId,
            Trigger = _triggerKey,
            Modifiers = _triggerModifiers,
            Action = action,
            Target = target,
            IntervalMilliseconds = interval,
            RepeatCount = repeatCount,
            IsEnabled = EnabledCheckBox.IsChecked == true
        };

        var issues = BindingValidator.Validate([candidate]);
        if (issues.Count > 0)
        {
            ValidationText.Text = string.Join(Environment.NewLine, issues.Select(issue => issue.Message));
            return;
        }

        Result = candidate;
        DialogResult = true;
    }
}
