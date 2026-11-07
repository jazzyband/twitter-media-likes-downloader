using System.Runtime.ExceptionServices;
using System.Windows.Forms;

namespace MediaDownloader;

internal static class WindowsDialogs
{
    public static string? PickExecutable(string title, string? initialDirectory = null, string? suggestedFileName = null)
    {
        return ShowInForeground(owner =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false,
                RestoreDirectory = true,
                InitialDirectory = initialDirectory is not null && Directory.Exists(initialDirectory) ? initialDirectory : @"C:\",
                FileName = suggestedFileName ?? string.Empty
            };

            return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
        });
    }

    public static string? PickFolder(string description)
    {
        return ShowInForeground(owner =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = description,
                ShowNewFolderButton = true,
                SelectedPath = @"C:\"
            };

            return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.SelectedPath : null;
        });
    }

    private static T? ShowInForeground<T>(Func<IWin32Window, T?> showDialog)
    {
        T? result = default;
        Exception? error = null;

        // Common file/folder dialogs require STA. A dedicated foreground owner also keeps the picker visible when the application is launched from Windows Terminal
        var thread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                using var owner = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    Location = Cursor.Position,
                    Size = new System.Drawing.Size(1, 1),
                    Opacity = 0.01,
                    TopMost = true
                };

                owner.Show();
                owner.Activate();
                result = showDialog(owner);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
            ExceptionDispatchInfo.Capture(error).Throw();

        return result;
    }
}
