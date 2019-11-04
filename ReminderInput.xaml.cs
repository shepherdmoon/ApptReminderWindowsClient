using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ApptReminderWindowsClient
{
    /// <summary>
    /// Interaction logic for ReminderInput.xaml
    /// </summary>
    public partial class ReminderInput : UserControl
    {
        private readonly string NONE = "None";
        public ReminderInput()
        {
            InitializeComponent();
            DaySelector.ItemsSource = Enumerable.Range(0, 31);
            List<KeyValuePair<int, string>> hours = new List<KeyValuePair<int, string>>
            {
                new KeyValuePair<int, string>(-1, NONE)
            };
            foreach(int hour in Enumerable.Range(0, 24))
            {
                hours.Add(new KeyValuePair<int, string>(hour, hour.ToString()));
            }
            HourSelector.ItemsSource = hours;
            KeyValuePair<string, string>[] contactTypes = new KeyValuePair<string, string>[]
            {
                new KeyValuePair<string, string>(null, NONE),
                new KeyValuePair<string, string>("Email", "Email"),
                new KeyValuePair<string, string>("Text", "Text")
            };
            MainContactSelector.ItemsSource = contactTypes;
            SecondaryContactSelector.ItemsSource = contactTypes;
        }

        public Reminder Data
        {
            get => (Reminder)GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }
        public static readonly DependencyProperty DataProperty = DependencyProperty.Register("Data", typeof(Reminder), typeof(UserControl), new UIPropertyMetadata(null));
    }
}
