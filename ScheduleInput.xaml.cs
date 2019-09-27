using System;
using System.Windows.Controls;

namespace ApptReminderWindowsClient
{
    internal class ScheduleValue
    {
        public ScheduleValue(int value)
        {
            Value = value;
            if (value == 0) Display = "12 AM";
            else if (value < 12) Display = $"{value} AM";
            else if (value == 12) Display = "12 PM";
            else if (value < 24) Display = $"{value - 12} PM";
            else if (value == 24) Display = "12 AM";
            else Display = value.ToString();
        }
        public ScheduleValue(int value, string display)
        {
            Value = value;
            Display = display;
        }
        public int Value { get; set; }
        public string Display { get; set; }
        public override string ToString()
        {
            return Display;
        }
        public override bool Equals(Object obj)
        {
            if (obj is ScheduleValue) return Value == ((ScheduleValue)obj).Value;
            return Value == (int)obj;
        }
        public override int GetHashCode()
        {
            return Value;
        }
    }

    /// <summary>
    /// Interaction logic for ScheduleInput.xaml
    /// </summary>
    public partial class ScheduleInput : UserControl
    {
        private readonly string disabled = "Disabled";
        public ScheduleInput()
        {
            InitializeComponent();
            DataContext = this;
            var StartValues = new ScheduleValue[25];
            StartValues[0] = new ScheduleValue(24, disabled);
            for(int i = 0; i < 24; ++i)
            {
                StartValues[i + 1] = new ScheduleValue(i);
            }
            StartValue.ItemsSource = StartValues;
            StartValue.SelectedIndex = 0;
        }

        public int[] Value
        {
            get
            {
                if (StartValue.SelectedIndex == 0) return null;
                return new int[] { StartValue.SelectedIndex - 1, StartValue.SelectedIndex + EndValue.SelectedIndex };
            }
            set
            {
                if (value == null)
                {
                    StartValue.SelectedIndex = 0;
                }
                else
                {
                    StartValue.SelectedIndex = value[0] + 1;
                    EndValue.SelectedIndex = value[1] - value[0] - 1;
                }
            }
        }

        private void StartValue_SelectionChanged(object _, SelectionChangedEventArgs _1)
        {
            int index = StartValue.SelectedIndex;
            if (index == 0)
            {
                EndValue.ItemsSource = new ScheduleValue[] { new ScheduleValue(0, disabled) };
                EndValue.SelectedIndex = 0;
            }
            else
            {
                ScheduleValue current = (ScheduleValue)EndValue.SelectedItem;
                var values = new ScheduleValue[25 - index];
                for (int i = 0; i < 25 - index; ++i)
                {
                    values[i] = new ScheduleValue(i + index);
                }
                EndValue.ItemsSource = values;
                if (current.Value >= index) EndValue.SelectedItem = current;
                else EndValue.SelectedIndex = 0;
            }
            EndValue.IsEnabled = index != 0;
        }
    }
}
