using AutoPresser.Models;
using System.Windows.Input;

namespace AutoPresser.Services;

/// <summary>Validates a complete profile before it is activated or saved.</summary>
public static class BindingValidator
{
    private const TriggerModifiers KnownModifiers =
        TriggerModifiers.Control | TriggerModifiers.Shift | TriggerModifiers.Alt | TriggerModifiers.Windows;

    private const TriggerModifiers ReservedEmergencyModifiers = TriggerModifiers.Control | TriggerModifiers.Shift;

    public static IReadOnlyList<BindingValidationIssue> Validate(List<BindingConfiguration> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        var issues = new List<BindingValidationIssue>();
        var duplicateTriggers = new Dictionary<TriggerKey, BindingConfiguration>();
        var duplicateIds = new HashSet<Guid>();

        foreach (var binding in bindings)
        {
            if (binding is null)
            {
                issues.Add(new BindingValidationIssue(null, "The configuration contains a null binding."));
                continue;
            }

            if (!duplicateIds.Add(binding.Id))
            {
                issues.Add(new BindingValidationIssue(binding.Id,
                    $"Binding ID '{binding.Id}' is duplicated."));
            }

            if (!IsValidTrigger(binding.Trigger))
            {
                issues.Add(new BindingValidationIssue(binding.Id,
                    $"'{binding.Trigger}' is not a supported trigger key. Choose a non-modifier key."));
            }

            if ((binding.Modifiers & ~KnownModifiers) != 0)
            {
                issues.Add(new BindingValidationIssue(binding.Id,
                    "The trigger contains an unsupported modifier value."));
            }

            if (binding.Trigger == Key.F12 &&
                (binding.Modifiers & ReservedEmergencyModifiers) == ReservedEmergencyModifiers)
            {
                issues.Add(new BindingValidationIssue(binding.Id,
                    "Ctrl+Shift+F12 is reserved for the emergency stop and cannot be assigned to a binding."));
            }

            if (IsValidTrigger(binding.Trigger) && (binding.Modifiers & ~KnownModifiers) == 0)
            {
                var trigger = new TriggerKey(binding.Trigger, binding.Modifiers);
                if (duplicateTriggers.TryGetValue(trigger, out var existing))
                {
                    issues.Add(new BindingValidationIssue(binding.Id,
                        $"Trigger {FormatTrigger(binding.Trigger, binding.Modifiers)} conflicts with binding '{existing.Id}'. Each trigger combination must be unique."));
                }
                else
                {
                    duplicateTriggers.Add(trigger, binding);
                }
            }

            if (!Enum.IsDefined(binding.Action))
            {
                issues.Add(new BindingValidationIssue(binding.Id, "The binding action is invalid."));
            }

            if (binding.Action == BindingAction.AutoClick)
            {
                if (binding.IntervalMilliseconds < 1)
                {
                    issues.Add(new BindingValidationIssue(binding.Id,
                        "Auto Click interval must be at least 1 millisecond."));
                }

                if (binding.RepeatCount < 0)
                {
                    issues.Add(new BindingValidationIssue(binding.Id,
                        "Auto Click repeat count cannot be negative. Use 0 for infinite repetition."));
                }
            }

            ValidateTarget(binding, issues);
        }

        return issues;
    }

    public static void EnsureValid(List<BindingConfiguration> bindings)
    {
        var issues = Validate(bindings);
        if (issues.Count > 0)
        {
            throw new BindingValidationException(issues);
        }
    }

    public static string FormatTrigger(Key key, TriggerModifiers modifiers)
    {
        var parts = new List<string>(5);
        if (modifiers.HasFlag(TriggerModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(TriggerModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(TriggerModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(TriggerModifiers.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    private static bool IsValidTrigger(Key key)
    {
        if (!Enum.IsDefined(key) || key is Key.None or Key.System or Key.DeadCharProcessed or Key.ImeProcessed)
        {
            return false;
        }

        return !IsModifierKey(key) && KeyInterop.VirtualKeyFromKey(key) != 0;
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftShift or Key.RightShift or
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LWin or Key.RWin;

    private static void ValidateTarget(BindingConfiguration binding, List<BindingValidationIssue> issues)
    {
        if (!Enum.IsDefined(binding.Target.Device))
        {
            issues.Add(new BindingValidationIssue(binding.Id, "The target device is invalid."));
            return;
        }

        switch (binding.Target.Device)
        {
            case TargetDevice.Keyboard:
                if (!Enum.IsDefined(binding.Target.Key) || binding.Target.Key is Key.None or Key.System ||
                    KeyInterop.VirtualKeyFromKey(binding.Target.Key) == 0)
                {
                    issues.Add(new BindingValidationIssue(binding.Id, "The target keyboard key is invalid."));
                }
                break;
            case TargetDevice.Mouse:
                if (!Enum.IsDefined(binding.Target.MouseButton))
                {
                    issues.Add(new BindingValidationIssue(binding.Id, "The target mouse button is invalid."));
                }
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private readonly record struct TriggerKey(Key Key, TriggerModifiers Modifiers);
}

public sealed record BindingValidationIssue(Guid? BindingId, string Message);

public sealed class BindingValidationException(IReadOnlyList<BindingValidationIssue> issues)
    : InvalidOperationException(string.Join(Environment.NewLine, issues.Select(issue => issue.Message)))
{
    public IReadOnlyList<BindingValidationIssue> Issues { get; } = issues;
}
