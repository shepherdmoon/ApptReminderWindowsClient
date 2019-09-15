using System;
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

        public Task<dynamic> ApiCall { get; set; }

        public Action<dynamic> Callback { get; set; }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;
            button.IsEnabled = false;
            Click?.Invoke(this, e);
            if (ErrorMessageBox != null) ErrorMessageBox.Text = "";
            StackPanel children = (StackPanel)button.Content;
            children.Children[1].Visibility = Visibility.Visible;
            children.Children[2].Visibility = Visibility.Collapsed;
            children.Children[3].Visibility = Visibility.Collapsed;
            if (ApiCall != null)
            {
                var response = await ApiCall;
                children.Children[1].Visibility = Visibility.Collapsed;
                if (response is string)
                {
                    children.Children[3].Visibility = Visibility.Visible;
                    if (ErrorMessageBox != null) ErrorMessageBox.Text = response;
                }
                else
                {
                    children.Children[2].Visibility = Visibility.Visible;
                    Callback?.Invoke(response);
                }
            }
            else
            {
                children.Children[1].Visibility = Visibility.Collapsed;
                children.Children[2].Visibility = Visibility.Visible;
            }
            button.IsEnabled = true;
        }
    }
}
