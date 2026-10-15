using Wpf.Ui.Controls;

namespace AutoPresser.Extensions;

internal static class MessageBoxExtensions
{
    extension(MessageBox messageBox)
    {
        public void ShowDialogSafe(Action<Exception>? onException = null,
            bool showAsDialog = true,
            CancellationToken cancellationToken = default)
        {
            _ = messageBox.ShowDialogSafeAsync(onException, showAsDialog, cancellationToken);
        }

        public async Task<MessageBoxResult?> ShowDialogSafeAsync(Action<Exception>? onException = null,
            bool showAsDialog = true,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await messageBox.ShowDialogAsync(showAsDialog, cancellationToken);
            }
            catch (Exception exception)
            {
                onException?.Invoke(exception);
                return null;
            }
        }
    }
}
