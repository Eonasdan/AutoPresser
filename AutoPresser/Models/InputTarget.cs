using System.Windows.Input;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace AutoPresser.Models;

public enum TargetDevice
{
    [Description("Mouse")]
    Mouse,
    [Description("Keyboard")]
    Keyboard
}
public sealed class InputTarget
{
    public TargetDevice Device { get; set; } = TargetDevice.Mouse;
    public MouseButton MouseButton { get; set; } = MouseButton.Left;
    public Key Key { get; set; } = Key.E;

    [JsonIgnore]
    public string DeviceDisplay => Device == TargetDevice.Mouse ? "Mouse" : "Keyboard";

    [JsonIgnore]
    public string MouseButtonDisplay => Device != TargetDevice.Mouse ? "—" : MouseButton switch
    {
        MouseButton.Left => "Left button",
        MouseButton.Middle => "Middle button",
        MouseButton.Right => "Right button",
        MouseButton.XButton1 => "Extra button 1",
        MouseButton.XButton2 => "Extra button 2",
        _ => MouseButton.ToString()
    };

    [JsonIgnore]
    public string KeyDisplay => Device != TargetDevice.Keyboard ? "—" : FormatKey(Key);

    private static string FormatKey(Key key) => key switch
    {
        Key.Oem3 => "` / ~",
        Key.OemMinus => "- / _",
        Key.OemPlus => "= / +",
        Key.OemOpenBrackets => "[ / {",
        Key.Oem6 => "] / }",
        Key.Oem5 => "\\ / |",
        Key.Oem1 => "; / :",
        Key.OemQuotes => "' / \"",
        Key.OemComma => ", / <",
        Key.OemPeriod => ". / >",
        Key.OemQuestion => "/ / ?",
        Key.D0 => "0", Key.D1 => "1", Key.D2 => "2", Key.D3 => "3", Key.D4 => "4",
        Key.D5 => "5", Key.D6 => "6", Key.D7 => "7", Key.D8 => "8", Key.D9 => "9",
        _ => System.Text.RegularExpressions.Regex.Replace(key.ToString(), "(?<=[a-z0-9])(?=[A-Z])", " ")
    };
}