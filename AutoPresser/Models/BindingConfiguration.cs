using System.Windows.Input;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Serialization;

namespace AutoPresser.Models;

public sealed class BindingConfiguration
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Key Trigger { get; set; } = Key.F6;
    public TriggerModifiers Modifiers { get; set; } = TriggerModifiers.None;
    public BindingAction Action { get; set; } = BindingAction.AutoClick;
    public InputTarget Target { get; set; } = new();

    public int IntervalMilliseconds { get; set; } = 100;
    public int RepeatCount { get; set; } = 0;

    // Zero means repeat indefinitely.
    public bool IsEnabled { get; set; } = true;

    [JsonIgnore]
    public string TriggerDisplay => FormatKey(Trigger);

    [JsonIgnore]
    public string ModifiersDisplay => FormatModifiers(Modifiers);

    [JsonIgnore]
    public string ActionDisplay => GetDescription(Action);

    private static string FormatModifiers(TriggerModifiers modifiers)
    {
        if (modifiers == TriggerModifiers.None) return "None";
        var parts = new List<string>();
        if (modifiers.HasFlag(TriggerModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(TriggerModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(TriggerModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(TriggerModifiers.Windows)) parts.Add("Windows");
        return string.Join(" + ", parts);
    }

    private static string GetDescription<T>(T value) where T : struct, Enum
    {
        var member = typeof(T).GetMember(value.ToString()).FirstOrDefault();
        return member?.GetCustomAttribute<DescriptionAttribute>()?.Description ??
            System.Text.RegularExpressions.Regex.Replace(value.ToString(), "(?<=[a-z0-9])(?=[A-Z])", " ");
    }

    private static string FormatKey(Key key) => key switch
    {
        Key.Oem3 => "` / ~", Key.OemMinus => "- / _", Key.OemPlus => "= / +",
        Key.OemOpenBrackets => "[ / {", Key.Oem6 => "] / }", Key.Oem5 => "\\ / |",
        Key.Oem1 => "; / :", Key.OemQuotes => "' / \"", Key.OemComma => ", / <",
        Key.OemPeriod => ". / >", Key.OemQuestion => "/ / ?",
        Key.D0 => "0", Key.D1 => "1", Key.D2 => "2", Key.D3 => "3", Key.D4 => "4",
        Key.D5 => "5", Key.D6 => "6", Key.D7 => "7", Key.D8 => "8", Key.D9 => "9",
        _ => System.Text.RegularExpressions.Regex.Replace(key.ToString(), "(?<=[a-z0-9])(?=[A-Z])", " ")
    };
}