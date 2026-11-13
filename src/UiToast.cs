namespace TitanGameHub
{
    public enum NotificationType
    {
        Info = 0,
        Success = 1,
        Warning = 2,
        Error = 3
    }

    public partial class WpfMainWindow
    {
        private void ShowToast(string message, NotificationType type)
        {
            try
            {
                if (_txtBottomStatus != null)
                    _txtBottomStatus.Text = message ?? string.Empty;
            }
            catch { }
        }
    }
}
