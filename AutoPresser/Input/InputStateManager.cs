using AutoPresser.Models;
using System.Windows.Input;

namespace AutoPresser.Input;

/// <summary>
/// Tracks AutoPresser's logical ownership of keys and mouse buttons so one action
/// cannot release an input that another active action still owns.
/// All input sent through the application should pass through this class.
/// </summary>
public sealed class InputStateManager(InputSimulator inputSimulator)
{
    private readonly Lock _sync = new();
    private readonly Dictionary<InputTargetKey, (InputTarget Target, int Owners)> _held = [];

    public void Press(InputTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var key = InputTargetKey.From(target);

        lock (_sync)
        {
            if (_held.TryGetValue(key, out var current))
            {
                _held[key] = (current.Target, checked(current.Owners + 1));
                return;
            }

            inputSimulator.Press(target);
            _held.Add(key, (target, 1));
        }
    }

    public void Release(InputTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var key = InputTargetKey.From(target);

        lock (_sync)
        {
            if (!_held.TryGetValue(key, out var current))
            {
                // A release without an owned press is intentionally a no-op.
                return;
            }

            if (current.Owners > 1)
            {
                _held[key] = (current.Target, current.Owners - 1);
                return;
            }

            // Keep the entry if sending key-up fails so ReleaseAll can retry it.
            inputSimulator.Release(current.Target);
            _held.Remove(key);
        }
    }

    public void Click(InputTarget target)
    {
        Press(target);
        Release(target);
    }

    /// <summary>
    /// Attempts to release every distinct input AutoPresser believes it owns.
    /// The state is cleared even if Windows rejects a release, so cleanup can
    /// continue for the remaining inputs and the failures can be reported.
    /// </summary>
    public IReadOnlyList<Exception> ReleaseAll()
    {
        lock (_sync)
        {
            List<Exception> errors = [];

            foreach (var (target, _) in _held.Values)
            {
                try
                {
                    inputSimulator.Release(target);
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }

            _held.Clear();
            return errors;
        }
    }

    private readonly record struct InputTargetKey(TargetDevice Device, int Code)
    {
        public static InputTargetKey From(InputTarget target) => target.Device switch
        {
            TargetDevice.Keyboard => new InputTargetKey(
                target.Device,
                KeyInterop.VirtualKeyFromKey(target.Key) is var vk && vk != 0
                    ? vk
                    : throw new ArgumentOutOfRangeException(nameof(target), target.Key, "Unsupported keyboard key.")),
            TargetDevice.Mouse => new InputTargetKey(target.Device, (int)target.MouseButton),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target.Device, "Unsupported target device.")
        };
    }
}
