using System;
using System.Globalization;

namespace ApptReminderWindowsClient
{
    public class Appointment
    {
        private readonly string dateFormat;
        public Appointment(string id, string name, long date, string date_format)
        {
            Id = id;
            Name = name;
            Date = DateTimeOffset.FromUnixTimeMilliseconds(date).LocalDateTime.ToString(Helper.DateStringFormat, CultureInfo.CurrentCulture);
            dateFormat = date_format;
        }

        public string Id { get; }
        public string Name { get; }
        public string Date { get; }
        
        public string GetDateFormat()
        {
            return dateFormat;
        }
    }
}