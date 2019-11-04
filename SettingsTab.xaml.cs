using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Dynamic;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

namespace ApptReminderWindowsClient
{
    /// <summary>
    /// Interaction logic for SettingsTab.xaml
    /// </summary>
    public partial class SettingsTab : UserControl
    {
        public SettingsTab()
        {
            InitializeComponent();
        }

        public Helper Utils
        {
            get => (Helper)GetValue(UtilsProperty);
            set => SetValue(UtilsProperty, value);
        }
        public static readonly DependencyProperty UtilsProperty = DependencyProperty.Register("Utils", typeof(Helper), typeof(SettingsTab), new UIPropertyMetadata(null));

        private async void Settings_Visibility_Listener(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue) return;
            List<UIElement> elements = Helper.GetAllElements((Grid)sender, new Type[] { typeof(ApiCallButton), typeof(TextInput) });
            elements.ForEach(element => element.IsEnabled = false);

            // get the saved values for url and key
            var url = Utils.GetBaseUrl();
            if (url != null) ApiUrl.Value = url;
            var key = Utils.GetApiKey();
            if (key != null) ApiKey.Value = key;

            // query for the sms redirect value
            if (url != null && key != null)
            {
                SmsRedirectResponseMessage.Text = "";
                var response = await Utils.CallApiEndpoint(HttpMethod.Get, "/org/smsCallRedirect", null, null, url, key);
                if (response is string)
                {
                    SmsRedirectResponseMessage.Text = response;
                }
                else
                {
                    List<object> smsRedirects = response.numbers;
                    if (smsRedirects.Count > 0) SMSRedirect1.Value = (string)smsRedirects[0];
                    if (smsRedirects.Count > 1) SMSRedirect2.Value = (string)smsRedirects[1];
                    if (smsRedirects.Count > 2) SMSRedirect3.Value = (string)smsRedirects[2];
                    if (smsRedirects.Count > 3) SMSRedirect4.Value = (string)smsRedirects[3];
                    if (smsRedirects.Count > 4) SMSRedirect5.Value = (string)smsRedirects[4];
                }
            }
            elements.ForEach(element => element.IsEnabled = true);
        }

        private void HealthCheck_Click(object sender, RoutedEventArgs _)
        {
            ApiCallButton button = (ApiCallButton)sender;
            if (button.ErrorMessageBox == null) button.ErrorMessageBox = HealthcheckResponseMessage;
            button.SetApiCalls(Utils.CallApiEndpoint(HttpMethod.Get, "/healthcheck", null, null, ApiUrl.Value, ApiKey.Value));
        }

        private void SaveApiSettings_Click(object sender, RoutedEventArgs _)
        {
            ApiCallButton button = (ApiCallButton)sender;
            if (button.ErrorMessageBox == null) button.ErrorMessageBox = HealthcheckResponseMessage;
            button.SetApiCalls(Utils.AsyncRunQuery(() =>
            {
                SQLiteCommand insert = new SQLiteCommand($"REPLACE INTO {Helper.SettingsTable} (type, value) VALUES(@urlType, @urlValue),(@keyType, @keyValue)");
                insert.Parameters.AddWithValue("@urlType", Helper.ApiUrlType);
                insert.Parameters.AddWithValue("@urlValue", ApiUrl.Value);
                insert.Parameters.AddWithValue("@keyType", Helper.ApiKeyType);
                insert.Parameters.AddWithValue("@keyValue", ApiKey.Value);
                return insert;
            }));
        }

        private void SmsRedirect_Click(object sender, RoutedEventArgs _)
        {
            ApiCallButton button = (ApiCallButton)sender;
            if (button.ErrorMessageBox == null) button.ErrorMessageBox = SmsRedirectResponseMessage;
            List<string> numbers = new List<string>();
            if (SMSRedirect1.Value != "") numbers.Add(SMSRedirect1.Value);
            if (SMSRedirect2.Value != "") numbers.Add(SMSRedirect2.Value);
            if (SMSRedirect3.Value != "") numbers.Add(SMSRedirect3.Value);
            if (SMSRedirect4.Value != "") numbers.Add(SMSRedirect4.Value);
            if (SMSRedirect5.Value != "") numbers.Add(SMSRedirect5.Value);
            dynamic parameters = new ExpandoObject();
            if (numbers.Count != 0) parameters.numbers = numbers;
            button.SetApiCalls(Utils.CallApiEndpoint(HttpMethod.Put, "/org/smsCallRedirect", parameters));
        }
    }
}
