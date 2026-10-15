using System.ComponentModel;

namespace AutoPresser.Models;

public enum BindingAction
{
    [Description("Auto Click")]
    AutoClick,
    [Description("Hold Key/Button")]
    Hold
}