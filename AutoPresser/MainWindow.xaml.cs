using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoPresser.Extensions;
using AutoPresser.Input;
using AutoPresser.Models;
using AutoPresser.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using UiMessageBox = Wpf.Ui.Controls.MessageBox;
using UiMessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace AutoPresser;

/// <summary>Interaction logic for the AutoPresser dashboard.</summary>
public partial class MainWindow
{
    private readonly GlobalKeyboardHook _keyboardHook;
    private readonly BindingManager _bindingManager;
    private readonly BindingDiagnostics _diagnostics;
    private readonly ConfigurationService _configurationService;
    private readonly AutoPresserConfiguration _configuration;
    private readonly ObservableCollection<ProfileConfiguration> _profileItems;
    private readonly ObservableCollection<BindingConfiguration> _bindingItems;
    private BindingTestWindow? _testWindow;
    private Guid? _editingEnabledBindingId;
    private bool _previousEnabledValue;
    private bool _masterWasEnabledBeforeCheckboxEdit;
    private int _emergencyStopVersionBeforeCheckboxEdit;
    private int _emergencyStopVersion;
    private bool _updatingUi;

    public MainWindow()
    {
        InitializeComponent();
        
        // Apply the current Windows theme and keep it synchronized with future changes.
        SystemThemeWatcher.Watch(this, WindowBackdropType.None);

        _diagnostics = new BindingDiagnostics();
        _bindingManager = new BindingManager(new InputSimulator(), _diagnostics);
        _configurationService = new ConfigurationService();

        var loadResult = _configurationService.LoadOrCreateDefault(CreateDefaultConfiguration);
        _configuration = loadResult;
        Loaded += MainWindow_Loaded;

        _profileItems = new ObservableCollection<ProfileConfiguration>(_configuration.Profiles);
        var activeProfile = GetActiveProfile();
        _bindingItems = new ObservableCollection<BindingConfiguration>(activeProfile.Bindings);
        _updatingUi = true;
        ProfileComboBox.ItemsSource = _profileItems;
        ProfileComboBox.SelectedItem = activeProfile;
        BindingsGrid.ItemsSource = _bindingItems;
        _updatingUi = false;
        ConfigurationPathText.Text = $"Configuration: {_configurationService.FilePath}";

        _bindingManager.SetBindings(activeProfile.Bindings);

        _keyboardHook = new GlobalKeyboardHook();
        _keyboardHook.KeyDown += (_, e) =>
        {
            try
            {
                const TriggerModifiers emergencyStopModifiers = TriggerModifiers.Control | TriggerModifiers.Shift;
                if (e.Key == Key.F12 && (e.Modifiers & emergencyStopModifiers) == emergencyStopModifiers)
                {
                    _diagnostics.Log("Emergency stop activated by Ctrl+Shift+F12.");
                    Interlocked.Increment(ref _emergencyStopVersion);
                    _bindingManager.EmergencyStop();
                    Dispatcher.BeginInvoke(UpdateStatus);
                    return;
                }

                _bindingManager.Toggle(e.Key, e.Modifiers);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(exception);
                _diagnostics.Log($"Hotkey processing failed: {exception.Message}");
            }
        };

        UpdateStatus();
        UpdateProfileControls();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        SystemThemeWatcher.UnWatch(this);
        _keyboardHook.Dispose();
        _bindingManager.Dispose();
        base.OnClosing(e);
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;

        if (_configurationService.LastLoadWarning is { } warning)
        {
            ShowMessage(
                "AutoPresser configuration recovered",
                warning);
        }
    }

    private void ShowMessage(
        string title,
        string content,
        string closeButtonText = "OK")
    {
        CreateMessageBox(
                title,
                content,
                closeButtonText: closeButtonText)
            .ShowDialogSafe(LogDialogException);
    }

    private async Task<UiMessageBoxResult?> ShowMessageAsync(
        string title,
        string content,
        string closeButtonText = "OK",
        string primaryButtonText = "",
        string secondaryButtonText = "")
    {
        return await CreateMessageBox(
                title,
                content,
                closeButtonText,
                primaryButtonText,
                secondaryButtonText)
            .ShowDialogSafeAsync(LogDialogException);
    }

    private UiMessageBox CreateMessageBox(
        string title,
        string content,
        string closeButtonText = "OK",
        string primaryButtonText = "",
        string secondaryButtonText = "")
    {
        return new UiMessageBox
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Title = title,
            Content = content,
            CloseButtonText = closeButtonText,
            PrimaryButtonText = primaryButtonText,
            SecondaryButtonText = secondaryButtonText,
        };
    }

    private void LogDialogException(Exception exception)
    {
        System.Diagnostics.Debug.WriteLine(exception);
        _diagnostics.Log($"Could not show message box: {exception.Message}");
    }

    private async Task<bool> ShowYesNoAsync(string title, string content)
    {
        var result = await ShowMessageAsync(
            title,
            content,
            closeButtonText: "No",
            primaryButtonText: "Yes");

        return result == UiMessageBoxResult.Primary;
    }

    private static AutoPresserConfiguration CreateDefaultConfiguration()
    {
        var profile = new ProfileConfiguration
        {
            Name = "Default",
            Bindings =
            [
                new BindingConfiguration
                {
                    Trigger = Key.F6,
                    Action = BindingAction.AutoClick,
                    Target = new InputTarget { Device = TargetDevice.Mouse, MouseButton = MouseButton.Left },
                    IntervalMilliseconds = 100,
                    RepeatCount = 10
                },
                new BindingConfiguration
                {
                    Trigger = Key.Oem3,
                    Action = BindingAction.Hold,
                    Target = new InputTarget { Device = TargetDevice.Keyboard, Key = Key.E }
                },
                new BindingConfiguration
                {
                    Trigger = Key.F8,
                    Modifiers = TriggerModifiers.Control,
                    Action = BindingAction.AutoClick,
                    Target = new InputTarget { Device = TargetDevice.Mouse, MouseButton = MouseButton.Right },
                    IntervalMilliseconds = 150,
                    RepeatCount = 5
                }
            ]
        };

        return new AutoPresserConfiguration
        {
            SchemaVersion = 1,
            ActiveProfileId = profile.Id,
            Profiles = [profile]
        };
    }

    private ProfileConfiguration GetActiveProfile() =>
        _configuration.Profiles.Single(profile => profile.Id == _configuration.ActiveProfileId);

    private void MasterSwitch_Checked(object sender, RoutedEventArgs e)
    {
        if (_updatingUi) return;

        if (!_bindingManager.SetMasterEnabled(true))
        {
            _updatingUi = true;
            MasterSwitch.IsChecked = false;
            _updatingUi = false;
        }

        UpdateStatus();
    }

    private void MasterSwitch_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_updatingUi) return;
        _bindingManager.SetMasterEnabled(false);
        UpdateStatus();
    }

    private void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi || ProfileComboBox.SelectedItem is not ProfileConfiguration selectedProfile ||
            selectedProfile.Id == _configuration.ActiveProfileId)
        {
            return;
        }

        var previousProfile = GetActiveProfile();
        RunWithBindingsSuspended(() =>
        {
            try
            {
                _bindingManager.SetBindings(selectedProfile.Bindings);
                _configuration.ActiveProfileId = selectedProfile.Id;
                RefreshBindingItems(selectedProfile);
                if (!SaveConfiguration())
                {
                    _configuration.ActiveProfileId = previousProfile.Id;
                    _bindingManager.SetBindings(previousProfile.Bindings);
                    RefreshBindingItems(previousProfile);
                    _updatingUi = true;
                    ProfileComboBox.SelectedItem = previousProfile;
                    _updatingUi = false;
                    return;
                }

                ProfileNameTextBox.Clear();
                _diagnostics.Log($"Switched to profile '{selectedProfile.Name}'.");
            }
            catch (Exception exception)
            {
                _configuration.ActiveProfileId = previousProfile.Id;
                _bindingManager.SetBindings(previousProfile.Bindings);
                RefreshBindingItems(previousProfile);
                _updatingUi = true;
                ProfileComboBox.SelectedItem = previousProfile;
                _updatingUi = false;
                ShowMessage("Could not switch profile", exception.Message);
            }
        });
    }

    private void NewProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var name = ProfileNameTextBox.Text.Trim();
        if (!ValidateNewProfileName(name)) return;

        var previousProfile = GetActiveProfile();
        var profile = new ProfileConfiguration { Name = name };
        RunWithBindingsSuspended(() =>
        {
            _configuration.Profiles.Add(profile);
            _profileItems.Add(profile);
            _configuration.ActiveProfileId = profile.Id;
            _bindingManager.SetBindings(profile.Bindings);
            RefreshBindingItems(profile);
            _updatingUi = true;
            ProfileComboBox.SelectedItem = profile;
            _updatingUi = false;

            if (!SaveConfiguration())
            {
                _configuration.Profiles.Remove(profile);
                _profileItems.Remove(profile);
                _configuration.ActiveProfileId = previousProfile.Id;
                _bindingManager.SetBindings(previousProfile.Bindings);
                RefreshBindingItems(previousProfile);
                _updatingUi = true;
                ProfileComboBox.SelectedItem = previousProfile;
                _updatingUi = false;
                return;
            }

            ProfileNameTextBox.Clear();
            _diagnostics.Log($"Created profile '{profile.Name}'. The new profile has no bindings yet.");
        });
    }

    private void RenameProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var profile = GetActiveProfile();
        var name = ProfileNameTextBox.Text.Trim();
        if (name.Length == 0)
        {
            ShowMessage(
                "Profile name required",
                "Enter a name for the selected profile.");

            return;
        }

        if (_configuration.Profiles.Any(other =>
                other.Id != profile.Id && string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            ShowMessage(
                "Duplicate profile name",
                "A profile with that name already exists.");

            return;
        }

        profile.Name = name;
        ProfileComboBox.Items.Refresh();
        ProfileComboBox.SelectedItem = profile;
        ProfileNameTextBox.Clear();
        SaveConfiguration();
        _diagnostics.Log($"Renamed profile to '{profile.Name}'.");
        UpdateProfileControls();
    }

    private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
    {
        _ = DeleteProfileButton_ClickAsync();
    }

    private async Task DeleteProfileButton_ClickAsync()
    {
        if (_configuration.Profiles.Count <= 1)
        {
            ShowMessage("Cannot delete profile", "AutoPresser must have at least one profile.");
            return;
        }

        await RunWithBindingsSuspendedAsync(async () =>
        {
            try
            {
                var profile = GetActiveProfile();
                var answer = await ShowYesNoAsync(
                    "Delete profile",
                    $"Delete the profile '{profile.Name}'? This cannot be undone.");
                if (!answer) return;

                var replacement = _configuration.Profiles.First(other => other.Id != profile.Id);
                _configuration.Profiles.Remove(profile);
                _profileItems.Remove(profile);
                _configuration.ActiveProfileId = replacement.Id;
                _bindingManager.SetBindings(replacement.Bindings);
                RefreshBindingItems(replacement);
                _updatingUi = true;
                ProfileComboBox.SelectedItem = replacement;
                _updatingUi = false;

                if (!SaveConfiguration())
                {
                    _configuration.Profiles.Add(profile);
                    _profileItems.Add(profile);
                    _configuration.ActiveProfileId = profile.Id;
                    _bindingManager.SetBindings(profile.Bindings);
                    RefreshBindingItems(profile);
                    _updatingUi = true;
                    ProfileComboBox.SelectedItem = profile;
                    _updatingUi = false;
                    return;
                }

                ProfileNameTextBox.Clear();
                _diagnostics.Log($"Deleted profile '{profile.Name}' and switched to '{replacement.Name}'.");
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(exception);
                _diagnostics.Log($"Could not delete profile: {exception.Message}");
                ShowMessage("Could not delete profile", exception.Message);
            }
        });
    }

    private bool ValidateNewProfileName(string name)
    {
        if (name.Length == 0)
        {
            ShowMessage(
                "Profile name required",
                "Enter a name for the new profile.");

            return false;
        }

        if (_configuration.Profiles.Any(profile =>
                string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            ShowMessage(
                "Duplicate profile name",
                "A profile with that name already exists.");

            return false;
        }

        return true;
    }

    private bool SaveConfiguration()
    {
        try
        {
            _configurationService.Save(_configuration);
            return true;
        }
        catch (Exception exception)
        {
            ShowMessage(
                "Configuration save failed",
                $"AutoPresser could not save its configuration.\n\n{exception.Message}");
            return false;
        }
    }

    private void UpdateStatus()
    {
        var profile = GetActiveProfile();
        var enabledBindingCount = profile.Bindings.Count(binding => binding.IsEnabled);
        BindingSummaryText.Text = $"{enabledBindingCount} enabled / {profile.Bindings.Count} configured";

        if (_bindingManager.IsMasterEnabled)
        {
            StatusText.Text = $"Bindings enabled — profile: {profile.Name}";
            FooterStatusText.Text = "Bindings are active";
        }
        else
        {
            StatusText.Text = $"Bindings disabled — profile: {profile.Name}";
            FooterStatusText.Text = "Bindings are inactive";
        }
    }

    private void UpdateProfileControls()
    {
        ProfileComboBox.IsEnabled = true;
        ProfileNameTextBox.IsEnabled = true;
        DeleteProfileButton.IsEnabled = _configuration.Profiles.Count > 1;
        NewProfileButton.IsEnabled = true;
        RenameProfileButton.IsEnabled = true;
        BindingsGrid.IsEnabled = true;
        AddBindingButton.IsEnabled = true;
        UpdateBindingSelectionControls();
    }

    private void RefreshBindingItems(ProfileConfiguration profile)
    {
        _bindingItems.Clear();
        foreach (var binding in profile.Bindings)
        {
            _bindingItems.Add(binding);
        }
        UpdateBindingSelectionControls();
        if (BindingSummaryText is not null)
        {
            var enabledCount = profile.Bindings.Count(binding => binding.IsEnabled);
            BindingSummaryText.Text = $"{enabledCount} enabled / {profile.Bindings.Count} configured";
        }
    }

    private void UpdateBindingSelectionControls()
    {
        var hasSelection = BindingsGrid?.SelectedItem is BindingConfiguration;
        if (EditBindingButton is not null) EditBindingButton.IsEnabled = hasSelection;
        if (RemoveBindingButton is not null) RemoveBindingButton.IsEnabled = hasSelection;
        if (AddBindingButton is not null) AddBindingButton.IsEnabled = true;
    }

    private static BindingConfiguration CloneBinding(BindingConfiguration source) => new()
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

    private void AddBindingButton_Click(object sender, RoutedEventArgs e)
    {
        var editor = new BindingEditorWindow { Owner = this };
        RunWithBindingsSuspended(() =>
        {
            if (editor.ShowDialog() != true || editor.Result is not { } binding) return;
            ApplyBindingChange(binding, isNew: true);
        });
    }

    private void EditBindingButton_Click(object sender, RoutedEventArgs e)
    {
        if (BindingsGrid.SelectedItem is not BindingConfiguration selected) return;

        var editor = new BindingEditorWindow(CloneBinding(selected)) { Owner = this };
        RunWithBindingsSuspended(() =>
        {
            if (editor.ShowDialog() != true || editor.Result is not { } binding) return;
            ApplyBindingChange(binding, isNew: false);
        });
    }

    private void RunWithBindingsSuspended(Action action)
    {
        var wasEnabled = _bindingManager.IsMasterEnabled;
        var emergencyStopVersion = Volatile.Read(ref _emergencyStopVersion);
        _bindingManager.SetMasterEnabled(false);
        UpdateStatus();

        try
        {
            action();
        }
        finally
        {
            if (wasEnabled && emergencyStopVersion == Volatile.Read(ref _emergencyStopVersion))
                _bindingManager.SetMasterEnabled(true);
            UpdateStatus();
            UpdateProfileControls();
        }
    }

    private async Task RunWithBindingsSuspendedAsync(Func<Task> action)
    {
        var wasEnabled = _bindingManager.IsMasterEnabled;
        var emergencyStopVersion = Volatile.Read(ref _emergencyStopVersion);
        _bindingManager.SetMasterEnabled(false);
        UpdateStatus();

        try
        {
            await action();
        }
        finally
        {
            if (wasEnabled && emergencyStopVersion == Volatile.Read(ref _emergencyStopVersion))
                _bindingManager.SetMasterEnabled(true);
            UpdateStatus();
            UpdateProfileControls();
        }
    }

    private void ApplyBindingChange(BindingConfiguration binding, bool isNew)
    {
        var profile = GetActiveProfile();
        var candidate = profile.Bindings.ToList();
        var index = candidate.FindIndex(item => item.Id == binding.Id);
        if (isNew)
        {
            candidate.Add(binding);
        }
        else if (index >= 0)
        {
            candidate[index] = binding;
        }
        else
        {
            ShowMessage("Binding no longer exists", "The selected binding could not be found. Refresh the list and try again.");
            return;
        }

        var issues = BindingValidator.Validate(candidate);
        if (issues.Count > 0)
        {
            ShowMessage("Invalid binding configuration", string.Join(Environment.NewLine, issues.Select(issue => issue.Message)));
            return;
        }

        var previousBindings = profile.Bindings;
        try
        {
            // SetBindings validates again and stops any actions before swapping configuration references.
            _bindingManager.SetBindings(candidate);
            profile.Bindings = candidate;
            RefreshBindingItems(profile);
            if (!SaveConfiguration())
            {
                profile.Bindings = previousBindings;
                _bindingManager.SetBindings(previousBindings);
                RefreshBindingItems(profile);
                return;
            }

            _diagnostics.Log($"{(isNew ? "Added" : "Updated")} binding {BindingValidator.FormatTrigger(binding.Trigger, binding.Modifiers)} in profile '{profile.Name}'.");
            UpdateStatus();
            UpdateProfileControls();
        }
        catch (Exception exception)
        {
            profile.Bindings = previousBindings;
            _bindingManager.SetBindings(previousBindings);
            RefreshBindingItems(profile);
            ShowMessage("Could not save binding", exception.Message);
        }
    }

    private void RemoveBindingButton_Click(object sender, RoutedEventArgs e)
    {
        _ = RemoveBindingButton_ClickAsync();
    }

    private async Task RemoveBindingButton_ClickAsync()
    {
        if (BindingsGrid.SelectedItem is not BindingConfiguration selected) return;

        await RunWithBindingsSuspendedAsync(async () =>
        {
            var confirmed = await ShowYesNoAsync(
                "Remove binding",
                $"Remove the {BindingValidator.FormatTrigger(selected.Trigger, selected.Modifiers)} binding? This cannot be undone.");
            if (!confirmed) return;

            var profile = GetActiveProfile();
            var previousBindings = profile.Bindings;
            var candidate = previousBindings.Where(binding => binding.Id != selected.Id).ToList();
            try
            {
                _bindingManager.SetBindings(candidate);
                profile.Bindings = candidate;
                RefreshBindingItems(profile);
                if (!SaveConfiguration())
                {
                    profile.Bindings = previousBindings;
                    _bindingManager.SetBindings(previousBindings);
                    RefreshBindingItems(profile);
                    return;
                }

                _diagnostics.Log($"Removed binding {selected.Id} from profile '{profile.Name}'.");
                UpdateProfileControls();
            }
            catch (Exception exception)
            {
                profile.Bindings = previousBindings;
                _bindingManager.SetBindings(previousBindings);
                RefreshBindingItems(profile);
                ShowMessage("Could not remove binding", exception.Message);
            }
        });
    }

    private void BindingsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateBindingSelectionControls();

    private void BindingsGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Column is DataGridCheckBoxColumn && e.Row.Item is BindingConfiguration binding)
        {
            _editingEnabledBindingId = binding.Id;
            _previousEnabledValue = binding.IsEnabled;
            _masterWasEnabledBeforeCheckboxEdit = _bindingManager.IsMasterEnabled;
            _emergencyStopVersionBeforeCheckboxEdit = Volatile.Read(ref _emergencyStopVersion);
            _bindingManager.SetMasterEnabled(false);
            UpdateStatus();
        }
    }

    private void BindingsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.Column is not DataGridCheckBoxColumn) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (_editingEnabledBindingId is not { } bindingId) return;
                var binding = GetActiveProfile().Bindings.FirstOrDefault(item => item.Id == bindingId);
                if (binding is null) return;

                if (!SaveConfiguration())
                {
                    binding.IsEnabled = _previousEnabledValue;
                    BindingsGrid.Items.Refresh();
                    return;
                }

                _diagnostics.Log($"Binding {binding.Id} enabled state changed to {binding.IsEnabled}.");
            }
            finally
            {
                _editingEnabledBindingId = null;
                if (_masterWasEnabledBeforeCheckboxEdit &&
                    _emergencyStopVersionBeforeCheckboxEdit == Volatile.Read(ref _emergencyStopVersion))
                    _bindingManager.SetMasterEnabled(true);
                UpdateStatus();
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OpenBindingTestWindow()
    {
        if (_testWindow is null)
        {
            _testWindow = new BindingTestWindow(_diagnostics);
            _testWindow.Closed += (_, _) => _testWindow = null;
            _testWindow.Show();
        }
        else
        {
            _testWindow.Activate();
        }
    }

    private void BindingTestButton_Click(object sender, RoutedEventArgs e) => OpenBindingTestWindow();
}