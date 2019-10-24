using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ApptReminderWindowsClient
{
    /// <summary>
    /// Interaction logic for AddItemDialog.xaml
    /// </summary>
    public partial class AddItemDialog : Window
    {
        public AddItemDialog()
        {
            InitializeComponent();
        }

        public Func<string, bool> Validation { get; set; }

        public string Value { get; protected set; }

        private void Save_Click(object _, RoutedEventArgs _1)
        {
            if (Validation != null && !Validation(Text.Text))
            {
                Text.BorderBrush = Brushes.Red;
            }
            else
            {
                Value = Text.Text;
                this.Close();
            }
        }

        private void Cancel_Click(object _, RoutedEventArgs _1)
        {
            Value = null;
            this.Close();
        }

        private void Grid_IsVisibleChanged(object _, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue) return;
            Text.Focus();
        }

        private void Text_KeyDown(object _, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Save_Click(null, null);
            }
        }
    }
}
