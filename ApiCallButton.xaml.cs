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
            StackPanel children = (StackPanel)button.Content;
            var loading = children.Children[0];
            var success = children.Children[1];
            var failure = children.Children[2];
            loading.Visibility = Visibility.Visible;
            success.Visibility = Visibility.Collapsed;
            failure.Visibility = Visibility.Collapsed;
            Click?.Invoke(this, e);
            if (ErrorMessageBox != null) ErrorMessageBox.Text = "";
            if (apiCalls != null)
            {
                var responses = await Task.WhenAll(apiCalls);
                loading.Visibility = Visibility.Collapsed;
                string errorMessage = string.Join("\n", responses.Where(response => response is string));
                if (errorMessage.Length > 0)
                {
                    failure.Visibility = Visibility.Visible;
                    if (ErrorMessageBox != null) ErrorMessageBox.Text = errorMessage;
                }
                else
                {
                    success.Visibility = Visibility.Visible;
                    Callback?.Invoke(responses.Length == 1 ? responses[0] : responses);
                }
            }
            else
            {
                loading.Visibility = Visibility.Collapsed;
                success.Visibility = Visibility.Visible;
            }
            apiCalls = null;
            button.IsEnabled = true;
        }

        private void Button_Visibility_Listener(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue) return;
            Button button = (Button)sender;
            StackPanel children = (StackPanel)button.Content;
            var success = children.Children[1];
            if (success.Visibility == Visibility.Visible)
            {
                success.Visibility = Visibility.Collapsed;
            }
        }
    }
}
