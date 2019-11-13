using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ApptReminderWindowsClient
{
    /// <summary>
    /// Interaction logic for ApiCallButton.xaml
    /// </summary>
    public partial class ApiCallButton : UserControl
    {
        private Task<dynamic>[] apiCalls;
        public ApiCallButton()
        {
            InitializeComponent();
            DataContext = this;
        }

        public string ButtonPadding { get; set; }
        
        public string ButtonMargin { get; set; }

        public string Text { get; set; }

        public bool ShowSuccess { get; set; } = true;

        public TextBlock ErrorMessageBox { get; set; }

        public event RoutedEventHandler Click;

        public void SetApiCalls(Task<dynamic>[] apiCalls)
        {
            this.apiCalls = apiCalls;
        }
        public void SetApiCalls(IEnumerable<Task<dynamic>> apiCalls)
        {
            this.SetApiCalls(apiCalls.ToArray());
        }
        public void SetApiCalls(Task<dynamic> apiCall)
        {
            this.SetApiCalls(new Task<dynamic>[] { apiCall });
        }

        public Action<dynamic> Callback { get; set; }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;
            button.IsEnabled = false;
            Loading.Visibility = Visibility.Visible;
            Success.Visibility = Visibility.Collapsed;
            Failure.Visibility = Visibility.Collapsed;
            Click?.Invoke(this, e);
            if (ErrorMessageBox != null) ErrorMessageBox.Text = "";
            if (apiCalls != null)
            {
                var responses = await Task.WhenAll(apiCalls);
                Loading.Visibility = Visibility.Collapsed;
                string errorMessage = string.Join("\n", responses.Where(response => response is string));
                if (errorMessage.Length > 0)
                {
                    Failure.Visibility = Visibility.Visible;
                    if (ErrorMessageBox != null) ErrorMessageBox.Text = errorMessage;
                }
                else
                {
                    if (ShowSuccess) Success.Visibility = Visibility.Visible;
                    Callback?.Invoke(responses.Length == 1 ? responses[0] : responses);
                }
            }
            else
            {
                Loading.Visibility = Visibility.Collapsed;
                if (ShowSuccess) Success.Visibility = Visibility.Visible;
            }
            apiCalls = null;
            Callback = null;
            button.IsEnabled = true;
        }

        public void ResetSuccess()
        {
            Success.Visibility = Visibility.Collapsed;
        }

        public void ResetFailure()
        {
            Failure.Visibility = Visibility.Collapsed;
            if (ErrorMessageBox != null) ErrorMessageBox.Text = "";
        }

        private void Button_Visibility_Listener(object _, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue) return;
            ResetSuccess();
        }
    }
}
