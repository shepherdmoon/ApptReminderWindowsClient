using System.Windows;
using System.Windows.Controls;

namespace ApptReminderWindowsClient
{
    /// <summary>
    /// Interaction logic for TextInput.xaml
    /// </summary>
    public partial class TextInput : UserControl
    {
        public TextInput()
        {
            InitializeComponent();
            DataContext = this;
        }

        public string Title { get; set; }

        public int MaxLength { get; set; }

        public string Value
        {
            get => (string) GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register("Value", typeof(string), typeof(UserControl), new UIPropertyMetadata(""));
    }
}
